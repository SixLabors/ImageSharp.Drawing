// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Scene-model types are grouped together in one file.

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Builds the flush-scoped scene payload consumed by the staged WebGPU rasterizer.
/// </summary>
/// <remarks>
/// Commands are lowered into Vello-style scene streams (path tags, path data, draw tags,
/// draw data, transforms, styles) plus auxiliary gradient and image payloads. Large batches
/// are planned and encoded in parallel over contiguous command-range partitions whose
/// boundaries are snapped forward so no layer scope is cut and whose open clip scopes are
/// replayed per partition; the partitions are then concatenated in timeline order into one
/// packed scene buffer.
/// </remarks>
internal static class WebGPUSceneEncoder
{
    /// <summary>
    /// The pixel width of every sampled gradient ramp row uploaded to the gradient texture.
    /// Shared with <see cref="WebGPUEncodedScene.GradientPixels"/> so the packed buffer slice
    /// always matches the encoder's ramp stride.
    /// </summary>
    public const int GradientWidth = 512;

    // Gradient ramps use RGBA16Float: each texel occupies two uint words.
    internal const int GradientRowWordCount = GradientWidth * 2;

    /// <summary>
    /// The word count of the path-gradient payload header: center x/y, max distance, and four center-color components.
    /// </summary>
    private const int PathGradientHeaderWordCount = 7;

    /// <summary>
    /// The word count of one path-gradient edge record: start x/y, end x/y, and four components for each endpoint color.
    /// </summary>
    private const int PathGradientEdgeWordCount = 12;

    /// <summary>
    /// The word count of one RecolorBrush auxiliary record: two f32 colors and the squared-distance threshold.
    /// </summary>
    private const int RecolorDataWordCount = 9;

    /// <summary>
    /// The word count of one encoded style record. Must match STYLE_SIZE_IN_WORDS in pathtag.wgsl.
    /// </summary>
    private const int StyleWordCount = 10;

    /// <summary>
    /// The word count of one encoded transform: 2x2 linear part, translation, and the m14/m24/m44 projective terms.
    /// </summary>
    private const int TransformWordCount = 9;

    /// <summary>
    /// The tile width in pixels used by binning, coarse, and fine. Must match TILE_WIDTH in config.wgsl.
    /// </summary>
    private const int TileWidth = 16;

    /// <summary>
    /// The tile height in pixels used by binning, coarse, and fine. Must match TILE_HEIGHT in config.wgsl.
    /// </summary>
    public const int TileHeight = 16;

    /// <summary>
    /// Half the length of the synthetic horizontal segment substituted for point-like strokes so caps
    /// still render a dot. The full segment length equals <see cref="StrokeMicroSegmentEpsilon"/>, keeping
    /// the substitute just above the micro-segment collapse threshold.
    /// </summary>
    private const float PointStrokeSegmentHalfLength = 1F / 128F;

    /// <summary>
    /// The stroke micro-segment collapse threshold in pixels. Mirrors the CPU stroker's 1/64 px
    /// preprocessing in <c>DefaultRasterizer.StrokeLinearizer</c> so GPU strokes consume the same
    /// segment list the CPU stroker strokes.
    /// </summary>
    private const float StrokeMicroSegmentEpsilon = 1F / 64F;

    /// <summary>
    /// Vello's clip blend word, (MIX_CLIP &lt;&lt; 8) | COMPOSE_SRC_OVER. Values must match blend.wgsl.
    /// </summary>
    private const uint ClipBlendMode = (128U << 8) | 3U;

    /// <summary>
    /// High bit of the clip blend word marking a Difference clip, an ImageSharp extension over
    /// Vello's clip records. Must match CLIP_DIFFERENCE_MASK_BIT in clip.wgsl and ptcl.wgsl.
    /// </summary>
    private const uint ClipDifferenceMaskBit = 0x80000000U;

    /// <summary>
    /// Bit of the clip blend word marking a hard-edge (aliased) clip mask.
    /// Must match CLIP_HARD_MASK_BIT in drawtag.wgsl.
    /// </summary>
    private const uint ClipHardMaskBit = 0x40000000U;

    /// <summary>
    /// Bit of the clip blend word marking an isolated group (a layer). Isolated groups seed
    /// transparent on the GPU and composite back as a unit; canvas clips leave this clear and
    /// render as coverage masks over the live tile content, matching the CPU backend's
    /// per-draw clip masking. Must match CLIP_ISOLATED_MASK_BIT in drawtag.wgsl.
    /// </summary>
    private const uint ClipIsolatedMaskBit = 0x20000000U;

    /// <summary>
    /// Default options used when encoding a layer's bounding-rectangle clip mask. The layer's real
    /// graphics options apply when the layer composites back, not when its mask is bounded.
    /// </summary>
    private static readonly GraphicsOptions LayerMaskGraphicsOptions = new();

    /// <summary>
    /// Tags packed into the byte path-tag stream consumed by the WebGPU shaders.
    /// </summary>
    /// <remarks>
    /// The low two bits store the segment type; the remaining bits are flags/count markers.
    /// Values must match the PATH_TAG_* constants in pathtag.wgsl.
    /// </remarks>
    [Flags]
    private enum PathTag : byte
    {
        None = 0,
        LineTo = 1 << 0,
        QuadTo = 1 << 1,
        CubicTo = LineTo | QuadTo,
        SubpathEnd = 1 << 2,
        F32 = 1 << 3,
        LineToF32 = F32 | LineTo,
        QuadToF32 = F32 | QuadTo,
        Path = 1 << 4,
        Transform = 1 << 5,
        Style = 1 << 6
    }

    /// <summary>
    /// Flags packed into the first style word consumed by the WebGPU shaders.
    /// </summary>
    /// <remarks>
    /// Cap styles are stored as two two-bit fields: end caps in bits 24-25 and start caps
    /// in bits 26-27. The flatten shader shifts the start-cap field down before decoding it.
    /// </remarks>
    [Flags]
    private enum StyleFlags : uint
    {
        None = 0U,
        JoinMiterRevert = 1U << 22,
        JoinMiterRound = 1U << 23,
        EndCapSquare = 1U << 24,
        EndCapRound = 1U << 25,
        StartCapSquare = EndCapSquare << 2,
        StartCapRound = EndCapRound << 2,
        JoinMiter = 1U << 28,
        JoinRound = 1U << 29,
        Fill = 1U << 30,
        Stroke = 1U << 31
    }

    /// <summary>
    /// Converts one path tag to its byte-stream wire representation.
    /// </summary>
    /// <param name="tag">The path tag to pack.</param>
    /// <returns>The packed tag byte.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PackPathTag(PathTag tag) => (byte)tag;

    /// <summary>
    /// Encodes composition commands into flush-scoped scene buffers.
    /// </summary>
    /// <param name="scene">The command batch to encode.</param>
    /// <param name="targetBounds">The root target bounds used for target-local coordinate conversion.</param>
    /// <param name="allocator">The allocator used for temporary and packed scene storage.</param>
    /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism used for encoder planning and partitioned encoding.</param>
    /// <param name="encodedScene">Receives the encoded scene on success.</param>
    /// <param name="error">Receives the staged-scene support failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the scene encoded successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryEncode(
        DrawingCommandBatch scene,
        in Rectangle targetBounds,
        MemoryAllocator allocator,
        int maxDegreeOfParallelism,
        out WebGPUEncodedScene encodedScene,
        out string? error)
    {
        if (scene.CommandCount == 0)
        {
            encodedScene = WebGPUEncodedScene.Empty;
            error = null;
            return true;
        }

        int firstTargetRowBandIndex = targetBounds.Top / TileHeight;
        int lastTargetRowBandIndex = (targetBounds.Bottom - 1) / TileHeight;
        int targetRowCount = (lastTargetRowBandIndex - firstTargetRowBandIndex) + 1;
        int commandCount = scene.CommandCount;

        // Clip scopes do not force a single range: each partition replays the clip scopes open at
        // its boundary and closes its own, so contiguous partitions concatenate into one valid
        // push/pop clip sequence. Layer scopes composite their contents as one group with the
        // layer's graphics options when they close, so a partition boundary may not cut through
        // an open layer; the prescan snaps boundaries forward to the next point where no layer
        // scope is open instead of serializing the whole scene.
        int partitionCount = GetPartitionCount(maxDegreeOfParallelism, commandCount, targetRowCount);
        int[]? partitionBoundaries = null;
        CompositionCommand[][]? partitionSeedClips = null;
        if (partitionCount > 1 && (scene.HasClipControls || scene.HasLayers))
        {
            CreatePartitionCommandRanges(scene, partitionCount, out partitionBoundaries, out partitionSeedClips);
        }

        SceneEncodingPlan plan = SceneEncodingPlan.Create(scene, maxDegreeOfParallelism, partitionCount, partitionBoundaries);
        if (partitionCount > 1)
        {
            Rectangle targetRectangle = targetBounds;
            SceneEncodingPartition?[] partitions = new SceneEncodingPartition?[partitionCount];
            bool[] failed = new bool[partitionCount];
            string?[] errors = new string?[partitionCount];

            try
            {
                _ = Parallel.For(
                    0,
                    partitionCount,
                    CreateParallelOptions(maxDegreeOfParallelism, partitionCount),
                    (partitionIndex, loopState) =>
                    {
                        // Match the CPU retained scene builder: partitions are contiguous command ranges,
                        // and the final merge appends them by partition index to preserve timeline order.
                        // Boundary-snapped ranges can be empty; empty ranges skip clip seeding so they
                        // do not emit balanced-but-useless clip records.
                        int commandStart = partitionBoundaries is null ? (partitionIndex * commandCount) / partitionCount : partitionBoundaries[partitionIndex];
                        int commandEnd = partitionBoundaries is null ? ((partitionIndex + 1) * commandCount) / partitionCount : partitionBoundaries[partitionIndex + 1];
                        SceneEncodingPlan partitionPlan = plan.GetPartitionPlan(partitionIndex);
                        SupportedSubsetSceneEncoding partitionEncoding = new(allocator, targetRectangle, partitionPlan);

                        try
                        {
                            CompositionCommand[]? seedClips = commandStart == commandEnd ? null : partitionSeedClips?[partitionIndex];
                            if (!partitionEncoding.TryBuild(scene, partitionPlan, commandStart, commandEnd, seedClips, out string? partitionError))
                            {
                                failed[partitionIndex] = true;
                                errors[partitionIndex] = partitionError;
                                loopState.Stop();
                                return;
                            }

                            partitions[partitionIndex] = SceneEncodingPartition.Detach(ref partitionEncoding);
                        }
                        finally
                        {
                            partitionEncoding.Dispose();
                        }
                    });
            }
            catch
            {
                DisposePartitions(partitions);
                throw;
            }

            for (int i = 0; i < failed.Length; i++)
            {
                if (failed[i])
                {
                    DisposePartitions(partitions);
                    encodedScene = WebGPUEncodedScene.Empty;
                    error = errors[i];
                    return false;
                }
            }

            int fillCount = 0;
            for (int i = 0; i < partitions.Length; i++)
            {
                fillCount += partitions[i]!.FillCount;
            }

            // Rasterization mode and the aliased coverage threshold are carried per fill, so partitions
            // impose no flush-wide rasterization constraint. These scene-wide values are retained only
            // as encoded-scene metadata and are no longer consumed by the fine pass.
            RasterizationMode fineRasterizationMode = RasterizationMode.Antialiased;
            float fineCoverageThreshold = 0F;

            if (fillCount == 0)
            {
                DisposePartitions(partitions);
                encodedScene = WebGPUEncodedScene.Empty;
                error = null;
                return true;
            }

            try
            {
                encodedScene = SupportedSubsetSceneResolver.Resolve(partitions, targetBounds, allocator, fineRasterizationMode, fineCoverageThreshold);
                error = null;
                return true;
            }
            finally
            {
                DisposePartitions(partitions);
            }
        }

        SupportedSubsetSceneEncoding encoding = new(allocator, targetBounds, plan);
        try
        {
            if (!encoding.TryBuild(scene, plan, out error))
            {
                encodedScene = WebGPUEncodedScene.Empty;
                return false;
            }

            if (encoding.IsEmpty)
            {
                encodedScene = WebGPUEncodedScene.Empty;
                error = null;
                return true;
            }

            encodedScene = SupportedSubsetSceneResolver.Resolve(ref encoding, targetBounds, allocator, []);
            error = null;
            return true;
        }
        finally
        {
            encoding.Dispose();
        }
    }

    /// <summary>
    /// Encodes a prepared command batch containing Apply into ordered WebGPU scene operations.
    /// </summary>
    /// <param name="scene">The prepared command batch.</param>
    /// <param name="targetBounds">The target bounds used for target-local coordinate conversion.</param>
    /// <param name="allocator">The allocator used for temporary and packed scene storage.</param>
    /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism used by leaf scene encoding.</param>
    /// <param name="encodedScene">Receives the encoded ordered scene on success.</param>
    /// <param name="error">Receives the failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the scene encoded successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryEncodeOrdered(
        DrawingCommandBatch scene,
        in Rectangle targetBounds,
        MemoryAllocator allocator,
        int maxDegreeOfParallelism,
        out WebGPUEncodedScene encodedScene,
        out string? error)
    {
        // Ordered scenes interleave draw ranges with Apply/layer barriers whose results feed later
        // draws, so encoding stays sequential; the parallelism argument is accepted only for
        // signature parity with TryEncode.
        _ = maxDegreeOfParallelism;
        SceneEncodingPlan plan = SceneEncodingPlan.CreateDefault(scene.CommandCount);
        SupportedSubsetSceneEncoding encoding = new(allocator, targetBounds, plan);
        List<WebGPUSceneOperation> operations = [];
        int nextLayerTargetId = 1;

        try
        {
            SceneEncodingCheckpoint rangeStart = encoding.BeginPackedIndependentRange(targetBounds, ClipBlendMode, replayActiveClips: true);
            if (!TryEncodeOrderedOperations(scene, 0, scene.CommandCount, targetBounds, 0, ref encoding, operations, ref rangeStart, ref nextLayerTargetId, out error))
            {
                encodedScene = WebGPUEncodedScene.Empty;
                return false;
            }

            WebGPUSceneRange finalRange = encoding.EndPackedIndependentRange(targetBounds, rangeStart, closeActiveClips: true);
            if (finalRange.FillCount != 0)
            {
                operations.Add(new WebGPUSceneOperation(finalRange));
            }

            if (encoding.IsEmpty)
            {
                encodedScene = WebGPUEncodedScene.Empty;
                error = null;
                return true;
            }

            FinalizeOrderedOperations(operations);
            encodedScene = SupportedSubsetSceneResolver.Resolve(ref encoding, targetBounds, allocator, [.. operations]);
            error = null;
            return true;
        }
        finally
        {
            encoding.Dispose();
        }
    }

    /// <summary>
    /// Encodes one command range into ordered scene operations, recursing into scoped layers and
    /// splitting the packed draw stream at every Apply barrier.
    /// </summary>
    /// <param name="scene">The prepared command batch.</param>
    /// <param name="commandStart">The inclusive command start index.</param>
    /// <param name="commandEnd">The exclusive command end index.</param>
    /// <param name="targetBounds">The bounds of the target the range renders into.</param>
    /// <param name="targetId">The stable identity of the target the range renders into.</param>
    /// <param name="encoding">The shared mutable scene encoding.</param>
    /// <param name="operations">Receives the ordered scene operations.</param>
    /// <param name="rangeStart">The checkpoint of the currently open packed range; updated whenever the range is split.</param>
    /// <param name="nextLayerTargetId">The next stable target identity available for a scoped layer.</param>
    /// <param name="error">Receives the failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the range encoded successfully.</returns>
    private static bool TryEncodeOrderedOperations(
        DrawingCommandBatch scene,
        int commandStart,
        int commandEnd,
        Rectangle targetBounds,
        int targetId,
        ref SupportedSubsetSceneEncoding encoding,
        List<WebGPUSceneOperation> operations,
        ref SceneEncodingCheckpoint rangeStart,
        ref int nextLayerTargetId,
        out string? error)
    {
        IReadOnlyList<CompositionSceneCommand> commands = scene.Commands;

        for (int i = commandStart; i < commandEnd; i++)
        {
            CompositionSceneCommand sceneCommand = commands[i];
            LinearGeometry? geometry = null;

            if (sceneCommand is PathCompositionSceneCommand pathCommand)
            {
                CompositionCommand command = pathCommand.Command;
                if (command.Kind == CompositionCommandKind.BeginLayer && command.RequiresScopedApply)
                {
                    WebGPUSceneRange pendingRange = encoding.EndPackedIndependentRange(targetBounds, rangeStart, closeActiveClips: true);
                    if (pendingRange.FillCount != 0)
                    {
                        operations.Add(new WebGPUSceneOperation(pendingRange));
                    }

                    int layerEnd = FindMatchingLayerEnd(scene, i, commandEnd);
                    if (layerEnd < 0)
                    {
                        error = "The WebGPU Apply scene encoder could not find the matching EndLayer command.";
                        return false;
                    }

                    Rectangle layerBounds = command.LayerBounds;
                    int layerTargetId = nextLayerTargetId++;
                    operations.Add(new WebGPUSceneOperation(layerBounds, layerTargetId, targetId));

                    SceneEncodingCheckpoint childStart = encoding.BeginPackedIndependentRange(layerBounds, ClipBlendMode, replayActiveClips: true);
                    if (!TryEncodeOrderedOperations(scene, i + 1, layerEnd, layerBounds, layerTargetId, ref encoding, operations, ref childStart, ref nextLayerTargetId, out error))
                    {
                        return false;
                    }

                    WebGPUSceneRange childRange = encoding.EndPackedIndependentRange(layerBounds, childStart, closeActiveClips: true);
                    if (childRange.FillCount != 0)
                    {
                        operations.Add(new WebGPUSceneOperation(childRange));
                    }

                    // Children are rendered into the temporary layer with the active clip stack
                    // already replayed. Replaying the same clips again while compositing the
                    // layer would clip antialias/filter coverage twice and diverge from the CPU
                    // retained renderer's scoped-layer execution.
                    SceneEncodingCheckpoint compositeStart = encoding.BeginPackedIndependentRange(targetBounds, ClipBlendMode, replayActiveClips: false);
                    if (!TryAppendLayerCompositeCommand(command, targetBounds, ref encoding, out error))
                    {
                        return false;
                    }

                    WebGPUSceneRange compositeRange = encoding.EndPackedIndependentRange(targetBounds, compositeStart, closeActiveClips: false);
                    operations.Add(new WebGPUSceneOperation(compositeRange, layerTargetId, targetId));

                    rangeStart = encoding.BeginPackedIndependentRange(targetBounds, ClipBlendMode, replayActiveClips: true);
                    i = layerEnd;
                    continue;
                }

                if (command.Kind == CompositionCommandKind.Apply)
                {
                    uint applyClipBlendMode = PackBlendMode(command.DrawingOptions.GraphicsOptions);
                    AddRenderRange(targetBounds, ref encoding, operations, ref rangeStart, applyClipBlendMode);

                    if (!TryAppendApplyCommand(command, targetBounds, ref encoding, rangeStart, out WebGPUSceneOperation? applyOperation, out error))
                    {
                        return false;
                    }

                    if (applyOperation is not null)
                    {
                        operations.Add(applyOperation);
                        rangeStart = encoding.BeginPackedIndependentRange(targetBounds, ClipBlendMode, replayActiveClips: true);
                    }

                    continue;
                }
            }

            if (sceneCommand is PathCompositionSceneCommand fillCommand)
            {
                CompositionCommand command = fillCommand.Command;
                if (command.Kind == CompositionCommandKind.FillLayer)
                {
                    geometry = command.SourcePath.ToLinearGeometry(MatrixUtilities.GetScale(command.Transform));
                }

                if (!encoding.TryAppend(command, geometry, out error))
                {
                    return false;
                }
            }
            else if (sceneCommand is StrokePathCompositionSceneCommand strokePathCommand)
            {
                geometry = strokePathCommand.Command.SourcePath.ToLinearGeometry(MatrixUtilities.GetScale(strokePathCommand.Command.Transform));
                if (!encoding.TryAppend(strokePathCommand.Command, geometry, out error))
                {
                    return false;
                }
            }
            else if (sceneCommand is LineSegmentCompositionSceneCommand lineSegmentCommand)
            {
                if (!encoding.TryAppend(lineSegmentCommand.Command, out error))
                {
                    return false;
                }
            }
            else if (sceneCommand is PolylineCompositionSceneCommand polylineCommand)
            {
                geometry = LinearGeometry.CreateOpenPolyline(polylineCommand.Command.SourcePoints, MatrixUtilities.GetScale(polylineCommand.Command.Transform));
                if (!encoding.TryAppend(polylineCommand.Command, geometry, out error))
                {
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Closes the currently open packed range, emitting it as an operation when it produced fills,
    /// and opens the next range with the supplied clip blend mode.
    /// </summary>
    /// <param name="targetBounds">The bounds of the target the ranges render into.</param>
    /// <param name="encoding">The shared mutable scene encoding.</param>
    /// <param name="operations">Receives the closed range when it produced fills.</param>
    /// <param name="rangeStart">The checkpoint of the open packed range; replaced with the new range's checkpoint.</param>
    /// <param name="nextClipBlendMode">The blend mode used when the next range closes replayed clips.</param>
    private static void AddRenderRange(
        Rectangle targetBounds,
        ref SupportedSubsetSceneEncoding encoding,
        List<WebGPUSceneOperation> operations,
        ref SceneEncodingCheckpoint rangeStart,
        uint nextClipBlendMode)
    {
        WebGPUSceneRange range = encoding.EndPackedIndependentRange(targetBounds, rangeStart, closeActiveClips: true);

        if (range.FillCount != 0)
        {
            operations.Add(new WebGPUSceneOperation(range));
        }

        rangeStart = encoding.BeginPackedIndependentRange(targetBounds, nextClipBlendMode, replayActiveClips: true);
    }

    /// <summary>
    /// Encodes one Apply command as an external-texture fill and wraps the pending draw range in an
    /// Apply scene item that carries the canvas source rectangle to sample at render time.
    /// </summary>
    /// <param name="source">The Apply composition command.</param>
    /// <param name="targetBounds">The bounds of the target the Apply composites into.</param>
    /// <param name="encoding">The shared mutable scene encoding.</param>
    /// <param name="drawStart">The checkpoint of the packed range that owns the Apply fill.</param>
    /// <param name="operation">Receives the Apply operation, or <see langword="null"/> when the source rectangle is empty.</param>
    /// <param name="error">Receives the failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the command encoded or was skipped as empty.</returns>
    private static bool TryAppendApplyCommand(
        in CompositionCommand source,
        Rectangle targetBounds,
        ref SupportedSubsetSceneEncoding encoding,
        in SceneEncodingCheckpoint drawStart,
        out WebGPUSceneOperation? operation,
        out string? error)
    {
        RectangleF rawOutputBounds = RectangleF.Transform(source.ApplyOutputBounds, source.DrawingOptions.Transform);
        Rectangle outputRect = Rectangle.Intersect(source.ApplyCanvasBounds, ToConservativeBounds(rawOutputBounds));

        if (outputRect.Width <= 0 || outputRect.Height <= 0)
        {
            operation = null;
            error = null;
            return true;
        }

        Point brushOffset = new(
            outputRect.X - (int)MathF.Floor(rawOutputBounds.Left),
            outputRect.Y - (int)MathF.Floor(rawOutputBounds.Top));

        IWebGPUShaderEffectSource? shaderEffect = source.ApplyEffect as IWebGPUShaderEffectSource;
        bool hasNativeEffect = shaderEffect is not null;
        GpuImageSource imageSource = hasNativeEffect
            ? new GpuImageSource(new Size(outputRect.Width, outputRect.Height), brushOffset, WrapMode.Repeat, WrapMode.Repeat, WebGPUShaderEffectWorkingTexture.Descriptor)
            : new GpuImageSource(new Size(outputRect.Width, outputRect.Height), brushOffset, WrapMode.Repeat, WrapMode.Repeat);

        RasterizerOptions rasterizerOptions = source.RasterizerOptions;
        LinearGeometry geometry = source.SourcePath.ToLinearGeometry(MatrixUtilities.GetScale(source.Transform));
        if (!encoding.TryAppendExternalTextureFill(
            source.SourcePath,
            imageSource,
            source.DrawingOptions,
            in rasterizerOptions,
            source.DestinationOffset,
            targetBounds,
            geometry,
            out error))
        {
            operation = null;
            return false;
        }

        WebGPUSceneRange drawRange = encoding.EndPackedIndependentRange(targetBounds, drawStart, closeActiveClips: true);
        operation = hasNativeEffect
            ? new WebGPUSceneOperation(new WebGPUShaderEffectSceneItem(shaderEffect!, outputRect, outputRect, source.ApplyWriteBackOffset, drawRange))
            : new WebGPUSceneOperation(new WebGPUApplySceneItem(source.ApplyOperation, source.ApplyEffect, outputRect, outputRect, source.ApplyWriteBackOffset, drawRange));
        error = null;
        return true;
    }

    /// <summary>
    /// Encodes the fill that composites a scoped layer's temporary texture back into the target,
    /// applying the layer's graphics options at composite time.
    /// </summary>
    /// <param name="beginCommand">The begin-layer command that opened the scoped layer.</param>
    /// <param name="targetBounds">The bounds of the target the layer composites into.</param>
    /// <param name="encoding">The shared mutable scene encoding.</param>
    /// <param name="error">Receives the failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the composite fill encoded successfully.</returns>
    private static bool TryAppendLayerCompositeCommand(
        in CompositionCommand beginCommand,
        Rectangle targetBounds,
        ref SupportedSubsetSceneEncoding encoding,
        out string? error)
    {
        Rectangle destinationBounds = beginCommand.LayerBounds;
        DrawingOptions options = new()
        {
            GraphicsOptions = beginCommand.LayerOptions
        };

        RasterizerOptions rasterizerOptions = CreateRasterizerOptions(destinationBounds, options);
        RectanglePolygon path = new(0, 0, destinationBounds.Width, destinationBounds.Height);
        LinearGeometry geometry = path.ToLinearGeometry(MatrixUtilities.GetScale(options.Transform));
        return encoding.TryAppendExternalTextureFill(
            path,
            new GpuImageSource(new Size(destinationBounds.Width, destinationBounds.Height), Point.Empty, WrapMode.Repeat, WrapMode.Repeat),
            options,
            in rasterizerOptions,
            destinationBounds.Location,
            targetBounds,
            geometry,
            out error);
    }

    /// <summary>
    /// Finds the EndLayer command that closes the BeginLayer at <paramref name="layerBegin"/> by depth counting.
    /// </summary>
    /// <param name="scene">The prepared command batch.</param>
    /// <param name="layerBegin">The command index of the BeginLayer command.</param>
    /// <param name="commandEnd">The exclusive command index bounding the search.</param>
    /// <returns>The matching EndLayer command index, or -1 when the scope is unbalanced within the range.</returns>
    private static int FindMatchingLayerEnd(DrawingCommandBatch scene, int layerBegin, int commandEnd)
    {
        int depth = 0;
        for (int i = layerBegin; i < commandEnd; i++)
        {
            if (scene.Commands[i] is not PathCompositionSceneCommand pathCommand)
            {
                continue;
            }

            switch (pathCommand.Command.Kind)
            {
                case CompositionCommandKind.BeginLayer:
                    depth++;
                    break;
                case CompositionCommandKind.EndLayer:
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>
    /// Completes immutable Apply-group and barrier-capacity metadata after the ordered stream has
    /// been flattened into explicit layer entry and exit operations.
    /// </summary>
    /// <param name="operations">The ordered operations to finalize in place.</param>
    private static void FinalizeOrderedOperations(List<WebGPUSceneOperation> operations)
    {
        int pendingStatusCapacity = 0;
        int denseApplyIndex = 0;

        for (int i = 0; i < operations.Count; i++)
        {
            WebGPUSceneOperation operation = operations[i];
            switch (operation.Kind)
            {
                case WebGPUSceneOperationKind.RenderRange:
                case WebGPUSceneOperationKind.EndLayer:
                    pendingStatusCapacity = checked(pendingStatusCapacity + operation.Range.MaximumStatusRecordCount);
                    break;

                case WebGPUSceneOperationKind.Apply:
                    int groupCount = 1;

                    // Every candidate read must be disjoint from every earlier conservative write.
                    // Pairwise testing represents the true union of rectangles; replacing it with
                    // one bounding rectangle would incorrectly split groups across empty gaps.
                    for (int candidateIndex = i + 1; candidateIndex < operations.Count; candidateIndex++)
                    {
                        WebGPUSceneOperation candidateOperation = operations[candidateIndex];
                        if (candidateOperation.Kind != WebGPUSceneOperationKind.Apply)
                        {
                            break;
                        }

                        WebGPUApplySceneItem candidate = candidateOperation.Apply!;
                        Rectangle candidateRead = candidate.InputRect;
                        candidateRead.Offset(-candidate.ReadOffset.X, -candidate.ReadOffset.Y);
                        bool intersectsEarlierWrite = false;

                        for (int previousIndex = i; previousIndex < candidateIndex; previousIndex++)
                        {
                            Rectangle previousWrite = operations[previousIndex].Apply!.OutputRect;
                            if (candidateRead.Left < previousWrite.Right &&
                                candidateRead.Right > previousWrite.Left &&
                                candidateRead.Top < previousWrite.Bottom &&
                                candidateRead.Bottom > previousWrite.Top)
                            {
                                intersectsEarlierWrite = true;
                                break;
                            }
                        }

                        if (intersectsEarlierWrite)
                        {
                            break;
                        }

                        groupCount++;
                    }

                    operation.SetApplyGroup(groupCount, pendingStatusCapacity);

                    // The Apply draw ranges execute after this barrier and become the complete
                    // pending transaction seen by the next group or end-of-flush validation.
                    pendingStatusCapacity = 0;
                    for (int operationIndex = i; operationIndex < i + groupCount; operationIndex++)
                    {
                        operations[operationIndex].SetApplyIndex(denseApplyIndex++);
                        pendingStatusCapacity = checked(pendingStatusCapacity + operations[operationIndex].Apply!.DrawRange.MaximumStatusRecordCount);
                    }

                    i += groupCount - 1;
                    break;

                case WebGPUSceneOperationKind.ShaderEffect:
                    // A native effect consumes the committed source produced by every preceding
                    // range. Its write-back becomes the first range in the next transaction.
                    pendingStatusCapacity = operation.ShaderEffect!.DrawRange.MaximumStatusRecordCount;
                    break;
            }
        }
    }

    /// <summary>
    /// Creates rasterizer options for an encoder-synthesized fill from the drawing options it composites with.
    /// </summary>
    /// <param name="interest">The absolute raster interest bounds of the synthesized fill.</param>
    /// <param name="options">The drawing options supplying antialiasing and intersection state.</param>
    /// <returns>The created rasterizer options.</returns>
    private static RasterizerOptions CreateRasterizerOptions(Rectangle interest, DrawingOptions options)
    {
        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        return new RasterizerOptions(
            interest,
            options.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);
    }

    /// <summary>
    /// Rounds floating-point bounds outward to the smallest enclosing integer rectangle.
    /// </summary>
    /// <param name="bounds">The bounds to round.</param>
    /// <returns>The enclosing integer rectangle.</returns>
    private static Rectangle ToConservativeBounds(RectangleF bounds)
        => Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

    /// <summary>
    /// Expands path bounds by the stroke joins and caps that can extend past the path geometry.
    /// </summary>
    /// <param name="bounds">The path geometry bounds before stroke inflation.</param>
    /// <param name="pen">The pen that defines stroke width, joins, caps, and miter limit.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The inflated stroke bounds.</returns>
    private static RectangleF GetStrokeBounds(RectangleF bounds, Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;
        float joinInflate = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * Math.Max(pen.StrokeOptions.MiterLimit, 1D)),
            _ => halfWidth
        };

        float capInflate = pen.StrokeOptions.LineCap == LineCap.Square
            ? halfWidth * MathF.Sqrt(2F)
            : halfWidth;

        float inflate = MathF.Max(joinInflate, capInflate);

        bounds.Inflate(new SizeF(inflate, inflate));
        return bounds;
    }

    /// <summary>
    /// Immutable snapshot of every scene stream length and record count, captured at a stream position
    /// so two checkpoints delimit one renderable range.
    /// </summary>
    private readonly struct SceneEncodingCheckpoint
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneEncodingCheckpoint"/> struct.
        /// </summary>
        /// <param name="pathTagByteCount">The path-tag stream length in bytes.</param>
        /// <param name="pathDataWordCount">The path-data stream length in words.</param>
        /// <param name="drawTagCount">The draw-tag stream length.</param>
        /// <param name="drawDataWordCount">The draw-data stream length in words.</param>
        /// <param name="transformWordCount">The transform stream length in words.</param>
        /// <param name="styleWordCount">The style stream length in words.</param>
        /// <param name="infoWordCount">The info-word count implied by the emitted draw tags.</param>
        /// <param name="pathCount">The number of paths emitted so far.</param>
        /// <param name="clipCount">The number of clip records emitted so far.</param>
        /// <param name="fillCount">The number of fill records emitted so far.</param>
        /// <param name="lineCount">The number of non-horizontal line segments emitted so far.</param>
        /// <param name="estimatedPathRowCount">The accumulated sparse path-row estimate.</param>
        /// <param name="estimatedTileCrossings">The accumulated tile-crossing upper bound.</param>
        /// <param name="estimatedBinFootprint">The accumulated per-(draw, bin) record upper bound.</param>
        public SceneEncodingCheckpoint(
            int pathTagByteCount,
            int pathDataWordCount,
            int drawTagCount,
            int drawDataWordCount,
            int transformWordCount,
            int styleWordCount,
            int infoWordCount,
            int pathCount,
            int clipCount,
            int fillCount,
            int lineCount,
            long estimatedPathRowCount,
            long estimatedTileCrossings,
            long estimatedBinFootprint)
        {
            this.PathTagByteCount = pathTagByteCount;
            this.PathDataWordCount = pathDataWordCount;
            this.DrawTagCount = drawTagCount;
            this.DrawDataWordCount = drawDataWordCount;
            this.TransformWordCount = transformWordCount;
            this.StyleWordCount = styleWordCount;
            this.InfoWordCount = infoWordCount;
            this.PathCount = pathCount;
            this.ClipCount = clipCount;
            this.FillCount = fillCount;
            this.LineCount = lineCount;
            this.EstimatedPathRowCount = estimatedPathRowCount;
            this.EstimatedTileCrossings = estimatedTileCrossings;
            this.EstimatedBinFootprint = estimatedBinFootprint;
        }

        /// <summary>
        /// Gets the path-tag stream length in bytes.
        /// </summary>
        public int PathTagByteCount { get; }

        /// <summary>
        /// Gets the path-data stream length in words.
        /// </summary>
        public int PathDataWordCount { get; }

        /// <summary>
        /// Gets the draw-tag stream length.
        /// </summary>
        public int DrawTagCount { get; }

        /// <summary>
        /// Gets the draw-data stream length in words.
        /// </summary>
        public int DrawDataWordCount { get; }

        /// <summary>
        /// Gets the transform stream length in words.
        /// </summary>
        public int TransformWordCount { get; }

        /// <summary>
        /// Gets the style stream length in words.
        /// </summary>
        public int StyleWordCount { get; }

        /// <summary>
        /// Gets the info-word count implied by the draw tags emitted so far.
        /// </summary>
        public int InfoWordCount { get; }

        /// <summary>
        /// Gets the number of paths emitted so far.
        /// </summary>
        public int PathCount { get; }

        /// <summary>
        /// Gets the number of clip records emitted so far.
        /// </summary>
        public int ClipCount { get; }

        /// <summary>
        /// Gets the number of fill records emitted so far.
        /// </summary>
        public int FillCount { get; }

        /// <summary>
        /// Gets the number of non-horizontal line segments emitted so far.
        /// </summary>
        public int LineCount { get; }

        /// <summary>
        /// Gets the accumulated sparse path-row estimate.
        /// </summary>
        public long EstimatedPathRowCount { get; }

        /// <summary>
        /// Gets the accumulated tile-crossing upper bound.
        /// </summary>
        public long EstimatedTileCrossings { get; }

        /// <summary>
        /// Gets the accumulated per-(draw, bin) record upper bound.
        /// </summary>
        public long EstimatedBinFootprint { get; }
    }

    /// <summary>
    /// Disposes any partition encodings that were produced before the parallel operation completed.
    /// </summary>
    /// <param name="partitions">The partition slots to dispose; unproduced slots are <see langword="null"/>.</param>
    private static void DisposePartitions(SceneEncodingPartition?[] partitions)
    {
        for (int i = 0; i < partitions.Length; i++)
        {
            partitions[i]?.Dispose();
        }
    }

    /// <summary>
    /// Encoding plan produced before the mutable scene streams are allocated.
    /// </summary>
    private readonly struct SceneEncodingPlan
    {
        private readonly LinearGeometry?[]? geometries;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneEncodingPlan"/> struct.
        /// </summary>
        /// <param name="pathTagCapacity">The initial byte capacity for path tags.</param>
        /// <param name="pathDataWordCapacity">The initial word capacity for path data.</param>
        /// <param name="drawTagCapacity">The initial draw-tag capacity.</param>
        /// <param name="drawDataWordCapacity">The initial word capacity for draw data.</param>
        /// <param name="transformWordCapacity">The initial word capacity for transforms.</param>
        /// <param name="styleWordCapacity">The initial word capacity for styles.</param>
        /// <param name="gradientPixelCapacity">The initial gradient-pixel capacity.</param>
        /// <param name="pathGradientDataWordCapacity">The initial path-gradient data capacity.</param>
        /// <param name="geometries">The prepared geometry per command, or <see langword="null"/> when planning was skipped.</param>
        /// <param name="partitionPlans">The per-partition plans, or <see langword="null"/> for single-partition encodes.</param>
        public SceneEncodingPlan(
            int pathTagCapacity,
            int pathDataWordCapacity,
            int drawTagCapacity,
            int drawDataWordCapacity,
            int transformWordCapacity,
            int styleWordCapacity,
            int gradientPixelCapacity,
            int pathGradientDataWordCapacity,
            LinearGeometry?[]? geometries,
            SceneEncodingPlan[]? partitionPlans)
        {
            this.PathTagCapacity = pathTagCapacity;
            this.PathDataWordCapacity = pathDataWordCapacity;
            this.DrawTagCapacity = drawTagCapacity;
            this.DrawDataWordCapacity = drawDataWordCapacity;
            this.TransformWordCapacity = transformWordCapacity;
            this.StyleWordCapacity = styleWordCapacity;
            this.GradientPixelCapacity = gradientPixelCapacity;
            this.PathGradientDataWordCapacity = pathGradientDataWordCapacity;
            this.geometries = geometries;
            this.PartitionPlans = partitionPlans;
        }

        /// <summary>
        /// Gets the initial byte capacity for path tags.
        /// </summary>
        public int PathTagCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for path data.
        /// </summary>
        public int PathDataWordCapacity { get; }

        /// <summary>
        /// Gets the initial draw-tag capacity.
        /// </summary>
        public int DrawTagCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for draw data.
        /// </summary>
        public int DrawDataWordCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for transforms.
        /// </summary>
        public int TransformWordCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for styles.
        /// </summary>
        public int StyleWordCapacity { get; }

        /// <summary>
        /// Gets the initial gradient-pixel capacity.
        /// </summary>
        public int GradientPixelCapacity { get; }

        /// <summary>
        /// Gets the initial path-gradient data capacity.
        /// </summary>
        public int PathGradientDataWordCapacity { get; }

        /// <summary>
        /// Gets per-partition capacity plans produced by the parallel planning pass.
        /// </summary>
        public SceneEncodingPlan[]? PartitionPlans { get; }

        /// <summary>
        /// Creates an encoding plan for the supplied command batch.
        /// </summary>
        /// <param name="scene">The command batch to plan.</param>
        /// <param name="maxDegreeOfParallelism">The maximum parallel worker count.</param>
        /// <param name="partitionCount">The number of command-range partitions.</param>
        /// <param name="partitionBoundaries">The snapped range boundaries, or <see langword="null"/> for uniform ranges.</param>
        /// <returns>The created encoding plan.</returns>
        public static SceneEncodingPlan Create(
            DrawingCommandBatch scene,
            int maxDegreeOfParallelism,
            int partitionCount,
            int[]? partitionBoundaries)
        {
            int commandCount = scene.CommandCount;
            if (partitionCount <= 1)
            {
                return CreateDefault(commandCount);
            }

            LinearGeometry?[] geometries = new LinearGeometry?[commandCount];
            SceneCapacityEstimate[] estimates = new SceneCapacityEstimate[partitionCount];

            _ = Parallel.For(
                0,
                partitionCount,
                CreateParallelOptions(maxDegreeOfParallelism, partitionCount),
                partitionIndex =>
                {
                    int commandStart = partitionBoundaries is null ? (partitionIndex * commandCount) / partitionCount : partitionBoundaries[partitionIndex];
                    int commandEnd = partitionBoundaries is null ? ((partitionIndex + 1) * commandCount) / partitionCount : partitionBoundaries[partitionIndex + 1];
                    SceneCapacityEstimate estimate = default;

                    for (int i = commandStart; i < commandEnd; i++)
                    {
                        estimate.Add(EstimateCommand(scene.Commands[i], i, geometries));
                    }

                    estimates[partitionIndex] = estimate;
                });

            SceneCapacityEstimate total = SceneCapacityEstimate.CreateInitial();
            SceneEncodingPlan[] partitionPlans = new SceneEncodingPlan[partitionCount];
            for (int i = 0; i < estimates.Length; i++)
            {
                total.Add(estimates[i]);

                int commandStart = partitionBoundaries is null ? (i * commandCount) / partitionCount : partitionBoundaries[i];
                int commandEnd = partitionBoundaries is null ? ((i + 1) * commandCount) / partitionCount : partitionBoundaries[i + 1];
                partitionPlans[i] = estimates[i].ToPlan(commandEnd - commandStart, geometries, null);
            }

            return total.ToPlan(commandCount, geometries, partitionPlans);
        }

        /// <summary>
        /// Gets prepared geometry for a command when the parallel plan computed it.
        /// </summary>
        /// <param name="commandIndex">The command index.</param>
        /// <returns>The prepared geometry, or <see langword="null"/> when no geometry was prepared.</returns>
        public LinearGeometry? GetGeometry(int commandIndex)
            => this.geometries?[commandIndex];

        /// <summary>
        /// Gets the stream capacity plan for one contiguous command-range partition.
        /// </summary>
        /// <param name="partitionIndex">The partition index.</param>
        /// <returns>The partition plan.</returns>
        public readonly SceneEncodingPlan GetPartitionPlan(int partitionIndex)
            => this.PartitionPlans is null ? this : this.PartitionPlans[partitionIndex];

        /// <summary>
        /// Creates the fallback plan used when no per-command planning pass ran, sizing streams
        /// by fixed per-command multipliers.
        /// </summary>
        /// <param name="commandCount">The number of commands covered by the plan.</param>
        /// <returns>The heuristic default plan.</returns>
        public static SceneEncodingPlan CreateDefault(int commandCount)
            => new(
                Math.Max(commandCount * 8, 256),
                Math.Max(commandCount * 16, 256),
                Math.Max(commandCount, 16),
                Math.Max(commandCount, 16),
                16,
                Math.Max(commandCount * StyleWordCount, 16),
                16,
                16,
                null,
                null);
    }

    /// <summary>
    /// Accumulates initial stream capacities for one large-batch encoder plan.
    /// </summary>
    private struct SceneCapacityEstimate
    {
        /// <summary>
        /// The estimated path-tag byte count.
        /// </summary>
        public long PathTagCount;

        /// <summary>
        /// The estimated path-data word count.
        /// </summary>
        public long PathDataWordCount;

        /// <summary>
        /// The estimated draw-tag count.
        /// </summary>
        public long DrawTagCount;

        /// <summary>
        /// The estimated draw-data word count.
        /// </summary>
        public long DrawDataWordCount;

        /// <summary>
        /// The estimated transform word count.
        /// </summary>
        public long TransformWordCount;

        /// <summary>
        /// The estimated style word count.
        /// </summary>
        public long StyleWordCount;

        /// <summary>
        /// The estimated gradient-ramp pixel count.
        /// </summary>
        public long GradientPixelCount;

        /// <summary>
        /// The estimated path-gradient payload word count.
        /// </summary>
        public long PathGradientDataWordCount;

        /// <summary>
        /// Creates the initial estimate for the transform emitted by every scene.
        /// </summary>
        /// <returns>The initial estimate.</returns>
        public static SceneCapacityEstimate CreateInitial()
            => new()
            {
                PathTagCount = 1,
                TransformWordCount = WebGPUSceneEncoder.TransformWordCount
            };

        /// <summary>
        /// Adds another estimate into this accumulator.
        /// </summary>
        /// <param name="other">The estimate to add.</param>
        public void Add(in SceneCapacityEstimate other)
        {
            this.PathTagCount += other.PathTagCount;
            this.PathDataWordCount += other.PathDataWordCount;
            this.DrawTagCount += other.DrawTagCount;
            this.DrawDataWordCount += other.DrawDataWordCount;
            this.TransformWordCount += other.TransformWordCount;
            this.StyleWordCount += other.StyleWordCount;
            this.GradientPixelCount += other.GradientPixelCount;
            this.PathGradientDataWordCount += other.PathGradientDataWordCount;
        }

        /// <summary>
        /// Adds capacity for one encoded fill path.
        /// </summary>
        /// <param name="geometryInfo">The fill geometry information.</param>
        /// <param name="brush">The fill brush.</param>
        public void AddFill(LinearGeometryInfo geometryInfo, Brush brush)
        {
            uint drawTag = GetDrawTag(brush);
            this.PathTagCount += geometryInfo.SegmentCount + 3L;
            this.PathDataWordCount += (geometryInfo.ContourCount * 2L) + (geometryInfo.SegmentCount * 2L);
            this.AddDrawPayload(drawTag, brush);
        }

        /// <summary>
        /// Adds capacity for one explicit begin-clip command.
        /// </summary>
        /// <param name="descriptor">The clip descriptor opened by the command.</param>
        public void AddBeginClip(DrawingClipDescriptor descriptor)
        {
            int rectangleCount = GetRectangleClipCount(in descriptor);

            // The encoder resets clip commands to identity before emitting descriptor
            // geometry so a preceding draw transform cannot leak into the clip stack.
            this.PathTagCount++;
            this.TransformWordCount += WebGPUSceneEncoder.TransformWordCount;

            if (rectangleCount > 0)
            {
                this.PathTagCount += (rectangleCount * 4L) + 2L;
                this.PathDataWordCount += rectangleCount * 10L;
            }
            else if (descriptor.Kind == DrawingClipKind.Path)
            {
                LinearGeometry clipGeometry = descriptor.ToLinearGeometry(out Matrix4x4 descriptorTransform);
                this.PathTagCount += clipGeometry.Info.SegmentCount + 2L;
                this.PathDataWordCount += clipGeometry.Info.SegmentCount == 0
                    ? 4L
                    : (clipGeometry.Info.ContourCount * 2L) + (clipGeometry.Info.SegmentCount * 2L);

                if (descriptorTransform != Matrix4x4.Identity)
                {
                    // Clip descriptors share the path stream with ordinary draws. Count the
                    // transform transition explicitly so a path clip cannot leave a residual
                    // transform active for later draws.
                    this.PathTagCount++;
                    this.TransformWordCount += WebGPUSceneEncoder.TransformWordCount;
                }
            }
            else
            {
                this.PathTagCount += 2L;
                this.PathDataWordCount += 4L;
            }

            this.DrawTagCount++;
            this.DrawDataWordCount += 2;
            this.StyleWordCount += WebGPUSceneEncoder.StyleWordCount;
        }

        /// <summary>
        /// Adds capacity for one explicit end-clip command.
        /// </summary>
        public void AddEndClip()
        {
            this.PathTagCount++;
            this.DrawTagCount++;
        }

        /// <summary>
        /// Adds capacity for one encoded stroked path.
        /// </summary>
        /// <param name="geometry">The stroke geometry.</param>
        /// <param name="brush">The stroke brush.</param>
        public void AddStroke(LinearGeometry geometry, Brush brush)
        {
            uint drawTag = GetDrawTag(brush);
            int markerWordCount = 0;

            for (int i = 0; i < geometry.Contours.Count; i++)
            {
                markerWordCount += geometry.Contours[i].IsClosed ? 2 : 4;
            }

            this.PathTagCount += geometry.Info.SegmentCount + geometry.Info.ContourCount + 3L;
            this.PathDataWordCount += (geometry.Info.ContourCount * 2L) + (geometry.Info.SegmentCount * 2L) + markerWordCount;
            this.AddDrawPayload(drawTag, brush);
        }

        /// <summary>
        /// Adds capacity for one encoded explicit two-point stroke.
        /// </summary>
        /// <param name="brush">The stroke brush.</param>
        public void AddOpenSegmentStroke(Brush brush)
        {
            uint drawTag = GetDrawTag(brush);
            this.PathTagCount += 4;
            this.PathDataWordCount += 8;
            this.AddDrawPayload(drawTag, brush);
        }

        /// <summary>
        /// Adds capacity for one begin-layer marker.
        /// </summary>
        public void AddBeginLayer()
        {
            this.PathTagCount += 6;
            this.PathDataWordCount += 10;
            this.DrawTagCount++;
            this.DrawDataWordCount += 2;
            this.StyleWordCount += WebGPUSceneEncoder.StyleWordCount;
        }

        /// <summary>
        /// Adds capacity for one end-layer marker.
        /// </summary>
        public void AddEndLayer()
        {
            this.PathTagCount++;
            this.DrawTagCount++;
        }

        /// <summary>
        /// Converts the accumulated counts into an encoder plan.
        /// </summary>
        /// <param name="commandCount">The number of commands covered by the plan.</param>
        /// <param name="geometries">The prepared geometry array.</param>
        /// <param name="partitionPlans">The per-partition plans.</param>
        /// <returns>The created encoder plan.</returns>
        public readonly SceneEncodingPlan ToPlan(
            int commandCount,
            LinearGeometry?[] geometries,
            SceneEncodingPlan[]? partitionPlans)
        {
            SceneEncodingPlan defaults = SceneEncodingPlan.CreateDefault(commandCount);

            return new SceneEncodingPlan(
                Math.Max(defaults.PathTagCapacity, checked((int)this.PathTagCount)),
                Math.Max(defaults.PathDataWordCapacity, checked((int)this.PathDataWordCount)),
                Math.Max(defaults.DrawTagCapacity, checked((int)this.DrawTagCount)),
                Math.Max(defaults.DrawDataWordCapacity, checked((int)this.DrawDataWordCount)),
                Math.Max(defaults.TransformWordCapacity, checked((int)this.TransformWordCount)),
                Math.Max(defaults.StyleWordCapacity, checked((int)this.StyleWordCount)),
                Math.Max(defaults.GradientPixelCapacity, checked((int)this.GradientPixelCount)),
                Math.Max(defaults.PathGradientDataWordCapacity, checked((int)this.PathGradientDataWordCount)),
                geometries,
                partitionPlans);
        }

        /// <summary>
        /// Adds capacity for the draw tag, draw data, transform, style, and brush payload shared by every draw record.
        /// </summary>
        /// <param name="drawTag">The draw tag selected for the brush.</param>
        /// <param name="brush">The brush that determines auxiliary payload sizes.</param>
        private void AddDrawPayload(uint drawTag, Brush brush)
        {
            this.DrawTagCount++;
            this.DrawDataWordCount += GetDrawDataWordCount(drawTag);
            this.TransformWordCount += WebGPUSceneEncoder.TransformWordCount;
            this.StyleWordCount += WebGPUSceneEncoder.StyleWordCount;

            if (DrawTagUsesGradientRamp(drawTag))
            {
                this.GradientPixelCount += GradientRowWordCount;
            }

            this.PathGradientDataWordCount += GetPathGradientDataWordCount(brush);
        }
    }

    /// <summary>
    /// Estimates one command and stores prepared geometry for the sequential append phase.
    /// </summary>
    /// <param name="command">The command to estimate.</param>
    /// <param name="commandIndex">The command's index in the batch.</param>
    /// <param name="geometries">Receives the prepared geometry at <paramref name="commandIndex"/> when the command flattens a path.</param>
    /// <returns>The capacity estimate for the command; zero when the command is unsupported.</returns>
    private static SceneCapacityEstimate EstimateCommand(
        CompositionSceneCommand command,
        int commandIndex,
        LinearGeometry?[] geometries)
    {
        SceneCapacityEstimate estimate = default;

        if (command is PathCompositionSceneCommand pathCommand)
        {
            CompositionCommand composition = pathCommand.Command;
            switch (composition.Kind)
            {
                case CompositionCommandKind.FillLayer:
                    if (!IsSupportedBrush(composition.Brush))
                    {
                        return estimate;
                    }

                    LinearGeometry fillGeometry = composition.SourcePath.ToLinearGeometry(MatrixUtilities.GetScale(composition.Transform));
                    geometries[commandIndex] = fillGeometry;
                    estimate.AddFill(fillGeometry.Info, composition.Brush);
                    return estimate;

                case CompositionCommandKind.BeginLayer:
                    estimate.AddBeginLayer();
                    return estimate;

                case CompositionCommandKind.EndLayer:
                    estimate.AddEndLayer();
                    return estimate;

                case CompositionCommandKind.BeginClip:
                    estimate.AddBeginClip(composition.ClipDescriptor);
                    return estimate;

                case CompositionCommandKind.EndClip:
                    estimate.AddEndClip();
                    return estimate;
            }
        }

        if (command is StrokePathCompositionSceneCommand strokePathCommand)
        {
            StrokePathCommand composition = strokePathCommand.Command;
            if (!IsSupportedBrush(composition.Brush))
            {
                return estimate;
            }

            LinearGeometry strokeGeometry = composition.SourcePath.ToLinearGeometry(MatrixUtilities.GetScale(composition.Transform));
            geometries[commandIndex] = strokeGeometry;
            estimate.AddStroke(strokeGeometry, composition.Brush);
            return estimate;
        }

        if (command is LineSegmentCompositionSceneCommand lineSegmentCommand)
        {
            StrokeLineSegmentCommand composition = lineSegmentCommand.Command;
            if (IsSupportedBrush(composition.Brush))
            {
                estimate.AddOpenSegmentStroke(composition.Brush);
            }

            return estimate;
        }

        if (command is not PolylineCompositionSceneCommand polylineCommand)
        {
            return estimate;
        }

        StrokePolylineCommand polyline = polylineCommand.Command;
        if (!IsSupportedBrush(polyline.Brush))
        {
            return estimate;
        }

        LinearGeometry polylineGeometry = LinearGeometry.CreateOpenPolyline(polyline.SourcePoints, MatrixUtilities.GetScale(polyline.Transform));
        geometries[commandIndex] = polylineGeometry;
        estimate.AddStroke(polylineGeometry, polyline.Brush);
        return estimate;
    }

    /// <summary>
    /// Computes the number of useful partitions using the same limits as the retained CPU scene builder.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">The configured maximum parallelism; -1 selects the processor count.</param>
    /// <param name="workItemCount">The number of work items available to split.</param>
    /// <param name="secondaryLimit">An additional cap, here the number of target tile rows.</param>
    /// <returns>The partition count.</returns>
    private static int GetPartitionCount(
        int maxDegreeOfParallelism,
        int workItemCount,
        int secondaryLimit)
        => Math.Min(
            maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism,
            Math.Min(workItemCount, secondaryLimit));

    /// <summary>
    /// Creates the parallel options for an already-sized partitioned encoder operation.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">The configured maximum parallelism.</param>
    /// <param name="partitionCount">The number of partitions to process.</param>
    /// <returns>The parallel options.</returns>
    private static ParallelOptions CreateParallelOptions(int maxDegreeOfParallelism, int partitionCount)
        => new() { MaxDegreeOfParallelism = Math.Min(maxDegreeOfParallelism, partitionCount) };

    /// <summary>
    /// Computes the partition command-range boundaries and, for each partition's command start,
    /// the stack of clip scopes opened by earlier partitions, outermost first.
    /// </summary>
    /// <remarks>
    /// This single sequential prescan only inspects command kinds, so it stays cheap relative to
    /// the parallel encode it enables. Each partition replays its seed clips before its range and
    /// closes every open clip after it, keeping the concatenated clip stream balanced. Layer
    /// scopes cannot be replayed the same way because a layer composites its contents as one
    /// group when it closes, so each boundary snaps forward from its ideal position to the next
    /// command index where no layer scope is open. Snapping can produce empty trailing ranges
    /// when one layer spans most of the scene; those partitions simply encode nothing.
    /// </remarks>
    /// <param name="scene">The command batch being partitioned.</param>
    /// <param name="partitionCount">The partition count used to compute the ideal boundaries.</param>
    /// <param name="boundaries">Receives the <paramref name="partitionCount"/> + 1 range boundaries.</param>
    /// <param name="seedClips">Receives the per-partition seed clip stacks.</param>
    private static void CreatePartitionCommandRanges(
        DrawingCommandBatch scene,
        int partitionCount,
        out int[] boundaries,
        out CompositionCommand[][] seedClips)
    {
        int commandCount = scene.CommandCount;
        boundaries = new int[partitionCount + 1];
        boundaries[partitionCount] = commandCount;
        seedClips = new CompositionCommand[partitionCount][];
        seedClips[0] = [];

        List<CompositionCommand> openClips = [];
        int layerDepth = 0;
        int cursor = 0;

        for (int partitionIndex = 1; partitionIndex < partitionCount; partitionIndex++)
        {
            int ideal = (partitionIndex * commandCount) / partitionCount;
            while (cursor < commandCount && (cursor < ideal || layerDepth > 0))
            {
                if (scene.Commands[cursor] is PathCompositionSceneCommand pathCommand)
                {
                    switch (pathCommand.Command.Kind)
                    {
                        case CompositionCommandKind.BeginClip:
                            openClips.Add(pathCommand.Command);
                            break;
                        case CompositionCommandKind.EndClip:
                            if (openClips.Count > 0)
                            {
                                openClips.RemoveAt(openClips.Count - 1);
                            }

                            break;
                        case CompositionCommandKind.BeginLayer:
                            layerDepth++;
                            break;
                        case CompositionCommandKind.EndLayer:
                            if (layerDepth > 0)
                            {
                                layerDepth--;
                            }

                            break;
                    }
                }

                cursor++;
            }

            boundaries[partitionIndex] = cursor;
            seedClips[partitionIndex] = openClips.Count == 0 ? [] : [.. openClips];
        }
    }

    /// <summary>
    /// Mutable flush-scoped encoder state used while appending supported commands into contiguous scene streams.
    /// </summary>
    private ref struct SupportedSubsetSceneEncoding
    {
        private bool hasLastStyle;
        private bool streamsDetached;
        private bool gradientPixelsDetached;
        private bool pathGradientDataDetached;
        private long estimatedPathRowCount;

        // CPU-side scratch-demand estimates accumulated per draw so the first GPU attempt can be
        // seeded near true demand instead of discovering it through the overflow retry protocol.
        // estimatedTileCrossings bounds tile-boundary crossings (segment/seg-count/path-tile
        // records); estimatedBinFootprint bounds per-(draw, bin) binning records.
        private long estimatedTileCrossings;
        private long estimatedBinFootprint;

        // Cache of the last emitted 10-word style record. Consecutive draws with identical
        // styles share one record, so the encoder only appends a style when a word changes.
        private uint lastStyle0;
        private uint lastStyle1;
        private uint lastStyle2;
        private uint lastStyle3;
        private uint lastStyle4;
        private uint lastStyle5;
        private uint lastStyle6;
        private uint lastStyle7;
        private uint lastStyle8;
        private uint lastStyle9;
        private GpuSceneTransform lastTransform;
        private Rectangle rootTargetBounds;
        private List<CompositionCommand>? activeClips;
        private List<Rectangle>? openLayerBounds;

        /// <summary>
        /// Initializes a new instance of the <see cref="SupportedSubsetSceneEncoding"/> struct.
        /// </summary>
        /// <param name="allocator">The allocator used for all temporary scene streams.</param>
        /// <param name="rootTargetBounds">The root target bounds used for target-local coordinate conversion.</param>
        /// <param name="plan">The large-batch encoding plan used for initial stream sizing and prepared geometry.</param>
        public SupportedSubsetSceneEncoding(
            MemoryAllocator allocator,
            in Rectangle rootTargetBounds,
            in SceneEncodingPlan plan)
        {
            this.PathTags = new OwnedStream<byte>(allocator, plan.PathTagCapacity);
            this.PathData = new OwnedStream<uint>(allocator, plan.PathDataWordCapacity);
            this.DrawTags = new OwnedStream<uint>(allocator, plan.DrawTagCapacity);
            this.DrawData = new OwnedStream<uint>(allocator, plan.DrawDataWordCapacity);
            this.Transforms = new OwnedStream<uint>(allocator, plan.TransformWordCapacity);
            this.Styles = new OwnedStream<uint>(allocator, plan.StyleWordCapacity);

            // Gradient payloads are sparse in common scenes, so grow these only when a gradient brush is encoded.
            this.GradientPixels = new OwnedStream<uint>(allocator, plan.GradientPixelCapacity);
            this.PathGradientData = new OwnedStream<uint>(allocator, plan.PathGradientDataWordCapacity);
            this.Images = [];
            this.Recolors = [];
            this.FillCount = 0;
            this.PathCount = 0;
            this.LineCount = 0;
            this.InfoWordCount = 0;
            this.ClipCount = 0;
            this.GradientRowCount = 0;
            this.hasLastStyle = false;
            this.streamsDetached = false;
            this.gradientPixelsDetached = false;
            this.pathGradientDataDetached = false;
            this.lastStyle0 = 0;
            this.lastStyle1 = 0;
            this.lastStyle2 = 0;
            this.lastStyle3 = 0;
            this.lastStyle4 = 0;
            this.lastStyle5 = 0;
            this.lastStyle6 = 0;
            this.lastStyle7 = 0;
            this.lastStyle8 = 0;
            this.lastStyle9 = 0;
            this.rootTargetBounds = rootTargetBounds;
            this.lastTransform = GpuSceneTransform.Identity;
            this.activeClips = null;
            this.openLayerBounds = null;
            this.estimatedPathRowCount = 0;
            this.estimatedTileCrossings = 0;
            this.estimatedBinFootprint = 0;
            this.VisibleFillCount = 0;
            this.FineRasterizationMode = RasterizationMode.Antialiased;
            this.FineCoverageThreshold = 0F;

            this.PathTags.Add(PackPathTag(PathTag.Transform));
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
        /// Gets or sets the encoded path-gradient payload stream.
        /// </summary>
        public OwnedStream<uint> PathGradientData;

        /// <summary>
        /// Gets or sets the deferred image payload descriptors that are patched after atlas creation.
        /// </summary>
        public List<GpuImageDescriptor> Images;

        /// <summary>
        /// Gets or sets the deferred RecolorBrush payload descriptors specialized for the render target pixel type.
        /// </summary>
        public List<GpuRecolorDescriptor> Recolors;

        /// <summary>
        /// Gets the number of emitted fill records.
        /// </summary>
        public int FillCount { get; private set; }

        /// <summary>
        /// Gets the number of visible fills accepted by staged-scene validation.
        /// </summary>
        public int VisibleFillCount { get; private set; }

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
        /// Gets the number of emitted gradient-ramp rows.
        /// </summary>
        public int GradientRowCount { get; private set; }

        /// <summary>
        /// Gets the CPU-side estimate of the number of active tile rows referenced by sparse path metadata.
        /// </summary>
        public readonly int EstimatedPathRowCount => (int)Math.Min(this.estimatedPathRowCount, int.MaxValue);

        /// <summary>
        /// Gets the CPU-side upper-bound estimate of tile-boundary crossings produced by the
        /// scene's flattened lines. Bounds the GPU segment, segment-count, and path-tile demand.
        /// </summary>
        public readonly long EstimatedTileCrossings => this.estimatedTileCrossings;

        /// <summary>
        /// Gets the CPU-side upper-bound estimate of per-(draw, bin) binning records.
        /// </summary>
        public readonly long EstimatedBinFootprint => this.estimatedBinFootprint;

        /// <summary>
        /// Gets the flush-wide fine rasterization mode selected while encoding visible fills.
        /// </summary>
        public RasterizationMode FineRasterizationMode { get; private set; }

        /// <summary>
        /// Gets the aliased coverage threshold consumed by the fine pass when aliased mode is selected.
        /// </summary>
        public float FineCoverageThreshold { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the encoding produced no fill work.
        /// </summary>
        public readonly bool IsEmpty => this.FillCount == 0;

        /// <summary>
        /// Captures the current stream offsets for a renderable range inside this scene.
        /// </summary>
        /// <returns>The captured checkpoint.</returns>
        public readonly SceneEncodingCheckpoint CaptureCheckpoint()
            => new(
                this.PathTags.Count,
                this.PathData.Count,
                this.DrawTags.Count,
                this.DrawData.Count,
                this.Transforms.Count,
                this.Styles.Count,
                this.InfoWordCount,
                this.PathCount,
                this.ClipCount,
                this.FillCount,
                this.LineCount,
                this.estimatedPathRowCount,
                this.estimatedTileCrossings,
                this.estimatedBinFootprint);

        /// <summary>
        /// Starts a range that can be decoded independently by the staged pipeline.
        /// </summary>
        /// <param name="targetBounds">The target bounds for the independent range.</param>
        private void BeginIndependentRange(Rectangle targetBounds)
        {
            this.PadPathTagsToSegmentBoundary();
            this.rootTargetBounds = targetBounds;
            this.openLayerBounds?.Clear();
            this.hasLastStyle = false;
            this.lastTransform = GpuSceneTransform.Identity;
        }

        /// <summary>
        /// Starts a packed independent range and replays the logical clip stack into it.
        /// </summary>
        /// <param name="targetBounds">The target bounds for the independent range.</param>
        /// <param name="clipBlendMode">The blend mode used when closing replayed clips.</param>
        /// <param name="replayActiveClips">Whether the current active clip stack is replayed into the range.</param>
        /// <returns>The checkpoint captured before replaying active clips.</returns>
        public SceneEncodingCheckpoint BeginPackedIndependentRange(
            Rectangle targetBounds,
            uint clipBlendMode,
            bool replayActiveClips)
        {
            this.BeginIndependentRange(targetBounds);
            SceneEncodingCheckpoint checkpoint = this.CaptureCheckpoint();

            // Each packed range is scanned from its own path-tag base, but flatten.wgsl
            // still rebases transform indices by the implicit identity transform. Emit
            // that identity inside the range so range-local identity paths resolve to
            // transform index zero instead of underflowing into unrelated transform data.
            this.PathTags.EnsureAdditionalCapacity(1);
            this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
            this.PathTags.Add(PackPathTag(PathTag.Transform));
            AppendIdentityTransform(ref this.Transforms);
            this.lastTransform = GpuSceneTransform.Identity;

            if (replayActiveClips)
            {
                this.AppendActiveClipBegins(clipBlendMode);
            }

            return checkpoint;
        }

        /// <summary>
        /// Ends a packed independent range after balancing the logical clip stack.
        /// </summary>
        /// <param name="targetBounds">The target bounds for the independent range.</param>
        /// <param name="start">The range checkpoint captured by <see cref="BeginPackedIndependentRange"/>.</param>
        /// <param name="closeActiveClips">Whether the current active clip stack is closed before the range ends.</param>
        /// <returns>The balanced range.</returns>
        public WebGPUSceneRange EndPackedIndependentRange(
            Rectangle targetBounds,
            in SceneEncodingCheckpoint start,
            bool closeActiveClips)
        {
            if (closeActiveClips)
            {
                this.AppendActiveClipEnds();
            }

            this.EndIndependentRange();
            return CreateRange(start, this.CaptureCheckpoint(), targetBounds);
        }

        /// <summary>
        /// Records one logical begin-clip command in the active clip stack.
        /// </summary>
        /// <param name="command">The begin-clip command to keep active for later packed ranges.</param>
        private void PushActiveClip(in CompositionCommand command)
            => (this.activeClips ??= []).Add(command);

        /// <summary>
        /// Removes the most recently opened logical clip from the active clip stack.
        /// </summary>
        private readonly void PopActiveClip()
        {
            List<CompositionCommand>? activeClips = this.activeClips;
            activeClips?.RemoveAt(activeClips.Count - 1);
        }

        /// <summary>
        /// Replays the logical active clip stack at the start of a packed independent range.
        /// </summary>
        /// <param name="clipBlendMode">The blend mode used when the replayed clips close.</param>
        private void AppendActiveClipBegins(uint clipBlendMode)
        {
            List<CompositionCommand>? activeClips = this.activeClips;
            if (activeClips is null)
            {
                return;
            }

            for (int i = 0; i < activeClips.Count; i++)
            {
                this.AppendBeginClip(activeClips[i], clipBlendMode);
            }
        }

        /// <summary>
        /// Appends physical end-clip records to balance the current packed independent range.
        /// </summary>
        private void AppendActiveClipEnds()
        {
            List<CompositionCommand>? activeClips = this.activeClips;
            if (activeClips is null)
            {
                return;
            }

            for (int i = activeClips.Count - 1; i >= 0; i--)
            {
                this.AppendEndClip();
            }
        }

        /// <summary>
        /// Ends a range that can be decoded independently by the staged pipeline.
        /// </summary>
        private void EndIndependentRange() => this.PadPathTagsToSegmentBoundary();

        /// <summary>
        /// Creates one render range from two stream checkpoints.
        /// </summary>
        /// <remarks>
        /// Ranges begin on the padded scan-segment boundaries produced by
        /// <see cref="PadPathTagsToSegmentBoundary"/>, so the starting path-tag byte offset always
        /// divides exactly into the word offset consumed by the tag-scan shaders.
        /// </remarks>
        /// <param name="start">The checkpoint captured at the start of the range.</param>
        /// <param name="end">The checkpoint captured at the end of the range.</param>
        /// <param name="targetBounds">The target bounds for the range.</param>
        /// <returns>The created scene range.</returns>
        public static WebGPUSceneRange CreateRange(
            in SceneEncodingCheckpoint start,
            in SceneEncodingCheckpoint end,
            Rectangle targetBounds)
            => new(
                targetBounds,
                start.PathTagByteCount / 4,
                end.PathTagByteCount - start.PathTagByteCount,
                start.PathDataWordCount,
                end.PathDataWordCount - start.PathDataWordCount,
                start.DrawTagCount,
                end.DrawTagCount - start.DrawTagCount,
                start.DrawDataWordCount,
                end.DrawDataWordCount - start.DrawDataWordCount,
                start.TransformWordCount,
                end.TransformWordCount - start.TransformWordCount,
                start.StyleWordCount,
                end.StyleWordCount - start.StyleWordCount,
                end.InfoWordCount - start.InfoWordCount,
                end.PathCount - start.PathCount,
                end.ClipCount - start.ClipCount,
                end.FillCount - start.FillCount,
                end.LineCount - start.LineCount,
                checked((int)(end.EstimatedPathRowCount - start.EstimatedPathRowCount)),
                end.EstimatedTileCrossings - start.EstimatedTileCrossings,
                end.EstimatedBinFootprint - start.EstimatedBinFootprint);

        /// <summary>
        /// Disposes all owned stream storage that has not already been detached.
        /// </summary>
        public void Dispose()
        {
            if (!this.gradientPixelsDetached)
            {
                this.GradientPixels.Dispose();
            }

            if (!this.pathGradientDataDetached)
            {
                this.PathGradientData.Dispose();
            }

            if (!this.streamsDetached)
            {
                this.Styles.Dispose();
                this.Transforms.Dispose();
                this.DrawData.Dispose();
                this.DrawTags.Dispose();
                this.PathData.Dispose();
                this.PathTags.Dispose();
            }
        }

        /// <summary>
        /// Marks the main scene streams as detached so disposal does not free retained partition storage.
        /// </summary>
        public void MarkStreamsDetached() => this.streamsDetached = true;

        /// <summary>
        /// Marks the gradient pixel stream as detached so disposal does not free it twice.
        /// </summary>
        public void MarkGradientPixelsDetached() => this.gradientPixelsDetached = true;

        /// <summary>
        /// Marks the path-gradient payload stream as detached so disposal does not free it twice.
        /// </summary>
        public void MarkPathGradientDataDetached() => this.pathGradientDataDetached = true;

        /// <summary>
        /// Appends all supported scene operations into the mutable scene streams.
        /// </summary>
        /// <param name="scene">The command batch to append.</param>
        /// <param name="plan">The encoding plan for the command batch.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when all supported commands were appended.</returns>
        public bool TryBuild(
            DrawingCommandBatch scene,
            in SceneEncodingPlan plan,
            out string? error)
            => this.TryBuild(scene, plan, 0, scene.CommandCount, out error);

        /// <summary>
        /// Appends a contiguous command range into this partition's mutable scene streams.
        /// </summary>
        /// <param name="scene">The command batch to append.</param>
        /// <param name="plan">The encoding plan for the command batch.</param>
        /// <param name="commandStart">The inclusive command start index.</param>
        /// <param name="commandEnd">The exclusive command end index.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when all supported commands were appended.</returns>
        public bool TryBuild(
            DrawingCommandBatch scene,
            in SceneEncodingPlan plan,
            int commandStart,
            int commandEnd,
            out string? error)
            => this.TryBuild(scene, plan, commandStart, commandEnd, seedClips: null, out error);

        /// <summary>
        /// Appends a contiguous command range into this partition's mutable scene streams,
        /// replaying clip scopes opened by earlier partitions and closing every clip scope
        /// still open at the range end.
        /// </summary>
        /// <remarks>
        /// Seeding and closing keep each partition's clip stream balanced independently, so
        /// contiguous partitions concatenate into one valid Vello-style push/pop clip sequence
        /// even when a partition boundary falls inside an open clip scope. This is what allows
        /// clipped scenes to encode in parallel.
        /// </remarks>
        /// <param name="scene">The command batch to append.</param>
        /// <param name="plan">The encoding plan for the command batch.</param>
        /// <param name="commandStart">The inclusive command start index.</param>
        /// <param name="commandEnd">The exclusive command end index.</param>
        /// <param name="seedClips">The begin-clip commands opened by earlier partitions, outermost first.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when all supported commands were appended.</returns>
        public bool TryBuild(
            DrawingCommandBatch scene,
            in SceneEncodingPlan plan,
            int commandStart,
            int commandEnd,
            CompositionCommand[]? seedClips,
            out string? error)
        {
            if (seedClips is not null)
            {
                for (int i = 0; i < seedClips.Length; i++)
                {
                    CompositionCommand seed = seedClips[i];
                    this.AppendBeginClip(seed, ClipBlendMode);
                    this.PushActiveClip(seed);
                }
            }

            if (!this.TryBuildCore(scene, plan, commandStart, commandEnd, out error))
            {
                return false;
            }

            while (this.activeClips is { Count: > 0 })
            {
                this.AppendEndClip();
                this.PopActiveClip();
            }

            return true;
        }

        /// <summary>
        /// Dispatches each command in the range to its typed append overload, reusing geometry
        /// prepared by the planning pass when available.
        /// </summary>
        /// <param name="scene">The command batch to append.</param>
        /// <param name="plan">The encoding plan carrying prepared geometry.</param>
        /// <param name="commandStart">The inclusive command start index.</param>
        /// <param name="commandEnd">The exclusive command end index.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when all supported commands were appended.</returns>
        private bool TryBuildCore(
            DrawingCommandBatch scene,
            in SceneEncodingPlan plan,
            int commandStart,
            int commandEnd,
            out string? error)
        {
            for (int i = commandStart; i < commandEnd; i++)
            {
                CompositionSceneCommand command = scene.Commands[i];
                LinearGeometry? geometry = plan.GetGeometry(i);
                if (command is PathCompositionSceneCommand pathCommand)
                {
                    if (!this.TryAppend(pathCommand.Command, geometry, out error))
                    {
                        return false;
                    }
                }
                else if (command is StrokePathCompositionSceneCommand strokePathCommand)
                {
                    if (!this.TryAppend(strokePathCommand.Command, geometry, out error))
                    {
                        return false;
                    }
                }
                else if (command is LineSegmentCompositionSceneCommand lineSegmentCommand)
                {
                    if (!this.TryAppend(lineSegmentCommand.Command, out error))
                    {
                        return false;
                    }
                }
                else if (command is PolylineCompositionSceneCommand polylineCommand)
                {
                    if (!this.TryAppend(polylineCommand.Command, geometry, out error))
                    {
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared fill-path or layer-based command to the scene streams when the command kind is supported.
        /// </summary>
        /// <param name="command">The command to append.</param>
        /// <param name="geometry">The prepared geometry for the command.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when the command was appended or ignored as non-renderable.</returns>
        public bool TryAppend(
            in CompositionCommand command,
            LinearGeometry? geometry,
            out string? error)
        {
            switch (command.Kind)
            {
                case CompositionCommandKind.FillLayer:
                    geometry ??= command.SourcePath.ToLinearGeometry(MatrixUtilities.GetScale(command.Transform));
                    if (geometry.Info.SegmentCount == 0)
                    {
                        error = null;
                        return true;
                    }

                    if (!TryResolveCommand(command, geometry, out ResolvedPathCommand resolved))
                    {
                        error = null;
                        return true;
                    }

                    if (!this.TryRegisterVisibleFill(resolved.RasterizerOptions, out error))
                    {
                        return false;
                    }

                    Matrix4x4 fillResidual = MatrixUtilities.GetResidual(MatrixUtilities.GetScale(command.Transform), command.Transform);
                    Point pathDataOffset = this.AppendTargetTransform(fillResidual, command.DestinationOffset);

                    this.AppendPlainFill(resolved, geometry, pathDataOffset);
                    error = null;
                    return true;

                case CompositionCommandKind.BeginLayer:
                    this.AppendBeginLayer(command);
                    error = null;
                    return true;

                case CompositionCommandKind.EndLayer:
                    this.AppendEndLayer();
                    error = null;
                    return true;

                case CompositionCommandKind.BeginClip:
                    this.AppendBeginClip(command, ClipBlendMode);
                    this.PushActiveClip(command);

                    error = null;
                    return true;

                case CompositionCommandKind.EndClip:
                    this.AppendEndClip();
                    this.PopActiveClip();

                    error = null;
                    return true;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Appends one image fill whose texture view is supplied by the WebGPU render path.
        /// </summary>
        /// <param name="path">The path to fill.</param>
        /// <param name="imageSource">The external texture source metadata to encode.</param>
        /// <param name="options">Drawing options used for the fill.</param>
        /// <param name="rasterizerOptions">Local rasterizer options used to generate coverage.</param>
        /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
        /// <param name="targetBounds">Absolute target bounds that own the composited pixels.</param>
        /// <param name="geometry">Optional pre-flattened geometry for <paramref name="path"/>.</param>
        /// <param name="error">The error message when the command cannot be encoded.</param>
        /// <returns><see langword="true"/> when the command was encoded.</returns>
        public bool TryAppendExternalTextureFill(
            IPath path,
            in GpuImageSource imageSource,
            DrawingOptions options,
            in RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle targetBounds,
            LinearGeometry? geometry,
            out string? error)
        {
            Vector2 scale = MatrixUtilities.GetScale(options.Transform);
            geometry ??= path.ToLinearGeometry(scale);
            Matrix4x4 residual = MatrixUtilities.GetResidual(scale, options.Transform);
            RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);

            if (!TryResolveRasterization(
                    rasterizerOptions,
                    geometryBounds,
                    destinationOffset,
                    targetBounds,
                    out RasterizerOptions resolvedOptions,
                    out Rectangle brushBounds))
            {
                error = null;
                return true;
            }

            if (!this.TryRegisterVisibleFill(resolvedOptions, out error))
            {
                return false;
            }

            ResolvedPathCommand resolved = new(
                path,
                imageSource,
                options.GraphicsOptions,
                resolvedOptions,
                destinationOffset,
                brushBounds,
                options.Transform);

            Point pathDataOffset = this.AppendTargetTransform(residual, destinationOffset);

            this.AppendPlainFill(resolved, geometry, pathDataOffset);
            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared stroked path command to the scene streams.
        /// </summary>
        /// <param name="command">The command to append.</param>
        /// <param name="geometry">The prepared geometry for the command.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when the command was appended or ignored as non-renderable.</returns>
        public bool TryAppend(
            in StrokePathCommand command,
            LinearGeometry? geometry,
            out string? error)
        {
            geometry ??= command.SourcePath.ToLinearGeometry(MatrixUtilities.GetScale(command.Transform));
            if (geometry.Info.SegmentCount == 0)
            {
                error = null;
                return true;
            }

            if (!TryResolveCommand(command, geometry, out ResolvedPathCommand resolved))
            {
                error = null;
                return true;
            }

            if (!this.TryRegisterVisibleFill(resolved.RasterizerOptions, out error))
            {
                return false;
            }

            Matrix4x4 strokeResidual = MatrixUtilities.GetResidual(MatrixUtilities.GetScale(command.Transform), command.Transform);
            Point pathDataOffset = this.AppendTargetTransform(strokeResidual, command.DestinationOffset);

            this.AppendPlainStroke(resolved, command.Brush, command.Pen, geometry, pathDataOffset);
            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared explicit line-segment stroke to the scene streams.
        /// </summary>
        /// <param name="command">The command to append.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when the command was appended or ignored as non-renderable.</returns>
        public bool TryAppend(in StrokeLineSegmentCommand command, out string? error)
        {
            if (!TryResolveCommand(command, out ResolvedLineSegmentCommand resolved))
            {
                error = null;
                return true;
            }

            if (!this.TryRegisterVisibleFill(resolved.RasterizerOptions, out error))
            {
                return false;
            }

            Vector2 segmentScale = MatrixUtilities.GetScale(command.Transform);
            Matrix4x4 segmentResidual = MatrixUtilities.GetResidual(segmentScale, command.Transform);
            PointF start = new(resolved.Start.X * segmentScale.X, resolved.Start.Y * segmentScale.Y);
            PointF end = new(resolved.End.X * segmentScale.X, resolved.End.Y * segmentScale.Y);
            float widthScale = GetTransformWidthScale(command.Transform);

            Point pathDataOffset = this.AppendTargetTransform(segmentResidual, resolved.DestinationOffset);

            this.AppendExplicitStroke(
                resolved.Brush,
                resolved.GraphicsOptions,
                resolved.RasterizerOptions,
                pathDataOffset,
                resolved.BrushBounds,
                resolved.Pen,
                widthScale,
                start,
                end);
            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared explicit polyline stroke to the scene streams.
        /// </summary>
        /// <param name="command">The command to append.</param>
        /// <param name="geometry">The prepared geometry for the command.</param>
        /// <param name="error">The error message when appending fails.</param>
        /// <returns><see langword="true"/> when the command was appended or ignored as non-renderable.</returns>
        public bool TryAppend(
            in StrokePolylineCommand command,
            LinearGeometry? geometry,
            out string? error)
        {
            Vector2 polylineScale = MatrixUtilities.GetScale(command.Transform);
            geometry ??= LinearGeometry.CreateOpenPolyline(command.SourcePoints, polylineScale);
            if (!TryResolveCommand(command, geometry, out ResolvedPolylineCommand resolved))
            {
                error = null;
                return true;
            }

            if (!this.TryRegisterVisibleFill(resolved.RasterizerOptions, out error))
            {
                return false;
            }

            float widthScale = GetTransformWidthScale(command.Transform);

            Matrix4x4 polylineResidual = MatrixUtilities.GetResidual(polylineScale, command.Transform);
            Point pathDataOffset = this.AppendTargetTransform(polylineResidual, resolved.DestinationOffset);

            this.AppendExplicitStroke(
                resolved.Brush,
                resolved.GraphicsOptions,
                resolved.RasterizerOptions,
                pathDataOffset,
                resolved.BrushBounds,
                resolved.Pen,
                widthScale,
                geometry);
            error = null;
            return true;
        }

        /// <summary>
        /// Counts one fill that passed visibility resolution. Kept as the single seam where
        /// staged-scene validation could reject a fill before any stream bytes are written.
        /// </summary>
        /// <param name="options">The resolved rasterizer options for the fill.</param>
        /// <param name="error">Always <see langword="null"/>; present for the validation contract.</param>
        /// <returns>Always <see langword="true"/>; per-fill rasterization state imposes no flush-wide constraint.</returns>
        private bool TryRegisterVisibleFill(in RasterizerOptions options, out string? error)
        {
            this.VisibleFillCount++;

            // Both the rasterization mode (draw-flags aliased bit) and the aliased coverage threshold
            // (the per-fill coverage word in CMD_FILL) travel to the fine pass per fill, matching the
            // CPU rasterizer exactly. A flush may freely mix antialiased and aliased fills with any
            // thresholds, so no flush-wide constraint remains.
            _ = options;
            error = null;
            return true;
        }

        /// <summary>
        /// Emits a transform tag + data if the transform differs from the last one emitted.
        /// </summary>
        /// <param name="matrix">The transform to emit.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AppendTransformIfChanged(Matrix4x4 matrix)
        {
            GpuSceneTransform transform = GpuSceneTransform.FromMatrix4x4(matrix);
            if (transform.Equals(this.lastTransform))
            {
                return;
            }

            this.PathTags.Add(PackPathTag(PathTag.Transform));
            AppendTransform(transform, ref this.Transforms);
            this.lastTransform = transform;
        }

        /// <summary>
        /// Emits the path transform with the canvas destination offset applied after local geometry transforms.
        /// </summary>
        /// <param name="transform">The residual transform for the draw's geometry.</param>
        /// <param name="destinationOffset">The absolute destination offset of the draw.</param>
        /// <returns>The destination offset that path data should still apply directly; the root
        /// target origin when the offset was folded into the emitted transform.</returns>
        private Point AppendTargetTransform(Matrix4x4 transform, Point destinationOffset)
        {
            float translateX = destinationOffset.X - this.rootTargetBounds.X;
            float translateY = destinationOffset.Y - this.rootTargetBounds.Y;
            if (translateX != 0F || translateY != 0F)
            {
                // Canvas offsets are post-transform translations. Encoding the offset in the
                // transform keeps rotated/skewed region-local clips aligned with the CPU path.
                transform *= Matrix4x4.CreateTranslation(translateX, translateY, 0F);
                destinationOffset = new Point(this.rootTargetBounds.X, this.rootTargetBounds.Y);
            }

            this.AppendTransformIfChanged(transform);
            return destinationOffset;
        }

        /// <summary>
        /// Pads the byte path-tag stream so a later render range can start at a WGSL scan-segment boundary.
        /// </summary>
        private void PadPathTagsToSegmentBoundary()
        {
            // One pathtag_reduce workgroup (WG_SIZE = 256) reduces 256 tag words of 4 tag bytes
            // each, so a range decodes independently only when it starts on a 1024-byte boundary.
            // Zero padding bytes are PathTag.None and contribute nothing to the tag monoid.
            const int segmentByteCount = 256 * 4;

            int padding = this.PathTags.Count & (segmentByteCount - 1);
            if (padding == 0)
            {
                return;
            }

            Span<byte> paddedTags = this.PathTags.GetAppendSpan(segmentByteCount - padding);
            paddedTags.Clear();
            this.PathTags.Advance(paddedTags.Length);
        }

        /// <summary>
        /// Encodes one visible fill command into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        /// <param name="command">The resolved fill command.</param>
        /// <param name="geometry">The prepared geometry, or <see langword="null"/> to flatten the command path here.</param>
        /// <param name="pathDataOffset">The destination offset that path data applies directly.</param>
        private void AppendPlainFill(
            in ResolvedPathCommand command,
            LinearGeometry? geometry,
            Point pathDataOffset)
        {
            uint drawTag = GetDrawTag(command);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            Rectangle interestBounds = ToTargetLocal(command.RasterizerOptions.Interest, this.rootTargetBounds);
            (uint style0, uint style1, uint style2, uint style3, uint style4, uint style5, uint style6, uint style7, uint style8, uint style9) =
                GetFillStyle(command.GraphicsOptions, command.RasterizerOptions.IntersectionRule, interestBounds, command.RasterizerOptions.CoverageBoost);
            int pathTagCheckpoint = this.PathTags.Count;
            int pathDataCheckpoint = this.PathData.Count;
            int transformCheckpoint = this.Transforms.Count;
            int styleCheckpoint = this.Styles.Count;
            GpuSceneTransform lastTransformCheckpoint = this.lastTransform;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4 ||
                style5 != this.lastStyle5 ||
                style6 != this.lastStyle6 ||
                style7 != this.lastStyle7 ||
                style8 != this.lastStyle8 ||
                style9 != this.lastStyle9;
            Vector2 scale = MatrixUtilities.GetScale(command.Transform);
            geometry ??= command.Path.ToLinearGeometry(scale);

            // Reserve the exact words/tags this item can append before encoding so the
            // subsequent Add calls stay on the already-allocated contiguous spans.
            ReservePlainFillCapacity(
                geometry.Info,
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(command),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
                this.Styles.Add(style5);
                this.Styles.Add(style6);
                this.Styles.Add(style7);
                this.Styles.Add(style8);
                this.Styles.Add(style9);
            }

            int encodedPathCount = EncodePath(
                pathDataOffset,
                geometry,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out long geometryTileCrossings);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.PathData.SetCount(pathDataCheckpoint);
                this.Transforms.SetCount(transformCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                this.lastTransform = lastTransformCheckpoint;
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.lastStyle5 = style5;
            this.lastStyle6 = style6;
            this.lastStyle7 = style7;
            this.lastStyle8 = style8;
            this.lastStyle9 = style9;
            this.AccumulateDrawRowEstimate(command.RasterizerOptions.Interest, geometryLineCount, geometryTileCrossings);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;
            Brush? brush = command.Brush;
            if (brush is null)
            {
                AppendImageData(
                    command.ImageSource,
                    ToTargetLocal(command.BrushBounds, this.rootTargetBounds),
                    ref this.DrawData,
                    this.Images);
            }
            else
            {
                AppendDrawData(
                    brush,
                    command.BrushBounds,
                    command.GraphicsOptions,
                    drawTag,
                    this.rootTargetBounds,
                    ref this.DrawData,
                    ref this.GradientPixels,
                    ref this.PathGradientData,
                    this.Images,
                    this.Recolors,
                    ref gradientRowCount);
            }

            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Encodes one visible stroke command into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        /// <param name="command">The resolved stroke command.</param>
        /// <param name="brush">The stroke brush.</param>
        /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
        /// <param name="geometry">The prepared centerline geometry, or <see langword="null"/> to flatten the command path here.</param>
        /// <param name="pathDataOffset">The destination offset that path data applies directly.</param>
        private void AppendPlainStroke(
            in ResolvedPathCommand command,
            Brush brush,
            Pen pen,
            LinearGeometry? geometry,
            Point pathDataOffset)
        {
            Vector2 scale = MatrixUtilities.GetScale(command.Transform);
            geometry ??= command.Path.ToLinearGeometry(scale);
            float widthScale = GetTransformWidthScale(command.Transform);
            uint drawTag = GetDrawTag(brush);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            Rectangle interestBounds = ToTargetLocal(command.RasterizerOptions.Interest, this.rootTargetBounds);
            (uint style0, uint style1, uint style2, uint style3, uint style4, uint style5, uint style6, uint style7, uint style8, uint style9) =
                GetStrokeStyle(command.GraphicsOptions, pen, widthScale, interestBounds);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4 ||
                style5 != this.lastStyle5 ||
                style6 != this.lastStyle6 ||
                style7 != this.lastStyle7 ||
                style8 != this.lastStyle8 ||
                style9 != this.lastStyle9;

            ReservePlainStrokeCapacity(
                geometry,
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(brush),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
                this.Styles.Add(style5);
                this.Styles.Add(style6);
                this.Styles.Add(style7);
                this.Styles.Add(style8);
                this.Styles.Add(style9);
            }

            int encodedPathCount = EncodeStrokePath(
                geometry,
                pathDataOffset,
                pen,
                widthScale,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out long geometryTileCrossings);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.lastStyle5 = style5;
            this.lastStyle6 = style6;
            this.lastStyle7 = style7;
            this.lastStyle8 = style8;
            this.lastStyle9 = style9;
            this.AccumulateDrawRowEstimate(command.RasterizerOptions.Interest, geometryLineCount, geometryTileCrossings);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                brush,
                command.BrushBounds,
                command.GraphicsOptions,
                drawTag,
                this.rootTargetBounds,
                ref this.DrawData,
                ref this.GradientPixels,
                ref this.PathGradientData,
                this.Images,
                this.Recolors,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Encodes one visible explicit stroke primitive into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        /// <param name="brush">The stroke brush.</param>
        /// <param name="graphicsOptions">The graphics options used for blending.</param>
        /// <param name="rasterizerOptions">The resolved rasterizer options for the stroke.</param>
        /// <param name="pathDataOffset">The destination offset that path data applies directly.</param>
        /// <param name="brushBounds">The absolute brush sampling bounds.</param>
        /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
        /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
        /// <param name="geometry">The prepared open centerline geometry.</param>
        private void AppendExplicitStroke(
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point pathDataOffset,
            Rectangle brushBounds,
            Pen pen,
            float widthScale,
            LinearGeometry geometry)
        {
            uint drawTag = GetDrawTag(brush);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            Rectangle interestBounds = ToTargetLocal(rasterizerOptions.Interest, this.rootTargetBounds);
            (uint style0, uint style1, uint style2, uint style3, uint style4, uint style5, uint style6, uint style7, uint style8, uint style9) =
                GetStrokeStyle(graphicsOptions, pen, widthScale, interestBounds);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4 ||
                style5 != this.lastStyle5 ||
                style6 != this.lastStyle6 ||
                style7 != this.lastStyle7 ||
                style8 != this.lastStyle8 ||
                style9 != this.lastStyle9;

            ReservePlainStrokeCapacity(
                geometry,
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(brush),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
                this.Styles.Add(style5);
                this.Styles.Add(style6);
                this.Styles.Add(style7);
                this.Styles.Add(style8);
                this.Styles.Add(style9);
            }

            int encodedPathCount = EncodeStrokePath(
                geometry,
                pathDataOffset,
                pen,
                widthScale,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out long geometryTileCrossings);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.lastStyle5 = style5;
            this.lastStyle6 = style6;
            this.lastStyle7 = style7;
            this.lastStyle8 = style8;
            this.lastStyle9 = style9;
            this.AccumulateDrawRowEstimate(rasterizerOptions.Interest, geometryLineCount, geometryTileCrossings);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                brush,
                brushBounds,
                graphicsOptions,
                drawTag,
                this.rootTargetBounds,
                ref this.DrawData,
                ref this.GradientPixels,
                ref this.PathGradientData,
                this.Images,
                this.Recolors,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Encodes one visible explicit line-segment stroke primitive into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        /// <param name="brush">The stroke brush.</param>
        /// <param name="graphicsOptions">The graphics options used for blending.</param>
        /// <param name="rasterizerOptions">The resolved rasterizer options for the stroke.</param>
        /// <param name="pathDataOffset">The destination offset that path data applies directly.</param>
        /// <param name="brushBounds">The absolute brush sampling bounds.</param>
        /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
        /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
        /// <param name="start">The segment start point with the transform scale applied.</param>
        /// <param name="end">The segment end point with the transform scale applied.</param>
        private void AppendExplicitStroke(
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point pathDataOffset,
            Rectangle brushBounds,
            Pen pen,
            float widthScale,
            PointF start,
            PointF end)
        {
            uint drawTag = GetDrawTag(brush);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            Rectangle interestBounds = ToTargetLocal(rasterizerOptions.Interest, this.rootTargetBounds);
            (uint style0, uint style1, uint style2, uint style3, uint style4, uint style5, uint style6, uint style7, uint style8, uint style9) =
                GetStrokeStyle(graphicsOptions, pen, widthScale, interestBounds);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4 ||
                style5 != this.lastStyle5 ||
                style6 != this.lastStyle6 ||
                style7 != this.lastStyle7 ||
                style8 != this.lastStyle8 ||
                style9 != this.lastStyle9;

            ReservePlainStrokeCapacityForOpenSegment(
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(brush),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
                this.Styles.Add(style5);
                this.Styles.Add(style6);
                this.Styles.Add(style7);
                this.Styles.Add(style8);
                this.Styles.Add(style9);
            }

            int encodedPathCount = EncodeOpenSegmentStrokePath(
                start,
                end,
                pathDataOffset,
                pen,
                widthScale,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out long geometryTileCrossings);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.lastStyle5 = style5;
            this.lastStyle6 = style6;
            this.lastStyle7 = style7;
            this.lastStyle8 = style8;
            this.lastStyle9 = style9;
            this.AccumulateDrawRowEstimate(rasterizerOptions.Interest, geometryLineCount, geometryTileCrossings);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                brush,
                brushBounds,
                graphicsOptions,
                drawTag,
                this.rootTargetBounds,
                ref this.DrawData,
                ref this.GradientPixels,
                ref this.PathGradientData,
                this.Images,
                this.Recolors,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Emits the clip descriptor carried by one explicit begin-clip command.
        /// </summary>
        /// <param name="command">The begin-clip command to append.</param>
        /// <param name="clipBlendMode">The blend mode used when closing this clip.</param>
        private void AppendBeginClip(in CompositionCommand command, uint clipBlendMode)
        {
            DrawingClipDescriptor descriptor = command.ClipDescriptor;

            // Clip descriptors share the path stream with ordinary draws. Reset the path
            // transform before emitting the descriptor so a previous draw transform cannot
            // leak into clip geometry.
            this.PathTags.EnsureAdditionalCapacity(1);
            this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
            this.AppendTransformIfChanged(Matrix4x4.Identity);
            _ = this.AppendClipDescriptor(in descriptor, command.DestinationOffset, clipBlendMode);
        }

        /// <summary>
        /// Emits one begin-clip record from a normalized clip descriptor.
        /// </summary>
        /// <param name="descriptor">The clip descriptor to emit.</param>
        /// <param name="destinationOffset">The destination offset used to place canvas-local clip geometry.</param>
        /// <param name="clipBlendMode">The blend mode used when closing this clip.</param>
        /// <returns><see langword="true"/> when a non-empty clip record was emitted.</returns>
        private bool AppendClipDescriptor(in DrawingClipDescriptor descriptor, Point destinationOffset, uint clipBlendMode)
        {
            Rectangle clipBounds = ToTargetLocal(descriptor.GetConservativeBounds(destinationOffset), this.rootTargetBounds);

            uint style0 = descriptor.IntersectionRule == IntersectionRule.EvenOdd ? (uint)StyleFlags.Fill : 0U;
            const uint style1 = 0U;
            const uint style2 = 0U;
            const uint style3 = 0U;
            const uint style4 = 0U;
            const uint style5 = 0U;

            // Clip coverage must not be restricted by a raster interest rectangle; a zero interest
            // makes binning intersect the clip path away. Use the full target region.
            uint style6 = BitcastSingle(0F);
            uint style7 = BitcastSingle(0F);
            uint style8 = BitcastSingle(this.rootTargetBounds.Width);
            uint style9 = BitcastSingle(this.rootTargetBounds.Height);

            int pathTagCheckpoint = this.PathTags.Count;
            int pathDataCheckpoint = this.PathData.Count;
            int transformCheckpoint = this.Transforms.Count;
            int styleCheckpoint = this.Styles.Count;
            GpuSceneTransform lastTransformCheckpoint = this.lastTransform;
            bool appendStyle = !this.hasLastStyle ||
                this.lastStyle0 != style0 ||
                this.lastStyle1 != style1 ||
                this.lastStyle2 != style2 ||
                this.lastStyle3 != style3 ||
                this.lastStyle4 != style4 ||
                this.lastStyle5 != style5 ||
                this.lastStyle6 != style6 ||
                this.lastStyle7 != style7 ||
                this.lastStyle8 != style8 ||
                this.lastStyle9 != style9;

            int encodedPathCount;
            int clipLineCount;
            int rectangleCount = GetRectangleClipCount(in descriptor);
            if (clipBounds.Width <= 0 || clipBounds.Height <= 0)
            {
                ReservePlainFillCapacity(
                    0,
                    0,
                    GpuSceneDrawTag.BeginClip,
                    appendStyle,
                    0,
                    ref this.PathTags,
                    ref this.PathData,
                    ref this.DrawTags,
                    ref this.DrawData,
                    ref this.Styles,
                    ref this.GradientPixels,
                    ref this.PathGradientData);

                if (appendStyle)
                {
                    this.PathTags.Add(PackPathTag(PathTag.Style));
                    this.Styles.Add(style0);
                    this.Styles.Add(style1);
                    this.Styles.Add(style2);
                    this.Styles.Add(style3);
                    this.Styles.Add(style4);
                    this.Styles.Add(style5);
                    this.Styles.Add(style6);
                    this.Styles.Add(style7);
                    this.Styles.Add(style8);
                    this.Styles.Add(style9);
                }

                this.PathTags.EnsureAdditionalCapacity(1);
                this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
                this.AppendTransformIfChanged(Matrix4x4.Identity);

                EncodeEmptyBeginClipPath(ref this.PathTags, ref this.PathData);
                encodedPathCount = 1;
                clipLineCount = 1;
            }
            else if (rectangleCount == 0 && descriptor.Kind == DrawingClipKind.Path)
            {
                LinearGeometry geometry = descriptor.ToLinearGeometry(out Matrix4x4 clipTransform);
                if (geometry.Info.SegmentCount == 0)
                {
                    ReservePlainFillCapacity(
                        0,
                        0,
                        GpuSceneDrawTag.BeginClip,
                        appendStyle,
                        0,
                        ref this.PathTags,
                        ref this.PathData,
                        ref this.DrawTags,
                        ref this.DrawData,
                        ref this.Styles,
                        ref this.GradientPixels,
                        ref this.PathGradientData);

                    if (appendStyle)
                    {
                        this.PathTags.Add(PackPathTag(PathTag.Style));
                        this.Styles.Add(style0);
                        this.Styles.Add(style1);
                        this.Styles.Add(style2);
                        this.Styles.Add(style3);
                        this.Styles.Add(style4);
                        this.Styles.Add(style5);
                        this.Styles.Add(style6);
                        this.Styles.Add(style7);
                        this.Styles.Add(style8);
                        this.Styles.Add(style9);
                    }

                    this.PathTags.EnsureAdditionalCapacity(1);
                    this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
                    this.AppendTransformIfChanged(Matrix4x4.Identity);

                    EncodeEmptyBeginClipPath(ref this.PathTags, ref this.PathData);
                    encodedPathCount = 1;
                    clipLineCount = 1;
                }
                else
                {
                    // General path clips are real clip nodes. They are not boolean-applied to the
                    // subject geometry; the GPU clip stack combines their coverage with later draws.
                    ReservePlainFillCapacity(
                        geometry.Info,
                        GpuSceneDrawTag.BeginClip,
                        appendStyle,
                        0,
                        ref this.PathTags,
                        ref this.PathData,
                        ref this.DrawTags,
                        ref this.DrawData,
                        ref this.Styles,
                        ref this.GradientPixels,
                        ref this.PathGradientData);

                    if (appendStyle)
                    {
                        this.PathTags.Add(PackPathTag(PathTag.Style));
                        this.Styles.Add(style0);
                        this.Styles.Add(style1);
                        this.Styles.Add(style2);
                        this.Styles.Add(style3);
                        this.Styles.Add(style4);
                        this.Styles.Add(style5);
                        this.Styles.Add(style6);
                        this.Styles.Add(style7);
                        this.Styles.Add(style8);
                        this.Styles.Add(style9);
                    }

                    this.PathTags.EnsureAdditionalCapacity(1);
                    this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
                    Point pathDataOffset = this.AppendTargetTransform(clipTransform, destinationOffset);

                    encodedPathCount = EncodePath(
                        pathDataOffset,
                        geometry,
                        this.rootTargetBounds,
                        ref this.PathTags,
                        ref this.PathData,
                        out clipLineCount,
                        out _);
                }
            }
            else if (rectangleCount > 0)
            {
                // Rectangular clip descriptors are already a path-equivalent rect-set. Encode the
                // contours directly so dirty regions never pass through Region.ToPath/LinearGeometry.
                ReservePlainFillCapacity(
                    rectangleCount,
                    rectangleCount * 4,
                    GpuSceneDrawTag.BeginClip,
                    appendStyle,
                    0,
                    ref this.PathTags,
                    ref this.PathData,
                    ref this.DrawTags,
                    ref this.DrawData,
                    ref this.Styles,
                    ref this.GradientPixels,
                    ref this.PathGradientData);

                if (appendStyle)
                {
                    this.PathTags.Add(PackPathTag(PathTag.Style));
                    this.Styles.Add(style0);
                    this.Styles.Add(style1);
                    this.Styles.Add(style2);
                    this.Styles.Add(style3);
                    this.Styles.Add(style4);
                    this.Styles.Add(style5);
                    this.Styles.Add(style6);
                    this.Styles.Add(style7);
                    this.Styles.Add(style8);
                    this.Styles.Add(style9);
                }

                this.PathTags.EnsureAdditionalCapacity(1);
                this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
                this.AppendTransformIfChanged(Matrix4x4.Identity);

                encodedPathCount = EncodeRectangleClipPath(
                    in descriptor,
                    destinationOffset,
                    this.rootTargetBounds,
                    ref this.PathTags,
                    ref this.PathData,
                    out clipLineCount);
            }
            else
            {
                encodedPathCount = 0;
                clipLineCount = 0;
            }

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.PathData.SetCount(pathDataCheckpoint);
                this.Transforms.SetCount(transformCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                this.lastTransform = lastTransformCheckpoint;

                ReservePlainFillCapacity(
                    0,
                    0,
                    GpuSceneDrawTag.BeginClip,
                    appendStyle,
                    0,
                    ref this.PathTags,
                    ref this.PathData,
                    ref this.DrawTags,
                    ref this.DrawData,
                    ref this.Styles,
                    ref this.GradientPixels,
                    ref this.PathGradientData);

                if (appendStyle)
                {
                    this.PathTags.Add(PackPathTag(PathTag.Style));
                    this.Styles.Add(style0);
                    this.Styles.Add(style1);
                    this.Styles.Add(style2);
                    this.Styles.Add(style3);
                    this.Styles.Add(style4);
                    this.Styles.Add(style5);
                    this.Styles.Add(style6);
                    this.Styles.Add(style7);
                    this.Styles.Add(style8);
                    this.Styles.Add(style9);
                }

                this.PathTags.EnsureAdditionalCapacity(1);
                this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
                this.AppendTransformIfChanged(Matrix4x4.Identity);

                // Packed ranges replay active clips independently. Even when the clipped
                // geometry is empty for this range, the physical BeginClip must remain in
                // the stream so the later EndClip closes a real GPU clip-stack entry.
                EncodeEmptyBeginClipPath(ref this.PathTags, ref this.PathData);
                encodedPathCount = 1;
                clipLineCount = 1;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.lastStyle5 = style5;
            this.lastStyle6 = style6;
            this.lastStyle7 = style7;
            this.lastStyle8 = style8;
            this.lastStyle9 = style9;
            this.AccumulateDrawRowEstimateLocal(clipBounds, clipLineCount);
            this.PathCount += encodedPathCount;
            this.LineCount += clipLineCount;
            this.InfoWordCount += (int)GpuSceneDrawTag.Map(GpuSceneDrawTag.BeginClip).InfoOffset;
            this.DrawTags.Add(GpuSceneDrawTag.BeginClip);
            AppendClipBeginData(descriptor.Operation, descriptor.EdgeMode, descriptor.AntialiasThreshold, clipBlendMode, ref this.DrawData);
            this.ClipCount++;
            return true;
        }

        /// <summary>
        /// Encodes one begin-layer clip command as a rectangular clip path and draw record.
        /// </summary>
        /// <param name="command">The begin-layer command to append.</param>
        private void AppendBeginLayer(in CompositionCommand command)
        {
            Rectangle layerBounds = ToTargetLocal(command.LayerBounds, this.rootTargetBounds);

            // The layer rectangle opens an isolated target; layer graphics options are applied
            // when that target is composited back, not when the temporary target is bounded.
            (uint style0, uint style1, uint style2, uint style3, uint style4, uint style5, uint style6, uint style7, uint style8, uint style9) =
                GetFillStyle(LayerMaskGraphicsOptions, IntersectionRule.NonZero, layerBounds);

            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4 ||
                style5 != this.lastStyle5 ||
                style6 != this.lastStyle6 ||
                style7 != this.lastStyle7 ||
                style8 != this.lastStyle8 ||
                style9 != this.lastStyle9;

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
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
                this.Styles.Add(style5);
                this.Styles.Add(style6);
                this.Styles.Add(style7);
                this.Styles.Add(style8);
                this.Styles.Add(style9);
            }

            this.PathTags.EnsureAdditionalCapacity(1);
            this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
            this.AppendTransformIfChanged(Matrix4x4.Identity);

            int encodedPathCount;
            if (EncodeRectanglePath(layerBounds, ref this.PathTags, ref this.PathData, out int clipLineCount) == 0)
            {
                EncodeEmptyBeginClipPath(ref this.PathTags, ref this.PathData);
                encodedPathCount = 1;
                clipLineCount = 1;
            }
            else
            {
                encodedPathCount = 1;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.lastStyle5 = style5;
            this.lastStyle6 = style6;
            this.lastStyle7 = style7;
            this.lastStyle8 = style8;
            this.lastStyle9 = style9;
            this.AccumulateDrawRowEstimateLocal(layerBounds, clipLineCount);
            this.PathCount += encodedPathCount;
            this.LineCount += clipLineCount;
            this.InfoWordCount += (int)GpuSceneDrawTag.Map(GpuSceneDrawTag.BeginClip).InfoOffset;
            this.DrawTags.Add(GpuSceneDrawTag.BeginClip);
            AppendBeginClipData(command.LayerOptions, ref this.DrawData);
            this.ClipCount++;
            this.openLayerBounds ??= new List<Rectangle>(4);
            this.openLayerBounds.Add(layerBounds);
        }

        /// <summary>
        /// Adds one draw object's target-space interest rectangle to the sparse scratch estimates after converting it to root-target-local coordinates.
        /// </summary>
        /// <param name="absoluteInterest">The draw object's absolute raster interest bounds.</param>
        /// <param name="lineCount">The draw object's flattened (or GPU-expansion estimated) line count.</param>
        /// <param name="tileCrossings">
        /// The exact summed per-line tile-crossing bound when the caller flattened the geometry on the CPU
        /// (fills), or a negative value to fall back to the bounding-box diagonal bound (strokes, clips).
        /// </param>
        private void AccumulateDrawRowEstimate(Rectangle absoluteInterest, int lineCount, long tileCrossings = -1)
            => this.AccumulateDrawRowEstimateLocal(ToTargetLocal(absoluteInterest, this.rootTargetBounds), lineCount, tileCrossings);

        /// <summary>
        /// Adds one draw object's clipped root-target-local footprint to the sparse scratch estimates:
        /// tile-row span for path rows, per-line tile-crossing bound for segments and path tiles, and
        /// bin footprint for binning records.
        /// </summary>
        /// <param name="localBounds">The draw object's root-target-local raster interest bounds.</param>
        /// <param name="lineCount">The draw object's flattened (or GPU-expansion estimated) line count.</param>
        /// <param name="tileCrossings">
        /// The exact summed per-line tile-crossing bound when the caller flattened the geometry on the CPU
        /// (fills), or a negative value to fall back to the bounding-box diagonal bound (strokes, clips).
        /// </param>
        private void AccumulateDrawRowEstimateLocal(Rectangle localBounds, int lineCount, long tileCrossings = -1)
        {
            Rectangle clippedBounds = Rectangle.Intersect(localBounds, new Rectangle(0, 0, this.rootTargetBounds.Width, this.rootTargetBounds.Height));

            if (this.openLayerBounds is { Count: > 0 })
            {
                for (int i = 0; i < this.openLayerBounds.Count && clippedBounds.Width > 0 && clippedBounds.Height > 0; i++)
                {
                    clippedBounds = Rectangle.Intersect(clippedBounds, this.openLayerBounds[i]);
                }
            }

            if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
            {
                return;
            }

            int tileY0 = clippedBounds.Top / TileHeight;
            int tileY1 = DivideRoundUp(clippedBounds.Bottom, TileHeight);
            long rowCount = Math.Max(tileY1 - tileY0, 0);
            this.estimatedPathRowCount = Math.Min(this.estimatedPathRowCount + rowCount, int.MaxValue);

            // A line inside this interest crosses at most (tilesWide + tilesHigh + slack) tile
            // boundaries, so lineCount x that diagonal bound caps the draw's segment and
            // path-tile records. Deliberately conservative; over-seeding wastes memory while
            // under-seeding costs a full-scene GPU retry.
            long tilesWide = (clippedBounds.Width / TileWidth) + 2;
            long tilesHigh = rowCount + 1;
            long boundingBoxCrossings = Math.Max(lineCount, 1) * (tilesWide + tilesHigh + 2);
            long crossings = tileCrossings >= 0 ? Math.Min(tileCrossings, boundingBoxCrossings) : boundingBoxCrossings;
            this.estimatedTileCrossings += crossings;

            // Binning emits one record per (draw, 16x16-tile bin) pair the draw's bounds touch.
            long binsWide = (clippedBounds.Width / (TileWidth * 16)) + 2;
            long binsHigh = (clippedBounds.Height / (TileHeight * 16)) + 2;
            this.estimatedBinFootprint += binsWide * binsHigh;
        }

        /// <summary>
        /// Encodes the closing record for the next end-layer command in the retained timeline.
        /// </summary>
        private void AppendEndLayer()
        {
            if (this.openLayerBounds is { Count: > 0 })
            {
                this.openLayerBounds.RemoveAt(this.openLayerBounds.Count - 1);
            }

            // A packed range can contain an EndLayer whose matching BeginLayer was emitted
            // in an earlier range. The command stream is still balanced, so the close marker
            // must be emitted even when the local row-estimate stack has no opener to pop.
            this.AppendEndClip();
        }

        /// <summary>
        /// Encodes one end-clip command.
        /// </summary>
        private void AppendEndClip()
        {
            // EndClip is fixed-size: one draw tag plus a zero-data path terminator. The canvas
            // records balanced command ranges, so this method does not inspect local stack state.
            ReserveEndLayerCapacity(ref this.PathTags, ref this.DrawTags);
            this.DrawTags.Add(GpuSceneDrawTag.EndClip);
            this.PathTags.Add(PackPathTag(PathTag.Path));
            this.PathCount++;
            this.ClipCount++;
        }
    }

    /// <summary>
    /// Owns one command-range encoding produced by the parallel scene encoder.
    /// </summary>
    private sealed class SceneEncodingPartition : IDisposable
    {
        private readonly IMemoryOwner<byte> pathTagsOwner;
        private readonly IMemoryOwner<uint> pathDataOwner;
        private readonly IMemoryOwner<uint> drawTagsOwner;
        private readonly IMemoryOwner<uint> drawDataOwner;
        private readonly IMemoryOwner<uint> transformsOwner;
        private readonly IMemoryOwner<uint> stylesOwner;
        private readonly IMemoryOwner<uint>? gradientPixelsOwner;
        private readonly IMemoryOwner<uint>? pathGradientDataOwner;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneEncodingPartition"/> class.
        /// </summary>
        /// <param name="pathTagsOwner">The detached path-tag storage.</param>
        /// <param name="pathDataOwner">The detached path-data storage.</param>
        /// <param name="drawTagsOwner">The detached draw-tag storage.</param>
        /// <param name="drawDataOwner">The detached draw-data storage.</param>
        /// <param name="transformsOwner">The detached transform storage.</param>
        /// <param name="stylesOwner">The detached style storage.</param>
        /// <param name="gradientPixelsOwner">The detached gradient-pixel storage, or <see langword="null"/> when no gradients were emitted.</param>
        /// <param name="pathGradientDataOwner">The detached path-gradient storage, or <see langword="null"/> when no path gradients were emitted.</param>
        /// <param name="images">The deferred image descriptors recorded by the partition.</param>
        /// <param name="recolors">The deferred recolor descriptors recorded by the partition.</param>
        /// <param name="fillCount">The number of emitted fill records.</param>
        /// <param name="visibleFillCount">The number of visible fills accepted by staged-scene validation.</param>
        /// <param name="pathCount">The number of emitted paths.</param>
        /// <param name="lineCount">The number of emitted non-horizontal lines.</param>
        /// <param name="infoWordCount">The total info-word count implied by the emitted draw tags.</param>
        /// <param name="clipCount">The number of emitted clip records.</param>
        /// <param name="gradientRowCount">The number of emitted gradient-ramp rows.</param>
        /// <param name="estimatedPathRowCount">The CPU-side estimate of active tile rows.</param>
        /// <param name="estimatedTileCrossings">The CPU-side upper bound for tile-boundary crossings.</param>
        /// <param name="estimatedBinFootprint">The CPU-side upper bound for per-(draw, bin) records.</param>
        /// <param name="pathTagByteCount">The unpadded path-tag byte count.</param>
        /// <param name="pathDataWordCount">The path-data word count.</param>
        /// <param name="drawTagCount">The draw-tag count.</param>
        /// <param name="drawDataWordCount">The draw-data word count.</param>
        /// <param name="transformWordCount">The transform word count.</param>
        /// <param name="styleWordCount">The style word count.</param>
        /// <param name="pathGradientDataWordCount">The path-gradient payload word count.</param>
        /// <param name="fineRasterizationMode">The fine-pass rasterization mode selected by the partition.</param>
        /// <param name="fineCoverageThreshold">The aliased coverage threshold selected by the partition.</param>
        private SceneEncodingPartition(
            IMemoryOwner<byte> pathTagsOwner,
            IMemoryOwner<uint> pathDataOwner,
            IMemoryOwner<uint> drawTagsOwner,
            IMemoryOwner<uint> drawDataOwner,
            IMemoryOwner<uint> transformsOwner,
            IMemoryOwner<uint> stylesOwner,
            IMemoryOwner<uint>? gradientPixelsOwner,
            IMemoryOwner<uint>? pathGradientDataOwner,
            List<GpuImageDescriptor> images,
            List<GpuRecolorDescriptor> recolors,
            int fillCount,
            int visibleFillCount,
            int pathCount,
            int lineCount,
            int infoWordCount,
            int clipCount,
            int gradientRowCount,
            int estimatedPathRowCount,
            long estimatedTileCrossings,
            long estimatedBinFootprint,
            int pathTagByteCount,
            int pathDataWordCount,
            int drawTagCount,
            int drawDataWordCount,
            int transformWordCount,
            int styleWordCount,
            int pathGradientDataWordCount,
            RasterizationMode fineRasterizationMode,
            float fineCoverageThreshold)
        {
            this.pathTagsOwner = pathTagsOwner;
            this.pathDataOwner = pathDataOwner;
            this.drawTagsOwner = drawTagsOwner;
            this.drawDataOwner = drawDataOwner;
            this.transformsOwner = transformsOwner;
            this.stylesOwner = stylesOwner;
            this.gradientPixelsOwner = gradientPixelsOwner;
            this.pathGradientDataOwner = pathGradientDataOwner;
            this.Images = images;
            this.Recolors = recolors;
            this.FillCount = fillCount;
            this.VisibleFillCount = visibleFillCount;
            this.PathCount = pathCount;
            this.LineCount = lineCount;
            this.InfoWordCount = infoWordCount;
            this.ClipCount = clipCount;
            this.GradientRowCount = gradientRowCount;
            this.EstimatedPathRowCount = estimatedPathRowCount;
            this.EstimatedTileCrossings = estimatedTileCrossings;
            this.EstimatedBinFootprint = estimatedBinFootprint;
            this.PathTagByteCount = pathTagByteCount;
            this.PathDataWordCount = pathDataWordCount;
            this.DrawTagCount = drawTagCount;
            this.DrawDataWordCount = drawDataWordCount;
            this.TransformWordCount = transformWordCount;
            this.StyleWordCount = styleWordCount;
            this.PathGradientDataWordCount = pathGradientDataWordCount;
            this.FineRasterizationMode = fineRasterizationMode;
            this.FineCoverageThreshold = fineCoverageThreshold;
        }

        /// <summary>
        /// Gets the encoded path-tag bytes.
        /// </summary>
        public ReadOnlySpan<byte> PathTags => this.pathTagsOwner.Memory.Span[..this.PathTagByteCount];

        /// <summary>
        /// Gets the encoded path-data words.
        /// </summary>
        public ReadOnlySpan<uint> PathData => this.pathDataOwner.Memory.Span[..this.PathDataWordCount];

        /// <summary>
        /// Gets the encoded draw tags.
        /// </summary>
        public ReadOnlySpan<uint> DrawTags => this.drawTagsOwner.Memory.Span[..this.DrawTagCount];

        /// <summary>
        /// Gets the encoded draw-data words.
        /// </summary>
        public ReadOnlySpan<uint> DrawData => this.drawDataOwner.Memory.Span[..this.DrawDataWordCount];

        /// <summary>
        /// Gets the encoded transform words.
        /// </summary>
        public ReadOnlySpan<uint> Transforms => this.transformsOwner.Memory.Span[..this.TransformWordCount];

        /// <summary>
        /// Gets the encoded style words.
        /// </summary>
        public ReadOnlySpan<uint> Styles => this.stylesOwner.Memory.Span[..this.StyleWordCount];

        /// <summary>
        /// Gets the packed gradient-ramp pixels.
        /// </summary>
        public ReadOnlySpan<uint> GradientPixels
            => this.gradientPixelsOwner is null
                ? ReadOnlySpan<uint>.Empty
                : this.gradientPixelsOwner.Memory.Span[..(this.GradientRowCount * GradientRowWordCount)];

        /// <summary>
        /// Gets the encoded path-gradient payload words.
        /// </summary>
        public ReadOnlySpan<uint> PathGradientData
            => this.pathGradientDataOwner is null
                ? ReadOnlySpan<uint>.Empty
                : this.pathGradientDataOwner.Memory.Span[..this.PathGradientDataWordCount];

        /// <summary>
        /// Gets the deferred image descriptors recorded by this partition.
        /// </summary>
        public List<GpuImageDescriptor> Images { get; }

        /// <summary>
        /// Gets the deferred recolor descriptors recorded by this partition.
        /// </summary>
        public List<GpuRecolorDescriptor> Recolors { get; }

        /// <summary>
        /// Gets the number of emitted fill records.
        /// </summary>
        public int FillCount { get; }

        /// <summary>
        /// Gets the number of visible fills accepted by staged-scene validation.
        /// </summary>
        public int VisibleFillCount { get; }

        /// <summary>
        /// Gets the number of emitted paths.
        /// </summary>
        public int PathCount { get; }

        /// <summary>
        /// Gets the number of emitted non-horizontal lines.
        /// </summary>
        public int LineCount { get; }

        /// <summary>
        /// Gets the total info-word count implied by the emitted draw tags.
        /// </summary>
        public int InfoWordCount { get; }

        /// <summary>
        /// Gets the number of emitted clip records.
        /// </summary>
        public int ClipCount { get; }

        /// <summary>
        /// Gets the number of emitted gradient-ramp rows.
        /// </summary>
        public int GradientRowCount { get; }

        /// <summary>
        /// Gets the CPU-side estimate of active tile rows referenced by sparse path metadata.
        /// </summary>
        public int EstimatedPathRowCount { get; }

        /// <summary>
        /// Gets the CPU-side upper bound for tile-boundary crossings.
        /// </summary>
        public long EstimatedTileCrossings { get; }

        /// <summary>
        /// Gets the CPU-side upper bound for per-(draw, bin) binning records.
        /// </summary>
        public long EstimatedBinFootprint { get; }

        /// <summary>
        /// Gets the unpadded path-tag byte count.
        /// </summary>
        public int PathTagByteCount { get; }

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
        /// Gets the encoded path-gradient payload word count.
        /// </summary>
        public int PathGradientDataWordCount { get; }

        /// <summary>
        /// Gets the fine-pass rasterization mode selected by this partition.
        /// </summary>
        public RasterizationMode FineRasterizationMode { get; }

        /// <summary>
        /// Gets the aliased coverage threshold selected by this partition.
        /// </summary>
        public float FineCoverageThreshold { get; }

        /// <summary>
        /// Detaches all retained streams from a mutable partition encoder.
        /// </summary>
        /// <param name="encoding">The mutable partition encoding.</param>
        /// <returns>The detached partition encoding.</returns>
        public static SceneEncodingPartition Detach(ref SupportedSubsetSceneEncoding encoding)
        {
            int pathTagByteCount = encoding.PathTags.Count;
            int pathDataWordCount = encoding.PathData.Count;
            int drawTagCount = encoding.DrawTags.Count;
            int drawDataWordCount = encoding.DrawData.Count;
            int transformWordCount = encoding.Transforms.Count;
            int styleWordCount = encoding.Styles.Count;
            int pathGradientDataWordCount = encoding.PathGradientData.Count;
            int gradientRowCount = encoding.GradientRowCount;

            IMemoryOwner<byte> pathTagsOwner = encoding.PathTags.DetachOwner();
            IMemoryOwner<uint> pathDataOwner = encoding.PathData.DetachOwner();
            IMemoryOwner<uint> drawTagsOwner = encoding.DrawTags.DetachOwner();
            IMemoryOwner<uint> drawDataOwner = encoding.DrawData.DetachOwner();
            IMemoryOwner<uint> transformsOwner = encoding.Transforms.DetachOwner();
            IMemoryOwner<uint> stylesOwner = encoding.Styles.DetachOwner();
            encoding.MarkStreamsDetached();

            IMemoryOwner<uint>? gradientPixelsOwner = null;
            if (gradientRowCount > 0)
            {
                gradientPixelsOwner = encoding.GradientPixels.DetachOwner();
                encoding.MarkGradientPixelsDetached();
            }

            IMemoryOwner<uint>? pathGradientDataOwner = null;
            if (pathGradientDataWordCount > 0)
            {
                pathGradientDataOwner = encoding.PathGradientData.DetachOwner();
                encoding.MarkPathGradientDataDetached();
            }

            return new SceneEncodingPartition(
                pathTagsOwner,
                pathDataOwner,
                drawTagsOwner,
                drawDataOwner,
                transformsOwner,
                stylesOwner,
                gradientPixelsOwner,
                pathGradientDataOwner,
                encoding.Images,
                encoding.Recolors,
                encoding.FillCount,
                encoding.VisibleFillCount,
                encoding.PathCount,
                encoding.LineCount,
                encoding.InfoWordCount,
                encoding.ClipCount,
                gradientRowCount,
                encoding.EstimatedPathRowCount,
                encoding.EstimatedTileCrossings,
                encoding.EstimatedBinFootprint,
                pathTagByteCount,
                pathDataWordCount,
                drawTagCount,
                drawDataWordCount,
                transformWordCount,
                styleWordCount,
                pathGradientDataWordCount,
                encoding.FineRasterizationMode,
                encoding.FineCoverageThreshold);
        }

        /// <summary>
        /// Releases all retained partition stream buffers.
        /// </summary>
        public void Dispose()
        {
            this.pathTagsOwner.Dispose();
            this.pathDataOwner.Dispose();
            this.drawTagsOwner.Dispose();
            this.drawDataOwner.Dispose();
            this.transformsOwner.Dispose();
            this.stylesOwner.Dispose();
            this.gradientPixelsOwner?.Dispose();
            this.pathGradientDataOwner?.Dispose();
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
        /// <param name="encoding">The mutable scene encoding.</param>
        /// <param name="targetBounds">The target bounds.</param>
        /// <param name="allocator">The allocator used for scene buffers.</param>
        /// <param name="operations">The scene operations associated with the encoded payload.</param>
        /// <returns>The resolved encoded scene.</returns>
        public static WebGPUEncodedScene Resolve(
            ref SupportedSubsetSceneEncoding encoding,
            in Rectangle targetBounds,
            MemoryAllocator allocator,
            WebGPUSceneOperation[] operations)
        {
            int pathTagByteCount = encoding.PathTags.Count;

            // Path tags are reduced in 256-word scan segments (pathtag_reduce WG_SIZE), so the
            // word count is padded up to a whole segment before the path-data stream begins.
            int pathTagWordCount = AlignUp(DivideRoundUp(pathTagByteCount, 4), 256);
            int pathDataWordCount = encoding.PathData.Count;
            int drawTagCount = encoding.DrawTags.Count;
            int drawDataWordCount = encoding.DrawData.Count;
            int transformWordCount = encoding.Transforms.Count;
            int styleWordCount = encoding.Styles.Count;
            int pathGradientDataWordCount = encoding.PathGradientData.Count;
            int drawTagBase = pathTagWordCount + pathDataWordCount;
            int drawDataBase = drawTagBase + drawTagCount;
            int transformBase = drawDataBase + drawDataWordCount;
            int styleBase = transformBase + transformWordCount;
            int sceneWordCount = styleBase + styleWordCount;
            GpuSceneLayout layout = new(
                (uint)drawTagCount,
                (uint)encoding.PathCount,
                (uint)encoding.ClipCount,
                (uint)(encoding.InfoWordCount + pathGradientDataWordCount),
                (uint)encoding.InfoWordCount,
                0U,
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
                    DetachPathGradientData(ref encoding),
                    pathGradientDataWordCount,
                    encoding.Images,
                    encoding.Recolors,
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
                    Math.Max(encoding.EstimatedPathRowCount, encoding.PathCount),
                    DivideRoundUp(targetBounds.Width, TileWidth),
                    DivideRoundUp(targetBounds.Height, TileHeight),
                    encoding.FineRasterizationMode,
                    encoding.FineCoverageThreshold,
                    encoding.EstimatedTileCrossings,
                    encoding.EstimatedBinFootprint,
                    operations);
            }
            catch
            {
                sceneDataOwner.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Resolves ordered partition encodings into the final packed scene buffers.
        /// </summary>
        /// <param name="partitions">The ordered partition encodings.</param>
        /// <param name="targetBounds">The target bounds.</param>
        /// <param name="allocator">The allocator used for scene buffers.</param>
        /// <param name="fineRasterizationMode">The fine-pass rasterization mode.</param>
        /// <param name="fineCoverageThreshold">The scene-wide aliased coverage threshold.</param>
        /// <returns>The resolved encoded scene.</returns>
        public static WebGPUEncodedScene Resolve(
            SceneEncodingPartition?[] partitions,
            in Rectangle targetBounds,
            MemoryAllocator allocator,
            RasterizationMode fineRasterizationMode,
            float fineCoverageThreshold)
        {
            int pathTagByteCount = 0;
            int pathDataWordCount = 0;
            int drawTagCount = 0;
            int drawDataWordCount = 0;
            int transformWordCount = 0;
            int styleWordCount = 0;
            int pathGradientDataWordCount = 0;
            int imageCount = 0;
            int recolorCount = 0;
            int gradientRowCount = 0;
            int fillCount = 0;
            int pathCount = 0;
            int lineCount = 0;
            int infoWordCount = 0;
            int clipCount = 0;
            long estimatedPathRowCount = 0;
            long estimatedTileCrossings = 0;
            long estimatedBinFootprint = 0;

            for (int i = 0; i < partitions.Length; i++)
            {
                SceneEncodingPartition partition = partitions[i]!;
                pathTagByteCount += partition.PathTagByteCount;
                pathDataWordCount += partition.PathDataWordCount;
                drawTagCount += partition.DrawTagCount;
                drawDataWordCount += partition.DrawDataWordCount;
                transformWordCount += partition.TransformWordCount;
                styleWordCount += partition.StyleWordCount;
                pathGradientDataWordCount += partition.PathGradientDataWordCount;
                imageCount += partition.Images.Count;
                recolorCount += partition.Recolors.Count;
                gradientRowCount += partition.GradientRowCount;
                fillCount += partition.FillCount;
                pathCount += partition.PathCount;
                lineCount += partition.LineCount;
                infoWordCount += partition.InfoWordCount;
                clipCount += partition.ClipCount;
                estimatedPathRowCount = Math.Min(estimatedPathRowCount + partition.EstimatedPathRowCount, int.MaxValue);
                estimatedTileCrossings += partition.EstimatedTileCrossings;
                estimatedBinFootprint += partition.EstimatedBinFootprint;
            }

            // Path tags are reduced in 256-word scan segments (pathtag_reduce WG_SIZE), so the
            // word count is padded up to a whole segment before the path-data stream begins.
            int pathTagWordCount = AlignUp(DivideRoundUp(pathTagByteCount, 4), 256);
            int drawTagBase = pathTagWordCount + pathDataWordCount;
            int drawDataBase = drawTagBase + drawTagCount;
            int transformBase = drawDataBase + drawDataWordCount;
            int styleBase = transformBase + transformWordCount;
            int sceneWordCount = styleBase + styleWordCount;
            GpuSceneLayout layout = new(
                (uint)drawTagCount,
                (uint)pathCount,
                (uint)clipCount,
                (uint)(infoWordCount + pathGradientDataWordCount),
                (uint)infoWordCount,
                0U,
                0U,
                (uint)pathTagWordCount,
                (uint)drawTagBase,
                (uint)drawDataBase,
                (uint)transformBase,
                (uint)styleBase);

            IMemoryOwner<uint>? sceneDataOwner = allocator.Allocate<uint>(sceneWordCount);
            IMemoryOwner<uint>? gradientPixelsOwner = gradientRowCount == 0 ? null : allocator.Allocate<uint>(gradientRowCount * GradientRowWordCount);
            IMemoryOwner<uint>? pathGradientDataOwner = pathGradientDataWordCount == 0 ? null : allocator.Allocate<uint>(pathGradientDataWordCount);
            try
            {
                Span<uint> sceneWords = sceneDataOwner.Memory.Span[..sceneWordCount];
                Span<byte> sceneBytes = MemoryMarshal.Cast<uint, byte>(sceneWords);
                List<GpuImageDescriptor> images = new(imageCount);
                List<GpuRecolorDescriptor> recolors = new(recolorCount);
                int pathTagOffset = 0;
                int pathDataOffset = 0;
                int drawTagOffset = 0;
                int drawDataOffset = 0;
                int transformOffset = 0;
                int styleOffset = 0;
                int gradientRowOffset = 0;
                int gradientPixelOffset = 0;
                int pathGradientDataOffset = 0;

                for (int i = 0; i < partitions.Length; i++)
                {
                    SceneEncodingPartition partition = partitions[i]!;
                    partition.PathTags.CopyTo(sceneBytes[pathTagOffset..]);
                    pathTagOffset += partition.PathTagByteCount;

                    partition.PathData.CopyTo(sceneWords.Slice((int)layout.PathDataBase + pathDataOffset, partition.PathDataWordCount));
                    pathDataOffset += partition.PathDataWordCount;

                    partition.DrawTags.CopyTo(sceneWords.Slice((int)layout.DrawTagBase + drawTagOffset, partition.DrawTagCount));
                    drawTagOffset += partition.DrawTagCount;

                    CopyDrawDataWithOffsets(
                        partition,
                        sceneWords.Slice((int)layout.DrawDataBase + drawDataOffset, partition.DrawDataWordCount),
                        gradientRowOffset,
                        pathGradientDataOffset);

                    for (int imageIndex = 0; imageIndex < partition.Images.Count; imageIndex++)
                    {
                        GpuImageDescriptor image = partition.Images[imageIndex];
                        images.Add(new GpuImageDescriptor(image.Source, drawDataOffset + image.DrawDataWordOffset));
                    }

                    for (int recolorIndex = 0; recolorIndex < partition.Recolors.Count; recolorIndex++)
                    {
                        GpuRecolorDescriptor recolor = partition.Recolors[recolorIndex];

                        recolors.Add(new GpuRecolorDescriptor(
                            recolor.SourceColor,
                            recolor.TargetColor,
                            recolor.Threshold,
                            pathGradientDataOffset + recolor.BrushDataWordOffset));
                    }

                    drawDataOffset += partition.DrawDataWordCount;

                    partition.Transforms.CopyTo(sceneWords.Slice((int)layout.TransformBase + transformOffset, partition.TransformWordCount));
                    transformOffset += partition.TransformWordCount;

                    partition.Styles.CopyTo(sceneWords.Slice((int)layout.StyleBase + styleOffset, partition.StyleWordCount));
                    styleOffset += partition.StyleWordCount;

                    if (partition.GradientRowCount > 0)
                    {
                        partition.GradientPixels.CopyTo(gradientPixelsOwner!.Memory.Span.Slice(gradientPixelOffset, partition.GradientRowCount * GradientRowWordCount));
                        gradientPixelOffset += partition.GradientRowCount * GradientRowWordCount;
                    }

                    if (partition.PathGradientDataWordCount > 0)
                    {
                        partition.PathGradientData.CopyTo(pathGradientDataOwner!.Memory.Span.Slice(pathGradientDataOffset, partition.PathGradientDataWordCount));
                    }

                    gradientRowOffset += partition.GradientRowCount;
                    pathGradientDataOffset += partition.PathGradientDataWordCount;
                }

                sceneBytes[pathTagByteCount..(pathTagWordCount * sizeof(uint))].Clear();
                WebGPUEncodedScene resolved = new(
                    targetBounds.Size,
                    infoWordCount,
                    sceneDataOwner,
                    sceneWordCount,
                    gradientPixelsOwner,
                    pathGradientDataOwner,
                    pathGradientDataWordCount,
                    images,
                    recolors,
                    gradientRowCount,
                    layout,
                    fillCount,
                    pathCount,
                    lineCount,
                    pathTagByteCount,
                    pathTagWordCount,
                    pathDataWordCount,
                    drawTagCount,
                    drawDataWordCount,
                    transformWordCount,
                    styleWordCount,
                    clipCount,
                    fillCount,
                    Math.Max((int)estimatedPathRowCount, pathCount),
                    DivideRoundUp(targetBounds.Width, TileWidth),
                    DivideRoundUp(targetBounds.Height, TileHeight),
                    fineRasterizationMode,
                    fineCoverageThreshold,
                    estimatedTileCrossings,
                    estimatedBinFootprint,
                    []);

                sceneDataOwner = null;
                gradientPixelsOwner = null;
                pathGradientDataOwner = null;
                return resolved;
            }
            finally
            {
                sceneDataOwner?.Dispose();
                gradientPixelsOwner?.Dispose();
                pathGradientDataOwner?.Dispose();
            }
        }

        /// <summary>
        /// Copies draw-data while rebasing partition-local auxiliary payload offsets into final scene offsets.
        /// </summary>
        /// <param name="partition">The partition supplying draw tags and draw data.</param>
        /// <param name="destination">The destination slice in the packed scene buffer.</param>
        /// <param name="gradientRowOffset">The number of gradient rows emitted by earlier partitions.</param>
        /// <param name="pathGradientDataOffset">The number of path-gradient words emitted by earlier partitions.</param>
        private static void CopyDrawDataWithOffsets(
            SceneEncodingPartition partition,
            Span<uint> destination,
            int gradientRowOffset,
            int pathGradientDataOffset)
        {
            ReadOnlySpan<uint> drawTags = partition.DrawTags;
            ReadOnlySpan<uint> drawData = partition.DrawData;
            int sourceOffset = 0;
            int destinationOffset = 0;

            for (int i = 0; i < drawTags.Length; i++)
            {
                uint drawTag = drawTags[i];
                int wordCount = GetDrawDataWordCount(drawTag);
                drawData.Slice(sourceOffset, wordCount).CopyTo(destination[destinationOffset..]);

                if (DrawTagUsesGradientRamp(drawTag))
                {
                    // The gradient index_mode word packs (ramp row << 2) | extend mode, so the
                    // row rebase is shifted past the two extend-mode bits.
                    destination[destinationOffset] += (uint)(gradientRowOffset << 2);
                }
                else if (drawTag is GpuSceneDrawTag.FillRecolor or GpuSceneDrawTag.FillPathGradient)
                {
                    destination[destinationOffset] += (uint)pathGradientDataOffset;
                }

                sourceOffset += wordCount;
                destinationOffset += wordCount;
            }
        }

        /// <summary>
        /// Detaches the gradient pixel payload when gradients were emitted for the flush.
        /// </summary>
        /// <param name="encoding">The mutable scene encoding.</param>
        /// <returns>The detached storage, or <see langword="null"/> when no gradients were emitted.</returns>
        private static IMemoryOwner<uint>? DetachGradientPixels(ref SupportedSubsetSceneEncoding encoding)
        {
            if (encoding.GradientRowCount == 0)
            {
                return null;
            }

            encoding.MarkGradientPixelsDetached();
            return encoding.GradientPixels.DetachOwner();
        }

        /// <summary>
        /// Detaches the path-gradient payload when path gradients were emitted for the flush.
        /// </summary>
        /// <param name="encoding">The mutable scene encoding.</param>
        /// <returns>The detached storage, or <see langword="null"/> when no path gradients were emitted.</returns>
        private static IMemoryOwner<uint>? DetachPathGradientData(ref SupportedSubsetSceneEncoding encoding)
        {
            if (encoding.PathGradientData.Count == 0)
            {
                return null;
            }

            encoding.MarkPathGradientDataDetached();
            return encoding.PathGradientData.DetachOwner();
        }
    }

    /// <summary>
    /// Returns whether the staged scene encoder knows how to lower the supplied brush type.
    /// </summary>
    /// <param name="brush">The brush to test.</param>
    /// <returns><see langword="true"/> when the brush type has a draw-tag lowering.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSupportedBrush(Brush brush)
        => brush is SolidBrush
            or RecolorBrush
            or LinearGradientBrush
            or RadialGradientBrush
            or EllipticGradientBrush
            or SweepGradientBrush
            or PathGradientBrush
            or PatternBrush
            or ImageBrush;

    /// <summary>
    /// Resolves one prepared fill command's raster bounds against its target and captures the
    /// state the encoder needs to emit it.
    /// </summary>
    /// <param name="command">The fill command to resolve.</param>
    /// <param name="geometry">The prepared geometry for the command.</param>
    /// <param name="resolved">Receives the resolved command state.</param>
    /// <returns><see langword="true"/> when the command is a fill that intersects its target.</returns>
    private static bool TryResolveCommand(
        in CompositionCommand command,
        LinearGeometry geometry,
        out ResolvedPathCommand resolved)
    {
        if (command.Kind is not CompositionCommandKind.FillLayer)
        {
            resolved = default;
            return false;
        }

        Matrix4x4 transform = command.Transform;
        Vector2 scale = MatrixUtilities.GetScale(transform);
        Matrix4x4 residual = MatrixUtilities.GetResidual(scale, transform);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);

        if (!TryResolveRasterization(
            command.RasterizerOptions,
            geometryBounds,
            command.DestinationOffset,
            command.TargetBounds,
            out RasterizerOptions rasterizerOptions,
            out Rectangle brushBounds))
        {
            resolved = default;
            return false;
        }

        resolved = new ResolvedPathCommand(
            command.SourcePath,
            command.Brush,
            command.DrawingOptions.GraphicsOptions,
            rasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? brushBounds : default,
            command.Transform);
        return true;
    }

    /// <summary>
    /// Resolves one prepared stroked-path command's stroke-inflated raster bounds against its
    /// target and captures the state the encoder needs to emit it.
    /// </summary>
    /// <param name="command">The stroke command to resolve.</param>
    /// <param name="geometry">The prepared centerline geometry for the command.</param>
    /// <param name="resolved">Receives the resolved command state.</param>
    /// <returns><see langword="true"/> when the stroke intersects its target.</returns>
    private static bool TryResolveCommand(
        in StrokePathCommand command,
        LinearGeometry geometry,
        out ResolvedPathCommand resolved)
    {
        Matrix4x4 transform = command.Transform;
        Vector2 scale = MatrixUtilities.GetScale(transform);
        Matrix4x4 residual = MatrixUtilities.GetResidual(scale, transform);
        float widthScale = GetTransformWidthScale(transform);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        RectangleF strokeBounds = GetStrokeBounds(geometryBounds, command.Pen, widthScale);

        if (!TryResolveRasterization(
            command.RasterizerOptions,
            strokeBounds,
            command.DestinationOffset,
            command.TargetBounds,
            out RasterizerOptions rasterizerOptions,
            out Rectangle brushBounds))
        {
            resolved = default;
            return false;
        }

        resolved = new ResolvedPathCommand(
            command.SourcePath,
            command.Brush,
            command.GraphicsOptions,
            rasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? brushBounds : default,
            command.Transform);
        return true;
    }

    /// <summary>
    /// Resolves one explicit line-segment stroke's stroke-inflated raster bounds against its
    /// target and captures the state the encoder needs to emit it.
    /// </summary>
    /// <param name="command">The line-segment stroke command to resolve.</param>
    /// <param name="resolved">Receives the resolved command state.</param>
    /// <returns><see langword="true"/> when the stroke intersects its target.</returns>
    private static bool TryResolveCommand(in StrokeLineSegmentCommand command, out ResolvedLineSegmentCommand resolved)
    {
        Matrix4x4 transform = command.Transform;
        PointF start = transform.IsIdentity ? command.SourceStart : PointF.Transform(command.SourceStart, transform);
        PointF end = transform.IsIdentity ? command.SourceEnd : PointF.Transform(command.SourceEnd, transform);
        float widthScale = GetTransformWidthScale(transform);
        RectangleF segmentBounds = RectangleF.FromLTRB(
            MathF.Min(start.X, end.X),
            MathF.Min(start.Y, end.Y),
            MathF.Max(start.X, end.X),
            MathF.Max(start.Y, end.Y));
        RectangleF strokeBounds = GetStrokeBounds(segmentBounds, command.Pen, widthScale);

        if (!TryResolveRasterization(
            command.RasterizerOptions,
            strokeBounds,
            command.DestinationOffset,
            command.TargetBounds,
            out RasterizerOptions rasterizerOptions,
            out Rectangle brushBounds))
        {
            resolved = default;
            return false;
        }

        resolved = new ResolvedLineSegmentCommand(
            command.SourceStart,
            command.SourceEnd,
            command.Pen,
            command.Brush,
            command.GraphicsOptions,
            rasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? brushBounds : default);
        return true;
    }

    /// <summary>
    /// Resolves one explicit polyline stroke's stroke-inflated raster bounds against its target
    /// and captures the state the encoder needs to emit it.
    /// </summary>
    /// <param name="command">The polyline stroke command to resolve.</param>
    /// <param name="geometry">The prepared open polyline geometry for the command.</param>
    /// <param name="resolved">Receives the resolved command state.</param>
    /// <returns><see langword="true"/> when the stroke intersects its target.</returns>
    private static bool TryResolveCommand(
        in StrokePolylineCommand command,
        LinearGeometry geometry,
        out ResolvedPolylineCommand resolved)
    {
        Matrix4x4 transform = command.Transform;
        Vector2 scale = MatrixUtilities.GetScale(transform);
        Matrix4x4 residual = MatrixUtilities.GetResidual(scale, transform);
        float widthScale = GetTransformWidthScale(transform);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        RectangleF strokeBounds = GetStrokeBounds(geometryBounds, command.Pen, widthScale);

        if (!TryResolveRasterization(
            command.RasterizerOptions,
            strokeBounds,
            command.DestinationOffset,
            command.TargetBounds,
            out RasterizerOptions rasterizerOptions,
            out Rectangle brushBounds))
        {
            resolved = default;
            return false;
        }

        resolved = new ResolvedPolylineCommand(
            command.SourcePoints,
            command.Pen,
            command.Brush,
            command.GraphicsOptions,
            rasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? brushBounds : default);
        return true;
    }

    /// <summary>
    /// Resolves local command raster bounds into absolute target bounds for scene encoding.
    /// </summary>
    /// <param name="options">The local rasterizer options stored on the command.</param>
    /// <param name="bounds">The prepared local geometry bounds used for rasterization.</param>
    /// <param name="destinationOffset">The absolute destination offset for the command.</param>
    /// <param name="targetBounds">The absolute target bounds that own the command pixels.</param>
    /// <param name="resolvedOptions">The rasterizer options clipped to the owning target bounds.</param>
    /// <param name="brushBounds">The unclipped absolute brush sampling bounds.</param>
    /// <returns><see langword="true"/> when the command intersects the target bounds.</returns>
    private static bool TryResolveRasterization(
        in RasterizerOptions options,
        RectangleF bounds,
        Point destinationOffset,
        in Rectangle targetBounds,
        out RasterizerOptions resolvedOptions,
        out Rectangle brushBounds)
    {
        Rectangle localInterest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right) + 1,
            (int)MathF.Ceiling(bounds.Bottom) + 1);

        Rectangle absoluteInterest = new(
            localInterest.X + destinationOffset.X,
            localInterest.Y + destinationOffset.Y,
            localInterest.Width,
            localInterest.Height);

        Rectangle clippedDestination = Rectangle.Intersect(targetBounds, absoluteInterest);

        if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
        {
            resolvedOptions = default;
            brushBounds = default;
            return false;
        }

        resolvedOptions = new RasterizerOptions(
            clippedDestination,
            options.IntersectionRule,
            options.RasterizationMode,
            options.AntialiasThreshold,
            options.CoverageBoost);

        brushBounds = absoluteInterest;
        return true;
    }

    /// <summary>
    /// Target-resolved state for one fill or stroked-path command, painted by either a brush
    /// or an external WebGPU texture.
    /// </summary>
    private readonly struct ResolvedPathCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResolvedPathCommand"/> struct for a brush-painted command.
        /// </summary>
        /// <param name="path">The source path.</param>
        /// <param name="brush">The paint brush.</param>
        /// <param name="graphicsOptions">The graphics options used for blending.</param>
        /// <param name="rasterizerOptions">The rasterizer options clipped to the owning target.</param>
        /// <param name="destinationOffset">The absolute destination offset for the command.</param>
        /// <param name="brushBounds">The absolute brush sampling bounds; default unless the brush is an image brush.</param>
        /// <param name="transform">The full command transform.</param>
        public ResolvedPathCommand(
            IPath path,
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds,
            Matrix4x4 transform)
        {
            this.Path = path;
            this.Brush = brush;
            this.ImageSource = new GpuImageSource(brush);
            this.GraphicsOptions = graphicsOptions;
            this.RasterizerOptions = rasterizerOptions;
            this.DestinationOffset = destinationOffset;
            this.BrushBounds = brushBounds;
            this.Transform = transform;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResolvedPathCommand"/> struct for a fill
        /// sampled from an external render-time texture instead of a brush.
        /// </summary>
        /// <param name="path">The source path.</param>
        /// <param name="imageSource">The external texture source metadata.</param>
        /// <param name="graphicsOptions">The graphics options used for blending.</param>
        /// <param name="rasterizerOptions">The rasterizer options clipped to the owning target.</param>
        /// <param name="destinationOffset">The absolute destination offset for the command.</param>
        /// <param name="brushBounds">The absolute texture sampling bounds.</param>
        /// <param name="transform">The full command transform.</param>
        public ResolvedPathCommand(
            IPath path,
            in GpuImageSource imageSource,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds,
            Matrix4x4 transform)
        {
            this.Path = path;
            this.Brush = null;
            this.ImageSource = imageSource;
            this.GraphicsOptions = graphicsOptions;
            this.RasterizerOptions = rasterizerOptions;
            this.DestinationOffset = destinationOffset;
            this.BrushBounds = brushBounds;
            this.Transform = transform;
        }

        /// <summary>
        /// Gets the source path.
        /// </summary>
        public IPath Path { get; }

        /// <summary>
        /// Gets the paint brush, or <see langword="null"/> when the fill samples an external texture.
        /// </summary>
        public Brush? Brush { get; }

        /// <summary>
        /// Gets the image source metadata for image-painted fills.
        /// </summary>
        public GpuImageSource ImageSource { get; }

        /// <summary>
        /// Gets the graphics options used for blending.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the rasterizer options clipped to the owning target.
        /// </summary>
        public RasterizerOptions RasterizerOptions { get; }

        /// <summary>
        /// Gets the absolute destination offset for the command.
        /// </summary>
        public Point DestinationOffset { get; }

        /// <summary>
        /// Gets the absolute brush or texture sampling bounds.
        /// </summary>
        public Rectangle BrushBounds { get; }

        /// <summary>
        /// Gets the full command transform.
        /// </summary>
        public Matrix4x4 Transform { get; }
    }

    /// <summary>
    /// Target-resolved state for one explicit two-point line-segment stroke.
    /// </summary>
    private readonly struct ResolvedLineSegmentCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResolvedLineSegmentCommand"/> struct.
        /// </summary>
        /// <param name="start">The untransformed segment start point.</param>
        /// <param name="end">The untransformed segment end point.</param>
        /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
        /// <param name="brush">The stroke brush.</param>
        /// <param name="graphicsOptions">The graphics options used for blending.</param>
        /// <param name="rasterizerOptions">The rasterizer options clipped to the owning target.</param>
        /// <param name="destinationOffset">The absolute destination offset for the command.</param>
        /// <param name="brushBounds">The absolute brush sampling bounds; default unless the brush is an image brush.</param>
        public ResolvedLineSegmentCommand(
            PointF start,
            PointF end,
            Pen pen,
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds)
        {
            this.Start = start;
            this.End = end;
            this.Pen = pen;
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.RasterizerOptions = rasterizerOptions;
            this.DestinationOffset = destinationOffset;
            this.BrushBounds = brushBounds;
        }

        /// <summary>
        /// Gets the untransformed segment start point.
        /// </summary>
        public PointF Start { get; }

        /// <summary>
        /// Gets the untransformed segment end point.
        /// </summary>
        public PointF End { get; }

        /// <summary>
        /// Gets the pen that defines stroke width, joins, and caps.
        /// </summary>
        public Pen Pen { get; }

        /// <summary>
        /// Gets the stroke brush.
        /// </summary>
        public Brush Brush { get; }

        /// <summary>
        /// Gets the graphics options used for blending.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the rasterizer options clipped to the owning target.
        /// </summary>
        public RasterizerOptions RasterizerOptions { get; }

        /// <summary>
        /// Gets the absolute destination offset for the command.
        /// </summary>
        public Point DestinationOffset { get; }

        /// <summary>
        /// Gets the absolute brush sampling bounds.
        /// </summary>
        public Rectangle BrushBounds { get; }
    }

    /// <summary>
    /// Target-resolved state for one explicit open polyline stroke.
    /// </summary>
    private readonly struct ResolvedPolylineCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResolvedPolylineCommand"/> struct.
        /// </summary>
        /// <param name="points">The untransformed polyline points.</param>
        /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
        /// <param name="brush">The stroke brush.</param>
        /// <param name="graphicsOptions">The graphics options used for blending.</param>
        /// <param name="rasterizerOptions">The rasterizer options clipped to the owning target.</param>
        /// <param name="destinationOffset">The absolute destination offset for the command.</param>
        /// <param name="brushBounds">The absolute brush sampling bounds; default unless the brush is an image brush.</param>
        public ResolvedPolylineCommand(
            PointF[] points,
            Pen pen,
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds)
        {
            this.Points = points;
            this.Pen = pen;
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.RasterizerOptions = rasterizerOptions;
            this.DestinationOffset = destinationOffset;
            this.BrushBounds = brushBounds;
        }

        /// <summary>
        /// Gets the untransformed polyline points.
        /// </summary>
        public PointF[] Points { get; }

        /// <summary>
        /// Gets the pen that defines stroke width, joins, and caps.
        /// </summary>
        public Pen Pen { get; }

        /// <summary>
        /// Gets the stroke brush.
        /// </summary>
        public Brush Brush { get; }

        /// <summary>
        /// Gets the graphics options used for blending.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the rasterizer options clipped to the owning target.
        /// </summary>
        public RasterizerOptions RasterizerOptions { get; }

        /// <summary>
        /// Gets the absolute destination offset for the command.
        /// </summary>
        public Point DestinationOffset { get; }

        /// <summary>
        /// Gets the absolute brush sampling bounds.
        /// </summary>
        public Rectangle BrushBounds { get; }
    }

    /// <summary>
    /// Maps one prepared brush to the draw-tag consumed by the staged scene pipeline.
    /// </summary>
    /// <param name="brush">The brush to map.</param>
    /// <returns>The draw tag.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetDrawTag(Brush brush)
        => brush switch
        {
            SolidBrush => GpuSceneDrawTag.FillColor,
            RecolorBrush => GpuSceneDrawTag.FillRecolor,
            LinearGradientBrush => GpuSceneDrawTag.FillLinGradient,
            RadialGradientBrush => GpuSceneDrawTag.FillRadGradient,
            EllipticGradientBrush => GpuSceneDrawTag.FillEllipticGradient,
            SweepGradientBrush => GpuSceneDrawTag.FillSweepGradient,
            PathGradientBrush => GpuSceneDrawTag.FillPathGradient,
            PatternBrush => GpuSceneDrawTag.FillImage,
            ImageBrush => GpuSceneDrawTag.FillImage,
            _ => throw new UnreachableException($"Unsupported brush type '{brush.GetType().Name}' should have been rejected before scene encoding.")
        };

    /// <summary>
    /// Maps one resolved fill source to the draw-tag consumed by the staged scene pipeline.
    /// </summary>
    /// <param name="command">The resolved command; brushless commands sample an external texture.</param>
    /// <returns>The draw tag.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetDrawTag(in ResolvedPathCommand command)
        => command.Brush is not null ? GetDrawTag(command.Brush) : GpuSceneDrawTag.FillImage;

    /// <summary>
    /// Encodes a lowered path into path-tag and path-data streams in target-local space.
    /// </summary>
    /// <param name="destinationOffset">The absolute destination offset applied to every point.</param>
    /// <param name="geometry">The flattened geometry to encode.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the number of non-horizontal line segments encoded.</param>
    /// <param name="tileCrossings">
    /// Receives the summed conservative tile-crossing bound over every encoded segment, used to seed the
    /// segment and path-tile scratch buffers far more tightly than the draw's bounding-box diagonal.
    /// </param>
    /// <returns>The number of encoded path objects: 1, or 0 when the geometry has no segments.</returns>
    private static int EncodePath(
        Point destinationOffset,
        LinearGeometry geometry,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out long tileCrossings)
    {
        Vector2 translate = new(destinationOffset.X - rootTargetBounds.X, destinationOffset.Y - rootTargetBounds.Y);
        lineCount = 0;
        tileCrossings = 0;

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            LinearContour contour = geometry.Contours[i];

            // Each contour writes one starting point, then one end point and one line tag per
            // derived segment. Reserving the exact slice up front lets the hot loop fill the
            // contiguous output directly instead of re-checking stream capacity per word/tag.
            Span<uint> contourData = pathData.GetAppendSpan(2 + (contour.SegmentCount * 2));
            Span<byte> contourTags = pathTags.GetAppendSpan(contour.SegmentCount);

            // dataIndex tracks the next uint slot within this contour's reserved point payload.
            // The tag index is just the segment index because there is exactly one emitted tag
            // for each derived segment in the contour.
            int dataIndex = 0;
            Vector2 current = (Vector2)geometry.Points[contour.PointStart] + translate;
            contourData[dataIndex++] = BitcastSingle(current.X);
            contourData[dataIndex++] = BitcastSingle(current.Y);

            for (int j = 0; j < contour.SegmentCount; j++)
            {
                int endPointIndex = contour.PointStart + ((j + 1) == contour.PointCount ? 0 : j + 1);
                Vector2 end = (Vector2)geometry.Points[endPointIndex] + translate;
                contourData[dataIndex++] = BitcastSingle(end.X);
                contourData[dataIndex++] = BitcastSingle(end.Y);
                contourTags[j] = PackPathTag(PathTag.LineToF32);
                if (end.Y != current.Y)
                {
                    lineCount++;
                }

                tileCrossings += CountLineTileCrossings(current, end);
                current = end;
            }

            if (contour.SegmentCount > 0)
            {
                contourTags[^1] |= PackPathTag(PathTag.SubpathEnd);
            }

            // The reserved slices are staged locally until the contour is complete so the stream
            // length only advances once, after the final subpath-end marker is in place.
            pathData.Advance(contourData.Length);
            pathTags.Advance(contourTags.Length);
        }

        if (geometry.Info.SegmentCount == 0)
        {
            return 0;
        }

        pathTags.Add(PackPathTag(PathTag.Path));
        return 1;
    }

    /// <summary>
    /// Returns the number of 16x16 tiles a single line segment passes through: one starting tile plus the
    /// tile column and row boundaries crossed between the two endpoints.
    /// </summary>
    /// <param name="start">The segment start point in pixels.</param>
    /// <param name="end">The segment end point in pixels.</param>
    /// <returns>The number of tiles the segment covers.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CountLineTileCrossings(Vector2 start, Vector2 end)
    {
        Vector2 tileSize = new(TileWidth, TileHeight);
        Vector2 spanned = Vector2.Abs(Floor(end / tileSize) - Floor(start / tileSize));
        return 1 + (long)Sum(spanned);
    }

    /// <summary>
    /// Returns the component-wise floor of a vector.
    /// </summary>
    /// <param name="value">The vector to floor.</param>
    /// <returns>The floored vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 Floor(Vector2 value)
        => Vector128.Floor(value.AsVector128()).AsVector2();

    /// <summary>
    /// Returns the sum of a vector's two components.
    /// </summary>
    /// <param name="value">The vector to sum.</param>
    /// <returns>The sum of the X and Y components.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Sum(Vector2 value)
        => Vector128.Sum(value.AsVector128());

    /// <summary>
    /// Scales a stroke centerline's tile-crossing count up to a conservative bound on the tiles the GPU
    /// stroker's emitted geometry covers. The stroker expands each centerline into two offset sides plus
    /// joins and caps, and a stroke of the given width spreads each side across roughly one extra tile per
    /// 16 pixels, so the centerline count alone under-seeds the segment and path-tile scratch buffers.
    /// </summary>
    /// <param name="centerlineCrossings">The summed centerline tile-crossing count.</param>
    /// <param name="lineCount">The estimated emitted stroke line count, including joins, caps and arcs.</param>
    /// <param name="strokeWidth">The transform-scaled stroke width in pixels.</param>
    /// <returns>The conservative emitted-stroke tile-crossing bound.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ScaleStrokeTileCrossings(long centerlineCrossings, int lineCount, float strokeWidth)
    {
        long widthTiles = 1 + (long)MathF.Ceiling(MathF.Max(strokeWidth, 0F) / TileWidth);
        return (centerlineCrossings + Math.Max(lineCount, 1)) * widthTiles;
    }

    /// <summary>
    /// Encodes a stroke centerline into Vello-style path tags and path data in target-local space,
    /// applying the CPU stroker's contour preprocessing so the GPU stroker consumes the same
    /// segment list the CPU stroker strokes.
    /// </summary>
    /// <remarks>
    /// This ports <c>DefaultRasterizer.StrokeLinearizer.BuildContourSegments</c> (exact-duplicate
    /// and 1/64 px micro-segment collapse) and <c>IsContourClosedForEmission</c> (closed-contour
    /// normalization) to encode time. The GPU shader then expands clean segments with the ported
    /// CPU join and cap geometry; it never sees a degenerate-tangent segment.
    /// </remarks>
    /// <param name="geometry">The flattened centerline geometry to encode.</param>
    /// <param name="destinationOffset">The absolute destination offset applied to every point.</param>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the estimated flattened line workload for the stroke.</param>
    /// <param name="tileCrossings">Receives the conservative emitted-stroke tile-crossing bound used to seed the scratch buffers.</param>
    /// <returns>The number of encoded path objects: 1, or 0 when no contour survived preprocessing.</returns>
    private static int EncodeStrokePath(
        LinearGeometry geometry,
        Point destinationOffset,
        Pen pen,
        float widthScale,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out long tileCrossings)
    {
        float pointTranslateX = destinationOffset.X - rootTargetBounds.X;
        float pointTranslateY = destinationOffset.Y - rootTargetBounds.Y;
        Vector2 translate = new(pointTranslateX, pointTranslateY);
        lineCount = EstimateStrokeLineCount(geometry, pen, widthScale);
        tileCrossings = 0;
        float strokeWidth = pen.StrokeWidth * widthScale;
        int encodedContourCount = 0;
        IReadOnlyList<PointF> geometryPoints = geometry.Points;

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            LinearContour contour = geometry.Contours[i];
            if (contour.PointCount == 0)
            {
                continue;
            }

            PointF[] kept = ArrayPool<PointF>.Shared.Rent(contour.PointCount);
            try
            {
                // CPU stroker preprocessing: collapse exact duplicates and micro-segments below
                // 1/64 px forward onto the last kept point.
                int keptCount = 0;
                PointF firstPoint = geometryPoints[contour.PointStart];
                kept[keptCount++] = firstPoint;
                PointF pointLike = firstPoint;
                for (int j = 1; j < contour.PointCount; j++)
                {
                    PointF point = geometryPoints[contour.PointStart + j];
                    PointF previous = kept[keptCount - 1];
                    if (point == previous)
                    {
                        continue;
                    }

                    if (Vector2.DistanceSquared(previous, point) > StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon)
                    {
                        kept[keptCount++] = point;
                    }

                    pointLike = point;
                }

                int segmentCount = keptCount - 1;

                // CPU stroker closed-contour normalization.
                bool isClosed = false;
                if (contour.PointCount >= 3)
                {
                    PointF last = geometryPoints[contour.PointStart + contour.PointCount - 1];
                    float closeThreshold = MathF.Max(strokeWidth, 1E-3F);
                    isClosed = contour.IsClosed ||
                        firstPoint == last ||
                        Vector2.DistanceSquared(firstPoint, last) <= closeThreshold * closeThreshold;
                }

                bool duplicateClosingPoint = keptCount > 1 && kept[keptCount - 1] == kept[0];
                int distinctPointCount = keptCount - (isClosed && duplicateClosingPoint ? 1 : 0);

                if (segmentCount == 0)
                {
                    EncodePointStrokeContour(pointLike, pointTranslateX, pointTranslateY, ref pathTags, ref pathData);
                    Vector2 pointCenter = (Vector2)pointLike + translate;
                    Vector2 pointHalf = new(PointStrokeSegmentHalfLength, 0F);
                    tileCrossings += CountLineTileCrossings(pointCenter - pointHalf, pointCenter + pointHalf);

                    encodedContourCount++;
                    continue;
                }

                if (segmentCount == 1 || distinctPointCount == 2)
                {
                    // The CPU stroker emits these as one capped open segment even when declared closed.
                    EncodeOpenStrokeContour(kept.AsSpan(0, 2), pointTranslateX, pointTranslateY, ref pathTags, ref pathData);
                    tileCrossings += CountLineTileCrossings((Vector2)kept[0] + translate, (Vector2)kept[1] + translate);

                    encodedContourCount++;
                    continue;
                }

                if (isClosed)
                {
                    int emitCount = duplicateClosingPoint ? keptCount - 1 : keptCount;

                    // A closing segment exists when the contour ends exactly on its first point or
                    // the gap back to it is a real (non micro) segment; a sub-1/64 gap is bridged
                    // by the wrap join, matching the CPU stroker.
                    bool closingSegment = duplicateClosingPoint ||
                        (segmentCount > 1 &&
                         Vector2.DistanceSquared(kept[keptCount - 1], kept[0]) > StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon);

                    int linetoCount = (emitCount - 1) + (closingSegment ? 1 : 0);
                    Span<uint> contourData = pathData.GetAppendSpan(2 + (linetoCount * 2) + 2);
                    Span<byte> contourTags = pathTags.GetAppendSpan(linetoCount + 1);

                    int dataIndex = 0;
                    int tagIndex = 0;
                    contourData[dataIndex++] = BitcastSingle(kept[0].X + pointTranslateX);
                    contourData[dataIndex++] = BitcastSingle(kept[0].Y + pointTranslateY);
                    for (int j = 1; j < emitCount; j++)
                    {
                        contourData[dataIndex++] = BitcastSingle(kept[j].X + pointTranslateX);
                        contourData[dataIndex++] = BitcastSingle(kept[j].Y + pointTranslateY);
                        contourTags[tagIndex++] = PackPathTag(PathTag.LineToF32);
                    }

                    PointF lastEndpoint = kept[emitCount - 1];
                    if (closingSegment)
                    {
                        contourData[dataIndex++] = BitcastSingle(kept[0].X + pointTranslateX);
                        contourData[dataIndex++] = BitcastSingle(kept[0].Y + pointTranslateY);
                        contourTags[tagIndex++] = PackPathTag(PathTag.LineToF32);
                        lastEndpoint = kept[0];
                    }

                    // Closed-stroke marker: carries the first segment's tangent and length so the
                    // wrap join at the closing corner matches the CPU stroker's join arguments.
                    contourData[dataIndex++] = BitcastSingle(lastEndpoint.X + (kept[1].X - kept[0].X) + pointTranslateX);
                    contourData[dataIndex] = BitcastSingle(lastEndpoint.Y + (kept[1].Y - kept[0].Y) + pointTranslateY);
                    contourTags[tagIndex] = PackPathTag(PathTag.LineToF32 | PathTag.SubpathEnd);

                    pathData.Advance(contourData.Length);
                    pathTags.Advance(contourTags.Length);

                    Vector2 current = (Vector2)kept[0] + translate;
                    for (int j = 1; j < emitCount; j++)
                    {
                        Vector2 next = (Vector2)kept[j] + translate;
                        tileCrossings += CountLineTileCrossings(current, next);
                        current = next;
                    }

                    if (closingSegment)
                    {
                        tileCrossings += CountLineTileCrossings(current, (Vector2)kept[0] + translate);
                    }

                    encodedContourCount++;
                    continue;
                }

                EncodeOpenStrokeContour(kept.AsSpan(0, keptCount), pointTranslateX, pointTranslateY, ref pathTags, ref pathData);
                for (int j = 0; j < keptCount - 1; j++)
                {
                    tileCrossings += CountLineTileCrossings((Vector2)kept[j] + translate, (Vector2)kept[j + 1] + translate);
                }

                encodedContourCount++;
            }
            finally
            {
                ArrayPool<PointF>.Shared.Return(kept);
            }
        }

        if (encodedContourCount == 0)
        {
            return 0;
        }

        // The loop counted centerline tiles; the GPU stroker emits offset sides, joins and caps, so
        // scale the count up to the emitted geometry to avoid under-seeding the segment scratch.
        tileCrossings = ScaleStrokeTileCrossings(tileCrossings, lineCount, strokeWidth);
        pathTags.Add(PackPathTag(PathTag.Path));
        return 1;
    }

    /// <summary>
    /// Encodes one open (capped) stroke centerline contour from preprocessed points.
    /// </summary>
    /// <param name="points">The preprocessed contour points; at least two.</param>
    /// <param name="pointTranslateX">The x translation to target-local space.</param>
    /// <param name="pointTranslateY">The y translation to target-local space.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    private static void EncodeOpenStrokeContour(
        scoped ReadOnlySpan<PointF> points,
        float pointTranslateX,
        float pointTranslateY,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData)
    {
        int linetoCount = points.Length - 1;
        Span<uint> contourData = pathData.GetAppendSpan(2 + (linetoCount * 2) + 4);
        Span<byte> contourTags = pathTags.GetAppendSpan(linetoCount + 1);

        int dataIndex = 0;
        int tagIndex = 0;
        contourData[dataIndex++] = BitcastSingle(points[0].X + pointTranslateX);
        contourData[dataIndex++] = BitcastSingle(points[0].Y + pointTranslateY);
        for (int j = 1; j < points.Length; j++)
        {
            contourData[dataIndex++] = BitcastSingle(points[j].X + pointTranslateX);
            contourData[dataIndex++] = BitcastSingle(points[j].Y + pointTranslateY);
            contourTags[tagIndex++] = PackPathTag(PathTag.LineToF32);
        }

        // Open-stroke cap marker: stores the subpath start point and its outgoing tangent point.
        PointF tangentPoint = GetStrokeMarkerTangentPoint(points[0], points[1]);
        contourData[dataIndex++] = BitcastSingle(points[0].X + pointTranslateX);
        contourData[dataIndex++] = BitcastSingle(points[0].Y + pointTranslateY);
        contourData[dataIndex++] = BitcastSingle(tangentPoint.X + pointTranslateX);
        contourData[dataIndex] = BitcastSingle(tangentPoint.Y + pointTranslateY);
        contourTags[tagIndex] = PackPathTag(PathTag.QuadToF32 | PathTag.SubpathEnd);

        pathData.Advance(contourData.Length);
        pathTags.Advance(contourTags.Length);
    }

    /// <summary>
    /// Encodes one point-like stroke contour as a tiny centered open segment.
    /// </summary>
    /// <param name="point">The point the contour collapsed to.</param>
    /// <param name="pointTranslateX">The x translation to target-local space.</param>
    /// <param name="pointTranslateY">The y translation to target-local space.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    private static void EncodePointStrokeContour(
        PointF point,
        float pointTranslateX,
        float pointTranslateY,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData)
    {
        PointF start = new(point.X - PointStrokeSegmentHalfLength, point.Y);
        PointF end = new(point.X + PointStrokeSegmentHalfLength, point.Y);
        PointF tangentEnd = GetStrokeMarkerTangentPoint(start, end);

        Span<uint> contourData = pathData.GetAppendSpan(8);
        Span<byte> contourTags = pathTags.GetAppendSpan(2);
        contourData[0] = BitcastSingle(start.X + pointTranslateX);
        contourData[1] = BitcastSingle(start.Y + pointTranslateY);
        contourData[2] = BitcastSingle(end.X + pointTranslateX);
        contourData[3] = BitcastSingle(end.Y + pointTranslateY);
        contourTags[0] = PackPathTag(PathTag.LineToF32);
        contourData[4] = BitcastSingle(start.X + pointTranslateX);
        contourData[5] = BitcastSingle(start.Y + pointTranslateY);
        contourData[6] = BitcastSingle(tangentEnd.X + pointTranslateX);
        contourData[7] = BitcastSingle(tangentEnd.Y + pointTranslateY);
        contourTags[1] = PackPathTag(PathTag.QuadToF32 | PathTag.SubpathEnd);
        pathData.Advance(contourData.Length);
        pathTags.Advance(contourTags.Length);
    }

    /// <summary>
    /// Encodes one explicit open two-point stroke centerline into Vello-style path tags and path data in target-local space.
    /// </summary>
    /// <param name="start">The segment start point with the transform scale applied.</param>
    /// <param name="end">The segment end point with the transform scale applied.</param>
    /// <param name="destinationOffset">The absolute destination offset applied to every point.</param>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the estimated flattened line workload for the stroke.</param>
    /// <param name="tileCrossings">Receives the conservative emitted-stroke tile-crossing bound used to seed the scratch buffers.</param>
    /// <returns>The number of encoded path objects; always 1.</returns>
    private static int EncodeOpenSegmentStrokePath(
        PointF start,
        PointF end,
        Point destinationOffset,
        Pen pen,
        float widthScale,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out long tileCrossings)
    {
        float pointTranslateX = destinationOffset.X - rootTargetBounds.X;
        float pointTranslateY = destinationOffset.Y - rootTargetBounds.Y;
        NormalizePointStrokeSegment(ref start, ref end);
        Span<PointF> segmentPoints = [start, end];
        EncodeOpenStrokeContour(segmentPoints, pointTranslateX, pointTranslateY, ref pathTags, ref pathData);
        pathTags.Add(PackPathTag(PathTag.Path));
        lineCount = EstimateStrokeLineCountForOpenSegment(pen, widthScale);
        Vector2 translate = new(pointTranslateX, pointTranslateY);
        tileCrossings = ScaleStrokeTileCrossings(
            CountLineTileCrossings((Vector2)start + translate, (Vector2)end + translate),
            lineCount,
            pen.StrokeWidth * widthScale);

        return 1;
    }

    /// <summary>
    /// Replaces a point-like explicit stroke segment with the tiny tangent segment used for point strokes.
    /// </summary>
    /// <param name="start">The segment start point; replaced when the segment is point-like.</param>
    /// <param name="end">The segment end point; replaced when the segment is point-like.</param>
    private static void NormalizePointStrokeSegment(ref PointF start, ref PointF end)
    {
        if (Vector2.DistanceSquared(start, end) > StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon)
        {
            return;
        }

        PointF center = new((start.X + end.X) * 0.5F, (start.Y + end.Y) * 0.5F);
        start = new PointF(center.X - PointStrokeSegmentHalfLength, center.Y);
        end = new PointF(center.X + PointStrokeSegmentHalfLength, center.Y);
    }

    /// <summary>
    /// Encodes a rectangle as a fixed-size closed path.
    /// </summary>
    /// <param name="rectangle">The rectangle to encode.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the number of non-horizontal line segments encoded.</param>
    /// <returns>The number of encoded path objects: 1, or 0 when the rectangle is degenerate.</returns>
    private static int EncodeRectanglePath(
        in Rectangle rectangle,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount)
        => EncodeRectanglePath(new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), ref pathTags, ref pathData, out lineCount);

    /// <summary>
    /// Encodes the Vello empty path shape required by begin-clip records with no drawable geometry.
    /// </summary>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    private static void EncodeEmptyBeginClipPath(
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData)
    {
        // Vello reserves a bare Path tag for EndClip dummy paths. Empty BeginClip paths still
        // carry one degenerate segment so the path scan has a real path object to associate
        // with the BeginClip draw record.
        Span<uint> data = pathData.GetAppendSpan(4);
        Span<byte> tags = pathTags.GetAppendSpan(2);
        data.Clear();
        tags[0] = PackPathTag(PathTag.LineToF32 | PathTag.SubpathEnd);
        tags[1] = PackPathTag(PathTag.Path);

        pathData.Advance(data.Length);
        pathTags.Advance(tags.Length);
    }

    /// <summary>
    /// Encodes a floating-point rectangle as a fixed-size closed path.
    /// </summary>
    /// <param name="rectangle">The rectangle to encode.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the number of non-horizontal line segments encoded.</param>
    /// <returns>The number of encoded path objects: 1, or 0 when the rectangle is degenerate.</returns>
    private static int EncodeRectanglePath(
        in RectangleF rectangle,
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

        Span<uint> data = pathData.GetAppendSpan(10);
        Span<byte> tags = pathTags.GetAppendSpan(5);
        data[0] = BitcastSingle(left);
        data[1] = BitcastSingle(top);

        data[2] = BitcastSingle(right);
        data[3] = BitcastSingle(top);
        tags[0] = PackPathTag(PathTag.LineToF32);

        data[4] = BitcastSingle(right);
        data[5] = BitcastSingle(bottom);
        tags[1] = PackPathTag(PathTag.LineToF32);

        data[6] = BitcastSingle(left);
        data[7] = BitcastSingle(bottom);
        tags[2] = PackPathTag(PathTag.LineToF32);

        data[8] = BitcastSingle(left);
        data[9] = BitcastSingle(top);
        tags[3] = PackPathTag(PathTag.LineToF32 | PathTag.SubpathEnd);

        tags[4] = PackPathTag(PathTag.Path);

        pathData.Advance(data.Length);
        pathTags.Advance(tags.Length);
        lineCount = 2;
        return 1;
    }

    /// <summary>
    /// Gets the number of rectangle contours represented by a normalized clip descriptor.
    /// </summary>
    /// <param name="descriptor">The clip descriptor to inspect.</param>
    /// <returns>The non-degenerate rectangle count; 0 for non-rectangular descriptors.</returns>
    private static int GetRectangleClipCount(in DrawingClipDescriptor descriptor)
        => descriptor.Kind switch
        {
            DrawingClipKind.Rectangle => descriptor.Rectangle.Width > 0 && descriptor.Rectangle.Height > 0 ? 1 : 0,
            DrawingClipKind.IntegerRegion => GetPositiveIntegerRectangleCount(descriptor.IntegerRectangles),
            DrawingClipKind.Region => GetPositiveRectangleCount(descriptor.Rectangles),
            _ => 0
        };

    /// <summary>
    /// Encodes a rectangle or rectangle-set clip descriptor as one path with multiple contours.
    /// </summary>
    /// <param name="descriptor">The rectangular clip descriptor to encode.</param>
    /// <param name="destinationOffset">The absolute destination offset applied to every rectangle.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the number of non-horizontal line segments encoded.</param>
    /// <returns>The number of encoded path objects: 1, or 0 when every rectangle is degenerate.</returns>
    private static int EncodeRectangleClipPath(
        in DrawingClipDescriptor descriptor,
        Point destinationOffset,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount)
    {
        float translateX = destinationOffset.X - rootTargetBounds.X;
        float translateY = destinationOffset.Y - rootTargetBounds.Y;
        lineCount = 0;

        switch (descriptor.Kind)
        {
            case DrawingClipKind.Rectangle:
            {
                if (descriptor.Rectangle.Width > 0 && descriptor.Rectangle.Height > 0)
                {
                    AppendRectangleContour(descriptor.Rectangle, translateX, translateY, ref pathTags, ref pathData);
                    lineCount = 2;
                }

                break;
            }

            case DrawingClipKind.IntegerRegion:
            {
                IReadOnlyList<Rectangle> rectangles = descriptor.IntegerRectangles;
                for (int i = 0; i < rectangles.Count; i++)
                {
                    Rectangle rectangle = rectangles[i];
                    if (rectangle.Width <= 0 || rectangle.Height <= 0)
                    {
                        continue;
                    }

                    AppendRectangleContour(new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), translateX, translateY, ref pathTags, ref pathData);
                    lineCount += 2;
                }

                break;
            }

            case DrawingClipKind.Region:
            {
                IReadOnlyList<RectangleF> rectangles = descriptor.Rectangles;
                for (int i = 0; i < rectangles.Count; i++)
                {
                    RectangleF rectangle = rectangles[i];
                    if (rectangle.Width <= 0 || rectangle.Height <= 0)
                    {
                        continue;
                    }

                    AppendRectangleContour(rectangle, translateX, translateY, ref pathTags, ref pathData);
                    lineCount += 2;
                }

                break;
            }
        }

        if (lineCount == 0)
        {
            return 0;
        }

        pathTags.Add(PackPathTag(PathTag.Path));
        return 1;
    }

    /// <summary>
    /// Counts region rectangles that can produce non-degenerate GPU path contours.
    /// </summary>
    /// <param name="rectangles">The region rectangles to count.</param>
    /// <returns>The number of rectangles with positive width and height.</returns>
    private static int GetPositiveIntegerRectangleCount(IReadOnlyList<Rectangle> rectangles)
    {
        int count = 0;
        for (int i = 0; i < rectangles.Count; i++)
        {
            Rectangle rectangle = rectangles[i];
            if (rectangle.Width > 0 && rectangle.Height > 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts region rectangles that can produce non-degenerate GPU path contours.
    /// </summary>
    /// <param name="rectangles">The region rectangles to count.</param>
    /// <returns>The number of rectangles with positive width and height.</returns>
    private static int GetPositiveRectangleCount(IReadOnlyList<RectangleF> rectangles)
    {
        int count = 0;
        for (int i = 0; i < rectangles.Count; i++)
        {
            RectangleF rectangle = rectangles[i];
            if (rectangle.Width > 0 && rectangle.Height > 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Appends one rectangle contour to a pending path without terminating the path.
    /// </summary>
    /// <param name="rectangle">The rectangle to append.</param>
    /// <param name="translateX">The x translation to target-local space.</param>
    /// <param name="translateY">The y translation to target-local space.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    private static void AppendRectangleContour(
        RectangleF rectangle,
        float translateX,
        float translateY,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData)
    {
        float left = rectangle.Left + translateX;
        float top = rectangle.Top + translateY;
        float right = rectangle.Right + translateX;
        float bottom = rectangle.Bottom + translateY;

        Span<uint> data = pathData.GetAppendSpan(10);
        Span<byte> tags = pathTags.GetAppendSpan(4);
        data[0] = BitcastSingle(left);
        data[1] = BitcastSingle(top);

        data[2] = BitcastSingle(right);
        data[3] = BitcastSingle(top);
        tags[0] = PackPathTag(PathTag.LineToF32);

        data[4] = BitcastSingle(right);
        data[5] = BitcastSingle(bottom);
        tags[1] = PackPathTag(PathTag.LineToF32);

        data[6] = BitcastSingle(left);
        data[7] = BitcastSingle(bottom);
        tags[2] = PackPathTag(PathTag.LineToF32);

        data[8] = BitcastSingle(left);
        data[9] = BitcastSingle(top);
        tags[3] = PackPathTag(PathTag.LineToF32 | PathTag.SubpathEnd);

        pathData.Advance(data.Length);
        pathTags.Advance(tags.Length);
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one plain fill item.
    /// </summary>
    /// <param name="contourCount">The number of geometry contours.</param>
    /// <param name="segmentCount">The number of geometry segments.</param>
    /// <param name="drawTag">The draw tag selected for the item.</param>
    /// <param name="appendStyle">Whether a new style record will be emitted.</param>
    /// <param name="pathGradientDataWordCount">The path-gradient payload word count for the item's brush.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="drawTags">The draw-tag stream.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="styles">The style stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="pathGradientData">The path-gradient payload stream.</param>
    private static void ReservePlainFillCapacity(
        int contourCount,
        int segmentCount,
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
    {
        int pathTagAdd = segmentCount + 1 + (appendStyle ? 1 : 0);
        int pathDataAdd = (contourCount * 2) + (segmentCount * 2);

        pathTags.EnsureAdditionalCapacity(pathTagAdd);
        pathData.EnsureAdditionalCapacity(pathDataAdd);
        drawTags.EnsureAdditionalCapacity(1);
        drawData.EnsureAdditionalCapacity(GetDrawDataWordCount(drawTag));

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }

        if (DrawTagUsesGradientRamp(drawTag))
        {
            gradientPixels.EnsureAdditionalCapacity(GradientRowWordCount);
        }

        if (pathGradientDataWordCount > 0)
        {
            pathGradientData.EnsureAdditionalCapacity(pathGradientDataWordCount);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one plain fill item.
    /// </summary>
    /// <param name="geometryInfo">The geometry information supplying contour and segment counts.</param>
    /// <param name="drawTag">The draw tag selected for the item.</param>
    /// <param name="appendStyle">Whether a new style record will be emitted.</param>
    /// <param name="pathGradientDataWordCount">The path-gradient payload word count for the item's brush.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="drawTags">The draw-tag stream.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="styles">The style stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="pathGradientData">The path-gradient payload stream.</param>
    private static void ReservePlainFillCapacity(
        LinearGeometryInfo geometryInfo,
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
        => ReservePlainFillCapacity(
            geometryInfo.ContourCount,
            geometryInfo.SegmentCount,
            drawTag,
            appendStyle,
            pathGradientDataWordCount,
            ref pathTags,
            ref pathData,
            ref drawTags,
            ref drawData,
            ref styles,
            ref gradientPixels,
            ref pathGradientData);

    /// <summary>
    /// Reserves the exact stream growth needed for one stroke item.
    /// </summary>
    /// <param name="geometry">The centerline geometry supplying contour and segment counts.</param>
    /// <param name="drawTag">The draw tag selected for the item.</param>
    /// <param name="appendStyle">Whether a new style record will be emitted.</param>
    /// <param name="pathGradientDataWordCount">The path-gradient payload word count for the item's brush.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="drawTags">The draw-tag stream.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="styles">The style stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="pathGradientData">The path-gradient payload stream.</param>
    private static void ReservePlainStrokeCapacity(
        LinearGeometry geometry,
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
    {
        int markerTagCount = geometry.Info.ContourCount;
        int markerWordCount = 0;
        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            markerWordCount += geometry.Contours[i].IsClosed ? 2 : 4;
        }

        int pathTagAdd = geometry.Info.SegmentCount + markerTagCount + 1 + (appendStyle ? 1 : 0);
        int pathDataAdd = (geometry.Info.ContourCount * 2) + (geometry.Info.SegmentCount * 2) + markerWordCount;

        pathTags.EnsureAdditionalCapacity(pathTagAdd);
        pathData.EnsureAdditionalCapacity(pathDataAdd);
        drawTags.EnsureAdditionalCapacity(1);
        drawData.EnsureAdditionalCapacity(GetDrawDataWordCount(drawTag));

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }

        if (DrawTagUsesGradientRamp(drawTag))
        {
            gradientPixels.EnsureAdditionalCapacity(GradientRowWordCount);
        }

        if (pathGradientDataWordCount > 0)
        {
            pathGradientData.EnsureAdditionalCapacity(pathGradientDataWordCount);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one explicit open line segment stroke.
    /// </summary>
    /// <param name="drawTag">The draw tag selected for the item.</param>
    /// <param name="appendStyle">Whether a new style record will be emitted.</param>
    /// <param name="pathGradientDataWordCount">The path-gradient payload word count for the item's brush.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="drawTags">The draw-tag stream.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="styles">The style stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="pathGradientData">The path-gradient payload stream.</param>
    private static void ReservePlainStrokeCapacityForOpenSegment(
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
    {
        pathTags.EnsureAdditionalCapacity(3 + (appendStyle ? 1 : 0));
        pathData.EnsureAdditionalCapacity(8);
        drawTags.EnsureAdditionalCapacity(1);
        drawData.EnsureAdditionalCapacity(GetDrawDataWordCount(drawTag));

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }

        if (DrawTagUsesGradientRamp(drawTag))
        {
            gradientPixels.EnsureAdditionalCapacity(GradientRowWordCount);
        }

        if (pathGradientDataWordCount > 0)
        {
            pathGradientData.EnsureAdditionalCapacity(pathGradientDataWordCount);
        }
    }

    /// <summary>
    /// Estimates the flattened line workload for one stroke.
    /// </summary>
    /// <param name="geometry">The centerline geometry to estimate.</param>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The conservative line count; at least 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateStrokeLineCount(LinearGeometry geometry, Pen pen, float widthScale)
    {
        int joinCost = GetStrokeJoinLineCost(pen, widthScale);
        int capCost = GetStrokeCapLineCost(pen, widthScale);
        int total = 0;

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            LinearContour contour = geometry.Contours[i];

            total += contour.SegmentCount * 2;
            if (contour.IsClosed)
            {
                total += contour.SegmentCount * joinCost;
            }
            else
            {
                total += Math.Max(contour.SegmentCount - 1, 0) * joinCost;
                total += capCost * 2;
            }
        }

        return Math.Max(total, 1);
    }

    /// <summary>
    /// Estimates the flattened line workload for one explicit open two-point stroke segment.
    /// </summary>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The conservative line count; at least 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateStrokeLineCountForOpenSegment(Pen pen, float widthScale)
        => Math.Max(2 + (GetStrokeCapLineCost(pen, widthScale) * 2), 1);

    /// <summary>
    /// Returns the conservative flattened line cost of one stroke join.
    /// </summary>
    /// <param name="pen">The pen that defines the join style.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The line cost of one join.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeJoinLineCost(Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;
        int outerCost = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter => 3,
            LineJoin.MiterRound => GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale),
            LineJoin.MiterRevert => 2,
            LineJoin.Round => GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale),
            _ => 2
        };
        return outerCost + 2;
    }

    /// <summary>
    /// Returns the conservative flattened line cost of one stroke cap.
    /// </summary>
    /// <param name="pen">The pen that defines the cap style.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The line cost of one cap.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeCapLineCost(Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;
        return pen.StrokeOptions.LineCap switch
        {
            LineCap.Square => 3,
            LineCap.Round => GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale),
            _ => 1
        };
    }

    /// <summary>
    /// Returns the conservative flattened line cost of one round arc in the stroke shaders.
    /// </summary>
    /// <param name="radius">The arc radius in pixels.</param>
    /// <param name="angle">The swept angle in radians.</param>
    /// <param name="arcDetailScale">The pen's arc detail scale.</param>
    /// <returns>The line cost of the arc; at least 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeArcLineCost(float radius, float angle, double arcDetailScale)
    {
        float safeRadius = Math.Max(radius, 0.25F);
        float safeScale = (float)Math.Max(arcDetailScale, 0.01D);
        float ratio = Math.Clamp(safeRadius / (safeRadius + (0.125F / safeScale)), -1F, 1F);
        float theta = Math.Max(0.0001F, 2F * MathF.Acos(ratio));
        return Math.Max(1, (int)MathF.Ceiling(angle / theta));
    }

    /// <summary>
    /// Returns the encoded tangent point used by the staged stroker's cap marker segment for one explicit open line segment.
    /// </summary>
    /// <param name="start">The subpath start point.</param>
    /// <param name="end">The first segment end point.</param>
    /// <returns>The tangent point, one third of the way from <paramref name="start"/> to <paramref name="end"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF GetStrokeMarkerTangentPoint(PointF start, PointF end)
        => new(
            start.X + ((end.X - start.X) / 3F),
            start.Y + ((end.Y - start.Y) / 3F));

    /// <summary>
    /// Reserves the exact stream growth needed for one begin-layer clip item.
    /// </summary>
    /// <param name="appendStyle">Whether a new style record will be emitted.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="drawTags">The draw-tag stream.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="styles">The style stream.</param>
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
        // top-left, top-right, bottom-right, bottom-left, then top-left again.
        // Each point is two uint words, so 5 * 2 = 10 words.
        pathData.EnsureAdditionalCapacity(10);
        drawTags.EnsureAdditionalCapacity(1);

        // BeginClip emits two draw-data words for blend mode / alpha state.
        drawData.EnsureAdditionalCapacity(2);

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one end-layer item.
    /// </summary>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="drawTags">The draw-tag stream.</param>
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
    /// <param name="drawTag">The draw tag to size.</param>
    /// <returns>The draw-data word count.</returns>
    private static int GetDrawDataWordCount(uint drawTag)
        => drawTag switch
        {
            // Each case matches the exact number of uint words written by AppendDrawData.
            GpuSceneDrawTag.BeginClip => 2,
            GpuSceneDrawTag.FillColor => 2,
            GpuSceneDrawTag.FillRecolor => 1,
            GpuSceneDrawTag.FillLinGradient => 5,
            GpuSceneDrawTag.FillRadGradient => 7,
            GpuSceneDrawTag.FillEllipticGradient => 7,
            GpuSceneDrawTag.FillSweepGradient => 5,
            GpuSceneDrawTag.FillPathGradient => 4,
            GpuSceneDrawTag.FillImage => 5,
            GpuSceneDrawTag.EndClip => 0,
            _ => throw new UnreachableException($"Unsupported draw tag '{drawTag}' reached draw-data sizing.")
        };

    /// <summary>
    /// Gets the exact auxiliary brush-data word count emitted for one brush.
    /// </summary>
    /// <param name="brush">The brush to size.</param>
    /// <returns>The payload word count; 0 for brushes without auxiliary data.</returns>
    private static int GetPathGradientDataWordCount(Brush brush)
        => brush switch
        {
            RecolorBrush => RecolorDataWordCount,
            PathGradientBrush pathGradientBrush => PathGradientHeaderWordCount + (pathGradientBrush.Points.Length * PathGradientEdgeWordCount),
            _ => 0
        };

    /// <summary>
    /// Gets the exact path-gradient payload word count emitted for one resolved fill source.
    /// </summary>
    /// <param name="command">The resolved command to size.</param>
    /// <returns>The payload word count; 0 for brushless or non-path-gradient sources.</returns>
    private static int GetPathGradientDataWordCount(in ResolvedPathCommand command)
        => command.Brush is not null ? GetPathGradientDataWordCount(command.Brush) : 0;

    /// <summary>
    /// Returns a value indicating whether the draw tag emits one gradient ramp row.
    /// </summary>
    /// <param name="drawTag">The draw tag to test.</param>
    /// <returns><see langword="true"/> when the tag samples a gradient ramp.</returns>
    private static bool DrawTagUsesGradientRamp(uint drawTag)
        => drawTag is GpuSceneDrawTag.FillLinGradient
            or GpuSceneDrawTag.FillRadGradient
            or GpuSceneDrawTag.FillEllipticGradient
            or GpuSceneDrawTag.FillSweepGradient;

    /// <summary>
    /// Appends one 2x3 affine transform to the packed transform stream.
    /// </summary>
    /// <param name="transform">The transform to append.</param>
    /// <param name="transforms">The transform stream.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendTransform(GpuSceneTransform transform, ref OwnedStream<uint> transforms)
    {
        transforms.Add(BitcastSingle(transform.M11));
        transforms.Add(BitcastSingle(transform.M12));
        transforms.Add(BitcastSingle(transform.M21));
        transforms.Add(BitcastSingle(transform.M22));
        transforms.Add(BitcastSingle(transform.Tx));
        transforms.Add(BitcastSingle(transform.Ty));
        transforms.Add(BitcastSingle(transform.M14));
        transforms.Add(BitcastSingle(transform.M24));
        transforms.Add(BitcastSingle(transform.M44));
    }

    /// <summary>
    /// Appends the identity transform to the packed transform stream.
    /// </summary>
    /// <param name="transforms">The transform stream.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendIdentityTransform(ref OwnedStream<uint> transforms)
        => AppendTransform(GpuSceneTransform.Identity, ref transforms);

    /// <summary>
    /// Packs the style words that describe stroke behavior for one draw record.
    /// </summary>
    /// <remarks>
    /// Word layout consumed by flatten.wgsl: 0 = style flags, 1 = device-space stroke width,
    /// 2 = packed draw flags, 3 = miter limit, 4 = arc detail scale, 5 = coverage parameter,
    /// 6-9 = target-local interest rectangle as left, top, right, bottom.
    /// The coverage parameter carries the quantization threshold for aliased strokes and zero
    /// for antialiased strokes: fine interprets the antialiased slot as the perceptual coverage
    /// boost, which never applies to strokes.
    /// </remarks>
    /// <param name="options">The graphics options used for blending.</param>
    /// <param name="pen">The pen that defines stroke width, joins, caps, and miter limit.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <param name="interestBounds">The target-local raster interest bounds.</param>
    /// <returns>The ten packed style words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (
        uint Style0,
        uint Style1,
        uint Style2,
        uint Style3,
        uint Style4,
        uint Style5,
        uint Style6,
        uint Style7,
        uint Style8,
        uint Style9) GetStrokeStyle(
        GraphicsOptions options,
        Pen pen,
        float widthScale,
        Rectangle interestBounds)
    {
        StyleFlags styleFlags = StyleFlags.Stroke
            | EncodeStrokeJoinFlags(pen.StrokeOptions.LineJoin)
            | EncodeStrokeCapFlags(pen.StrokeOptions.LineCap);

        return (
            (uint)styleFlags,
            BitcastSingle(pen.StrokeWidth * widthScale),
            PackStyleDrawFlags(options),
            BitcastSingle((float)pen.StrokeOptions.MiterLimit),
            BitcastSingle((float)pen.StrokeOptions.ArcDetailScale),
            BitcastSingle(options.Antialias ? 0F : options.AntialiasThreshold),
            BitcastSingle(interestBounds.Left),
            BitcastSingle(interestBounds.Top),
            BitcastSingle(interestBounds.Right),
            BitcastSingle(interestBounds.Bottom));
    }

    /// <summary>
    /// Returns the isotropic scale factor embedded in a drawing transform so stroke widths match device-space pixels.
    /// </summary>
    /// <param name="transform">The transform to inspect.</param>
    /// <returns>The scale factor to apply to stroke widths.</returns>
    /// <remarks>
    /// Uses the square root of the absolute 2D determinant, the SVG-style fallback for non-uniform
    /// scale. Reduces to the uniform scale for pure scale/rotate/translate matrices.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetTransformWidthScale(Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return 1F;
        }

        float det = (transform.M11 * transform.M22) - (transform.M12 * transform.M21);
        return MathF.Sqrt(MathF.Abs(det));
    }

    /// <summary>
    /// Packs the stroke join flags consumed by the staged flatten shader.
    /// </summary>
    /// <param name="lineJoin">The pen join style.</param>
    /// <returns>The join style flags.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StyleFlags EncodeStrokeJoinFlags(LineJoin lineJoin)
        => lineJoin switch
        {
            LineJoin.Miter => StyleFlags.JoinMiter,
            LineJoin.MiterRevert => StyleFlags.JoinMiter | StyleFlags.JoinMiterRevert,
            LineJoin.MiterRound => StyleFlags.JoinMiter | StyleFlags.JoinMiterRound,
            LineJoin.Round => StyleFlags.JoinRound,
            _ => StyleFlags.None
        };

    /// <summary>
    /// Packs the start and end cap flags consumed by the staged flatten shader.
    /// </summary>
    /// <param name="lineCap">The pen cap style, applied to both segment ends.</param>
    /// <returns>The cap style flags.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StyleFlags EncodeStrokeCapFlags(LineCap lineCap)
        => lineCap switch
        {
            LineCap.Square => StyleFlags.StartCapSquare | StyleFlags.EndCapSquare,
            LineCap.Round => StyleFlags.StartCapRound | StyleFlags.EndCapRound,
            _ => StyleFlags.None
        };

    /// <summary>
    /// Packs the style words that describe fill behavior for one draw record.
    /// </summary>
    /// <remarks>
    /// Shares the stroke style word layout with the stroke-only words zeroed: 0 = style flags
    /// (the fill bit selects even-odd), 2 = packed draw flags, 5 = coverage parameter,
    /// 6-9 = target-local interest rectangle as left, top, right, bottom.
    /// The coverage parameter is mode-dependent because the two uses are mutually exclusive:
    /// aliased fills carry the quantization threshold, antialiased fills carry the perceptual
    /// coverage boost applied by fine (zero for plain fills, non-zero only for text).
    /// </remarks>
    /// <param name="options">The graphics options used for blending.</param>
    /// <param name="intersectionRule">The fill rule for self-intersecting geometry.</param>
    /// <param name="interestBounds">The target-local raster interest bounds.</param>
    /// <param name="coverageBoost">The perceptual coverage boost applied to antialiased coverage; zero disables it.</param>
    /// <returns>The ten packed style words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (
        uint Style0,
        uint Style1,
        uint Style2,
        uint Style3,
        uint Style4,
        uint Style5,
        uint Style6,
        uint Style7,
        uint Style8,
        uint Style9) GetFillStyle(
        GraphicsOptions options,
        IntersectionRule intersectionRule,
        Rectangle interestBounds,
        float coverageBoost = 0F)
    {
        StyleFlags styleFlags = intersectionRule == IntersectionRule.EvenOdd ? StyleFlags.Fill : StyleFlags.None;
        float coverageParam = options.Antialias ? coverageBoost : options.AntialiasThreshold;
        return (
            (uint)styleFlags,
            0U,
            PackStyleDrawFlags(options),
            0U,
            0U,
            BitcastSingle(coverageParam),
            BitcastSingle(interestBounds.Left),
            BitcastSingle(interestBounds.Top),
            BitcastSingle(interestBounds.Right),
            BitcastSingle(interestBounds.Bottom));
    }

    /// <summary>
    /// Packs the draw-flags word carried through flatten into coarse and fine.
    /// </summary>
    /// <param name="options">The graphics options supplying blend mode, blend percentage, and antialiasing.</param>
    /// <returns>The packed draw-flags word: blend mode in bits 1-13, alpha in bits 14-29, and the aliased bit.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackStyleDrawFlags(GraphicsOptions options)
        => (PackBlendMode(options) << 1)
            | (PackBlendAlpha(options.BlendPercentage) << 14)
            | (options.Antialias ? 0U : GpuSceneDrawTag.FillInfoFlagsAliasedBit);

    /// <summary>
    /// Packs the blend percentage into the shader draw-flags alpha field.
    /// </summary>
    /// <param name="blendPercentage">The blend percentage in the range [0, 1].</param>
    /// <returns>The alpha value quantized to 16 bits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackBlendAlpha(float blendPercentage)
        => (uint)Math.Clamp((int)MathF.Round(Math.Clamp(blendPercentage, 0F, 1F) * 65535F), 0, 65535);

    /// <summary>
    /// Packs the individual scene streams into one final scene-word buffer using the resolved layout.
    /// </summary>
    /// <param name="layout">The resolved scene layout supplying each stream's base offset.</param>
    /// <param name="pathTagWordCount">The padded path-tag word count.</param>
    /// <param name="pathTags">The path-tag bytes.</param>
    /// <param name="pathData">The path-data words.</param>
    /// <param name="drawTags">The draw tags.</param>
    /// <param name="drawData">The draw-data words.</param>
    /// <param name="transforms">The transform words.</param>
    /// <param name="styles">The style words.</param>
    /// <param name="sceneWords">The destination scene-word buffer.</param>
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
        // only for the tail bytes that are not overwritten by the copied tag payload.
        Span<byte> sceneBytes = MemoryMarshal.Cast<uint, byte>(sceneWords);
        int paddedPathTagBytes = checked(pathTagWordCount * sizeof(uint));
        pathTags.CopyTo(sceneBytes);
        sceneBytes[pathTags.Length..paddedPathTagBytes].Clear();
        pathData.CopyTo(sceneWords[(int)layout.PathDataBase..]);
        drawTags.CopyTo(sceneWords[(int)layout.DrawTagBase..]);
        drawData.CopyTo(sceneWords[(int)layout.DrawDataBase..]);
        transforms.CopyTo(sceneWords[(int)layout.TransformBase..]);
        styles.CopyTo(sceneWords[(int)layout.StyleBase..]);
    }

    /// <summary>
    /// Reinterprets one single-precision float as its raw IEEE 754 bit pattern.
    /// </summary>
    /// <param name="value">The value to reinterpret.</param>
    /// <returns>The raw bit pattern.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BitcastSingle(float value)
        => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    /// <summary>
    /// Rounds up integer division for tile and buffer planning.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The quotient rounded toward positive infinity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.
    /// </summary>
    /// <param name="value">The value to align.</param>
    /// <param name="alignment">The alignment granularity.</param>
    /// <returns>The aligned value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment)
        => value + ((alignment - (value % alignment)) % alignment);

    /// <summary>
    /// Converts an absolute scene rectangle into root-target-local coordinates.
    /// </summary>
    /// <param name="absoluteBounds">The absolute rectangle.</param>
    /// <param name="rootTargetBounds">The root target bounds supplying the local origin.</param>
    /// <returns>The target-local rectangle.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rectangle ToTargetLocal(in Rectangle absoluteBounds, in Rectangle rootTargetBounds)
        => new(
            absoluteBounds.X - rootTargetBounds.X,
            absoluteBounds.Y - rootTargetBounds.Y,
            absoluteBounds.Width,
            absoluteBounds.Height);

    /// <summary>
    /// Appends one solid brush color to the staged scene in associated binary16 form.
    /// </summary>
    /// <param name="solidBrush">The solid brush to encode.</param>
    /// <param name="drawData">The draw-data stream.</param>
    private static void AppendSolidColor(SolidBrush solidBrush, ref OwnedStream<uint> drawData)
        => AppendColor(solidBrush.Color.ToScaledVector4(PixelAlphaRepresentation.Associated), ref drawData);

    /// <summary>
    /// Appends the draw-data payload for one encoded draw tag.
    /// </summary>
    /// <param name="brush">The brush selected by the draw tag.</param>
    /// <param name="brushBounds">The absolute brush sampling bounds.</param>
    /// <param name="graphicsOptions">The graphics options used for blending.</param>
    /// <param name="drawTag">The draw tag that selects the payload layout.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="pathGradientData">The path-gradient payload stream.</param>
    /// <param name="images">Receives deferred image patch sites.</param>
    /// <param name="recolors">Receives deferred recolor payload patch sites.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced when a ramp is emitted.</param>
    private static void AppendDrawData(
        Brush brush,
        Rectangle brushBounds,
        GraphicsOptions graphicsOptions,
        uint drawTag,
        Rectangle rootTargetBounds,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData,
        List<GpuImageDescriptor> images,
        List<GpuRecolorDescriptor> recolors,
        ref int gradientRowCount)
    {
        Rectangle localBrushBounds = brush is ImageBrush
            ? ToTargetLocal(brushBounds, rootTargetBounds)
            : brushBounds;

        // The draw tag selects the payload layout, so this switch is the single place
        // where the encoded draw-data stream shape is kept in sync with the sizing logic.
        switch (drawTag)
        {
            case GpuSceneDrawTag.BeginClip:
                AppendBeginClipData(graphicsOptions, ref drawData);
                break;
            case GpuSceneDrawTag.FillColor:
                AppendSolidColor((SolidBrush)brush, ref drawData);
                break;
            case GpuSceneDrawTag.FillRecolor:
                AppendRecolorData((RecolorBrush)brush, ref drawData, ref pathGradientData, recolors);
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
            case GpuSceneDrawTag.FillPathGradient:
                AppendPathGradientData((PathGradientBrush)brush, rootTargetBounds, ref drawData, ref pathGradientData);
                break;
            case GpuSceneDrawTag.FillImage:
                AppendImageData(new GpuImageSource(brush), localBrushBounds, ref drawData, images);
                break;
            default:
                throw new UnreachableException($"Unsupported draw tag '{drawTag}' reached scene draw-data encoding.");
        }
    }

    /// <summary>
    /// Appends the draw-data payload for a begin-clip record.
    /// </summary>
    /// <param name="options">The graphics options applied when the clip group composites.</param>
    /// <param name="drawData">The draw-data stream.</param>
    private static void AppendBeginClipData(GraphicsOptions options, ref OwnedStream<uint> drawData)
    {
        // Layers are ISOLATED groups: they seed transparent on the GPU and composite back as a
        // unit, matching the CPU backend's clean layer targets. Canvas clips never set this bit;
        // they seed with the current content and pop with a coverage lerp (see drawtag.wgsl).
        drawData.Add(PackBlendMode(options) | ClipIsolatedMaskBit);
        drawData.Add(BitcastSingle(Math.Clamp(options.BlendPercentage, 0F, 1F)));
    }

    /// <summary>
    /// Appends the draw-data payload for an ordinary clip record.
    /// </summary>
    /// <param name="operation">The clip set operation; Difference sets the mask-inversion bit.</param>
    /// <param name="edgeMode">The clip edge mode; hard edges set the hard-mask bit.</param>
    /// <param name="antialiasThreshold">The coverage threshold used for hard-edge clips.</param>
    /// <param name="clipBlendMode">The base clip blend word.</param>
    /// <param name="drawData">The draw-data stream.</param>
    private static void AppendClipBeginData(
        ClipOperation operation,
        DrawingClipEdgeMode edgeMode,
        float antialiasThreshold,
        uint clipBlendMode,
        ref OwnedStream<uint> drawData)
    {
        // Difference is ImageSharp-specific clip state, not a Vello blend mode; encode it
        // in a spare bit while ordinary clips keep Vello's exact MIX_CLIP|SRC_OVER marker.
        uint blendMode = operation == ClipOperation.Difference
            ? clipBlendMode | ClipDifferenceMaskBit
            : clipBlendMode;

        if (edgeMode == DrawingClipEdgeMode.Hard)
        {
            blendMode |= ClipHardMaskBit;
        }

        drawData.Add(blendMode);
        drawData.Add(BitcastSingle(edgeMode == DrawingClipEdgeMode.Hard ? antialiasThreshold : 1F));
    }

    /// <summary>
    /// Packs the color and alpha composition modes into the draw-data layout consumed by the shaders.
    /// </summary>
    /// <param name="options">The graphics options supplying the blend and composition modes.</param>
    /// <returns>The packed blend word: color mix mode in bits 8-15, compose mode in bits 0-7.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackBlendMode(GraphicsOptions options)
        => (MapColorBlendMode(options.ColorBlendingMode) << 8) | MapAlphaCompositionMode(options.AlphaCompositionMode);

    /// <summary>
    /// Maps ImageSharp's color blend mode enum to the staged-scene shader contract.
    /// </summary>
    /// <param name="mode">The color blend mode to map.</param>
    /// <returns>The MIX_* constant declared in blend.wgsl.</returns>
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
    /// Maps ImageSharp's alpha composition mode enum to the staged-scene shader contract.
    /// </summary>
    /// <param name="mode">The alpha composition mode to map.</param>
    /// <returns>The COMPOSE_* constant declared in blend.wgsl.</returns>
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
    /// <param name="brush">The recolor brush.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="brushData">The auxiliary brush-data stream.</param>
    /// <param name="recolors">Receives the target-specialized payload patch site.</param>
    private static void AppendRecolorData(
        RecolorBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> brushData,
        List<GpuRecolorDescriptor> recolors)
    {
        int dataOffset = brushData.Count;

        // The target pixel type is known only when the retained scene is rendered. Reserve its
        // fixed record now and patch the exact CPU Color conversions at the generic resource boundary.
        brushData.GetAppendSpan(RecolorDataWordCount).Clear();
        brushData.Advance(RecolorDataWordCount);
        drawData.Add(checked((uint)dataOffset));
        recolors.Add(new GpuRecolorDescriptor(brush.SourceColor, brush.TargetColor, brush.Threshold * 4F, dataOffset));
    }

    /// <summary>
    /// Appends the deferred image payload and records the patch site.
    /// </summary>
    /// <param name="source">The image source metadata.</param>
    /// <param name="brushBounds">The target-local texture sampling bounds.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="images">Receives the patch site descriptor.</param>
    private static void AppendImageData(
        in GpuImageSource source,
        Rectangle brushBounds,
        ref OwnedStream<uint> drawData,
        List<GpuImageDescriptor> images)
    {
        // The image payload words are patched once the atlas is built because the
        // uploader is the first place that knows the concrete TPixel texture format.
        int payloadWordOffset = drawData.Count;
        drawData.Add(0);
        drawData.Add(0);
        drawData.Add(0);
        if (source.Brush is ImageBrush imageBrush)
        {
            drawData.Add(BitcastSingle(brushBounds.Left + imageBrush.Offset.X));
            drawData.Add(BitcastSingle(brushBounds.Top + imageBrush.Offset.Y));
        }
        else if (source.IsExternalTexture)
        {
            drawData.Add(BitcastSingle(brushBounds.Left + source.Offset.X));
            drawData.Add(BitcastSingle(brushBounds.Top + source.Offset.Y));
        }
        else
        {
            drawData.Add(0);
            drawData.Add(0);
        }

        images.Add(new GpuImageDescriptor(source, payloadWordOffset));
    }

    /// <summary>
    /// Appends the linear gradient payload and its packed ramp row.
    /// </summary>
    /// <param name="brush">The linear gradient brush.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
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
    /// <param name="brush">The radial gradient brush.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
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

    /// <summary>
    /// Appends the elliptic gradient payload and its packed ramp row.
    /// </summary>
    /// <param name="brush">The elliptic gradient brush.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
    private static void AppendEllipticGradientData(
        EllipticGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        // Brushes reach the encoder after batcher normalization, so gradient payload points are
        // already in render coordinates. The shader still expects the ellipse as three points,
        // so reconstruct the secondary-axis endpoint from the stored center, axis, and ratio.
        PointF localCenter = brush.Center;
        PointF localAxisEnd = brush.ReferenceAxisEnd;
        Vector2 localAxis = new(localAxisEnd.X - localCenter.X, localAxisEnd.Y - localCenter.Y);
        Vector2 localPerpendicular = new Vector2(-localAxis.Y, localAxis.X) * brush.AxisRatio;
        PointF localSecondEnd = new(localCenter.X + localPerpendicular.X, localCenter.Y + localPerpendicular.Y);

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(localCenter.X));
        drawData.Add(BitcastSingle(localCenter.Y));
        drawData.Add(BitcastSingle(localAxisEnd.X));
        drawData.Add(BitcastSingle(localAxisEnd.Y));
        drawData.Add(BitcastSingle(localSecondEnd.X));
        drawData.Add(BitcastSingle(localSecondEnd.Y));
    }

    /// <summary>
    /// Appends the sweep gradient payload and its packed ramp row.
    /// </summary>
    /// <param name="brush">The sweep gradient brush.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
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
            // Match the CPU brush contract: only endpoints equal within the degree-space
            // epsilon denote a full turn. Applying this epsilon after converting to turns
            // would incorrectly expand small, non-zero sweeps.
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
    /// Appends the path-gradient payload in target-local coordinates.
    /// </summary>
    /// <param name="brush">The path gradient brush.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="pathGradientData">The path-gradient payload stream.</param>
    private static void AppendPathGradientData(
        PathGradientBrush brush,
        Rectangle rootTargetBounds,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> pathGradientData)
    {
        ReadOnlySpan<PointF> points = brush.Points;
        ReadOnlySpan<Color> colors = brush.Colors;
        int edgeCount = points.Length;
        PointF center = default;

        for (int i = 0; i < edgeCount; i++)
        {
            PointF sourcePoint = points[i];
            PointF point = new(sourcePoint.X - rootTargetBounds.X, sourcePoint.Y - rootTargetBounds.Y);
            center = new PointF(center.X + point.X, center.Y + point.Y);
        }

        center = new PointF(center.X / edgeCount, center.Y / edgeCount);

        float maxDistance = 0F;
        for (int i = 0; i < edgeCount; i++)
        {
            PointF sourcePoint = points[i];
            PointF point = new(sourcePoint.X - rootTargetBounds.X, sourcePoint.Y - rootTargetBounds.Y);
            maxDistance = MathF.Max(maxDistance, Vector2.Distance(point, center));
        }

        int dataOffset = pathGradientData.Count;
        Span<uint> payload = pathGradientData.GetAppendSpan(PathGradientHeaderWordCount + (edgeCount * PathGradientEdgeWordCount));
        payload[0] = BitcastSingle(center.X);
        payload[1] = BitcastSingle(center.Y);
        payload[2] = BitcastSingle(maxDistance);
        WriteAssociatedColor(payload, 3, brush.CenterColor);

        for (int i = 0; i < edgeCount; i++)
        {
            int payloadOffset = PathGradientHeaderWordCount + (i * PathGradientEdgeWordCount);
            PointF sourceStart = points[i];
            PointF sourceEnd = points[(i + 1) % edgeCount];
            PointF start = new(sourceStart.X - rootTargetBounds.X, sourceStart.Y - rootTargetBounds.Y);
            PointF end = new(sourceEnd.X - rootTargetBounds.X, sourceEnd.Y - rootTargetBounds.Y);

            payload[payloadOffset] = BitcastSingle(start.X);
            payload[payloadOffset + 1] = BitcastSingle(start.Y);
            payload[payloadOffset + 2] = BitcastSingle(end.X);
            payload[payloadOffset + 3] = BitcastSingle(end.Y);
            WriteAssociatedColor(payload, payloadOffset + 4, colors[i % colors.Length]);
            WriteAssociatedColor(payload, payloadOffset + 8, colors[(i + 1) % colors.Length]);
        }

        pathGradientData.Advance(payload.Length);
        drawData.Add((uint)dataOffset);
        drawData.Add((uint)edgeCount);
        drawData.Add(brush.HasExplicitCenterColor ? 1U : 0U);
        drawData.Add(0U);
    }

    /// <summary>
    /// Appends one packed gradient ramp row sampled across the fixed gradient width.
    /// </summary>
    /// <param name="colorStops">The gradient color stops, ordered by ratio.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    private static void AppendGradientRamp(ReadOnlySpan<ColorStop> colorStops, ref OwnedStream<uint> gradientPixels)
    {
        for (int x = 0; x < GradientWidth; x++)
        {
            float t = x / (float)(GradientWidth - 1);
            AppendColor(EvaluateGradientColor(colorStops, t), ref gradientPixels);
        }
    }

    /// <summary>
    /// Evaluates the premultiplied gradient color at the normalized parameter.
    /// </summary>
    /// <param name="colorStops">The gradient color stops, ordered by ratio.</param>
    /// <param name="t">The normalized sample position in the range [0, 1].</param>
    /// <returns>The associated floating-point sample.</returns>
    private static Vector4 EvaluateGradientColor(ReadOnlySpan<ColorStop> colorStops, float t)
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
            return from.Color.ToScaledVector4(PixelAlphaRepresentation.Associated);
        }

        float localT = (t - from.Ratio) / (to.Ratio - from.Ratio);

        // CSS Color 4 requires alpha premultiplication before interpolation. The ramp stores
        // associated RGBA16Float directly, matching the fine shader's internal composition space.
        return Vector4.Lerp(
            from.Color.ToScaledVector4(PixelAlphaRepresentation.Associated),
            to.Color.ToScaledVector4(PixelAlphaRepresentation.Associated),
            localT);
    }

    /// <summary>
    /// Appends one RGBA vector as two binary16 pairs.
    /// </summary>
    /// <param name="color">The vector to append.</param>
    /// <param name="destination">The stream receiving two words.</param>
    private static void AppendColor(Vector4 color, ref OwnedStream<uint> destination)
    {
        destination.Add(PackHalf2(color.X, color.Y));
        destination.Add(PackHalf2(color.Z, color.W));
    }

    /// <summary>
    /// Packs two floating-point components into one binary16 pair matching WGSL's <c>unpack2x16float</c>.
    /// </summary>
    /// <param name="x">The low component.</param>
    /// <param name="y">The high component.</param>
    /// <returns>The packed pair.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackHalf2(float x, float y)
        => BitConverter.HalfToUInt16Bits((Half)x) | ((uint)BitConverter.HalfToUInt16Bits((Half)y) << 16);

    /// <summary>
    /// Writes one associated color to the path-gradient payload without reducing it to RGBA8 first.
    /// </summary>
    /// <param name="destination">The payload receiving four consecutive floating-point words.</param>
    /// <param name="offset">The destination word offset.</param>
    /// <param name="color">The color to convert to associated components.</param>
    private static void WriteAssociatedColor(Span<uint> destination, int offset, in Color color)
    {
        // Path gradients interpolate endpoint colors directly in the shader. Keeping the source
        // precision here matches the CPU renderer and defers quantization until target encoding.
        Vector4 vector = color.ToScaledVector4(PixelAlphaRepresentation.Associated);
        destination[offset] = BitcastSingle(vector.X);
        destination[offset + 1] = BitcastSingle(vector.Y);
        destination[offset + 2] = BitcastSingle(vector.Z);
        destination[offset + 3] = BitcastSingle(vector.W);
    }

    /// <summary>
    /// Maps ImageSharp's gradient repetition mode to the staged-scene shader contract.
    /// </summary>
    /// <param name="repetitionMode">The gradient repetition mode to map.</param>
    /// <returns>The extend-mode value stored in the low two bits of the gradient index word.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MapExtendMode(GradientRepetitionMode repetitionMode)
        => repetitionMode switch
        {
            GradientRepetitionMode.Repeat => 1U,
            GradientRepetitionMode.Reflect => 2U,
            GradientRepetitionMode.DontFill => 3U,
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
        null,
        0,
        [],
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
        RasterizationMode.Antialiased,
        0F,
        0L,
        0L,
        []);

    private readonly IMemoryOwner<uint>? sceneDataOwner;
    private readonly IMemoryOwner<uint>? gradientPixelsOwner;
    private readonly IMemoryOwner<uint>? pathGradientDataOwner;
    private readonly List<GpuImageDescriptor> images;
    private readonly List<GpuRecolorDescriptor> recolors;
    private readonly WebGPUSceneOperation[] operations;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUEncodedScene"/> class.
    /// </summary>
    /// <param name="targetSize">The encoded scene target size.</param>
    /// <param name="infoWordCount">The number of info words.</param>
    /// <param name="sceneDataOwner">The owner for packed scene-data words.</param>
    /// <param name="sceneWordCount">The number of scene-data words.</param>
    /// <param name="gradientPixelsOwner">The owner for packed gradient pixels.</param>
    /// <param name="pathGradientDataOwner">The owner for path-gradient data words.</param>
    /// <param name="pathGradientDataWordCount">The number of path-gradient data words.</param>
    /// <param name="images">The image descriptors referenced by draw data.</param>
    /// <param name="recolors">The recolor descriptors referenced by auxiliary brush data.</param>
    /// <param name="gradientRowCount">The number of gradient texture rows.</param>
    /// <param name="layout">The packed GPU scene layout.</param>
    /// <param name="fillCount">The number of fill draw records.</param>
    /// <param name="pathCount">The number of encoded paths.</param>
    /// <param name="lineCount">The number of encoded line segments.</param>
    /// <param name="pathTagByteCount">The number of path-tag bytes.</param>
    /// <param name="pathTagWordCount">The number of path-tag words.</param>
    /// <param name="pathDataWordCount">The number of path-data words.</param>
    /// <param name="drawTagCount">The number of draw tags.</param>
    /// <param name="drawDataWordCount">The number of draw-data words.</param>
    /// <param name="transformWordCount">The number of transform words.</param>
    /// <param name="styleWordCount">The number of style words.</param>
    /// <param name="clipCount">The number of clip records.</param>
    /// <param name="uniqueDefinitionCount">The number of unique draw definitions.</param>
    /// <param name="totalPathRowCount">The total scheduled path-row count.</param>
    /// <param name="tileCountX">The horizontal tile count.</param>
    /// <param name="tileCountY">The vertical tile count.</param>
    /// <param name="fineRasterizationMode">The fine-pass rasterization mode.</param>
    /// <param name="fineCoverageThreshold">The scene-wide aliased coverage threshold.</param>
    /// <param name="estimatedTileCrossings">The CPU-side upper bound for tile-boundary crossings.</param>
    /// <param name="estimatedBinFootprint">The CPU-side upper bound for per-(draw, bin) binning records.</param>
    /// <param name="operations">The scene operations associated with the encoded payload.</param>
    public WebGPUEncodedScene(
        Size targetSize,
        int infoWordCount,
        IMemoryOwner<uint>? sceneDataOwner,
        int sceneWordCount,
        IMemoryOwner<uint>? gradientPixelsOwner,
        IMemoryOwner<uint>? pathGradientDataOwner,
        int pathGradientDataWordCount,
        List<GpuImageDescriptor> images,
        List<GpuRecolorDescriptor> recolors,
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
        int totalPathRowCount,
        int tileCountX,
        int tileCountY,
        RasterizationMode fineRasterizationMode,
        float fineCoverageThreshold,
        long estimatedTileCrossings,
        long estimatedBinFootprint,
        WebGPUSceneOperation[] operations)
    {
        this.TargetSize = targetSize;
        this.InfoWordCount = infoWordCount;
        this.sceneDataOwner = sceneDataOwner;
        this.gradientPixelsOwner = gradientPixelsOwner;
        this.pathGradientDataOwner = pathGradientDataOwner;
        this.PathGradientDataWordCount = pathGradientDataWordCount;
        this.images = images;
        this.recolors = recolors;
        this.operations = operations;
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
        this.TotalPathRowCount = totalPathRowCount;
        this.TileCountX = tileCountX;
        this.TileCountY = tileCountY;
        this.FineRasterizationMode = fineRasterizationMode;
        this.FineCoverageThreshold = fineCoverageThreshold;
        this.EstimatedTileCrossings = estimatedTileCrossings;
        this.EstimatedBinFootprint = estimatedBinFootprint;

        int targetCount = operations.Length == 0 ? 0 : 1;
        int applyCount = 0;
        int shaderEffectCount = 0;
        int maximumShaderEffectWidth = 0;
        int maximumShaderEffectHeight = 0;
        int maximumShaderUniformByteLength = 0;
        int maxApplyGroupCount = 0;
        int pendingRangeCount = 0;
        int maxPendingRangeCount = 0;
        int finalStatusCapacity = 0;
        int maxStatusCapacity = 0;

        for (int i = 0; i < operations.Length; i++)
        {
            WebGPUSceneOperation operation = operations[i];
            switch (operation.Kind)
            {
                case WebGPUSceneOperationKind.RenderRange:
                case WebGPUSceneOperationKind.EndLayer:
                    pendingRangeCount++;
                    finalStatusCapacity = checked(finalStatusCapacity + operation.Range.MaximumStatusRecordCount);
                    break;

                case WebGPUSceneOperationKind.BeginLayer:
                    targetCount = Math.Max(targetCount, operation.LayerTargetId + 1);
                    break;

                case WebGPUSceneOperationKind.Apply when operation.ApplyGroupCount != 0:
                    applyCount += operation.ApplyGroupCount;
                    maxApplyGroupCount = Math.Max(maxApplyGroupCount, operation.ApplyGroupCount);
                    maxPendingRangeCount = Math.Max(maxPendingRangeCount, pendingRangeCount);
                    maxStatusCapacity = Math.Max(maxStatusCapacity, operation.PendingStatusCapacity);

                    pendingRangeCount = operation.ApplyGroupCount;
                    finalStatusCapacity = 0;
                    for (int applyIndex = i; applyIndex < i + operation.ApplyGroupCount; applyIndex++)
                    {
                        finalStatusCapacity = checked(
                            finalStatusCapacity + operations[applyIndex].Apply!.DrawRange.MaximumStatusRecordCount);
                    }

                    i += operation.ApplyGroupCount - 1;
                    break;

                case WebGPUSceneOperationKind.ShaderEffect:
                    shaderEffectCount++;
                    WebGPUShaderEffectSceneItem shaderEffect = operation.ShaderEffect!;
                    maximumShaderEffectWidth = Math.Max(maximumShaderEffectWidth, shaderEffect.InputRect.Width);
                    maximumShaderEffectHeight = Math.Max(maximumShaderEffectHeight, shaderEffect.InputRect.Height);

                    ReadOnlySpan<WebGPUShaderPass> shaderPasses = shaderEffect.Effect.GetShaderPasses();
                    for (int passIndex = 0; passIndex < shaderPasses.Length; passIndex++)
                    {
                        maximumShaderUniformByteLength = Math.Max(
                            maximumShaderUniformByteLength,
                            shaderPasses[passIndex].Program.UniformLayout.ByteLength);
                    }

                    maxPendingRangeCount = Math.Max(maxPendingRangeCount, pendingRangeCount);
                    maxStatusCapacity = Math.Max(maxStatusCapacity, finalStatusCapacity);
                    pendingRangeCount = 1;
                    finalStatusCapacity = operation.ShaderEffect!.DrawRange.MaximumStatusRecordCount;
                    break;
            }
        }

        this.OrderedTargetCount = targetCount;
        this.OrderedApplyCount = applyCount;
        this.OrderedShaderEffectCount = shaderEffectCount;
        this.MaximumShaderEffectWidth = maximumShaderEffectWidth;
        this.MaximumShaderEffectHeight = maximumShaderEffectHeight;
        this.MaximumShaderUniformByteLength = maximumShaderUniformByteLength;
        this.MaxApplyGroupCount = maxApplyGroupCount;
        this.MaxPendingRangeCount = Math.Max(maxPendingRangeCount, pendingRangeCount);
        this.FinalStatusCapacity = finalStatusCapacity;
        this.MaxStatusCapacity = Math.Max(maxStatusCapacity, finalStatusCapacity);
    }

    /// <summary>
    /// Gets the CPU-side upper-bound estimate of tile-boundary crossings produced by the scene's
    /// flattened lines. Bounds the GPU segment, segment-count, and path-tile demand.
    /// </summary>
    public long EstimatedTileCrossings { get; }

    /// <summary>
    /// Gets the CPU-side upper-bound estimate of per-(draw, bin) binning records.
    /// </summary>
    public long EstimatedBinFootprint { get; }

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
    /// Gets the packed gradient-ramp pixels. Rows are <see cref="WebGPUSceneEncoder.GradientWidth"/>
    /// pixels wide, matching the encoder's fixed gradient ramp width.
    /// </summary>
    public ReadOnlyMemory<uint> GradientPixels
        => this.gradientPixelsOwner is null ? ReadOnlyMemory<uint>.Empty : this.gradientPixelsOwner.Memory[..(this.GradientRowCount * WebGPUSceneEncoder.GradientRowWordCount)];

    /// <summary>
    /// Gets the encoded path-gradient payload stream.
    /// </summary>
    public ReadOnlyMemory<uint> PathGradientData
        => this.pathGradientDataOwner is null ? ReadOnlyMemory<uint>.Empty : this.pathGradientDataOwner.Memory[..this.PathGradientDataWordCount];

    /// <summary>
    /// Gets the encoded path-gradient payload word count.
    /// </summary>
    public int PathGradientDataWordCount { get; }

    /// <summary>
    /// Gets the combined info/path-gradient prefix word count before dynamic bin data.
    /// </summary>
    public int InfoBufferWordCount => this.InfoWordCount + this.PathGradientDataWordCount;

    /// <summary>
    /// Gets the deferred image descriptors that must be patched after atlas creation.
    /// </summary>
    public IReadOnlyList<GpuImageDescriptor> Images => this.images;

    /// <summary>
    /// Gets the deferred recolor descriptors that must be specialized for the target pixel type.
    /// </summary>
    public IReadOnlyList<GpuRecolorDescriptor> Recolors => this.recolors;

    /// <summary>
    /// Gets the ordered retained operations for a scene containing Apply.
    /// </summary>
    public ReadOnlySpan<WebGPUSceneOperation> OrderedOperations => this.operations;

    /// <summary>
    /// Gets a value indicating whether this scene renders through ordered operations.
    /// </summary>
    public bool HasOperations => this.operations.Length != 0;

    /// <summary>
    /// Gets the number of stable root and scoped-layer target identities in the ordered plan.
    /// </summary>
    public int OrderedTargetCount { get; }

    /// <summary>
    /// Gets the number of Apply operations retained by the ordered plan.
    /// </summary>
    public int OrderedApplyCount { get; }

    /// <summary>
    /// Gets the number of native shader-effect operations retained by the ordered plan.
    /// </summary>
    public int OrderedShaderEffectCount { get; }

    /// <summary>
    /// Gets the largest native effect input width in this scene.
    /// </summary>
    public int MaximumShaderEffectWidth { get; }

    /// <summary>
    /// Gets the largest native effect input height in this scene.
    /// </summary>
    public int MaximumShaderEffectHeight { get; }

    /// <summary>
    /// Gets the largest user-uniform binding required by a native effect pass in this scene.
    /// </summary>
    public int MaximumShaderUniformByteLength { get; }

    /// <summary>
    /// Gets the largest dependency-independent Apply group retained by the ordered plan.
    /// </summary>
    public int MaxApplyGroupCount { get; }

    /// <summary>
    /// Gets the largest number of range submissions retained by one unvalidated transaction.
    /// </summary>
    public int MaxPendingRangeCount { get; }

    /// <summary>
    /// Gets the maximum scheduling-status record count required by any ordered barrier.
    /// </summary>
    public int MaxStatusCapacity { get; }

    /// <summary>
    /// Gets the maximum scheduling-status record count awaiting validation at end of flush.
    /// </summary>
    public int FinalStatusCapacity { get; }

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
    /// Gets the CPU-side estimate of the number of active tile rows used by sparse path metadata.
    /// </summary>
    public int TotalPathRowCount { get; }

    /// <summary>
    /// Gets the tile count on the X axis.
    /// </summary>
    public int TileCountX { get; }

    /// <summary>
    /// Gets the tile count on the Y axis.
    /// </summary>
    public int TileCountY { get; }

    /// <summary>
    /// Gets the fine-pass rasterization mode selected for this flush.
    /// </summary>
    public RasterizationMode FineRasterizationMode { get; }

    /// <summary>
    /// Gets the scene-wide aliased coverage threshold consumed by the fine pass.
    /// </summary>
    public float FineCoverageThreshold { get; }

    /// <summary>
    /// Gets the total tile count.
    /// </summary>
    public int TileCount => this.TileCountX * this.TileCountY;

    /// <summary>
    /// Gets the packed GPU scene layout.
    /// </summary>
    public GpuSceneLayout Layout { get; }

    /// <summary>
    /// Gets the version of the packed scene stream. <see cref="SetSceneWord"/> increments it for
    /// every effective mutation so resident GPU copies of the stream can detect staleness.
    /// </summary>
    public int SceneDataVersion { get; private set; }

    /// <summary>
    /// Gets the inclusive first scene-word index mutated after encoding, or <see cref="int.MaxValue"/> when none.
    /// </summary>
    public int MutatedSceneWordMin { get; private set; } = int.MaxValue;

    /// <summary>
    /// Gets the exclusive end of the scene-word span mutated after encoding, or zero when none.
    /// </summary>
    public int MutatedSceneWordEnd { get; private set; }

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

        // Identical rewrites (for example overflow replay re-staging the same range) must not
        // advance the version, or resident GPU streams would be patched needlessly every attempt.
        Span<uint> sceneWords = this.sceneDataOwner.Memory.Span;
        if (sceneWords[index] == value)
        {
            return;
        }

        sceneWords[index] = value;
        this.SceneDataVersion++;
        this.MutatedSceneWordMin = Math.Min(this.MutatedSceneWordMin, index);
        this.MutatedSceneWordEnd = Math.Max(this.MutatedSceneWordEnd, index + 1);
    }

    /// <summary>
    /// Releases the owned scene, gradient, and path-gradient buffers.
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
        this.pathGradientDataOwner?.Dispose();
    }
}

/// <summary>
/// Describes the source metadata for one encoded image fill.
/// </summary>
internal readonly struct GpuImageSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuImageSource"/> struct from a CPU-backed image or pattern brush.
    /// </summary>
    /// <param name="brush">The image or pattern brush.</param>
    public GpuImageSource(Brush brush)
    {
        this.Brush = brush;
        this.Size = default;
        this.Offset = default;
        this.WrapX = default;
        this.WrapY = default;
        this.Descriptor = default;
        this.HasExplicitDescriptor = false;
        this.IsExternalTexture = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuImageSource"/> struct from a render-time WebGPU texture.
    /// </summary>
    /// <param name="size">The render-time texture size.</param>
    /// <param name="offset">The image offset encoded into the fill payload.</param>
    /// <param name="wrapX">The horizontal wrap mode.</param>
    /// <param name="wrapY">The vertical wrap mode.</param>
    public GpuImageSource(Size size, Point offset, WrapMode wrapX, WrapMode wrapY)
    {
        this.Brush = null;
        this.Size = size;
        this.Offset = offset;
        this.WrapX = wrapX;
        this.WrapY = wrapY;
        this.Descriptor = default;
        this.HasExplicitDescriptor = false;
        this.IsExternalTexture = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuImageSource"/> struct from a render-time WebGPU texture whose representation differs from the target.
    /// </summary>
    /// <param name="size">The render-time texture size.</param>
    /// <param name="offset">The image offset encoded into the fill payload.</param>
    /// <param name="wrapX">The horizontal wrap mode.</param>
    /// <param name="wrapY">The vertical wrap mode.</param>
    /// <param name="descriptor">The physical and logical representation of the supplied texture.</param>
    public GpuImageSource(Size size, Point offset, WrapMode wrapX, WrapMode wrapY, WebGPUTargetDescriptor descriptor)
    {
        this.Brush = null;
        this.Size = size;
        this.Offset = offset;
        this.WrapX = wrapX;
        this.WrapY = wrapY;
        this.Descriptor = descriptor;
        this.HasExplicitDescriptor = true;
        this.IsExternalTexture = true;
    }

    /// <summary>
    /// Gets the CPU-backed image or pattern brush when this source is not an external texture.
    /// </summary>
    public Brush? Brush { get; }

    /// <summary>
    /// Gets the render-time texture size.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets the image offset encoded into the fill payload.
    /// </summary>
    public Point Offset { get; }

    /// <summary>
    /// Gets the horizontal wrap mode.
    /// </summary>
    public WrapMode WrapX { get; }

    /// <summary>
    /// Gets the vertical wrap mode.
    /// </summary>
    public WrapMode WrapY { get; }

    /// <summary>
    /// Gets the physical and logical representation of an external texture that differs from the render target.
    /// </summary>
    public WebGPUTargetDescriptor Descriptor { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Descriptor"/> replaces the render target descriptor for this source.
    /// </summary>
    public bool HasExplicitDescriptor { get; }

    /// <summary>
    /// Gets a value indicating whether the source texture is supplied by the WebGPU render path.
    /// </summary>
    public bool IsExternalTexture { get; }
}

/// <summary>
/// Describes one deferred image payload patch site in the encoded draw-data stream.
/// </summary>
internal readonly struct GpuImageDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuImageDescriptor"/> struct.
    /// </summary>
    /// <param name="source">The image source metadata.</param>
    /// <param name="drawDataWordOffset">The draw-data word offset to patch once the source texture is known.</param>
    public GpuImageDescriptor(in GpuImageSource source, int drawDataWordOffset)
    {
        this.Source = source;
        this.DrawDataWordOffset = drawDataWordOffset;
    }

    /// <summary>
    /// Gets the image source metadata.
    /// </summary>
    public GpuImageSource Source { get; }

    /// <summary>
    /// Gets the draw-data word offset to patch once the image atlas is built.
    /// </summary>
    public int DrawDataWordOffset { get; }
}

/// <summary>
/// Describes one RecolorBrush payload that must be converted for the render target pixel type.
/// </summary>
internal readonly struct GpuRecolorDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuRecolorDescriptor"/> struct.
    /// </summary>
    /// <param name="sourceColor">The full-precision match key.</param>
    /// <param name="targetColor">The replacement color.</param>
    /// <param name="threshold">The squared-distance threshold.</param>
    /// <param name="brushDataWordOffset">The auxiliary brush-data word offset to patch.</param>
    public GpuRecolorDescriptor(Color sourceColor, Color targetColor, float threshold, int brushDataWordOffset)
    {
        this.SourceColor = sourceColor;
        this.TargetColor = targetColor;
        this.Threshold = threshold;
        this.BrushDataWordOffset = brushDataWordOffset;
    }

    /// <summary>
    /// Gets the full-precision match key.
    /// </summary>
    public Color SourceColor { get; }

    /// <summary>
    /// Gets the replacement color.
    /// </summary>
    public Color TargetColor { get; }

    /// <summary>
    /// Gets the squared-distance threshold.
    /// </summary>
    public float Threshold { get; }

    /// <summary>
    /// Gets the auxiliary brush-data word offset to patch for the render target.
    /// </summary>
    public int BrushDataWordOffset { get; }
}

/// <summary>
/// Represents one 2D affine transform encoded in the GPU scene format.
/// </summary>
internal readonly struct GpuSceneTransform : IEquatable<GpuSceneTransform>
{
    /// <summary>
    /// Gets the identity transform.
    /// </summary>
    public static readonly GpuSceneTransform Identity = new(1F, 0F, 0F, 1F, 0F, 0F, 0F, 0F, 1F);

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneTransform"/> struct.
    /// </summary>
    /// <param name="m11">The row 1, column 1 matrix value.</param>
    /// <param name="m12">The row 1, column 2 matrix value.</param>
    /// <param name="m21">The row 2, column 1 matrix value.</param>
    /// <param name="m22">The row 2, column 2 matrix value.</param>
    /// <param name="tx">The x translation value.</param>
    /// <param name="ty">The y translation value.</param>
    /// <param name="m14">The row 1, column 4 perspective value.</param>
    /// <param name="m24">The row 2, column 4 perspective value.</param>
    /// <param name="m44">The row 4, column 4 homogeneous scale value.</param>
    public GpuSceneTransform(float m11, float m12, float m21, float m22, float tx, float ty, float m14, float m24, float m44)
    {
        this.M11 = m11;
        this.M12 = m12;
        this.M21 = m21;
        this.M22 = m22;
        this.Tx = tx;
        this.Ty = ty;
        this.M14 = m14;
        this.M24 = m24;
        this.M44 = m44;
    }

    /// <summary>
    /// Creates a <see cref="GpuSceneTransform"/> from a <see cref="Matrix4x4"/>.
    /// Extracts the 9 elements needed for projective 2D transformation with z=0.
    /// </summary>
    /// <param name="m">The matrix to convert.</param>
    /// <returns>The GPU scene transform.</returns>
    public static GpuSceneTransform FromMatrix4x4(Matrix4x4 m)
        => new(m.M11, m.M12, m.M21, m.M22, m.M41, m.M42, m.M14, m.M24, m.M44);

    /// <summary>
    /// Gets the row 1, column 1 linear element.
    /// </summary>
    public float M11 { get; }

    /// <summary>
    /// Gets the row 1, column 2 linear element.
    /// </summary>
    public float M12 { get; }

    /// <summary>
    /// Gets the row 2, column 1 linear element.
    /// </summary>
    public float M21 { get; }

    /// <summary>
    /// Gets the row 2, column 2 linear element.
    /// </summary>
    public float M22 { get; }

    /// <summary>
    /// Gets the x translation element.
    /// </summary>
    public float Tx { get; }

    /// <summary>
    /// Gets the y translation element.
    /// </summary>
    public float Ty { get; }

    /// <summary>
    /// Gets the perspective element from row 1, column 4.
    /// </summary>
    public float M14 { get; }

    /// <summary>
    /// Gets the perspective element from row 2, column 4.
    /// </summary>
    public float M24 { get; }

    /// <summary>
    /// Gets the homogeneous scale element from row 4, column 4.
    /// </summary>
    public float M44 { get; }

    /// <summary>
    /// Compares components with exact float equality; the encoder only uses this to skip
    /// re-emitting a transform identical to the last one written.
    /// </summary>
    /// <param name="other">The transform to compare against.</param>
    /// <returns><see langword="true"/> when every component matches exactly.</returns>
    public bool Equals(GpuSceneTransform other)
        => this.M11 == other.M11
        && this.M12 == other.M12
        && this.M21 == other.M21
        && this.M22 == other.M22
        && this.Tx == other.Tx
        && this.Ty == other.Ty
        && this.M14 == other.M14
        && this.M24 == other.M24
        && this.M44 == other.M44;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GpuSceneTransform other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(this.M11);
        hash.Add(this.M12);
        hash.Add(this.M21);
        hash.Add(this.M22);
        hash.Add(this.Tx);
        hash.Add(this.Ty);
        hash.Add(this.M14);
        hash.Add(this.M24);
        hash.Add(this.M44);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Owns one growable contiguous stream backed by allocator storage.
/// </summary>
/// <typeparam name="T">The unmanaged element type of the stream.</typeparam>
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
    /// Gets a writable reserved append window for a caller that will populate items in place.
    /// </summary>
    /// <param name="count">The number of contiguous items to reserve.</param>
    /// <returns>A writable span covering the reserved append window.</returns>
    public Span<T> GetAppendSpan(int count)
    {
        this.EnsureCapacity(this.Count + count);
        return this.span.Slice(this.Count, count);
    }

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
    /// Commits the requested number of previously reserved items to the logical stream length.
    /// </summary>
    /// <param name="count">The number of reserved items that were written.</param>
    public void Advance(int count) => this.Count += count;

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
