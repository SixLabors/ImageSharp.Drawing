// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Phase-1 scene-model types are grouped together in one file for now.

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Builds the flush-scoped scene payload consumed by the staged WebGPU rasterizer.
/// </summary>
internal static class WebGPUSceneEncoder
{
    private const int GradientWidth = 512;
    private const int TileWidth = 16;
    private const int TileHeight = 16;
    private const int FixedShift = 8;
    private const int FixedOne = 1 << FixedShift;
    private const byte PathTagLineToF32 = 0x09;
    private const byte PathTagTransform = 0x20;
    private const byte PathTagPath = 0x10;
    private const byte PathTagStyle = 0x40;
    private const byte PathTagSubpathEnd = 0x04;
    private const int StyleBlendModeShift = 1;
    private const uint StyleBlendModeMask = 0x1FFFU << StyleBlendModeShift;
    private const int StyleBlendAlphaShift = 14;
    private const uint StyleBlendAlphaMask = 0xFFFFU << StyleBlendAlphaShift;
    private const uint StyleFlagsFill = 0x40000000U;
    private static readonly GraphicsOptions DefaultClipGraphicsOptions = new();

    /// <summary>
    /// Encodes prepared composition commands into flush-scoped scene buffers.
    /// </summary>
    public static WebGPUEncodedScene Encode(
        IReadOnlyList<CompositionCommand> commands,
        in Rectangle targetBounds,
        MemoryAllocator allocator)
    {
        if (commands.Count == 0)
        {
            return WebGPUEncodedScene.Empty;
        }

        SupportedSubsetSceneEncoding encoding = SupportedSubsetSceneEncoding.Create(commands, targetBounds, allocator);
        try
        {
            if (encoding.IsEmpty)
            {
                return WebGPUEncodedScene.Empty;
            }

            return SupportedSubsetSceneResolver.Resolve(ref encoding, targetBounds, allocator);
        }
        finally
        {
            encoding.Dispose();
        }
    }

    /// <summary>
    /// Validates that the visible fill commands use brush types supported by the staged WebGPU scene format.
    /// </summary>
    /// <param name="commands">The prepared flush commands to validate.</param>
    /// <param name="error">Receives the first validation error when support checks fail.</param>
    /// <returns><see langword="true"/> when all visible fill brushes are supported; otherwise, <see langword="false"/>.</returns>
    public static bool TryValidateBrushSupport(IReadOnlyList<CompositionCommand> commands, out string? error)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            CompositionCommand command = commands[i];
            if (command.Kind is not CompositionCommandKind.FillLayer || !command.IsVisible)
            {
                continue;
            }

            if (!IsSupportedBrush(command.Brush))
            {
                error = $"The staged WebGPU scene pipeline does not support brush type '{command.Brush.GetType().Name}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Mutable flush-scoped encoder state used while appending supported commands into contiguous scene streams.
    /// </summary>
    private ref struct SupportedSubsetSceneEncoding
    {
        private bool hasLastStyle;
        private bool gradientPixelsDetached;
        private uint lastStyle0;
        private uint lastStyle1;
        private readonly Rectangle rootTargetBounds;
        private List<Rectangle>? openLayerBounds;

        /// <summary>
        /// Initializes a new instance of the <see cref="SupportedSubsetSceneEncoding"/> struct.
        /// </summary>
        /// <param name="allocator">The allocator used for all temporary scene streams.</param>
        /// <param name="commandCount">The total prepared command count for the flush.</param>
        /// <param name="rootTargetBounds">The root target bounds used for target-local coordinate conversion.</param>
        private SupportedSubsetSceneEncoding(MemoryAllocator allocator, int commandCount, in Rectangle rootTargetBounds)
        {
            this.PathTags = new OwnedStream<byte>(allocator, Math.Max(commandCount * 8, 256));
            this.PathData = new OwnedStream<uint>(allocator, Math.Max(commandCount * 16, 256));
            this.DrawTags = new OwnedStream<uint>(allocator, Math.Max(commandCount, 16));
            this.DrawData = new OwnedStream<uint>(allocator, Math.Max(commandCount, 16));
            this.Transforms = new OwnedStream<uint>(allocator, 8);
            this.Styles = new OwnedStream<uint>(allocator, Math.Max(commandCount * 2, 16));
            this.GradientPixels = new OwnedStream<uint>(allocator, Math.Max(commandCount * GradientWidth, GradientWidth));
            this.Images = [];
            this.FillCount = 0;
            this.PathCount = 0;
            this.LineCount = 0;
            this.InfoWordCount = 0;
            this.ClipCount = 0;
            this.TotalTileMembershipCount = 0;
            this.GradientRowCount = 0;
            this.hasLastStyle = false;
            this.gradientPixelsDetached = false;
            this.lastStyle0 = 0;
            this.lastStyle1 = 0;
            this.rootTargetBounds = rootTargetBounds;
            this.openLayerBounds = null;

            this.PathTags.Add(PathTagTransform);
            AppendIdentityTransform(ref this.Transforms);
        }

        /// <summary>
        /// Gets or sets the encoded path-tag byte stream.
        /// </summary>
        public OwnedStream<byte> PathTags;

        /// <summary>
        /// Gets or sets the encoded path-coordinate stream.
        /// </summary>
        public OwnedStream<uint> PathData;

        /// <summary>
        /// Gets or sets the encoded draw-tag stream.
        /// </summary>
        public OwnedStream<uint> DrawTags;

        /// <summary>
        /// Gets or sets the encoded draw-data stream.
        /// </summary>
        public OwnedStream<uint> DrawData;

        /// <summary>
        /// Gets or sets the encoded transform stream.
        /// </summary>
        public OwnedStream<uint> Transforms;

        /// <summary>
        /// Gets or sets the encoded style stream.
        /// </summary>
        public OwnedStream<uint> Styles;

        /// <summary>
        /// Gets or sets the packed gradient-ramp pixel stream.
        /// </summary>
        public OwnedStream<uint> GradientPixels;

        /// <summary>
        /// Gets or sets the deferred image payload descriptors that are patched after atlas creation.
        /// </summary>
        public List<GpuImageDescriptor> Images;

        /// <summary>
        /// Gets the number of emitted fill records.
        /// </summary>
        public int FillCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted paths.
        /// </summary>
        public int PathCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted non-horizontal line segments.
        /// </summary>
        public int LineCount { get; private set; }

        /// <summary>
        /// Gets the total info-word count implied by the emitted draw tags.
        /// </summary>
        public int InfoWordCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted clip records.
        /// </summary>
        public int ClipCount { get; private set; }

        /// <summary>
        /// Gets the total tile-membership count for all emitted destination regions.
        /// </summary>
        public int TotalTileMembershipCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted gradient-ramp rows.
        /// </summary>
        public int GradientRowCount { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the encoding produced no fill work.
        /// </summary>
        public readonly bool IsEmpty => this.FillCount == 0;

        /// <summary>
        /// Creates and populates the mutable encoding state for the given flush commands.
        /// </summary>
        public static SupportedSubsetSceneEncoding Create(
            IReadOnlyList<CompositionCommand> commands,
            in Rectangle targetBounds,
            MemoryAllocator allocator)
        {
            SupportedSubsetSceneEncoding encoding = new(allocator, commands.Count, targetBounds);
            encoding.Build(commands);
            return encoding;
        }

        /// <summary>
        /// Disposes all owned stream storage that has not already been detached.
        /// </summary>
        public void Dispose()
        {
            if (!this.gradientPixelsDetached)
            {
                this.GradientPixels.Dispose();
            }

            this.Styles.Dispose();
            this.Transforms.Dispose();
            this.DrawData.Dispose();
            this.DrawTags.Dispose();
            this.PathData.Dispose();
            this.PathTags.Dispose();
        }

        /// <summary>
        /// Marks the gradient pixel stream as detached so disposal does not free it twice.
        /// </summary>
        public void MarkGradientPixelsDetached() => this.gradientPixelsDetached = true;

        /// <summary>
        /// Appends all supported commands into the mutable scene streams.
        /// </summary>
        private void Build(IReadOnlyList<CompositionCommand> commands)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                this.Append(commands[i]);
            }
        }

        /// <summary>
        /// Appends one prepared command to the scene streams when the command kind is supported.
        /// </summary>
        private void Append(in CompositionCommand command)
        {
            switch (command.Kind)
            {
                case CompositionCommandKind.FillLayer:
                    if (!command.IsVisible)
                    {
                        return;
                    }

                    IPath preparedPath = command.PreparedPath!;
                    this.AppendPlainFill(command, preparedPath);
                    return;

                case CompositionCommandKind.BeginLayer:
                    this.AppendBeginLayer(command);
                    return;

                case CompositionCommandKind.EndLayer:
                    this.AppendEndLayer();
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// Encodes one visible fill command into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        private void AppendPlainFill(in CompositionCommand command, IPath preparedPath)
        {
            LinearGeometry geometry = preparedPath.ToLinearGeometry();
            uint drawTag = GetDrawTag(command);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            (uint style0, uint style1) = GetFillStyle(command.GraphicsOptions, command.RasterizerOptions.IntersectionRule);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle || style0 != this.lastStyle0 || style1 != this.lastStyle1;

            // Reserve the exact words/tags this item can append before encoding so the
            // subsequent Add calls stay on the already-allocated contiguous spans.
            ReservePlainFillCapacity(
                geometry.Info,
                drawTag,
                appendStyle,
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels);

            if (appendStyle)
            {
                this.PathTags.Add(PathTagStyle);
                this.Styles.Add(style0);
                this.Styles.Add(style1);
            }

            int encodedPathCount = EncodePath(
                command,
                geometry,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out _);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                command,
                drawTag,
                ref this.DrawData,
                ref this.GradientPixels,
                this.Images,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;

            this.TotalTileMembershipCount += CountTileMembership(GetTargetLocalDestination(command, this.rootTargetBounds));
        }

        /// <summary>
        /// Encodes one begin-layer clip command as a rectangular clip path and draw record.
        /// </summary>
        private void AppendBeginLayer(in CompositionCommand command)
        {
            Rectangle layerBounds = ToTargetLocal(command.LayerBounds, this.rootTargetBounds);
            (uint style0, uint style1) = GetFillStyle(DefaultClipGraphicsOptions, IntersectionRule.NonZero);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle || style0 != this.lastStyle0 || style1 != this.lastStyle1;

            // Begin-layer clip emission is fixed-size: one optional style record,
            // one rectangular clip path, and one BeginClip draw record.
            ReserveBeginLayerCapacity(
                appendStyle,
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles);

            if (appendStyle)
            {
                this.PathTags.Add(PathTagStyle);
                this.Styles.Add(style0);
                this.Styles.Add(style1);
            }

            if (EncodeRectanglePath(layerBounds, ref this.PathTags, ref this.PathData, out int clipLineCount) == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.PathCount++;
            this.LineCount += clipLineCount;
            this.InfoWordCount += (int)GpuSceneDrawTag.Map(GpuSceneDrawTag.BeginClip).InfoOffset;
            this.DrawTags.Add(GpuSceneDrawTag.BeginClip);
            AppendBeginClipData(command.GraphicsOptions, ref this.DrawData);
            this.ClipCount++;
            this.TotalTileMembershipCount += CountTileMembership(layerBounds);
            this.openLayerBounds ??= new List<Rectangle>(4);
            this.openLayerBounds.Add(layerBounds);
        }

        /// <summary>
        /// Encodes the closing record for the innermost open layer, if one exists.
        /// </summary>
        private void AppendEndLayer()
        {
            if (this.openLayerBounds is not { Count: > 0 })
            {
                return;
            }

            int lastIndex = this.openLayerBounds.Count - 1;
            Rectangle layerBounds = this.openLayerBounds[lastIndex];
            this.openLayerBounds.RemoveAt(lastIndex);

            // End-layer emission is fixed-size: one EndClip draw tag and one PathTagPath
            // terminator for the zero-data end marker.
            ReserveEndLayerCapacity(ref this.PathTags, ref this.DrawTags);
            this.DrawTags.Add(GpuSceneDrawTag.EndClip);
            this.PathTags.Add(PathTagPath);
            this.PathCount++;
            this.ClipCount++;
            this.TotalTileMembershipCount += CountTileMembership(layerBounds);
        }
    }

    /// <summary>
    /// Finalizes mutable scene streams into the immutable encoded payload handed to the GPU backend.
    /// </summary>
    private static class SupportedSubsetSceneResolver
    {
        /// <summary>
        /// Resolves the mutable encoding into the final packed scene buffers.
        /// </summary>
        public static WebGPUEncodedScene Resolve(
            ref SupportedSubsetSceneEncoding encoding,
            in Rectangle targetBounds,
            MemoryAllocator allocator)
        {
            int pathTagByteCount = encoding.PathTags.Count;
            int pathTagWordCount = AlignUp(DivideRoundUp(pathTagByteCount, 4), 256);
            int pathDataWordCount = encoding.PathData.Count;
            int drawTagCount = encoding.DrawTags.Count;
            int drawDataWordCount = encoding.DrawData.Count;
            int transformWordCount = encoding.Transforms.Count;
            int styleWordCount = encoding.Styles.Count;
            int drawTagBase = pathTagWordCount + pathDataWordCount;
            int drawDataBase = drawTagBase + drawTagCount;
            int transformBase = drawDataBase + drawDataWordCount;
            int styleBase = transformBase + transformWordCount;
            int sceneWordCount = styleBase + styleWordCount;
            GpuSceneLayout layout = new(
                (uint)drawTagCount,
                (uint)encoding.PathCount,
                (uint)encoding.ClipCount,
                (uint)encoding.InfoWordCount,
                0U,
                (uint)pathTagWordCount,
                (uint)drawTagBase,
                (uint)drawDataBase,
                (uint)transformBase,
                (uint)styleBase);

            IMemoryOwner<uint> sceneDataOwner = allocator.Allocate<uint>(sceneWordCount);
            try
            {
                PackSceneData(
                    layout,
                    pathTagWordCount,
                    encoding.PathTags.WrittenSpan,
                    encoding.PathData.WrittenSpan,
                    encoding.DrawTags.WrittenSpan,
                    encoding.DrawData.WrittenSpan,
                    encoding.Transforms.WrittenSpan,
                    encoding.Styles.WrittenSpan,
                    sceneDataOwner.Memory.Span[..sceneWordCount]);

                return new WebGPUEncodedScene(
                    targetBounds.Size,
                    encoding.InfoWordCount,
                    sceneDataOwner,
                    sceneWordCount,
                    DetachGradientPixels(ref encoding),
                    encoding.Images,
                    encoding.GradientRowCount,
                    layout,
                    encoding.FillCount,
                    encoding.PathCount,
                    encoding.LineCount,
                    pathTagByteCount,
                    pathTagWordCount,
                    pathDataWordCount,
                    drawTagCount,
                    drawDataWordCount,
                    transformWordCount,
                    styleWordCount,
                    encoding.ClipCount,
                    encoding.FillCount,
                    0,
                    encoding.TotalTileMembershipCount,
                    0,
                    DivideRoundUp(targetBounds.Width, TileWidth),
                    DivideRoundUp(targetBounds.Height, TileHeight));
            }
            catch
            {
                sceneDataOwner.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Detaches the gradient pixel payload when gradients were emitted for the flush.
        /// </summary>
        private static IMemoryOwner<uint>? DetachGradientPixels(ref SupportedSubsetSceneEncoding encoding)
        {
            if (encoding.GradientRowCount == 0)
            {
                return null;
            }

            encoding.MarkGradientPixelsDetached();
            return encoding.GradientPixels.DetachOwner();
        }
    }

    /// <summary>
    /// Returns a value indicating whether the brush type can be encoded into the staged WebGPU scene format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSupportedBrush(Brush brush)
        => brush is SolidBrush
            or RecolorBrush
            or LinearGradientBrush
            or RadialGradientBrush
            or EllipticGradientBrush
            or SweepGradientBrush
            or PatternBrush
            or ImageBrush;

    /// <summary>
    /// Maps one prepared draw command to its WebGPU scene draw tag.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetDrawTag(in CompositionCommand command)
        => command.Brush switch
        {
            SolidBrush => GpuSceneDrawTag.FillColor,
            RecolorBrush => GpuSceneDrawTag.FillRecolor,
            LinearGradientBrush => GpuSceneDrawTag.FillLinGradient,
            RadialGradientBrush => GpuSceneDrawTag.FillRadGradient,
            EllipticGradientBrush => GpuSceneDrawTag.FillEllipticGradient,
            SweepGradientBrush => GpuSceneDrawTag.FillSweepGradient,
            PatternBrush => GpuSceneDrawTag.FillImage,
            ImageBrush => GpuSceneDrawTag.FillImage,
            _ => throw new UnreachableException($"Unsupported brush type '{command.Brush.GetType().Name}' should have been rejected before scene encoding.")
        };

    /// <summary>
    /// Encodes a lowered path into path-tag and path-data streams in target-local space.
    /// </summary>
    private static int EncodePath(
        in CompositionCommand command,
        LinearGeometry geometry,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out int pathSegmentCount)
    {
        float samplingOffset = command.RasterizerOptions.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        float pointTranslateX = (command.DestinationOffset.X - rootTargetBounds.X) + samplingOffset;
        float pointTranslateY = (command.DestinationOffset.Y - rootTargetBounds.Y) + samplingOffset;
        pathSegmentCount = 0;
        lineCount = 0;

        // Prepared path points stay in the command's own path space until backend encoding.
        // Ordinary shapes often already live in target coordinates, but text glyphs are emitted
        // as local outlines plus a separate DestinationOffset. We therefore place every point by
        // applying the command's absolute destination offset directly rather than trying to
        // reconstruct placement from Interest/DestinationRegion/SourceOffset, which are clipping
        // artifacts in absolute destination space and can cancel the text offset entirely.
        // The lowered geometry is already contour-ordered, so the encoder walks the segment
        // stream once and emits each contour as: starting point, then one end point per segment.
        SegmentEnumerator segments = geometry.GetSegments();

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            LinearContour contour = geometry.Contours[i];
            _ = segments.MoveNext();
            LinearSegment segment = segments.Current;
            float firstX = segment.Start.X + pointTranslateX;
            float firstY = segment.Start.Y + pointTranslateY;
            pathData.Add(BitcastSingle(firstX));
            pathData.Add(BitcastSingle(firstY));

            for (int j = 0; j < contour.SegmentCount; j++)
            {
                if (j > 0)
                {
                    _ = segments.MoveNext();
                    segment = segments.Current;
                }

                float translatedEndX = segment.End.X + pointTranslateX;
                float translatedEndY = segment.End.Y + pointTranslateY;
                pathData.Add(BitcastSingle(translatedEndX));
                pathData.Add(BitcastSingle(translatedEndY));
                pathTags.Add(PathTagLineToF32);
                pathSegmentCount++;

                float y0 = segment.Start.Y + pointTranslateY;
                float y1 = segment.End.Y + pointTranslateY;
                int fy0 = (int)MathF.Round(y0 * FixedOne);
                int fy1 = (int)MathF.Round(y1 * FixedOne);
                if (fy0 != fy1)
                {
                    lineCount++;
                }
            }

            if (pathTags.Count > 0)
            {
                pathTags[^1] |= PathTagSubpathEnd;
            }
        }

        if (pathSegmentCount == 0)
        {
            return 0;
        }

        pathTags.Add(PathTagPath);
        return 1;
    }

    /// <summary>
    /// Encodes a rectangle as a fixed-size closed path.
    /// </summary>
    private static int EncodeRectanglePath(
        in Rectangle rectangle,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount)
    {
        lineCount = 0;
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return 0;
        }

        float left = rectangle.Left;
        float top = rectangle.Top;
        float right = rectangle.Right;
        float bottom = rectangle.Bottom;

        pathData.Add(BitcastSingle(left));
        pathData.Add(BitcastSingle(top));

        pathData.Add(BitcastSingle(right));
        pathData.Add(BitcastSingle(top));
        pathTags.Add(PathTagLineToF32);

        pathData.Add(BitcastSingle(right));
        pathData.Add(BitcastSingle(bottom));
        pathTags.Add(PathTagLineToF32);

        pathData.Add(BitcastSingle(left));
        pathData.Add(BitcastSingle(bottom));
        pathTags.Add(PathTagLineToF32);

        pathData.Add(BitcastSingle(left));
        pathData.Add(BitcastSingle(top));
        pathTags.Add(PathTagLineToF32 | PathTagSubpathEnd);

        pathTags.Add(PathTagPath);
        lineCount = 2;
        return 1;
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one plain fill item.
    /// </summary>
    private static void ReservePlainFillCapacity(
        LinearGeometryInfo geometryInfo,
        uint drawTag,
        bool appendStyle,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels)
    {
        // Path tags:
        // - one optional PathTagStyle when the style changes
        // - one line/curve-equivalent tag per lowered segment
        // - one final PathTagPath item to terminate the encoded path
        int pathTagAdd = geometryInfo.SegmentCount + 1 + (appendStyle ? 1 : 0);

        // Path data words:
        // - each contour writes its starting point once: 2 uint words (x, y)
        // - each lowered segment writes only its end point: 2 uint words (x, y)
        // The start points are not repeated for every segment because the stream is
        // reconstructed incrementally by the shader/consumer.
        int pathDataAdd = (geometryInfo.ContourCount * 2) + (geometryInfo.SegmentCount * 2);

        pathTags.EnsureAdditionalCapacity(pathTagAdd);
        pathData.EnsureAdditionalCapacity(pathDataAdd);
        drawTags.EnsureAdditionalCapacity(1);
        drawData.EnsureAdditionalCapacity(GetDrawDataWordCount(drawTag));

        if (appendStyle)
        {
            // A style record is always two packed words.
            styles.EnsureAdditionalCapacity(2);
        }

        if (DrawTagUsesGradientRamp(drawTag))
        {
            // Gradient draw records append exactly one packed ramp row.
            gradientPixels.EnsureAdditionalCapacity(GradientWidth);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one begin-layer clip item.
    /// </summary>
    private static void ReserveBeginLayerCapacity(
        bool appendStyle,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles)
    {
        // Rectangles are emitted as:
        // - optional PathTagStyle
        // - four line tags for the rectangle edges
        // - one final PathTagPath terminator
        pathTags.EnsureAdditionalCapacity((appendStyle ? 1 : 0) + 5);

        // Rectangles write five points total:
        // bottom-left, bottom-right, top-right, top-left, then bottom-left again.
        // Each point is two uint words, so 5 * 2 = 10 words.
        pathData.EnsureAdditionalCapacity(10);
        drawTags.EnsureAdditionalCapacity(1);

        // BeginClip emits two draw-data words for blend mode / alpha state.
        drawData.EnsureAdditionalCapacity(2);

        if (appendStyle)
        {
            // A style record is always two packed words.
            styles.EnsureAdditionalCapacity(2);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one end-layer item.
    /// </summary>
    private static void ReserveEndLayerCapacity(
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> drawTags)
    {
        // End-layer emission appends only:
        // - one EndClip draw tag
        // - one PathTagPath marker
        drawTags.EnsureAdditionalCapacity(1);
        pathTags.EnsureAdditionalCapacity(1);
    }

    /// <summary>
    /// Gets the exact draw-data word count emitted for one draw tag.
    /// </summary>
    private static int GetDrawDataWordCount(uint drawTag)
        => drawTag switch
        {
            // Each case matches the exact number of uint words written by AppendDrawData.
            GpuSceneDrawTag.BeginClip => 2,
            GpuSceneDrawTag.FillColor => 1,
            GpuSceneDrawTag.FillRecolor => 3,
            GpuSceneDrawTag.FillLinGradient => 5,
            GpuSceneDrawTag.FillRadGradient => 7,
            GpuSceneDrawTag.FillEllipticGradient => 5,
            GpuSceneDrawTag.FillSweepGradient => 5,
            GpuSceneDrawTag.FillImage => 5,
            _ => throw new UnreachableException($"Unsupported draw tag '{drawTag}' reached draw-data sizing.")
        };

    /// <summary>
    /// Returns a value indicating whether the draw tag emits one gradient ramp row.
    /// </summary>
    private static bool DrawTagUsesGradientRamp(uint drawTag)
        => drawTag is GpuSceneDrawTag.FillLinGradient
            or GpuSceneDrawTag.FillRadGradient
            or GpuSceneDrawTag.FillEllipticGradient
            or GpuSceneDrawTag.FillSweepGradient;

    /// <summary>
    /// Appends one transform record to the transform stream.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendTransform(GpuSceneTransform transform, ref OwnedStream<uint> transforms)
    {
        transforms.Add(BitcastSingle(transform.M11));
        transforms.Add(BitcastSingle(transform.M12));
        transforms.Add(BitcastSingle(transform.M21));
        transforms.Add(BitcastSingle(transform.M22));
        transforms.Add(BitcastSingle(transform.Tx));
        transforms.Add(BitcastSingle(transform.Ty));
    }

    /// <summary>
    /// Appends the identity transform record.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendIdentityTransform(ref OwnedStream<uint> transforms)
        => AppendTransform(GpuSceneTransform.Identity, ref transforms);

    /// <summary>
    /// Packs the fill style words used by the GPU scene format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (uint Style0, uint Style1) GetFillStyle(GraphicsOptions options, IntersectionRule intersectionRule)
    {
        uint style0 = intersectionRule == IntersectionRule.EvenOdd ? StyleFlagsFill : 0U;
        uint packedBlendMode = PackBlendMode(options);
        uint packedBlendAlpha = PackBlendAlpha(options.BlendPercentage);
        style0 |= (packedBlendMode << StyleBlendModeShift) & StyleBlendModeMask;
        style0 |= (packedBlendAlpha << StyleBlendAlphaShift) & StyleBlendAlphaMask;
        return (style0, 0U);
    }

    /// <summary>
    /// Packs the normalized blend percentage into the scene style alpha field.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackBlendAlpha(float blendPercentage)
        => (uint)Math.Clamp((int)MathF.Round(Math.Clamp(blendPercentage, 0F, 1F) * 65535F), 0, 65535);

    /// <summary>
    /// Packs the individual scene streams into one final scene-word buffer using the resolved layout.
    /// </summary>
    private static void PackSceneData(
        GpuSceneLayout layout,
        int pathTagWordCount,
        ReadOnlySpan<byte> pathTags,
        ReadOnlySpan<uint> pathData,
        ReadOnlySpan<uint> drawTags,
        ReadOnlySpan<uint> drawData,
        ReadOnlySpan<uint> transforms,
        ReadOnlySpan<uint> styles,
        Span<uint> sceneWords)
    {
        // Path tags are byte-addressed in the front of the packed scene buffer, but the
        // overall scene layout is word-addressed. The padded prefix is therefore cleared
        // before the written tag bytes are copied into it.
        Span<byte> sceneBytes = MemoryMarshal.Cast<uint, byte>(sceneWords);
        int paddedPathTagBytes = checked(pathTagWordCount * sizeof(uint));
        sceneBytes[..paddedPathTagBytes].Clear();
        pathTags.CopyTo(sceneBytes);
        pathData.CopyTo(sceneWords[(int)layout.PathDataBase..]);
        drawTags.CopyTo(sceneWords[(int)layout.DrawTagBase..]);
        drawData.CopyTo(sceneWords[(int)layout.DrawDataBase..]);
        transforms.CopyTo(sceneWords[(int)layout.TransformBase..]);
        styles.CopyTo(sceneWords[(int)layout.StyleBase..]);
    }

    /// <summary>
    /// Reinterprets a single-precision floating-point value as its raw uint bit pattern.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BitcastSingle(float value)
        => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    /// <summary>
    /// Divides a positive value and rounds the result up to the next integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    /// <summary>
    /// Aligns a value up to the requested alignment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment)
        => value + ((alignment - (value % alignment)) % alignment);

    /// <summary>
    /// Counts how many fixed-size tiles intersect the destination region.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountTileMembership(in Rectangle destinationRegion)
    {
        int tileMinX = Math.Max(0, destinationRegion.Left / TileWidth);
        int tileMinY = Math.Max(0, destinationRegion.Top / TileHeight);
        int tileMaxX = Math.Max(tileMinX + 1, DivideRoundUp(destinationRegion.Right, TileWidth));
        int tileMaxY = Math.Max(tileMinY + 1, DivideRoundUp(destinationRegion.Bottom, TileHeight));
        return (tileMaxX - tileMinX) * (tileMaxY - tileMinY);
    }

    /// <summary>
    /// Converts absolute bounds into root-target-local bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rectangle ToTargetLocal(in Rectangle absoluteBounds, in Rectangle rootTargetBounds)
        => new(
            absoluteBounds.X - rootTargetBounds.X,
            absoluteBounds.Y - rootTargetBounds.Y,
            absoluteBounds.Width,
            absoluteBounds.Height);

    /// <summary>
    /// Computes the command destination region in root-target-local coordinates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rectangle GetTargetLocalDestination(in CompositionCommand command, in Rectangle rootTargetBounds)
        => new(
            (command.TargetBounds.X - rootTargetBounds.X) + command.DestinationRegion.X,
            (command.TargetBounds.Y - rootTargetBounds.Y) + command.DestinationRegion.Y,
            command.DestinationRegion.Width,
            command.DestinationRegion.Height);

    /// <summary>
    /// Packs a solid brush color into premultiplied RGBA8 scene storage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackSolidColor(SolidBrush solidBrush)
    {
        Vector4 color = solidBrush.Color.ToScaledVector4();
        color.X *= color.W;
        color.Y *= color.W;
        color.Z *= color.W;
        return Rgba32.FromScaledVector4(color).Rgba;
    }

    /// <summary>
    /// Appends the draw-data payload for one encoded draw tag.
    /// </summary>
    private static void AppendDrawData(
        in CompositionCommand command,
        uint drawTag,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        List<GpuImageDescriptor> images,
        ref int gradientRowCount)
    {
        // The draw tag selects the payload layout, so this switch is the single place
        // where the encoded draw-data stream shape is kept in sync with the sizing logic.
        Brush brush = command.Brush;
        switch (drawTag)
        {
            case GpuSceneDrawTag.BeginClip:
                AppendBeginClipData(command.GraphicsOptions, ref drawData);
                break;
            case GpuSceneDrawTag.FillColor:
                drawData.Add(PackSolidColor((SolidBrush)brush));
                break;
            case GpuSceneDrawTag.FillRecolor:
                AppendRecolorData((RecolorBrush)brush, ref drawData);
                break;
            case GpuSceneDrawTag.FillLinGradient:
                AppendLinearGradientData((LinearGradientBrush)brush, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillRadGradient:
                AppendRadialGradientData((RadialGradientBrush)brush, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillEllipticGradient:
                AppendEllipticGradientData((EllipticGradientBrush)brush, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillSweepGradient:
                AppendSweepGradientData((SweepGradientBrush)brush, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillImage:
                AppendImageData(command, ref drawData, images);
                break;
            default:
                throw new UnreachableException($"Unsupported draw tag '{drawTag}' reached scene draw-data encoding.");
        }
    }

    /// <summary>
    /// Appends the draw-data payload for a begin-clip record.
    /// </summary>
    private static void AppendBeginClipData(GraphicsOptions options, ref OwnedStream<uint> drawData)
    {
        drawData.Add(PackBlendMode(options));
        drawData.Add(BitcastSingle(Math.Clamp(options.BlendPercentage, 0F, 1F)));
    }

    /// <summary>
    /// Packs the color blend mode and alpha composition mode into one scene word.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackBlendMode(GraphicsOptions options)
        => (MapColorBlendMode(options.ColorBlendingMode) << 8) | MapAlphaCompositionMode(options.AlphaCompositionMode);

    /// <summary>
    /// Maps the managed color blending mode to the GPU scene encoding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MapColorBlendMode(PixelColorBlendingMode mode)
        => mode switch
        {
            PixelColorBlendingMode.Normal => 0U,
            PixelColorBlendingMode.Multiply => 1U,
            PixelColorBlendingMode.Add => 16U,
            PixelColorBlendingMode.Subtract => 17U,
            PixelColorBlendingMode.Screen => 2U,
            PixelColorBlendingMode.Darken => 4U,
            PixelColorBlendingMode.Lighten => 5U,
            PixelColorBlendingMode.Overlay => 3U,
            PixelColorBlendingMode.HardLight => 8U,
            _ => throw new UnreachableException($"Unsupported color blending mode '{mode}'.")
        };

    /// <summary>
    /// Maps the managed alpha composition mode to the GPU scene encoding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MapAlphaCompositionMode(PixelAlphaCompositionMode mode)
        => mode switch
        {
            PixelAlphaCompositionMode.SrcOver => 3U,
            PixelAlphaCompositionMode.Src => 1U,
            PixelAlphaCompositionMode.SrcAtop => 9U,
            PixelAlphaCompositionMode.SrcIn => 5U,
            PixelAlphaCompositionMode.SrcOut => 7U,
            PixelAlphaCompositionMode.Dest => 2U,
            PixelAlphaCompositionMode.DestAtop => 10U,
            PixelAlphaCompositionMode.DestOver => 4U,
            PixelAlphaCompositionMode.DestIn => 6U,
            PixelAlphaCompositionMode.DestOut => 8U,
            PixelAlphaCompositionMode.Clear => 0U,
            PixelAlphaCompositionMode.Xor => 11U,
            _ => throw new UnreachableException($"Unsupported alpha composition mode '{mode}'.")
        };

    /// <summary>
    /// Appends the recolor brush payload.
    /// </summary>
    private static void AppendRecolorData(RecolorBrush brush, ref OwnedStream<uint> drawData)
    {
        drawData.Add(PackPremultipliedColor(brush.SourceColor));
        drawData.Add(PackPremultipliedColor(brush.TargetColor));
        drawData.Add(BitcastSingle(brush.Threshold * 4F));
    }

    /// <summary>
    /// Appends the deferred image payload placeholder and records the patch site.
    /// </summary>
    private static void AppendImageData(
        in CompositionCommand command,
        ref OwnedStream<uint> drawData,
        List<GpuImageDescriptor> images)
    {
        Brush brush = command.Brush;

        // The image payload words are patched once the atlas is built because the
        // uploader is the first place that knows the concrete TPixel texture format.
        int payloadWordOffset = drawData.Count;
        drawData.Add(0);
        drawData.Add(0);
        drawData.Add(0);
        if (brush is ImageBrush imageBrush)
        {
            drawData.Add(BitcastSingle(command.BrushBounds.Left + imageBrush.Offset.X));
            drawData.Add(BitcastSingle(command.BrushBounds.Top + imageBrush.Offset.Y));
        }
        else
        {
            drawData.Add(0);
            drawData.Add(0);
        }

        images.Add(new GpuImageDescriptor(brush, payloadWordOffset));
    }

    /// <summary>
    /// Appends the linear gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendLinearGradientData(
        LinearGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(brush.StartPoint.X));
        drawData.Add(BitcastSingle(brush.StartPoint.Y));
        drawData.Add(BitcastSingle(brush.EndPoint.X));
        drawData.Add(BitcastSingle(brush.EndPoint.Y));
    }

    /// <summary>
    /// Appends the radial gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendRadialGradientData(
        RadialGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        PointF center0;
        float radius0;
        PointF center1;
        float radius1;

        // The shader consumes both one-circle and two-circle radial gradients through the
        // same payload shape, so the one-circle case is normalized to a degenerate inner circle.
        if (brush.IsTwoCircle)
        {
            center0 = brush.Center0;
            radius0 = brush.Radius0;
            center1 = brush.Center1!.Value;
            radius1 = brush.Radius1!.Value;
        }
        else
        {
            center0 = brush.Center0;
            radius0 = 0F;
            center1 = brush.Center0;
            radius1 = brush.Radius0;
        }

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(center0.X));
        drawData.Add(BitcastSingle(center0.Y));
        drawData.Add(BitcastSingle(center1.X));
        drawData.Add(BitcastSingle(center1.Y));
        drawData.Add(BitcastSingle(radius0));
        drawData.Add(BitcastSingle(radius1));
    }

    /// <summary>
    /// Appends the elliptic gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendEllipticGradientData(
        EllipticGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(brush.Center.X));
        drawData.Add(BitcastSingle(brush.Center.Y));
        drawData.Add(BitcastSingle(brush.ReferenceAxisEnd.X));
        drawData.Add(BitcastSingle(brush.ReferenceAxisEnd.Y));
        drawData.Add(BitcastSingle(brush.AxisRatio));
    }

    /// <summary>
    /// Appends the sweep gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendSweepGradientData(
        SweepGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        // Zero-width sweeps are normalized to a full turn so the shader sees a stable range.
        float sweepDegrees = brush.EndAngleDegrees - brush.StartAngleDegrees;
        if (MathF.Abs(sweepDegrees) < 1e-6F)
        {
            sweepDegrees = 360F;
        }

        float t0 = brush.StartAngleDegrees / 360F;
        float t1 = t0 + (sweepDegrees / 360F);

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(brush.Center.X));
        drawData.Add(BitcastSingle(brush.Center.Y));
        drawData.Add(BitcastSingle(t0));
        drawData.Add(BitcastSingle(t1));
    }

    /// <summary>
    /// Appends one packed gradient ramp row sampled across the fixed gradient width.
    /// </summary>
    private static void AppendGradientRamp(ReadOnlySpan<ColorStop> colorStops, ref OwnedStream<uint> gradientPixels)
    {
        for (int x = 0; x < GradientWidth; x++)
        {
            float t = x / (float)(GradientWidth - 1);
            gradientPixels.Add(EvaluateGradientColor(colorStops, t));
        }
    }

    /// <summary>
    /// Evaluates the packed premultiplied gradient color at the normalized parameter.
    /// </summary>
    private static uint EvaluateGradientColor(ReadOnlySpan<ColorStop> colorStops, float t)
    {
        // Walk the stop list once to find the enclosing interval, then interpolate within it.
        ColorStop from = colorStops[0];
        ColorStop to = colorStops[0];
        for (int i = 0; i < colorStops.Length; i++)
        {
            to = colorStops[i];
            if (to.Ratio > t)
            {
                break;
            }

            from = to;
        }

        if (from.Color.Equals(to.Color) || to.Ratio == from.Ratio)
        {
            return PackPremultipliedColor(from.Color);
        }

        float localT = (t - from.Ratio) / (to.Ratio - from.Ratio);
        Vector4 color = Vector4.Lerp(from.Color.ToScaledVector4(), to.Color.ToScaledVector4(), localT);
        return PackPremultipliedColor(Color.FromScaledVector(color));
    }

    /// <summary>
    /// Packs a color into premultiplied RGBA8 scene storage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackPremultipliedColor(Color color)
    {
        Vector4 scaled = color.ToScaledVector4();
        scaled.X *= scaled.W;
        scaled.Y *= scaled.W;
        scaled.Z *= scaled.W;
        return Rgba32.FromScaledVector4(scaled).Rgba;
    }

    /// <summary>
    /// Maps the managed gradient repetition mode to the GPU scene encoding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MapExtendMode(GradientRepetitionMode repetitionMode)
        => repetitionMode switch
        {
            GradientRepetitionMode.Repeat => 1U,
            GradientRepetitionMode.Reflect => 2U,
            _ => 0U
        };
}

/// <summary>
/// Flush-scoped encoded scene payload.
/// </summary>
internal sealed class WebGPUEncodedScene : IDisposable
{
    /// <summary>
    /// Gets the empty encoded scene instance.
    /// </summary>
    public static WebGPUEncodedScene Empty { get; } = new(
        Size.Empty,
        0,
        null,
        0,
        null,
        [],
        0,
        default,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);

    private readonly IMemoryOwner<uint>? sceneDataOwner;
    private readonly IMemoryOwner<uint>? gradientPixelsOwner;
    private readonly List<GpuImageDescriptor> images;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUEncodedScene"/> class.
    /// </summary>
    public WebGPUEncodedScene(
        Size targetSize,
        int infoWordCount,
        IMemoryOwner<uint>? sceneDataOwner,
        int sceneWordCount,
        IMemoryOwner<uint>? gradientPixelsOwner,
        List<GpuImageDescriptor> images,
        int gradientRowCount,
        GpuSceneLayout layout,
        int fillCount,
        int pathCount,
        int lineCount,
        int pathTagByteCount,
        int pathTagWordCount,
        int pathDataWordCount,
        int drawTagCount,
        int drawDataWordCount,
        int transformWordCount,
        int styleWordCount,
        int clipCount,
        int uniqueDefinitionCount,
        int totalBinMembershipCount,
        int totalTileMembershipCount,
        int totalLineSliceCount,
        int tileCountX,
        int tileCountY)
    {
        this.TargetSize = targetSize;
        this.InfoWordCount = infoWordCount;
        this.sceneDataOwner = sceneDataOwner;
        this.gradientPixelsOwner = gradientPixelsOwner;
        this.images = images;
        this.SceneWordCount = sceneWordCount;
        this.GradientRowCount = gradientRowCount;
        this.Layout = layout;
        this.FillCount = fillCount;
        this.PathCount = pathCount;
        this.LineCount = lineCount;
        this.PathTagByteCount = pathTagByteCount;
        this.PathTagWordCount = pathTagWordCount;
        this.PathDataWordCount = pathDataWordCount;
        this.DrawTagCount = drawTagCount;
        this.DrawDataWordCount = drawDataWordCount;
        this.TransformWordCount = transformWordCount;
        this.StyleWordCount = styleWordCount;
        this.ClipCount = clipCount;
        this.UniqueDefinitionCount = uniqueDefinitionCount;
        this.TotalBinMembershipCount = totalBinMembershipCount;
        this.TotalTileMembershipCount = totalTileMembershipCount;
        this.TotalLineSliceCount = totalLineSliceCount;
        this.TileCountX = tileCountX;
        this.TileCountY = tileCountY;
    }

    /// <summary>
    /// Gets the target size for the encoded scene.
    /// </summary>
    public Size TargetSize { get; }

    /// <summary>
    /// Gets the packed scene-word buffer.
    /// </summary>
    public ReadOnlyMemory<uint> SceneData
        => this.sceneDataOwner is null ? ReadOnlyMemory<uint>.Empty : this.sceneDataOwner.Memory[..this.SceneWordCount];

    /// <summary>
    /// Gets the packed gradient-ramp pixels.
    /// </summary>
    public ReadOnlyMemory<uint> GradientPixels
        => this.gradientPixelsOwner is null ? ReadOnlyMemory<uint>.Empty : this.gradientPixelsOwner.Memory[..(this.GradientRowCount * 512)];

    /// <summary>
    /// Gets the deferred image descriptors that must be patched after atlas creation.
    /// </summary>
    public IReadOnlyList<GpuImageDescriptor> Images => this.images;

    /// <summary>
    /// Gets the number of fill records in the encoded scene.
    /// </summary>
    public int FillCount { get; }

    /// <summary>
    /// Gets the total info-word count for the encoded draw stream.
    /// </summary>
    public int InfoWordCount { get; }

    /// <summary>
    /// Gets the total number of uint words in the packed scene buffer.
    /// </summary>
    public int SceneWordCount { get; }

    /// <summary>
    /// Gets the number of gradient-ramp rows.
    /// </summary>
    public int GradientRowCount { get; }

    /// <summary>
    /// Gets the number of encoded paths.
    /// </summary>
    public int PathCount { get; }

    /// <summary>
    /// Gets the number of encoded non-horizontal lines.
    /// </summary>
    public int LineCount { get; }

    /// <summary>
    /// Gets the unpadded path-tag byte count.
    /// </summary>
    public int PathTagByteCount { get; }

    /// <summary>
    /// Gets the padded path-tag word count.
    /// </summary>
    public int PathTagWordCount { get; }

    /// <summary>
    /// Gets the path-data word count.
    /// </summary>
    public int PathDataWordCount { get; }

    /// <summary>
    /// Gets the draw-tag count.
    /// </summary>
    public int DrawTagCount { get; }

    /// <summary>
    /// Gets the draw-data word count.
    /// </summary>
    public int DrawDataWordCount { get; }

    /// <summary>
    /// Gets the transform word count.
    /// </summary>
    public int TransformWordCount { get; }

    /// <summary>
    /// Gets the style word count.
    /// </summary>
    public int StyleWordCount { get; }

    /// <summary>
    /// Gets the clip record count.
    /// </summary>
    public int ClipCount { get; }

    /// <summary>
    /// Gets the number of unique scene definitions.
    /// </summary>
    public int UniqueDefinitionCount { get; }

    /// <summary>
    /// Gets the total bin-membership count.
    /// </summary>
    public int TotalBinMembershipCount { get; }

    /// <summary>
    /// Gets the total tile-membership count.
    /// </summary>
    public int TotalTileMembershipCount { get; }

    /// <summary>
    /// Gets the total line-slice count.
    /// </summary>
    public int TotalLineSliceCount { get; }

    /// <summary>
    /// Gets the tile count on the X axis.
    /// </summary>
    public int TileCountX { get; }

    /// <summary>
    /// Gets the tile count on the Y axis.
    /// </summary>
    public int TileCountY { get; }

    /// <summary>
    /// Gets the total tile count.
    /// </summary>
    public int TileCount => this.TileCountX * this.TileCountY;

    /// <summary>
    /// Gets the packed GPU scene layout.
    /// </summary>
    public GpuSceneLayout Layout { get; }

    /// <summary>
    /// Overwrites one scene word in place.
    /// </summary>
    /// <param name="index">The scene-word index to update.</param>
    /// <param name="value">The replacement word value.</param>
    public void SetSceneWord(int index, uint value)
    {
        if (this.sceneDataOwner is null)
        {
            throw new InvalidOperationException("The scene buffer is not available.");
        }

        this.sceneDataOwner.Memory.Span[index] = value;
    }

    /// <summary>
    /// Releases the owned scene and gradient buffers.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.sceneDataOwner?.Dispose();
        this.gradientPixelsOwner?.Dispose();
    }
}

/// <summary>
/// Describes one deferred image payload patch site in the encoded draw-data stream.
/// </summary>
internal readonly struct GpuImageDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuImageDescriptor"/> struct.
    /// </summary>
    public GpuImageDescriptor(Brush brush, int drawDataWordOffset)
    {
        this.Brush = brush;
        this.DrawDataWordOffset = drawDataWordOffset;
    }

    /// <summary>
    /// Gets the image-producing brush instance.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the draw-data word offset to patch once the image atlas is built.
    /// </summary>
    public int DrawDataWordOffset { get; }
}

/// <summary>
/// Represents one 2D affine transform encoded in the GPU scene format.
/// </summary>
internal readonly struct GpuSceneTransform : IEquatable<GpuSceneTransform>
{
    /// <summary>
    /// Gets the identity transform.
    /// </summary>
    public static readonly GpuSceneTransform Identity = new(1F, 0F, 0F, 1F, 0F, 0F);

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneTransform"/> struct.
    /// </summary>
    public GpuSceneTransform(float m11, float m12, float m21, float m22, float tx, float ty)
    {
        this.M11 = m11;
        this.M12 = m12;
        this.M21 = m21;
        this.M22 = m22;
        this.Tx = tx;
        this.Ty = ty;
    }

    /// <summary>
    /// Gets the first row, first column element.
    /// </summary>
    public float M11 { get; }

    /// <summary>
    /// Gets the first row, second column element.
    /// </summary>
    public float M12 { get; }

    /// <summary>
    /// Gets the second row, first column element.
    /// </summary>
    public float M21 { get; }

    /// <summary>
    /// Gets the second row, second column element.
    /// </summary>
    public float M22 { get; }

    /// <summary>
    /// Gets the X translation component.
    /// </summary>
    public float Tx { get; }

    /// <summary>
    /// Gets the Y translation component.
    /// </summary>
    public float Ty { get; }

    /// <summary>
    /// Gets the translation vector.
    /// </summary>
    public Vector2 Translation => new(this.Tx, this.Ty);

    /// <summary>
    /// Returns a value indicating whether this transform matches another transform exactly.
    /// </summary>
    public bool Equals(GpuSceneTransform other)
        => this.M11 == other.M11
        && this.M12 == other.M12
        && this.M21 == other.M21
        && this.M22 == other.M22
        && this.Tx == other.Tx
        && this.Ty == other.Ty;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GpuSceneTransform other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(this.M11, this.M12, this.M21, this.M22, this.Tx, this.Ty);
}

/// <summary>
/// Owns one growable contiguous stream backed by allocator storage.
/// </summary>
internal ref struct OwnedStream<T>
    where T : unmanaged
{
    private readonly MemoryAllocator allocator;
    private IMemoryOwner<T> owner;
    private Span<T> span;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnedStream{T}"/> struct.
    /// </summary>
    /// <param name="allocator">The allocator used for backing storage growth.</param>
    /// <param name="initialCapacity">The requested initial item capacity.</param>
    public OwnedStream(MemoryAllocator allocator, int initialCapacity)
    {
        this.allocator = allocator;
        this.owner = allocator.Allocate<T>(Math.Max(initialCapacity, 16));
        this.span = this.owner.Memory.Span;
        this.Count = 0;
    }

    /// <summary>
    /// Gets the number of written items in the stream.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets a reference to the item at the specified written index.
    /// </summary>
    /// <param name="index">The index to access.</param>
    /// <returns>A reference to the requested item.</returns>
    public ref T this[int index] => ref this.span[index];

    /// <summary>
    /// Gets the contiguous written prefix of the stream.
    /// </summary>
    public readonly ReadOnlySpan<T> WrittenSpan => this.span[..this.Count];

    /// <summary>
    /// Appends one item to the stream.
    /// </summary>
    /// <param name="value">The item to append.</param>
    public void Add(T value)
    {
        this.EnsureCapacity(this.Count + 1);
        this.span[this.Count++] = value;
    }

    /// <summary>
    /// Ensures the stream can append the requested additional item count without growing again.
    /// </summary>
    /// <param name="additionalCount">The number of additional items that will be appended.</param>
    public void EnsureAdditionalCapacity(int additionalCount)
    {
        if (additionalCount <= 0)
        {
            return;
        }

        this.EnsureCapacity(this.Count + additionalCount);
    }

    /// <summary>
    /// Resets the logical item count without reallocating the backing storage.
    /// </summary>
    /// <param name="count">The new logical item count.</param>
    public void SetCount(int count) => this.Count = count;

    /// <summary>
    /// Disposes the current owner and clears the stream state.
    /// </summary>
    public void Dispose()
    {
        this.owner.Dispose();
        this.span = default;
        this.Count = 0;
    }

    /// <summary>
    /// Detaches the current owner from the stream without copying.
    /// </summary>
    /// <returns>The detached owner.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<T> DetachOwner()
    {
        IMemoryOwner<T> detached = this.owner;
        this.span = default;
        this.Count = 0;
        return detached;
    }

    /// <summary>
    /// Grows the backing storage to satisfy the required total capacity.
    /// </summary>
    /// <param name="requiredCapacity">The total item capacity that must be available.</param>
    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.span.Length)
        {
            return;
        }

        // Grow by doubling for amortized append cost, but never below the exact
        // required size when a caller has already pre-reserved a larger single jump.
        IMemoryOwner<T> next = this.allocator.Allocate<T>(Math.Max(requiredCapacity, this.span.Length * 2));
        this.span[..this.Count].CopyTo(next.Memory.Span);
        this.owner.Dispose();
        this.owner = next;
        this.span = next.Memory.Span;
    }
}

#pragma warning restore SA1201
