// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Phase-1 scene-model types are grouped together in one file for now.

using System.Buffers;
using System.Collections.Generic;
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
    private const uint StyleFlagsFill = 0x40000000U;

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

    public static bool TryValidateBrushSupport(IReadOnlyList<CompositionCommand> commands, out string? error)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            CompositionCommand command = commands[i];
            if (!command.IsVisible)
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

    private ref struct SupportedSubsetSceneEncoding
    {
        private bool hasLastStyle;
        private bool gradientPixelsDetached;
        private uint lastStyle0;
        private uint lastStyle1;

        private SupportedSubsetSceneEncoding(MemoryAllocator allocator, int commandCount)
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
            this.TotalTileMembershipCount = 0;
            this.GradientRowCount = 0;
            this.hasLastStyle = false;
            this.gradientPixelsDetached = false;
            this.lastStyle0 = 0;
            this.lastStyle1 = 0;

            this.PathTags.Add(PathTagTransform);
            AppendIdentityTransform(ref this.Transforms);
        }

        public OwnedStream<byte> PathTags;

        public OwnedStream<uint> PathData;

        public OwnedStream<uint> DrawTags;

        public OwnedStream<uint> DrawData;

        public OwnedStream<uint> Transforms;

        public OwnedStream<uint> Styles;

        public OwnedStream<uint> GradientPixels;

        public List<GpuImageDescriptor> Images;

        public int FillCount { get; private set; }

        public int PathCount { get; private set; }

        public int LineCount { get; private set; }

        public int InfoWordCount { get; private set; }

        public int TotalTileMembershipCount { get; private set; }

        public int GradientRowCount { get; private set; }

        public readonly bool IsEmpty => this.FillCount == 0;

        public static SupportedSubsetSceneEncoding Create(
            IReadOnlyList<CompositionCommand> commands,
            in Rectangle targetBounds,
            MemoryAllocator allocator)
        {
            SupportedSubsetSceneEncoding encoding = new(allocator, commands.Count);
            encoding.Build(commands, targetBounds);
            return encoding;
        }

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

        public void MarkGradientPixelsDetached() => this.gradientPixelsDetached = true;

        private void Build(IReadOnlyList<CompositionCommand> commands, in Rectangle targetBounds)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                this.Append(commands[i], targetBounds);
            }
        }

        private void Append(in CompositionCommand command, in Rectangle targetBounds)
        {
            if (!command.IsVisible)
            {
                return;
            }

            IPath preparedPath = command.PreparedPath
                ?? throw new InvalidOperationException("Commands must be prepared before GPU scene encoding.");

            uint drawTag = GetDrawTag(command);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            (uint style0, uint style1) = GetFillStyle(command.RasterizerOptions.IntersectionRule);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;

            if (!this.hasLastStyle || style0 != this.lastStyle0 || style1 != this.lastStyle1)
            {
                this.PathTags.Add(PathTagStyle);
                this.Styles.Add(style0);
                this.Styles.Add(style1);
            }

            int encodedPathCount = EncodePath(
                command,
                preparedPath,
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

            Rectangle destinationRegion = command.DestinationRegion;
            int tileMinX = Math.Max(0, destinationRegion.Left / TileWidth);
            int tileMinY = Math.Max(0, destinationRegion.Top / TileHeight);
            int tileMaxX = Math.Max(tileMinX + 1, DivideRoundUp(destinationRegion.Right, TileWidth));
            int tileMaxY = Math.Max(tileMinY + 1, DivideRoundUp(destinationRegion.Bottom, TileHeight));
            this.TotalTileMembershipCount += (tileMaxX - tileMinX) * (tileMaxY - tileMinY);
        }
    }

    private static class SupportedSubsetSceneResolver
    {
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
                (uint)encoding.FillCount,
                (uint)encoding.PathCount,
                0U,
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
                    0,
                    0,
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

    private static int EncodePath(
        in CompositionCommand command,
        IPath preparedPath,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out int pathSegmentCount)
    {
        Rectangle interest = command.RasterizerOptions.Interest;
        float samplingOffset = command.RasterizerOptions.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        float translateY = command.DestinationRegion.Y - command.SourceOffset.Y;
        pathSegmentCount = 0;
        lineCount = 0;

        foreach (ISimplePath subPath in preparedPath.Flatten())
        {
            ReadOnlySpan<PointF> points = subPath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            int segmentCount = subPath.IsClosed ? points.Length : points.Length - 1;
            if (segmentCount == 0)
            {
                continue;
            }

            // Emit moveto for the first point of each subpath.
            PointF startPoint = points[0];
            float firstX = startPoint.X + samplingOffset;
            float firstY = startPoint.Y + samplingOffset;
            pathData.Add(BitcastSingle(firstX));
            pathData.Add(BitcastSingle(firstY));
            float currentX = firstX;
            float currentY = firstY;

            for (int j = 0; j < segmentCount; j++)
            {
                PointF p0 = points[j];
                PointF p1 = points[(j + 1) % points.Length];
                if (PointsEqual(p0, p1))
                {
                    continue;
                }

                float translatedEndX = p1.X + samplingOffset;
                float translatedEndY = p1.Y + samplingOffset;
                pathData.Add(BitcastSingle(translatedEndX));
                pathData.Add(BitcastSingle(translatedEndY));
                pathTags.Add(PathTagLineToF32);
                pathSegmentCount++;
                currentX = translatedEndX;
                currentY = translatedEndY;

                float y0 = ((p0.Y - interest.Top) + samplingOffset) + translateY;
                float y1 = ((p1.Y - interest.Top) + samplingOffset) + translateY;
                int fy0 = (int)MathF.Round(y0 * FixedOne);
                int fy1 = (int)MathF.Round(y1 * FixedOne);
                if (fy0 != fy1)
                {
                    lineCount++;
                }
            }

            // Close the subpath.
            if (!PointsEqual(firstX, firstY, currentX, currentY))
            {
                pathData.Add(BitcastSingle(firstX));
                pathData.Add(BitcastSingle(firstY));
                pathTags.Add(PathTagLineToF32);
                pathSegmentCount++;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendIdentityTransform(ref OwnedStream<uint> transforms)
        => AppendTransform(GpuSceneTransform.Identity, ref transforms);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (uint Style0, uint Style1) GetFillStyle(IntersectionRule intersectionRule)
        => (intersectionRule == IntersectionRule.EvenOdd ? StyleFlagsFill : 0U, 0U);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PointsEqual(PointF a, PointF b)
        => a.X == b.X && a.Y == b.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PointsEqual(float ax, float ay, float bx, float by)
        => ax == bx && ay == by;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BitcastSingle(float value)
        => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    private static int CountLineTileSlices(float x0, float y0, float x1, float y1)
    {
        if (x0 == x1 && y0 == y1)
        {
            return 0;
        }

        bool isDown = y1 >= y0;
        float startX = isDown ? x0 : x1;
        float startY = isDown ? y0 : y1;
        float endX = isDown ? x1 : x0;
        float endY = isDown ? y1 : y0;
        float s0x = startX / TileWidth;
        float s0y = startY / TileHeight;
        float s1x = endX / TileWidth;
        float s1y = endY / TileHeight;

        int spanX = Math.Max((int)MathF.Ceiling(MathF.Max(s0x, s1x)) - (int)MathF.Floor(MathF.Min(s0x, s1x)), 1);
        int spanY = Math.Max((int)MathF.Ceiling(MathF.Max(s0y, s1y)) - (int)MathF.Floor(MathF.Min(s0y, s1y)), 1);
        return checked((spanX - 1) + spanY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment)
        => value + ((alignment - (value % alignment)) % alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackSolidColor(SolidBrush solidBrush)
    {
        Vector4 color = solidBrush.Color.ToScaledVector4();
        color.X *= color.W;
        color.Y *= color.W;
        color.Z *= color.W;
        return Rgba32.FromScaledVector4(color).Rgba;
    }

    private static void AppendDrawData(
        in CompositionCommand command,
        uint drawTag,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        List<GpuImageDescriptor> images,
        ref int gradientRowCount)
    {
        Brush brush = command.Brush;
        switch (drawTag)
        {
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

    private static void AppendRecolorData(RecolorBrush brush, ref OwnedStream<uint> drawData)
    {
        drawData.Add(PackPremultipliedColor(brush.SourceColor));
        drawData.Add(PackPremultipliedColor(brush.TargetColor));
        drawData.Add(BitcastSingle(brush.Threshold * 4F));
    }

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

    private static void AppendSweepGradientData(
        SweepGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

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

    private static void AppendGradientRamp(ReadOnlySpan<ColorStop> colorStops, ref OwnedStream<uint> gradientPixels)
    {
        for (int x = 0; x < GradientWidth; x++)
        {
            float t = x / (float)(GradientWidth - 1);
            gradientPixels.Add(EvaluateGradientColor(colorStops, t));
        }
    }

    private static uint EvaluateGradientColor(ReadOnlySpan<ColorStop> colorStops, float t)
    {
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackPremultipliedColor(Color color)
    {
        Vector4 scaled = color.ToScaledVector4();
        scaled.X *= scaled.W;
        scaled.Y *= scaled.W;
        scaled.Z *= scaled.W;
        return Rgba32.FromScaledVector4(scaled).Rgba;
    }

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

    public Size TargetSize { get; }

    public ReadOnlyMemory<uint> SceneData
        => this.sceneDataOwner is null ? ReadOnlyMemory<uint>.Empty : this.sceneDataOwner.Memory[..this.SceneWordCount];

    public ReadOnlyMemory<uint> GradientPixels
        => this.gradientPixelsOwner is null ? ReadOnlyMemory<uint>.Empty : this.gradientPixelsOwner.Memory[..(this.GradientRowCount * 512)];

    public IReadOnlyList<GpuImageDescriptor> Images => this.images;

    public int FillCount { get; }

    public int InfoWordCount { get; }

    public int SceneWordCount { get; }

    public int GradientRowCount { get; }

    public int PathCount { get; }

    public int LineCount { get; }

    public int PathTagByteCount { get; }

    public int PathTagWordCount { get; }

    public int PathDataWordCount { get; }

    public int DrawTagCount { get; }

    public int DrawDataWordCount { get; }

    public int TransformWordCount { get; }

    public int StyleWordCount { get; }

    public int ClipCount { get; }

    public int UniqueDefinitionCount { get; }

    public int TotalBinMembershipCount { get; }

    public int TotalTileMembershipCount { get; }

    public int TotalLineSliceCount { get; }

    public int TileCountX { get; }

    public int TileCountY { get; }

    public int TileCount => this.TileCountX * this.TileCountY;

    public GpuSceneLayout Layout { get; }

    public void SetSceneWord(int index, uint value)
    {
        if (this.sceneDataOwner is null)
        {
            throw new InvalidOperationException("The scene buffer is not available.");
        }

        this.sceneDataOwner.Memory.Span[index] = value;
    }

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

internal readonly struct GpuImageDescriptor
{
    public GpuImageDescriptor(Brush brush, int drawDataWordOffset)
    {
        this.Brush = brush;
        this.DrawDataWordOffset = drawDataWordOffset;
    }

    public Brush Brush { get; }

    public int DrawDataWordOffset { get; }
}

internal readonly struct GpuSceneTransform : IEquatable<GpuSceneTransform>
{
    public static readonly GpuSceneTransform Identity = new(1F, 0F, 0F, 1F, 0F, 0F);

    public GpuSceneTransform(float m11, float m12, float m21, float m22, float tx, float ty)
    {
        this.M11 = m11;
        this.M12 = m12;
        this.M21 = m21;
        this.M22 = m22;
        this.Tx = tx;
        this.Ty = ty;
    }

    public float M11 { get; }

    public float M12 { get; }

    public float M21 { get; }

    public float M22 { get; }

    public float Tx { get; }

    public float Ty { get; }

    public Vector2 Translation => new(this.Tx, this.Ty);

    public bool Equals(GpuSceneTransform other)
        => this.M11 == other.M11
        && this.M12 == other.M12
        && this.M21 == other.M21
        && this.M22 == other.M22
        && this.Tx == other.Tx
        && this.Ty == other.Ty;

    public override bool Equals(object? obj) => obj is GpuSceneTransform other && this.Equals(other);

    public override int GetHashCode() => HashCode.Combine(this.M11, this.M12, this.M21, this.M22, this.Tx, this.Ty);
}

internal ref struct OwnedStream<T>
    where T : unmanaged
{
    private readonly MemoryAllocator allocator;
    private IMemoryOwner<T> owner;
    private Span<T> span;

    public OwnedStream(MemoryAllocator allocator, int initialCapacity)
    {
        this.allocator = allocator;
        this.owner = allocator.Allocate<T>(Math.Max(initialCapacity, 16));
        this.span = this.owner.Memory.Span;
        this.Count = 0;
    }

    public int Count { get; private set; }

    public ref T this[int index] => ref this.span[index];

    public readonly ReadOnlySpan<T> WrittenSpan => this.span[..this.Count];

    public void Add(T value)
    {
        this.EnsureCapacity(this.Count + 1);
        this.span[this.Count++] = value;
    }

    public void SetCount(int count) => this.Count = count;

    public void Dispose()
    {
        this.owner.Dispose();
        this.span = default;
        this.Count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<T> DetachOwner()
    {
        IMemoryOwner<T> detached = this.owner;
        this.span = default;
        this.Count = 0;
        return detached;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.span.Length)
        {
            return;
        }

        IMemoryOwner<T> next = this.allocator.Allocate<T>(Math.Max(requiredCapacity, this.span.Length * 2));
        this.span[..this.Count].CopyTo(next.Memory.Span);
        this.owner.Dispose();
        this.owner = next;
        this.span = next.Memory.Span;
    }
}

#pragma warning restore SA1201
