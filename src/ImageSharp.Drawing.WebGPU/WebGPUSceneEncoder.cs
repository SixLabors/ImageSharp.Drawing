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
    public const int TileWidth = 16;

    /// <summary>
    /// The tile height in pixels used by binning, coarse, and fine. Must match TILE_HEIGHT in config.wgsl.
    /// </summary>
    public const int TileHeight = 16;

    /// <summary>
    /// The PTCL word count of one CMD_FILL command. Must match write_path in coarse.wgsl. An aliased
    /// fill writes 8 words and an antialiased fill 6; the bounds use the larger count for both.
    /// </summary>
    private const int PtclFillWords = 8;

    /// <summary>
    /// The extra PTCL words of one clip or layer on a tile: BeginClip (2), the forced solid and
    /// EndClip (5), and its path (2). Must match coarse.wgsl.
    /// </summary>
    private const int PtclClipWords = 9;

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
    /// The largest scene segment count of one encoding. A range has at most this count. coarse.wgsl stores the segment index in 8 bits.
    /// </summary>
    private const int MaxSceneSegmentCount = 255;

    /// <summary>
    /// The draw-object count limit of a scene with a segment table. coarse.wgsl stores the draw
    /// object index in 24 bits, so the count must be less than this value.
    /// </summary>
    private const int SegmentedDrawObjectLimit = 1 << 24;

    /// <summary>
    /// Gets the error message for a scene that reads retained scenes in place and has more draw
    /// objects than <see cref="SegmentedDrawObjectLimit"/> allows.
    /// </summary>
    private static string SegmentedDrawObjectLimitError
        => $"A WebGPU scene that reads retained scenes in place can have at most {SegmentedDrawObjectLimit - 1} draw objects.";

    /// <summary>
    /// The blend depth that fine keeps in registers. Must match BLEND_STACK_SPLIT in Shared/config.wgsl.
    /// </summary>
    public const int BlendStackSplit = 4;

    /// <summary>
    /// The bounding-box tile count from which a polyline stroke walks its rows for the row-span
    /// tile bound. The walk costs about one step for each row that a segment spans. A smaller
    /// draw keeps its area bound, which is at most this many tiles.
    /// </summary>
    private const int RowWalkMinimumTiles = 1024;

    /// <summary>
    /// The largest interior vertex count of one round arc in the stroke shaders. The count is
    /// stored in the low bits of a stroke style flags word, so it must fit in
    /// STYLE_FLAGS_ARC_LIMIT_MASK in Shared/pathtag.wgsl.
    /// </summary>
    private const int MaxStrokeArcSubdivisions = 1024;

    /// <summary>
    /// The words in one scene segment table entry. Must match SCENE_SEGMENT_WORDS in scene_segment.wgsl.
    /// </summary>
    public const int SceneSegmentEntryWordCount = 13;

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
    /// in bits 26-27. The path-lowering shader shifts the start-cap field down before decoding it.
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
        // A retained scene inside a layer is a gap in the outer streams at a stream position. One
        // partition keeps every gap position in the final streams, so the resolver can build the
        // segment table without a partition offset map. Only batches with a retained scene inside
        // a layer pay this.
        int partitionCount = scene.HasScenes ? 1 : GetPartitionCount(maxDegreeOfParallelism, commandCount, targetRowCount);
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
                        int commandEnd = partitionBoundaries is null
                            ? ((partitionIndex + 1) * commandCount) / partitionCount
                            : partitionBoundaries[partitionIndex + 1];

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

            if (fillCount == 0)
            {
                DisposePartitions(partitions);
                encodedScene = WebGPUEncodedScene.Empty;
                error = null;
                return true;
            }

            try
            {
                encodedScene = SupportedSubsetSceneResolver.Resolve(partitions, targetBounds, allocator);
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

            // TryAppendRetainedScene checks the limit at each retained scene. Outer draws after the
            // last one also count, so check the final total here.
            if (encoding.ExceedsSegmentedDrawObjectLimit)
            {
                encodedScene = WebGPUEncodedScene.Empty;
                error = SegmentedDrawObjectLimitError;
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
    /// <param name="scopedSceneLayers">
    /// For each command index, whether the BeginLayer command there opens a layer around a retained
    /// scene with ordered work, or <see langword="null"/> when there is no such layer.
    /// </param>
    /// <param name="targetBounds">The target bounds used for target-local coordinate conversion.</param>
    /// <param name="allocator">The allocator used for temporary and packed scene storage.</param>
    /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism used by leaf scene encoding.</param>
    /// <param name="encodedScene">Receives the encoded ordered scene on success.</param>
    /// <param name="error">Receives the failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the scene encoded successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryEncodeOrdered(
        DrawingCommandBatch scene,
        bool[]? scopedSceneLayers,
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
            if (!TryEncodeOrderedOperations(
                scene,
                scopedSceneLayers,
                0,
                scene.CommandCount,
                targetBounds,
                0,
                ref encoding,
                operations,
                ref rangeStart,
                ref nextLayerTargetId,
                out error))
            {
                encodedScene = WebGPUEncodedScene.Empty;
                return false;
            }

            WebGPUSceneRange finalRange = encoding.EndPackedIndependentRange(targetBounds, rangeStart, closeActiveClips: true);
            if (finalRange.FillCount != 0)
            {
                operations.Add(new WebGPUSceneOperation(finalRange));
            }

            // The same check as TryEncode. A range has at most the draw objects of the whole
            // scene, so the check of the whole scene holds for each range dispatch.
            if (encoding.ExceedsSegmentedDrawObjectLimit)
            {
                encodedScene = WebGPUEncodedScene.Empty;
                error = SegmentedDrawObjectLimitError;
                return false;
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
    /// <param name="scopedSceneLayers">
    /// For each command index, whether the BeginLayer command there opens a layer around a retained
    /// scene with ordered work, or <see langword="null"/> when there is no such layer.
    /// </param>
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
        bool[]? scopedSceneLayers,
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
                if (command.Kind == CompositionCommandKind.BeginLayer &&
                    (command.RequiresScopedApply || scopedSceneLayers?[i] == true))
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
                    if (!TryEncodeOrderedOperations(
                        scene,
                        scopedSceneLayers,
                        i + 1,
                        layerEnd,
                        layerBounds,
                        layerTargetId,
                        ref encoding,
                        operations,
                        ref childStart,
                        ref nextLayerTargetId,
                        out error))
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

            if (sceneCommand is RetainedSceneCompositionSceneCommand retainedCommand)
            {
                if (!TryAppendOrderedRetainedScene(
                    retainedCommand,
                    targetBounds,
                    targetId,
                    ref encoding,
                    operations,
                    ref rangeStart,
                    ref nextLayerTargetId,
                    out error))
                {
                    return false;
                }

                continue;
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
    /// Encodes one retained scene command of an ordered scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Directly inside a scoped layer, the retained scene renders as its own passes into the layer
    /// target, in stream order. An Apply there splits the layer content into passes in the same way.
    /// Its operations read its own encoded data. Their origin moves its data coordinates into the
    /// layer target, so the retained data does not change.
    /// </para>
    /// <para>
    /// Inside a layer of the current range that is not scoped, the layer content cannot split into
    /// passes, so the range reads the retained scene in place through the segment table.
    /// </para>
    /// </remarks>
    /// <param name="command">The retained scene command.</param>
    /// <param name="targetBounds">The bounds of the target the current range renders into.</param>
    /// <param name="targetId">The stable identity of that target.</param>
    /// <param name="encoding">The shared mutable scene encoding.</param>
    /// <param name="operations">Receives the ordered scene operations.</param>
    /// <param name="rangeStart">The checkpoint of the currently open packed range. The method updates it when it splits the range.</param>
    /// <param name="nextLayerTargetId">The next stable target identity available for a scoped layer.</param>
    /// <param name="error">Receives the failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the command encoded successfully.</returns>
    private static bool TryAppendOrderedRetainedScene(
        RetainedSceneCompositionSceneCommand command,
        Rectangle targetBounds,
        int targetId,
        ref SupportedSubsetSceneEncoding encoding,
        List<WebGPUSceneOperation> operations,
        ref SceneEncodingCheckpoint rangeStart,
        ref int nextLayerTargetId,
        out string? error)
    {
        if (encoding.OpenLayerCount != 0)
        {
            return encoding.TryAppendRetainedScene(command.Scene, out error);
        }

        // The retained data is in the coordinates of the root target, which the scene bounds must
        // match. The layer target lies inside the root target, so the origin is never negative.
        Rectangle rootTargetBounds = encoding.RootTargetBounds;
        Point origin = new(targetBounds.X - rootTargetBounds.X, targetBounds.Y - rootTargetBounds.Y);
        AddRenderRange(targetBounds, ref encoding, operations, ref rangeStart, ClipBlendMode);
        return AppendRetainedSceneOperations(command.Scene, rootTargetBounds, targetBounds, targetId, origin, operations, ref nextLayerTargetId, out error);
    }

    /// <summary>
    /// Appends the operations that render one retained scene into a scoped layer target.
    /// </summary>
    /// <param name="retainedScene">The retained scene. It must be a WebGPU scene.</param>
    /// <param name="rootTargetBounds">The bounds of the root target of the encoding, which the scene bounds must match.</param>
    /// <param name="targetBounds">The bounds of the layer target.</param>
    /// <param name="targetId">The stable identity of the layer target.</param>
    /// <param name="origin">The position of the layer target in the coordinates of the scene data.</param>
    /// <param name="operations">Receives the ordered scene operations.</param>
    /// <param name="nextLayerTargetId">The next stable target identity available for a scoped layer.</param>
    /// <param name="error">Receives the failure reason when the scene cannot render into the layer.</param>
    /// <returns><see langword="true"/> when the operations were appended.</returns>
    private static bool AppendRetainedSceneOperations(
        DrawingBackendScene retainedScene,
        Rectangle rootTargetBounds,
        Rectangle targetBounds,
        int targetId,
        Point origin,
        List<WebGPUSceneOperation> operations,
        ref int nextLayerTargetId,
        out string? error)
    {
        // The same rules as RenderScene: the scene must come from this backend, and it renders only
        // into a target with its own bounds.
        if (retainedScene is not WebGPUDrawingBackendScene scene)
        {
            error = "The scene is not compatible with the WebGPU drawing backend.";
            return false;
        }

        if (scene.Bounds != rootTargetBounds)
        {
            error = "The target bounds do not match the WebGPU drawing backend scene bounds.";
            return false;
        }

        if (scene.Parts is DrawingBackendScene[] parts)
        {
            // A parts scene renders its parts in order. The call checks each part.
            for (int i = 0; i < parts.Length; i++)
            {
                if (!AppendRetainedSceneOperations(parts[i], rootTargetBounds, targetBounds, targetId, origin, operations, ref nextLayerTargetId, out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        WebGPUEncodedScene encoded = scene.EncodedScene;
        if (!encoded.HasOperations)
        {
            if (encoded.FillCount != 0)
            {
                operations.Add(new WebGPUSceneOperation(WebGPUSceneRange.CreateWholeScene(encoded, targetBounds, origin))
                {
                    RetainedScene = scene
                });
            }

            error = null;
            return true;
        }

        // The operations of an ordered scene keep their order. Those at depth zero render into its
        // root target, which is now the layer target, so they take its bounds and the origin. Its
        // own scoped layers get new target identities and keep their own bounds. An operation that
        // already reads a retained scene of its own keeps that scene, because its range indexes
        // the data of that scene.
        ReadOnlySpan<WebGPUSceneOperation> source = encoded.OrderedOperations;
        int[] targetIds = new int[encoded.OrderedTargetCount];
        targetIds[0] = targetId;
        int depth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            WebGPUSceneOperation operation = source[i];
            switch (operation.Kind)
            {
                case WebGPUSceneOperationKind.RenderRange:
                    operations.Add(new WebGPUSceneOperation(depth == 0 ? operation.Range.WithTarget(targetBounds, origin) : operation.Range)
                    {
                        RetainedScene = operation.RetainedScene ?? scene
                    });
                    break;

                case WebGPUSceneOperationKind.BeginLayer:
                    targetIds[operation.LayerTargetId] = nextLayerTargetId++;
                    operations.Add(new WebGPUSceneOperation(operation.LayerBounds, targetIds[operation.LayerTargetId], targetIds[operation.ParentTargetId])
                    {
                        RetainedScene = operation.RetainedScene ?? scene
                    });

                    depth++;
                    break;

                case WebGPUSceneOperationKind.EndLayer:
                    depth--;
                    operations.Add(new WebGPUSceneOperation(
                        depth == 0 ? operation.Range.WithTarget(targetBounds, origin) : operation.Range,
                        targetIds[operation.LayerTargetId],
                        targetIds[operation.ParentTargetId])
                    {
                        RetainedScene = operation.RetainedScene ?? scene
                    });
                    break;

                case WebGPUSceneOperationKind.Apply:
                    WebGPUApplySceneItem apply = operation.Apply!;
                    operations.Add(new WebGPUSceneOperation(new WebGPUApplySceneItem(
                        apply.Operation,
                        apply.Effect,
                        apply.InputRect,
                        apply.OutputRect,
                        apply.ReadOffset,
                        depth == 0 ? apply.DrawRange.WithTarget(targetBounds, origin) : apply.DrawRange))
                    {
                        RetainedScene = operation.RetainedScene ?? scene
                    });
                    break;

                case WebGPUSceneOperationKind.ShaderEffect:
                    WebGPUShaderEffectSceneItem effect = operation.ShaderEffect!;
                    operations.Add(new WebGPUSceneOperation(new WebGPUShaderEffectSceneItem(
                        effect.Effect,
                        effect.InputRect,
                        effect.OutputRect,
                        effect.ReadOffset,
                        depth == 0 ? effect.DrawRange.WithTarget(targetBounds, origin) : effect.DrawRange))
                    {
                        RetainedScene = operation.RetainedScene ?? scene
                    });
                    break;
            }
        }

        error = null;
        return true;
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
            ? new GpuImageSource(
                new Size(outputRect.Width, outputRect.Height),
                brushOffset,
                WrapMode.Repeat,
                WrapMode.Repeat,
                WebGPUShaderEffectWorkingTexture.Descriptor)
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
            : new WebGPUSceneOperation(
                new WebGPUApplySceneItem(source.ApplyOperation, source.ApplyEffect, outputRect, outputRect, source.ApplyWriteBackOffset, drawRange));
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
            rasterizationMode);
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
        // The CPU backend inflates by the same radius, so both backends get the same interest
        // rectangle and the same brush origin.
        float inflate = pen.GetOutlineRadius(widthScale);
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
        /// <param name="estimatedPtclWords">The accumulated PTCL word upper bound.</param>
        /// <param name="estimatedBinFootprint">The accumulated per-(draw, bin) record upper bound.</param>
        /// <param name="insertionCount">The number of retained scenes recorded so far.</param>
        /// <param name="estimatedPathTiles">The accumulated path-tile bound.</param>
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
            long estimatedPtclWords,
            long estimatedBinFootprint,
            int insertionCount,
            long estimatedPathTiles)
        {
            this.InsertionCount = insertionCount;
            this.EstimatedPathTiles = estimatedPathTiles;
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
            this.EstimatedPtclWords = estimatedPtclWords;
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
        /// Gets the accumulated PTCL word upper bound.
        /// </summary>
        public long EstimatedPtclWords { get; }

        /// <summary>
        /// Gets the accumulated per-(draw, bin) record upper bound.
        /// </summary>
        public long EstimatedBinFootprint { get; }

        /// <summary>
        /// Gets the number of retained scenes recorded so far.
        /// </summary>
        public int InsertionCount { get; }

        /// <summary>
        /// Gets the accumulated path-tile bound.
        /// </summary>
        public long EstimatedPathTiles { get; }
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
                    int commandEnd = partitionBoundaries is null
                        ? ((partitionIndex + 1) * commandCount) / partitionCount
                        : partitionBoundaries[partitionIndex + 1];

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
        private readonly MemoryAllocator allocator;
        private bool hasLastStyle;
        private bool streamsDetached;
        private bool gradientPixelsDetached;
        private bool pathGradientDataDetached;
        private bool profileStorageDetached;
        private long estimatedPathRowCount;

        // CPU-side scratch-demand estimates accumulated per draw so the first GPU attempt can be
        // seeded near true demand instead of discovering it through the overflow retry protocol.
        // estimatedTileCrossings bounds tile-boundary crossings (segment/seg-count/path-tile
        // records); estimatedPtclWords bounds the dynamic PTCL words those crossings can write;
        // estimatedBinFootprint bounds per-(draw, bin) binning records.
        private long estimatedTileCrossings;
        private long estimatedPtclWords;
        private long estimatedBinFootprint;

        // The path-tile bound: the tiles of each draw's clipped bounds.
        private long estimatedPathTiles;

        // The clip and layer nesting depth in stream order, and the union region of each depth,
        // starting at depth 1, in the tile grid of the current range.
        private int blendDepth;
        private List<Rectangle>? blendLevels;

        // The row bounds of the current range, and every row bounds instance this encoding
        // created. The resolved scene or partition takes ownership of the list.
        private WebGPUSceneRowBounds? rowBounds;
        private List<WebGPUSceneRowBounds>? ownedRowBounds;
        private bool rowBoundsDetached;

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

        // The encoded data of every range is in the coordinates of the root target, so retained
        // scenes of the root target can be read in place in any range. A range for another target,
        // for example a scoped layer, has that target position as its origin (WebGPUSceneRange.Origin).
        private Rectangle rootTargetBounds;

        // The target of the current range, in the coordinates of the root target. The GPU tile grid
        // of the range starts at its top-left corner.
        private Rectangle rangeDataBounds;
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
            this.allocator = allocator;
            this.PathTags = new OwnedStream<byte>(allocator, plan.PathTagCapacity);
            this.ProfileFills = null;
            this.ProfileStorage = null;
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
            this.SolidColors = [];
            this.GradientRamps = [];
            this.Insertions = null;
            this.SegmentedRanges = null;
            this.retainedDrawTagCount = 0;
            this.rangeSegmentEntryCount = 0;

            // A scene with no retained scene is one piece of every stream.
            this.SegmentCount = 1;
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
            this.profileStorageDetached = false;
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
            this.rangeDataBounds = new Rectangle(0, 0, rootTargetBounds.Width, rootTargetBounds.Height);
            this.lastTransform = GpuSceneTransform.Identity;
            this.activeClips = null;
            this.openLayerBounds = null;
            this.estimatedPathRowCount = 0;
            this.estimatedTileCrossings = 0;
            this.estimatedPtclWords = 0;
            this.estimatedBinFootprint = 0;
            this.estimatedPathTiles = 0;
            this.blendDepth = 0;
            this.blendLevels = null;
            this.VisibleFillCount = 0;

            this.PathTags.Add(PackPathTag(PathTag.Transform));
            AppendIdentityTransform(ref this.Transforms);
        }

        /// <summary>
        /// Gets or sets the encoded path-tag byte stream.
        /// </summary>
        public OwnedStream<byte> PathTags;

        /// <summary>
        /// Aliased fills that need profile tags, in path-data order.
        /// </summary>
        /// <remarks>
        /// A partition creates at most one list. Each list element is a value type that stores a
        /// geometry reference, a view into the partition profile arena, and a translation. Repeated
        /// geometry uses the same arena entry, and no profile buffer is created for each fill.
        /// </remarks>
        public List<GpuProfileFillRecord>? ProfileFills;

        /// <summary>
        /// Gets or sets the partition-owned profile arena created by the first compatible aliased fill.
        /// </summary>
        public LinearGeometryProfileStorage? ProfileStorage;

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
        /// Gets or sets the deferred solid color descriptors specialized for the render target pixel type.
        /// </summary>
        public List<GpuSolidColorDescriptor> SolidColors;

        /// <summary>
        /// Gets or sets the deferred gradient ramp descriptors specialized for the render target pixel type.
        /// </summary>
        public List<GpuGradientRampDescriptor> GradientRamps;

        /// <summary>
        /// Gets or sets the retained scenes read in place inside layers, in stream order, or
        /// <see langword="null"/> when the encoding has none.
        /// </summary>
        public List<WebGPUSceneInsertion>? Insertions;

        /// <summary>
        /// Gets or sets the ranges that read retained scenes in place, in stream order, or
        /// <see langword="null"/> when no range does. The resolver writes one segment table for each.
        /// </summary>
        public List<WebGPUSceneRange>? SegmentedRanges;

        // The draw tags of the retained scenes in Insertions, which the virtual draw-tag stream adds.
        private int retainedDrawTagCount;

        // The table entries of the ranges in SegmentedRanges, which the range tables of the scene hold.
        private int rangeSegmentEntryCount;

        /// <summary>
        /// Gets the scene segment count that the retained scenes recorded so far give.
        /// </summary>
        public int SegmentCount { get; private set; }

        /// <summary>
        /// Gets the virtual draw-tag count: the draw tags of this encoding and of its retained scenes.
        /// </summary>
        public readonly int JoinedDrawTagCount => this.DrawTags.Count + this.retainedDrawTagCount;

        /// <summary>
        /// Gets a value indicating whether the encoding reads retained scenes in place and has more
        /// draw objects than coarse can pack.
        /// </summary>
        public readonly bool ExceedsSegmentedDrawObjectLimit
            => this.Insertions is not null && this.JoinedDrawTagCount >= SegmentedDrawObjectLimit;

        /// <summary>
        /// Gets the number of table entries of the ranges that read retained scenes in place.
        /// </summary>
        public readonly int RangeSegmentEntryCount => this.rangeSegmentEntryCount;

        /// <summary>
        /// Gets the number of layers open in the current range, which encode inline as clip groups.
        /// </summary>
        public readonly int OpenLayerCount => this.openLayerBounds?.Count ?? 0;

        /// <summary>
        /// Gets the bounds of the root target, whose coordinates every range of the encoding uses.
        /// </summary>
        public readonly Rectangle RootTargetBounds => this.rootTargetBounds;

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
        /// scene's final lines. Bounds the GPU segment, segment-count, and path-tile demand.
        /// </summary>
        public readonly long EstimatedTileCrossings => this.estimatedTileCrossings;

        /// <summary>
        /// Gets a value indicating whether the tile grid of the current range has the alignment of the
        /// tile grid of the root target.
        /// </summary>
        /// <remarks>
        /// The encoder counts tile crossings on the grid of the root target. When the grids do not
        /// align, each line can cross one more tile on each axis.
        /// </remarks>
        private readonly bool IsRangeTileAligned
            => this.rangeDataBounds.X % TileWidth == 0 && this.rangeDataBounds.Y % TileHeight == 0;

        /// <summary>
        /// Gets the CPU-side upper-bound estimate of the dynamic PTCL words the scene's tile
        /// crossings can write: one fill command plus the draw's paint command per crossing.
        /// </summary>
        public readonly long EstimatedPtclWords => this.estimatedPtclWords;

        /// <summary>
        /// Gets the CPU-side upper-bound estimate of per-(draw, bin) binning records.
        /// </summary>
        public readonly long EstimatedBinFootprint => this.estimatedBinFootprint;

        /// <summary>
        /// Gets the path-tile bound: the tiles of each draw's clipped bounds.
        /// </summary>
        public readonly long EstimatedPathTiles => this.estimatedPathTiles;

        /// <summary>
        /// Gets the union region of each blend depth, starting at depth 1, or <see langword="null"/>.
        /// </summary>
        public readonly List<Rectangle>? BlendLevels => this.blendLevels;

        /// <summary>
        /// Gets the row bounds of the current range, or <see langword="null"/> when it has no draws.
        /// </summary>
        public readonly WebGPUSceneRowBounds? RowBounds => this.rowBounds;

        /// <summary>
        /// Takes ownership of every row bounds instance that this encoding created.
        /// </summary>
        /// <returns>The instances, or an empty array.</returns>
        public WebGPUSceneRowBounds[] DetachRowBounds()
        {
            this.rowBoundsDetached = true;
            return this.ownedRowBounds is { } owned ? [.. owned] : [];
        }

        /// <summary>
        /// Gets the row bounds of the current range, and creates them for the range grid when the
        /// range has none yet.
        /// </summary>
        /// <returns>The row bounds.</returns>
        private WebGPUSceneRowBounds GetOrCreateRowBounds()
        {
            if (this.rowBounds is null)
            {
                this.rowBounds = new WebGPUSceneRowBounds(DivideRoundUp(this.rangeDataBounds.Height, TileHeight), this.rangeDataBounds.Y);
                (this.ownedRowBounds ??= []).Add(this.rowBounds);
            }

            return this.rowBounds;
        }

        /// <summary>
        /// Gets a value indicating whether the encoding produced no fill work. A retained scene is
        /// recorded only when it has fills.
        /// </summary>
        public readonly bool IsEmpty => this.FillCount == 0 && this.Insertions is null;

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
                this.estimatedPtclWords,
                this.estimatedBinFootprint,
                this.Insertions?.Count ?? 0,
                this.estimatedPathTiles);

        /// <summary>
        /// Starts a range that can be decoded independently by the staged pipeline.
        /// </summary>
        /// <param name="targetBounds">The target bounds for the independent range.</param>
        private void BeginIndependentRange(Rectangle targetBounds)
        {
            this.PadPathTagsToSegmentBoundary();
            this.rangeDataBounds = ToTargetLocal(targetBounds, this.rootTargetBounds);

            // The row bounds use the grid of one range. The new range starts its own.
            this.rowBounds = null;
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

            // Each packed range is scanned from its own path-tag base, but path_lowering.wgsl
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
            return this.CreateRange(start, this.CaptureCheckpoint(), targetBounds);
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
        /// A range that holds retained scenes gets joined counts and a segment table of its own.
        /// </remarks>
        /// <param name="start">The checkpoint captured at the start of the range.</param>
        /// <param name="end">The checkpoint captured at the end of the range.</param>
        /// <param name="targetBounds">The target bounds for the range.</param>
        /// <returns>The created scene range.</returns>
        private WebGPUSceneRange CreateRange(
            in SceneEncodingCheckpoint start,
            in SceneEncodingCheckpoint end,
            Rectangle targetBounds)
        {
            int pathTagByteCount = end.PathTagByteCount - start.PathTagByteCount;
            int drawTagCount = end.DrawTagCount - start.DrawTagCount;
            int drawDataWordCount = end.DrawDataWordCount - start.DrawDataWordCount;
            SupportedSubsetSceneResolver.JoinedSceneCounts counts = new(
                end.FillCount - start.FillCount,
                end.PathCount - start.PathCount,
                end.LineCount - start.LineCount,
                end.ClipCount - start.ClipCount,
                end.InfoWordCount - start.InfoWordCount,
                drawTagCount,
                drawDataWordCount,
                checked((int)(end.EstimatedPathRowCount - start.EstimatedPathRowCount)),
                end.EstimatedTileCrossings - start.EstimatedTileCrossings,
                end.EstimatedPtclWords - start.EstimatedPtclWords,
                end.EstimatedBinFootprint - start.EstimatedBinFootprint,
                end.EstimatedPathTiles - start.EstimatedPathTiles);

            WebGPUSceneRangeSegments? segments = null;
            int insertionCount = end.InsertionCount - start.InsertionCount;
            if (insertionCount != 0)
            {
                // The range starts on a scan-segment boundary and ends padded to one, so its own
                // path tags are whole words. Each retained scene adds its virtual path-tag words.
                ReadOnlySpan<WebGPUSceneInsertion> insertions = CollectionsMarshal.AsSpan(this.Insertions).Slice(start.InsertionCount, insertionCount);
                int pathTagWordCount = pathTagByteCount / 4;
                for (int i = 0; i < insertions.Length; i++)
                {
                    WebGPUEncodedScene retained = insertions[i].Scene.EncodedScene;
                    counts.Add(retained);
                    pathTagWordCount = checked(pathTagWordCount + retained.VirtualPathTagWordCount);
                }

                int segmentCount = SupportedSubsetSceneResolver.SceneSegmentTableWriter.CountSegments(insertions);
                segments = new WebGPUSceneRangeSegments(
                    start.InsertionCount,
                    insertionCount,
                    this.rangeSegmentEntryCount,
                    segmentCount,
                    pathTagByteCount / 4,
                    drawTagCount,
                    drawDataWordCount);

                this.rangeSegmentEntryCount = checked(this.rangeSegmentEntryCount + segmentCount);
                pathTagByteCount = checked(pathTagWordCount * 4);
            }

            // The range takes the blend depth regions and the row bounds recorded since the last
            // range, so the next range starts its own.
            long blendSpillSlots = WebGPUEncodedScene.GetBlendSpillSlots(CollectionsMarshal.AsSpan(this.blendLevels), int.MinValue, int.MaxValue);
            this.blendLevels?.Clear();
            WebGPUSceneRowBounds? rangeRowBounds = this.rowBounds;
            this.rowBounds = null;

            WebGPUSceneRange range = new(
                targetBounds,
                start.PathTagByteCount / 4,
                pathTagByteCount,
                start.PathDataWordCount,
                end.PathDataWordCount - start.PathDataWordCount,
                start.DrawTagCount,
                counts.DrawTagCount,
                start.DrawDataWordCount,
                counts.DrawDataWordCount,
                start.TransformWordCount,
                end.TransformWordCount - start.TransformWordCount,
                start.StyleWordCount,
                end.StyleWordCount - start.StyleWordCount,
                counts.InfoWordCount,
                counts.PathCount,
                counts.ClipCount,
                counts.FillCount,
                counts.LineCount,
                counts.EstimatedPathRowCount,
                counts.EstimatedTileCrossings,
                counts.EstimatedPtclWords,
                counts.EstimatedBinFootprint,
                segments)
            {
                EstimatedPathTiles = counts.EstimatedPathTiles,
                EstimatedBlendSpillSlots = blendSpillSlots,
                RowBounds = rangeRowBounds
            };

            // The data is in the coordinates of the root target. A range for another target, such
            // as a scoped layer, moves the data by the position of that target in those coordinates.
            Point origin = this.rangeDataBounds.Location;
            if (origin != Point.Empty)
            {
                range = range.WithTarget(targetBounds, origin);
            }

            if (segments is not null)
            {
                (this.SegmentedRanges ??= []).Add(range);
            }

            return range;
        }

        /// <summary>
        /// Disposes all owned stream storage that has not already been detached.
        /// </summary>
        public void Dispose()
        {
            if (!this.profileStorageDetached)
            {
                this.ProfileStorage?.Dispose();
            }

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

            if (!this.rowBoundsDetached && this.ownedRowBounds is { } ownedRowBounds)
            {
                foreach (WebGPUSceneRowBounds bounds in ownedRowBounds)
                {
                    bounds.Dispose();
                }
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
        /// Marks the profile arena as detached so disposal does not release partition-owned data twice.
        /// </summary>
        public void MarkProfileStorageDetached() => this.profileStorageDetached = true;

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
                else if (command is RetainedSceneCompositionSceneCommand sceneCommand)
                {
                    if (!this.TryAppendRetainedScene(sceneCommand.Scene, out error))
                    {
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Records one retained scene inside a layer, or each part of a parts scene in order, as gaps
        /// at the current position of every stream.
        /// </summary>
        /// <remarks>
        /// The backend renders a retained scene outside every layer as its own pass, so only a scene
        /// inside a layer reaches the encoder. The retained streams are not copied. The resolver
        /// builds the segment table from the gap positions, and the shaders read the retained scene
        /// in place (see scene_segment.wgsl).
        /// </remarks>
        /// <param name="scene">The retained scene.</param>
        /// <param name="error">The error message when the scene cannot be read in place.</param>
        /// <returns><see langword="true"/> when the scene was recorded.</returns>
        public bool TryAppendRetainedScene(DrawingBackendScene scene, out string? error)
        {
            if (scene is not WebGPUDrawingBackendScene webGPUScene)
            {
                error = "The scene is not compatible with the WebGPU drawing backend.";
                return false;
            }

            // A retained scene uses the coordinates of its own target, so it must match the root
            // target of this encoding, the same as RenderScene requires. Every range of the
            // encoding uses those coordinates (see rootTargetBounds).
            if (webGPUScene.Bounds != this.rootTargetBounds)
            {
                error = "The target bounds do not match the WebGPU drawing backend scene bounds.";
                return false;
            }

            // A parts scene renders its parts in order, so its parts are read in place in order.
            // A part with ordered operations makes the parts scene require a scoped layer, so
            // here every part is plain.
            if (webGPUScene.Parts is DrawingBackendScene[] parts)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!this.TryAppendRetainedScene(parts[i], out error))
                    {
                        return false;
                    }
                }

                error = null;
                return true;
            }

            if (webGPUScene.EncodedScene.HasOperations)
            {
                error = "A WebGPU retained scene with ordered operations cannot render inside a layer that is not scoped.";
                return false;
            }

            WebGPUEncodedScene retained = webGPUScene.EncodedScene;
            if (retained.FillCount == 0)
            {
                error = null;
                return true;
            }

            // coarse.wgsl packs the segment index in 8 bits and the draw object index in 24 bits.
            // One retained scene adds its own segments and one more outer piece.
            int segmentCount = checked(this.SegmentCount + Math.Max(retained.SegmentCount, 1) + 1);
            int drawObjectCount = checked(this.JoinedDrawTagCount + retained.DrawTagCount);
            if (segmentCount > MaxSceneSegmentCount)
            {
                error = $"A WebGPU scene can read at most {MaxSceneSegmentCount} scene segments in one encoding.";
                return false;
            }

            if (drawObjectCount >= SegmentedDrawObjectLimit)
            {
                error = SegmentedDrawObjectLimitError;
                return false;
            }

            this.SegmentCount = segmentCount;
            this.retainedDrawTagCount = checked(this.retainedDrawTagCount + retained.DrawTagCount);

            // The clips and layers of the retained scene nest inside the open clips and layers of
            // this encoding, so its blend depths start at the current depth.
            this.AddRetainedBlendDepthRegions(retained.BlendDepthRegions);

            // The retained crossings are counted on the grid of the root target. When the range grid
            // has another alignment, each retained line can cross one more tile on each axis.
            long alignmentCrossings = this.IsRangeTileAligned ? 0 : 2L * retained.LineCount;
            this.estimatedTileCrossings += alignmentCrossings;

            // The rows of the retained draws are not tracked here, so their totals count in every
            // chunk window of this range.
            this.GetOrCreateRowBounds().AddRetained(
                retained.TotalPathRowCount,
                retained.EstimatedPathTiles,
                retained.EstimatedTileCrossings + alignmentCrossings,
                retained.EstimatedPtclWords);

            // A gap starts on a path-tag word. The None bytes add nothing to the tag monoid.
            int padding = (4 - (this.PathTags.Count & 3)) & 3;
            if (padding != 0)
            {
                Span<byte> paddedTags = this.PathTags.GetAppendSpan(padding);
                paddedTags.Clear();
                this.PathTags.Advance(padding);
            }

            (this.Insertions ??= []).Add(new WebGPUSceneInsertion(
                webGPUScene,
                this.PathTags.Count / 4,
                this.PathData.Count,
                this.Transforms.Count,
                this.Styles.Count,
                this.DrawTags.Count,
                this.DrawData.Count));

            // The last transform and style in the virtual stream are now those of the retained
            // scene. Restart both, so the next outer path emits its own records and does not skip
            // them as equal to the cached outer values.
            this.PathTags.EnsureAdditionalCapacity(1);
            this.Transforms.EnsureAdditionalCapacity(TransformWordCount);
            this.PathTags.Add(PackPathTag(PathTag.Transform));
            AppendIdentityTransform(ref this.Transforms);
            this.lastTransform = GpuSceneTransform.Identity;
            this.hasLastStyle = false;

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
        /// <param name="geometry">Optional pre-linearized geometry for <paramref name="path"/>.</param>
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
                GetResidualStretch(segmentResidual),
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
                GetResidualStretch(polylineResidual),
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

            // The draw-flags aliased bit carries the rasterization mode per fill. A flush can mix
            // antialiased and centre-sampled fills without a flush-wide rasterization state.
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
        /// Returns whether the transform preserves the coordinate axes with unit scale, the
        /// condition under which geometry-space profile extents translate directly into
        /// absolute target space.
        /// </summary>
        /// <param name="transform">The scene transform to classify.</param>
        /// <returns><see langword="true"/> when the transform is a pure translation; otherwise <see langword="false"/>.</returns>
        private static bool IsProfileCompatibleTransform(in GpuSceneTransform transform)
            => transform.M11 == 1F && transform.M22 == 1F && transform.M12 == 0F && transform.M21 == 0F &&
                transform.M14 == 0F && transform.M24 == 0F && transform.M44 == 1F;

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
                out long geometryTileCrossings,
                out long geometryRowCrossings);

            // EncodePath counts tile crossings on the grid of the root target, before the GPU
            // transform. A translation, or a range grid with another alignment, moves the tile
            // alignment, so each line can cross one more tile on each axis. Any other transform
            // changes the lengths, so the bound falls back to the bounding box.
            GpuSceneTransform pathTransform = this.lastTransform;
            if (IsProfileCompatibleTransform(pathTransform))
            {
                if (pathTransform.Tx != 0F || pathTransform.Ty != 0F || !this.IsRangeTileAligned)
                {
                    geometryTileCrossings += 2L * geometryLineCount;
                }
            }
            else
            {
                geometryTileCrossings = -1;
            }

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
            RowSpanSource spanSource = this.CreateRowSpanSource(geometry, default, default, pathDataOffset, 0F, geometryRowCrossings, 0);
            this.AccumulateDrawRowEstimate(
                command.RasterizerOptions.Interest,
                geometryLineCount,
                geometryTileCrossings,
                GetPtclPaintWords(drawTag),
                spanSource);

            this.FillCount++;
            this.PathCount += encodedPathCount;

            // Profile ranges use the geometry's original X and Y axes. A translation can be added
            // when the scene is packed. Scale, rotation, or shear would require new ranges, so
            // those draws keep sentinel tags and do not apply the terminating-tip test.
            if (command.RasterizerOptions.RasterizationMode == RasterizationMode.Aliased && IsProfileCompatibleTransform(this.lastTransform))
            {
                LinearGeometryProfiles profiles = (this.ProfileStorage ??= new LinearGeometryProfileStorage(this.allocator)).GetOrAdd(geometry);
                (this.ProfileFills ??= []).Add(
                    new GpuProfileFillRecord(
                        pathDataCheckpoint,
                        geometry,
                        profiles,
                        this.lastTransform.Tx + (pathDataOffset.X - this.rootTargetBounds.X),
                        this.lastTransform.Ty + (pathDataOffset.Y - this.rootTargetBounds.Y)));
            }

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
                    this.SolidColors,
                    this.GradientRamps,
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
            float residualStretch = GetResidualStretch(MatrixUtilities.GetResidual(scale, command.Transform));
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
                residualStretch,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out long geometryTileCrossings,
                out long geometryRowCrossings,
                out long geometryEndCrossings);

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
            float outlineRadius = GetStrokeOutlineRadius(pen, widthScale);
            RowSpanSource spanSource = this.CreateRowSpanSource(
                geometry,
                default,
                default,
                pathDataOffset,
                outlineRadius,
                geometryRowCrossings,
                geometryEndCrossings);

            this.AccumulateDrawRowEstimate(
                command.RasterizerOptions.Interest,
                geometryLineCount,
                geometryTileCrossings,
                GetPtclPaintWords(drawTag),
                spanSource);

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
                this.SolidColors,
                this.GradientRamps,
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
        /// <param name="residualStretch">The largest length scale of the residual transform the GPU applies to the points.</param>
        /// <param name="geometry">The prepared open centerline geometry.</param>
        private void AppendExplicitStroke(
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point pathDataOffset,
            Rectangle brushBounds,
            Pen pen,
            float widthScale,
            float residualStretch,
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
                residualStretch,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out long geometryTileCrossings,
                out long geometryRowCrossings,
                out long geometryEndCrossings);

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
            float outlineRadius = GetStrokeOutlineRadius(pen, widthScale);
            RowSpanSource spanSource = this.CreateRowSpanSource(
                geometry,
                default,
                default,
                pathDataOffset,
                outlineRadius,
                geometryRowCrossings,
                geometryEndCrossings);

            this.AccumulateDrawRowEstimate(rasterizerOptions.Interest, geometryLineCount, geometryTileCrossings, GetPtclPaintWords(drawTag), spanSource);
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
                this.SolidColors,
                this.GradientRamps,
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
        /// <param name="residualStretch">The largest length scale of the residual transform the GPU applies to the points.</param>
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
            float residualStretch,
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
                residualStretch,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out long geometryTileCrossings,
                out long geometryRowCrossings,
                out long geometryEndCrossings);

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
            float outlineRadius = GetStrokeOutlineRadius(pen, widthScale);
            RowSpanSource spanSource = this.CreateRowSpanSource(
                null,
                (Vector2)start,
                (Vector2)end,
                pathDataOffset,
                outlineRadius,
                geometryRowCrossings,
                geometryEndCrossings);

            this.AccumulateDrawRowEstimate(rasterizerOptions.Interest, geometryLineCount, geometryTileCrossings, GetPtclPaintWords(drawTag), spanSource);
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
                this.SolidColors,
                this.GradientRamps,
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
            uint style2 = descriptor.EdgeMode == DrawingClipEdgeMode.Hard
                ? GpuSceneDrawTag.FillInfoFlagsAliasedBit
                : 0U;
            const uint style3 = 0U;
            const uint style4 = 0U;
            const uint style5 = 0U;

            // Clip coverage must not be restricted by a raster interest rectangle; a zero interest
            // makes binning intersect the clip path away. Use the full target region of the range.
            uint style6 = BitcastSingle(this.rangeDataBounds.Left);
            uint style7 = BitcastSingle(this.rangeDataBounds.Top);
            uint style8 = BitcastSingle(this.rangeDataBounds.Right);
            uint style9 = BitcastSingle(this.rangeDataBounds.Bottom);

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
                        out _,
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
            this.blendDepth++;

            // The clip interest is the whole range, so the GPU box of the clip is the box of its
            // path, and it can be larger than clipBounds:
            // - binning adds one pixel to the right edge of an aliased (Hard) path;
            // - the GPU box is floor and ceil of the f32 points after the GPU transform, and
            //   clipBounds is floor and ceil of the transformed path bounds.
            // One pixel on each side holds both.
            this.AccumulateDrawRowEstimateLocal(
                Rectangle.Inflate(clipBounds, 1, 1),
                clipLineCount,
                -1,
                GetPtclPaintWords(GpuSceneDrawTag.EndClip),
                true,
                RowSpanSource.None);

            this.PathCount += encodedPathCount;
            this.LineCount += clipLineCount;
            this.InfoWordCount += (int)GpuSceneDrawTag.Map(GpuSceneDrawTag.BeginClip).InfoOffset;
            this.DrawTags.Add(GpuSceneDrawTag.BeginClip);
            AppendClipBeginData(descriptor.Operation, clipBlendMode, ref this.DrawData);
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
            this.blendDepth++;
            this.AccumulateDrawRowEstimateLocal(layerBounds, clipLineCount, -1, GetPtclPaintWords(GpuSceneDrawTag.EndClip), true, RowSpanSource.None);
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
        /// <param name="lineCount">The draw object's linearized or GPU-expanded line count.</param>
        /// <param name="tileCrossings">
        /// The summed per-line tile-crossing bound of the geometry (fills and strokes), or a negative value
        /// to fall back to the bounding-box diagonal bound (clips).
        /// </param>
        /// <param name="paintWords">The PTCL word count of the draw's paint command.</param>
        /// <param name="spanSource">The source segments for the row-span tile bound.</param>
        private void AccumulateDrawRowEstimate(Rectangle absoluteInterest, int lineCount, long tileCrossings, int paintWords, in RowSpanSource spanSource)
            => this.AccumulateDrawRowEstimateLocal(
                ToTargetLocal(absoluteInterest, this.rootTargetBounds),
                lineCount,
                tileCrossings,
                paintWords,
                false,
                spanSource);

        /// <summary>
        /// Returns the row-span source of a draw whose points the GPU only translates.
        /// </summary>
        /// <param name="geometry">The source geometry, or <see langword="null"/> for one segment.</param>
        /// <param name="start">The segment start when <paramref name="geometry"/> is <see langword="null"/>.</param>
        /// <param name="end">The segment end when <paramref name="geometry"/> is <see langword="null"/>.</param>
        /// <param name="pathDataOffset">The destination offset that path data applies directly.</param>
        /// <param name="radius">The radius around the segments that contains the GPU lines.</param>
        /// <param name="crossingsPerRow">An upper bound for the tile crossings of the GPU body lines in one tile row.</param>
        /// <param name="endCrossings">The tile crossings of the other GPU lines, such as stroke joins and caps.</param>
        /// <returns>The source, or <see cref="RowSpanSource.None"/> when the transform is not a translation.</returns>
        private readonly RowSpanSource CreateRowSpanSource(
            LinearGeometry? geometry,
            Vector2 start,
            Vector2 end,
            Point pathDataOffset,
            float radius,
            long crossingsPerRow,
            long endCrossings)
        {
            GpuSceneTransform transform = this.lastTransform;
            if (!IsProfileCompatibleTransform(transform))
            {
                return RowSpanSource.None;
            }

            Vector2 translate = new(
                pathDataOffset.X - this.rootTargetBounds.X + transform.Tx,
                pathDataOffset.Y - this.rootTargetBounds.Y + transform.Ty);

            return new RowSpanSource(geometry, start, end, translate, radius, crossingsPerRow, endCrossings);
        }

        /// <summary>
        /// Adds one draw object's footprint, clipped to the range data bounds and the open layers, to
        /// the scratch estimates of the range.
        /// </summary>
        /// <remarks>
        /// The footprint is measured on the tile grid of the range. It adds these values:
        /// <list type="bullet">
        /// <item><description>the tile-row span for path rows;</description></item>
        /// <item><description>the tile-crossing bound for segments;</description></item>
        /// <item><description>the row-span bound for path tiles;</description></item>
        /// <item><description>the bin footprint for binning records;</description></item>
        /// <item><description>the PTCL words, the row bounds and the blend depth regions.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="localBounds">The draw object's root-target-local raster interest bounds.</param>
        /// <param name="lineCount">The draw object's linearized or GPU-expanded line count.</param>
        /// <param name="tileCrossings">
        /// The summed per-line tile-crossing bound of the geometry (fills and strokes), or a negative value
        /// to fall back to the bounding-box diagonal bound (clips).
        /// </param>
        /// <param name="paintWords">The PTCL word count of the draw's paint command.</param>
        /// <param name="isClip">Whether the draw opens a clip or a layer, which the GPU also closes with an EndClip.</param>
        /// <param name="spanSource">The source segments for the row-span tile bound, or <see cref="RowSpanSource.None"/>.</param>
        private void AccumulateDrawRowEstimateLocal(
            Rectangle localBounds,
            int lineCount,
            long tileCrossings,
            int paintWords,
            bool isClip,
            in RowSpanSource spanSource)
        {
            Rectangle clippedBounds = Rectangle.Intersect(localBounds, this.rangeDataBounds);

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

            // The tile grid of the range starts at the range target.
            clippedBounds.Offset(-this.rangeDataBounds.X, -this.rangeDataBounds.Y);

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

            // tile_alloc gives each row the span between its first and last touched tile, so the
            // tiles of the clipped bounds bound the path tiles. When that area is larger than the
            // crossings plus the rows, the geometry is sparse and the row-span bound can be smaller.
            // The row walk costs about one step for each row that a segment spans, so dense draws,
            // whose area is close to their crossings, skip it.
            int tileX0 = clippedBounds.Left / TileWidth;
            int tileX1 = DivideRoundUp(clippedBounds.Right, TileWidth);
            long tiles = (long)(tileX1 - tileX0) * rowCount;

            // coarse writes for every covered tile: a fill command (PtclFillWords) on a tile with
            // segments, a solid command (2 words) otherwise, and the paint command. A clip or a
            // layer also writes its PtclClipWords.
            int ptclWordsPerTile = PtclFillWords + paintWords + (isClip ? PtclClipWords : 0);

            // A stroke outline is thin, so its row spans can be far smaller than its bbox. A fill
            // is usually solid, so its row spans are close to the bbox width and it keeps the area
            // bound. A stroke with a positive outline radius and a sparse bbox gets a row bound:
            // - One segment: the capsule width in one row, the same in every row, O(1).
            // - A polyline: the row walk, which adds its span of each row to the row bounds.
            // A chunk window then gets the spans of its own rows instead of the bbox width.
            WebGPUSceneRowBounds rowBounds = this.GetOrCreateRowBounds();
            int areaWidthTiles = tileX1 - tileX0;
            if (spanSource.IsValid && spanSource.Radius > 0F && tiles > crossings + rowCount)
            {
                if (spanSource.Geometry is null)
                {
                    areaWidthTiles = Math.Min(areaWidthTiles, GetSegmentRowSpanTiles(spanSource.Start, spanSource.End, spanSource.Radius));
                    tiles = Math.Min(tiles, (long)areaWidthTiles * rowCount);
                }
                else if (spanSource.Geometry.Contours is [{ SegmentCount: 1, PointCount: 2 } contour])
                {
                    // A two-point polyline is one segment, so it uses the same O(1) bound.
                    IReadOnlyList<PointF> points = spanSource.Geometry.Points;
                    areaWidthTiles = Math.Min(
                        areaWidthTiles,
                        GetSegmentRowSpanTiles(points[contour.PointStart], points[contour.PointStart + 1], spanSource.Radius));

                    tiles = Math.Min(tiles, (long)areaWidthTiles * rowCount);
                }
                else if (tiles >= RowWalkMinimumTiles)
                {
                    Vector2 gridTranslate = spanSource.Translate - new Vector2(this.rangeDataBounds.X, this.rangeDataBounds.Y);
                    tiles = Math.Min(tiles, BoundRowSpanTiles(spanSource, gridTranslate, tileX0, tileX1, tileY0, tileY1, rowBounds, ptclWordsPerTile));
                    areaWidthTiles = 0;
                }
            }

            this.estimatedPathTiles += tiles;

            long segmentTiles = Math.Min(crossings, tiles);
            long ptclWords = (segmentTiles * (PtclFillWords - 2)) + (tiles * (2 + paintWords));
            if (isClip)
            {
                ptclWords += tiles * PtclClipWords;
            }

            this.estimatedPtclWords += ptclWords;

            // The per-row crossing bound needs the slopes of the GPU lines, so it exists only when
            // the GPU translates the points. Otherwise every row can hold all the crossings.
            long crossingsPerRow = spanSource.IsValid ? Math.Min(spanSource.CrossingsPerRow, crossings) : crossings;
            long endCrossings = spanSource.IsValid ? Math.Min(spanSource.EndCrossings, crossings) : 0;
            rowBounds.AddDraw(tileY0, tileY1, areaWidthTiles, tiles, crossings, ptclWords, ptclWordsPerTile, crossingsPerRow, endCrossings);

            // Binning emits one record per (draw, 16x16-tile bin) pair the draw's bounds touch. The
            // EndClip of a clip or a layer bins again with the same bounds.
            const int binSize = TileWidth * 16;
            long bins = (long)(DivideRoundUp(clippedBounds.Right, binSize) - (clippedBounds.Left / binSize)) *
                (DivideRoundUp(clippedBounds.Bottom, binSize) - (clippedBounds.Top / binSize));

            this.estimatedBinFootprint += isClip ? bins * 2 : bins;

            // A clip or a layer raises the blend depth of its tiles. Tiles past BlendStackSplit spill
            // TileWidth * TileHeight slots for each level, so each depth keeps the union of the
            // regions opened at it. Every depth is kept, not only the depths past BlendStackSplit.
            // A retained scene read in place adds its depths to the depth of the outer clips and
            // layers.
            if (isClip)
            {
                this.AddBlendDepthRegion(this.blendDepth - 1, clippedBounds);
            }
        }

        /// <summary>
        /// Adds a region to the union region of one blend depth.
        /// </summary>
        /// <param name="index">The blend depth minus one.</param>
        /// <param name="region">The region in tile-grid pixels of the current range.</param>
        private void AddBlendDepthRegion(int index, Rectangle region)
        {
            List<Rectangle> levels = this.blendLevels ??= [];
            while (levels.Count <= index)
            {
                levels.Add(Rectangle.Empty);
            }

            levels[index] = levels[index].IsEmpty ? region : Rectangle.Union(levels[index], region);
        }

        /// <summary>
        /// Adds the blend depth regions of a retained scene read in place at the current depth.
        /// </summary>
        /// <param name="regions">The union region of each blend depth of the retained scene, in root-target-local pixels.</param>
        private void AddRetainedBlendDepthRegions(Rectangle[] regions)
        {
            // The retained scene uses the root target coordinates. The regions of this encoding
            // use the tile grid of the current range, which starts at the range target.
            Rectangle rangeGrid = new(0, 0, this.rangeDataBounds.Width, this.rangeDataBounds.Height);
            for (int i = 0; i < regions.Length; i++)
            {
                Rectangle region = regions[i];
                region.Offset(-this.rangeDataBounds.X, -this.rangeDataBounds.Y);
                region = Rectangle.Intersect(region, rangeGrid);
                if (region.Width > 0 && region.Height > 0)
                {
                    this.AddBlendDepthRegion(this.blendDepth + i, region);
                }
            }
        }

        /// <summary>
        /// Returns the PTCL word count of the paint command coarse writes for one draw tag, matching
        /// the write_* helpers in coarse.wgsl.
        /// </summary>
        /// <param name="drawTag">The draw tag.</param>
        /// <returns>The paint command word count.</returns>
        private static int GetPtclPaintWords(uint drawTag)
            => drawTag switch
            {
                GpuSceneDrawTag.FillPathGradient => 5,
                GpuSceneDrawTag.FillImage => 2,
                GpuSceneDrawTag.BeginClip => 2,
                _ => 3
            };

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
            // EndClip is fixed-size: one draw tag plus a zero-data path terminator. The blend depth
            // counts across the ranges of this encoding, and a partition replays its seed clips, so
            // every EndClip closes a depth that this encoding opened.
            ReserveEndLayerCapacity(ref this.PathTags, ref this.DrawTags);
            this.blendDepth--;
            this.DrawTags.Add(GpuSceneDrawTag.EndClip);
            this.PathTags.Add(PackPathTag(PathTag.Path));
            this.PathCount++;
            this.ClipCount++;
        }
    }

    /// <summary>
    /// The source segments of one draw for the row-span tile bound.
    /// </summary>
    private readonly struct RowSpanSource
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RowSpanSource"/> struct.
        /// </summary>
        /// <param name="geometry">The source geometry, or <see langword="null"/> for one segment.</param>
        /// <param name="start">The segment start when <paramref name="geometry"/> is <see langword="null"/>.</param>
        /// <param name="end">The segment end when <paramref name="geometry"/> is <see langword="null"/>.</param>
        /// <param name="translate">The translation from source points to root-target-local device pixels.</param>
        /// <param name="radius">The radius around the segments that contains the GPU lines.</param>
        /// <param name="crossingsPerRow">An upper bound for the tile crossings of the GPU body lines in one tile row.</param>
        /// <param name="endCrossings">The tile crossings of the other GPU lines, such as stroke joins and caps.</param>
        public RowSpanSource(LinearGeometry? geometry, Vector2 start, Vector2 end, Vector2 translate, float radius, long crossingsPerRow, long endCrossings)
        {
            this.Geometry = geometry;
            this.Start = start;
            this.End = end;
            this.Translate = translate;
            this.Radius = radius;
            this.CrossingsPerRow = crossingsPerRow;
            this.EndCrossings = endCrossings;
            this.IsValid = true;
        }

        /// <summary>
        /// Gets a source that has no segments. The bound then uses the bounding box.
        /// </summary>
        public static RowSpanSource None => default;

        /// <summary>
        /// Gets the source geometry, or <see langword="null"/> for one segment.
        /// </summary>
        public LinearGeometry? Geometry { get; }

        /// <summary>
        /// Gets the segment start when <see cref="Geometry"/> is <see langword="null"/>.
        /// </summary>
        public Vector2 Start { get; }

        /// <summary>
        /// Gets the segment end when <see cref="Geometry"/> is <see langword="null"/>.
        /// </summary>
        public Vector2 End { get; }

        /// <summary>
        /// Gets the translation from source points to root-target-local device pixels.
        /// </summary>
        public Vector2 Translate { get; }

        /// <summary>
        /// Gets the radius around the segments that contains the GPU lines.
        /// </summary>
        public float Radius { get; }

        /// <summary>
        /// Gets an upper bound for the tile crossings of the GPU body lines in one tile row.
        /// </summary>
        public long CrossingsPerRow { get; }

        /// <summary>
        /// Gets the tile crossings of the other GPU lines, such as stroke joins and caps. They
        /// are near the segment ends, so all of them can be in one row.
        /// </summary>
        public long EndCrossings { get; }

        /// <summary>
        /// Gets a value indicating whether the source has segments.
        /// </summary>
        public bool IsValid { get; }
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
        private readonly LinearGeometryProfileStorage? profileStorage;
        private bool disposed;

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
        /// <param name="solidColors">The deferred solid color descriptors recorded by the partition.</param>
        /// <param name="gradientRamps">The deferred gradient ramp descriptors recorded by the partition.</param>
        /// <param name="fillCount">The number of emitted fill records.</param>
        /// <param name="visibleFillCount">The number of visible fills accepted by staged-scene validation.</param>
        /// <param name="pathCount">The number of emitted paths.</param>
        /// <param name="lineCount">The number of emitted non-horizontal lines.</param>
        /// <param name="infoWordCount">The total info-word count implied by the emitted draw tags.</param>
        /// <param name="clipCount">The number of emitted clip records.</param>
        /// <param name="gradientRowCount">The number of emitted gradient-ramp rows.</param>
        /// <param name="estimatedPathRowCount">The CPU-side estimate of active tile rows.</param>
        /// <param name="estimatedTileCrossings">The CPU-side upper bound for tile-boundary crossings.</param>
        /// <param name="estimatedPtclWords">The CPU-side upper bound for the dynamic PTCL words.</param>
        /// <param name="estimatedBinFootprint">The CPU-side upper bound for per-(draw, bin) records.</param>
        /// <param name="pathTagByteCount">The unpadded path-tag byte count.</param>
        /// <param name="pathDataWordCount">The path-data word count.</param>
        /// <param name="drawTagCount">The draw-tag count.</param>
        /// <param name="drawDataWordCount">The draw-data word count.</param>
        /// <param name="transformWordCount">The transform word count.</param>
        /// <param name="styleWordCount">The style word count.</param>
        /// <param name="pathGradientDataWordCount">The path-gradient payload word count.</param>
        /// <param name="profileStorage">The detached partition-owned profile arena.</param>
        /// <param name="profileFills">The aliased fills that have profile data.</param>
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
            List<GpuSolidColorDescriptor> solidColors,
            List<GpuGradientRampDescriptor> gradientRamps,
            int fillCount,
            int visibleFillCount,
            int pathCount,
            int lineCount,
            int infoWordCount,
            int clipCount,
            int gradientRowCount,
            int estimatedPathRowCount,
            long estimatedTileCrossings,
            long estimatedPtclWords,
            long estimatedBinFootprint,
            int pathTagByteCount,
            int pathDataWordCount,
            int drawTagCount,
            int drawDataWordCount,
            int transformWordCount,
            int styleWordCount,
            int pathGradientDataWordCount,
            LinearGeometryProfileStorage? profileStorage,
            List<GpuProfileFillRecord>? profileFills)
        {
            this.pathTagsOwner = pathTagsOwner;
            this.pathDataOwner = pathDataOwner;
            this.drawTagsOwner = drawTagsOwner;
            this.drawDataOwner = drawDataOwner;
            this.transformsOwner = transformsOwner;
            this.stylesOwner = stylesOwner;
            this.gradientPixelsOwner = gradientPixelsOwner;
            this.pathGradientDataOwner = pathGradientDataOwner;
            this.profileStorage = profileStorage;
            this.Images = images;
            this.Recolors = recolors;
            this.SolidColors = solidColors;
            this.GradientRamps = gradientRamps;
            this.FillCount = fillCount;
            this.VisibleFillCount = visibleFillCount;
            this.PathCount = pathCount;
            this.LineCount = lineCount;
            this.InfoWordCount = infoWordCount;
            this.ClipCount = clipCount;
            this.GradientRowCount = gradientRowCount;
            this.EstimatedPathRowCount = estimatedPathRowCount;
            this.EstimatedTileCrossings = estimatedTileCrossings;
            this.EstimatedPtclWords = estimatedPtclWords;
            this.EstimatedBinFootprint = estimatedBinFootprint;
            this.PathTagByteCount = pathTagByteCount;
            this.PathDataWordCount = pathDataWordCount;
            this.DrawTagCount = drawTagCount;
            this.DrawDataWordCount = drawDataWordCount;
            this.TransformWordCount = transformWordCount;
            this.StyleWordCount = styleWordCount;
            this.PathGradientDataWordCount = pathGradientDataWordCount;
            this.ProfileFills = profileFills;
        }

        /// <summary>
        /// Gets the aliased fills that have profile data. Their offsets are relative to this
        /// partition's path-data stream.
        /// </summary>
        public List<GpuProfileFillRecord>? ProfileFills { get; }

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
        /// Gets the deferred solid color descriptors recorded by this partition.
        /// </summary>
        public List<GpuSolidColorDescriptor> SolidColors { get; }

        /// <summary>
        /// Gets the deferred gradient ramp descriptors recorded by this partition.
        /// </summary>
        public List<GpuGradientRampDescriptor> GradientRamps { get; }

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
        /// Gets the CPU-side upper bound for the dynamic PTCL words.
        /// </summary>
        public long EstimatedPtclWords { get; }

        /// <summary>
        /// Gets the CPU-side upper bound for per-(draw, bin) binning records.
        /// </summary>
        public long EstimatedBinFootprint { get; }

        /// <summary>
        /// Gets the path-tile bound of the partition.
        /// </summary>
        public long EstimatedPathTiles { get; init; }

        /// <summary>
        /// Gets the union region of each clip and layer depth of the partition, starting at depth 1.
        /// </summary>
        public Rectangle[] BlendDepthRegions { get; init; } = [];

        /// <summary>
        /// Gets the row bounds of the partition, or <see langword="null"/> when it has no draws.
        /// </summary>
        public WebGPUSceneRowBounds? RowBounds { get; init; }

        /// <summary>
        /// Gets the row bounds instances that this partition disposes.
        /// </summary>
        public WebGPUSceneRowBounds[] OwnedRowBounds { get; init; } = [];

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
            encoding.MarkProfileStorageDetached();

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
                encoding.SolidColors,
                encoding.GradientRamps,
                encoding.FillCount,
                encoding.VisibleFillCount,
                encoding.PathCount,
                encoding.LineCount,
                encoding.InfoWordCount,
                encoding.ClipCount,
                gradientRowCount,
                encoding.EstimatedPathRowCount,
                encoding.EstimatedTileCrossings,
                encoding.EstimatedPtclWords,
                encoding.EstimatedBinFootprint,
                pathTagByteCount,
                pathDataWordCount,
                drawTagCount,
                drawDataWordCount,
                transformWordCount,
                styleWordCount,
                pathGradientDataWordCount,
                encoding.ProfileStorage,
                encoding.ProfileFills)
            {
                EstimatedPathTiles = encoding.EstimatedPathTiles,
                BlendDepthRegions = encoding.BlendLevels is { } blendLevels ? [.. blendLevels] : [],
                RowBounds = encoding.RowBounds,
                OwnedRowBounds = encoding.DetachRowBounds()
            };
        }

        /// <summary>
        /// Releases all retained partition stream buffers.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.pathTagsOwner.Dispose();
            this.pathDataOwner.Dispose();
            this.drawTagsOwner.Dispose();
            this.drawDataOwner.Dispose();
            this.transformsOwner.Dispose();
            this.stylesOwner.Dispose();
            this.gradientPixelsOwner?.Dispose();
            this.pathGradientDataOwner?.Dispose();
            this.profileStorage?.Dispose();
            for (int i = 0; i < this.OwnedRowBounds.Length; i++)
            {
                this.OwnedRowBounds[i].Dispose();
            }
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
            List<GpuProfileFillRecord>? profileFills = encoding.ProfileFills;
            int profileSlotWordCount = 0;
            int xProfileCount = 0;
            int yProfileCount = 0;
            int profileSlotsBase = 0;
            int profileRecordsBase = 0;
            if (profileFills is not null)
            {
                GetProfileRegionLayout(
                    profileFills,
                    pathTagWordCount,
                    pathDataWordCount,
                    out profileSlotWordCount,
                    out xProfileCount,
                    out yProfileCount);

                profileSlotsBase = sceneWordCount;
                profileRecordsBase = checked(profileSlotsBase + profileSlotWordCount);
                sceneWordCount = checked(profileRecordsBase + 2 + ((xProfileCount + yProfileCount) * 3));
            }

            // Retained scenes inside layers add their elements to the virtual streams, so the
            // counts that size the dispatch are joined totals. The physical stream bases and
            // extents above stay those of this scene. The segment table ends the scene data.
            List<WebGPUSceneInsertion>? insertions = encoding.Insertions;
            JoinedSceneCounts joined = new(ref encoding, drawTagCount, drawDataWordCount);
            SceneSegmentTableWriter? segmentWriter = null;
            int segmentCount = 0;
            int segmentTableBase = 0;
            int blockBrushDataWordCount = 0;
            if (insertions is not null)
            {
                for (int i = 0; i < insertions.Count; i++)
                {
                    joined.Add(insertions[i].Scene.EncodedScene);
                }

                // A scene with no ordered operations renders as one dispatch, which reads one table
                // for all its streams. Each range of an ordered scene is a dispatch of its own, so
                // each range that reads retained scenes has a table of its own.
                int tableEntryCount;
                if (operations.Length == 0)
                {
                    segmentCount = SceneSegmentTableWriter.CountSegments(CollectionsMarshal.AsSpan(insertions));
                    tableEntryCount = segmentCount;
                }
                else
                {
                    tableEntryCount = encoding.RangeSegmentEntryCount;
                }

                // The shaders pack the segment index and the draw object index into one word.
                // TryAppendRetainedScene and the final checks of TryEncode and TryEncodeOrdered refuse
                // a scene that exceeds these limits, so they hold here.
                // A range has at most the segments and draw objects of the whole scene.
                Debug.Assert(encoding.SegmentCount <= MaxSceneSegmentCount, "The segment count exceeds the coarse packing limit.");
                Debug.Assert(joined.DrawTagCount < SegmentedDrawObjectLimit, "The draw-object count exceeds the coarse packing limit.");
                segmentTableBase = AlignUp(sceneWordCount, 2);
                sceneWordCount = checked(segmentTableBase + (tableEntryCount * SceneSegmentEntryWordCount));

                // The blocks follow the scene data, and their gradient rows follow those of this
                // scene. Each table places the brush data and atlas rows of the blocks.
                segmentWriter = new SceneSegmentTableWriter(
                    CollectionsMarshal.AsSpan(insertions),
                    AlignUp(sceneWordCount, 2),
                    encoding.GradientRowCount);

                blockBrushDataWordCount = segmentWriter.BlockBrushDataWordCount;
            }

            GpuSceneLayout layout = new(
                (uint)joined.DrawTagCount,
                (uint)joined.PathCount,
                (uint)joined.ClipCount,
                (uint)(joined.InfoWordCount + pathGradientDataWordCount + blockBrushDataWordCount),
                (uint)joined.InfoWordCount,
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
                    sceneDataOwner.Memory.Span[..(profileSlotsBase == 0 ? sceneWordCount : profileSlotsBase)]);

                if (profileFills is not null)
                {
                    WriteProfileRegions(
                        sceneDataOwner.Memory.Span[..sceneWordCount],
                        profileFills,
                        pathTagWordCount,
                        profileSlotsBase,
                        profileSlotWordCount,
                        profileRecordsBase,
                        xProfileCount,
                        yProfileCount);
                }

                // The path-tag dispatch covers the virtual path-tag words, which the table defines.
                int virtualPathTagByteCount = pathTagByteCount;
                if (segmentWriter is not null)
                {
                    SceneSegmentSource source = new(
                        new SceneStreamWindow(0, 0, pathTagWordCount),
                        new SceneStreamWindow(pathTagWordCount, 0, pathDataWordCount),
                        new SceneStreamWindow(transformBase, 0, transformWordCount),
                        new SceneStreamWindow(styleBase, 0, styleWordCount),
                        new SceneStreamWindow(drawTagBase, 0, drawTagCount),
                        new SceneStreamWindow(drawDataBase, 0, drawDataWordCount),
                        profileSlotsBase,
                        profileRecordsBase);

                    // The brush data of the blocks follows the info prefix and the brush data of
                    // the dispatch. The atlas rows of the blocks follow the rows of its images.
                    Span<uint> tables = sceneDataOwner.Memory.Span[segmentTableBase..sceneWordCount];
                    ReadOnlySpan<WebGPUSceneInsertion> allInsertions = CollectionsMarshal.AsSpan(insertions);
                    if (operations.Length == 0)
                    {
                        int virtualPathTagWordCount = segmentWriter.Write(
                            tables,
                            source,
                            allInsertions,
                            joined.InfoWordCount,
                            joined.InfoWordCount + pathGradientDataWordCount,
                            WebGPUSceneResources.GetImageAtlasHeight(encoding.Images, null));

                        virtualPathTagByteCount = checked(virtualPathTagWordCount * 4);
                    }
                    else
                    {
                        // Every retained scene of an ordered scene is in a range, and the range
                        // already holds its joined counts.
                        foreach (WebGPUSceneRange range in encoding.SegmentedRanges!)
                        {
                            WebGPUSceneRangeSegments segments = range.Segments!;
                            int virtualPathTagWordCount = segmentWriter.Write(
                                tables.Slice(segments.SegmentEntryStart * SceneSegmentEntryWordCount, segments.SegmentCount * SceneSegmentEntryWordCount),
                                source.Slice(range),
                                allInsertions.Slice(segments.InsertionStart, segments.InsertionCount),
                                range.InfoWordCount,
                                range.InfoWordCount + pathGradientDataWordCount,
                                WebGPUSceneResources.GetImageAtlasHeight(encoding.Images, range));

                            Debug.Assert(virtualPathTagWordCount * 4 == range.PathTagByteCount, "The range path-tag count does not match its segment table.");
                        }
                    }
                }

                WebGPUEncodedScene resolvedScene = new(
                    targetBounds.Size,
                    joined.InfoWordCount,
                    sceneDataOwner,
                    sceneWordCount,
                    DetachGradientPixels(ref encoding),
                    DetachPathGradientData(ref encoding),
                    pathGradientDataWordCount,
                    encoding.Images,
                    encoding.Recolors,
                    encoding.SolidColors,
                    encoding.GradientRamps,
                    encoding.GradientRowCount,
                    layout,
                    joined.FillCount,
                    joined.PathCount,
                    joined.LineCount,
                    virtualPathTagByteCount,
                    pathTagWordCount,
                    pathDataWordCount,
                    joined.DrawTagCount,
                    joined.DrawDataWordCount,
                    transformWordCount,
                    styleWordCount,
                    joined.ClipCount,
                    joined.FillCount,
                    Math.Max(joined.EstimatedPathRowCount, joined.PathCount),
                    DivideRoundUp(targetBounds.Width, TileWidth),
                    DivideRoundUp(targetBounds.Height, TileHeight),
                    joined.EstimatedTileCrossings,
                    joined.EstimatedPtclWords,
                    joined.EstimatedBinFootprint,
                    operations);

                resolvedScene.SetProfileRegions((uint)profileSlotsBase, (uint)profileRecordsBase);
                resolvedScene.SetScratchBounds(
                    joined.EstimatedPathTiles,
                    WebGPUEncodedScene.GetBlendSpillSlots(CollectionsMarshal.AsSpan(encoding.BlendLevels), int.MinValue, int.MaxValue),
                    encoding.BlendLevels is { } blendLevels ? [.. blendLevels] : []);

                resolvedScene.SetRowBounds(encoding.RowBounds, encoding.DetachRowBounds());

                if (segmentWriter is not null)
                {
                    resolvedScene.SetSegments(
                        [.. insertions!],
                        segmentWriter.Blocks,
                        segmentCount,
                        segmentTableBase,
                        segmentWriter.SceneBufferWordCount,
                        segmentWriter.BlockBrushDataWordCount,
                        segmentWriter.BlockGradientRowCount);
                }

                return resolvedScene;
            }
            catch
            {
                sceneDataOwner.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Holds the counts that size a dispatch: the counts of one encoding plus those of the
        /// retained scenes that it reads in place.
        /// </summary>
        public struct JoinedSceneCounts
        {
            private bool hasInsertions;

            /// <summary>
            /// Initializes a new instance of the <see cref="JoinedSceneCounts"/> struct with the counts of one encoding.
            /// </summary>
            /// <param name="encoding">The encoding.</param>
            /// <param name="drawTagCount">The draw-tag count of the encoding.</param>
            /// <param name="drawDataWordCount">The draw-data word count of the encoding.</param>
            public JoinedSceneCounts(ref SupportedSubsetSceneEncoding encoding, int drawTagCount, int drawDataWordCount)
                : this(
                    encoding.FillCount,
                    encoding.PathCount,
                    encoding.LineCount,
                    encoding.ClipCount,
                    encoding.InfoWordCount,
                    drawTagCount,
                    drawDataWordCount,
                    encoding.EstimatedPathRowCount,
                    encoding.EstimatedTileCrossings,
                    encoding.EstimatedPtclWords,
                    encoding.EstimatedBinFootprint,
                    encoding.EstimatedPathTiles)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="JoinedSceneCounts"/> struct with the own counts of one scene or range.
            /// </summary>
            /// <param name="fillCount">The fill count.</param>
            /// <param name="pathCount">The path count.</param>
            /// <param name="lineCount">The line count.</param>
            /// <param name="clipCount">The clip record count.</param>
            /// <param name="infoWordCount">The info word count.</param>
            /// <param name="drawTagCount">The draw-tag count.</param>
            /// <param name="drawDataWordCount">The draw-data word count.</param>
            /// <param name="estimatedPathRowCount">The estimated path-row count.</param>
            /// <param name="estimatedTileCrossings">The estimated tile-crossing count.</param>
            /// <param name="estimatedPtclWords">The estimated dynamic PTCL word count.</param>
            /// <param name="estimatedBinFootprint">The estimated binning record count.</param>
            /// <param name="estimatedPathTiles">The path-tile bound.</param>
            public JoinedSceneCounts(
                int fillCount,
                int pathCount,
                int lineCount,
                int clipCount,
                int infoWordCount,
                int drawTagCount,
                int drawDataWordCount,
                int estimatedPathRowCount,
                long estimatedTileCrossings,
                long estimatedPtclWords,
                long estimatedBinFootprint,
                long estimatedPathTiles)
            {
                this.hasInsertions = false;
                this.FillCount = fillCount;
                this.PathCount = pathCount;
                this.LineCount = lineCount;
                this.ClipCount = clipCount;
                this.InfoWordCount = infoWordCount;
                this.DrawTagCount = drawTagCount;
                this.DrawDataWordCount = drawDataWordCount;
                this.EstimatedPathRowCount = estimatedPathRowCount;
                this.EstimatedTileCrossings = estimatedTileCrossings;
                this.EstimatedPtclWords = estimatedPtclWords;
                this.EstimatedBinFootprint = estimatedBinFootprint;
                this.EstimatedPathTiles = estimatedPathTiles;
            }

            /// <summary>
            /// Gets the fill count.
            /// </summary>
            public int FillCount { get; private set; }

            /// <summary>
            /// Gets the path count.
            /// </summary>
            public int PathCount { get; private set; }

            /// <summary>
            /// Gets the line count.
            /// </summary>
            public int LineCount { get; private set; }

            /// <summary>
            /// Gets the clip record count.
            /// </summary>
            public int ClipCount { get; private set; }

            /// <summary>
            /// Gets the info word count.
            /// </summary>
            public int InfoWordCount { get; private set; }

            /// <summary>
            /// Gets the draw-tag count.
            /// </summary>
            public int DrawTagCount { get; private set; }

            /// <summary>
            /// Gets the draw-data word count.
            /// </summary>
            public int DrawDataWordCount { get; private set; }

            /// <summary>
            /// Gets the estimated path-row count.
            /// </summary>
            public int EstimatedPathRowCount { get; private set; }

            /// <summary>
            /// Gets the estimated tile-crossing count.
            /// </summary>
            public long EstimatedTileCrossings { get; private set; }

            /// <summary>
            /// Gets the estimated dynamic PTCL word count.
            /// </summary>
            public long EstimatedPtclWords { get; private set; }

            /// <summary>
            /// Gets the estimated binning record count.
            /// </summary>
            public long EstimatedBinFootprint { get; private set; }

            /// <summary>
            /// Gets the path-tile bound.
            /// </summary>
            public long EstimatedPathTiles { get; private set; }

            /// <summary>
            /// Adds the counts of one retained scene. Its counts already include its own retained scenes.
            /// </summary>
            /// <param name="scene">The retained scene.</param>
            public void Add(WebGPUEncodedScene scene)
            {
                // With a segment table, every aliased fill command follows a two-word
                // CMD_PROFILE_TABLE (coarse.wgsl). Two words for each tile crossing bound these
                // commands, for this encoding and for each retained scene.
                if (!this.hasInsertions)
                {
                    this.hasInsertions = true;
                    this.EstimatedPtclWords += 2 * this.EstimatedTileCrossings;
                }

                this.FillCount = checked(this.FillCount + scene.FillCount);
                this.PathCount = checked(this.PathCount + scene.PathCount);
                this.LineCount = checked(this.LineCount + scene.LineCount);
                this.ClipCount = checked(this.ClipCount + scene.ClipCount);
                this.InfoWordCount = checked(this.InfoWordCount + scene.InfoWordCount);
                this.DrawTagCount = checked(this.DrawTagCount + scene.DrawTagCount);
                this.DrawDataWordCount = checked(this.DrawDataWordCount + scene.DrawDataWordCount);
                this.EstimatedPathRowCount = checked(this.EstimatedPathRowCount + scene.TotalPathRowCount);
                this.EstimatedTileCrossings += scene.EstimatedTileCrossings;
                this.EstimatedPtclWords += scene.EstimatedPtclWords + (2 * scene.EstimatedTileCrossings);
                this.EstimatedBinFootprint += scene.EstimatedBinFootprint;
                this.EstimatedPathTiles += scene.EstimatedPathTiles;
            }
        }

        /// <summary>
        /// Describes one physical stream window: where stream position zero is in the scene data,
        /// and the stream positions that the window covers.
        /// </summary>
        public readonly struct SceneStreamWindow
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SceneStreamWindow"/> struct.
            /// </summary>
            /// <param name="baseWord">The scene-data word of stream position zero.</param>
            /// <param name="start">The stream position of the window start.</param>
            /// <param name="end">The stream position of the window end.</param>
            public SceneStreamWindow(int baseWord, int start, int end)
            {
                this.Base = baseWord;
                this.Start = start;
                this.End = end;
            }

            /// <summary>
            /// Gets the scene-data word of stream position zero.
            /// </summary>
            public int Base { get; }

            /// <summary>
            /// Gets the stream position of the window start.
            /// </summary>
            public int Start { get; }

            /// <summary>
            /// Gets the stream position of the window end.
            /// </summary>
            public int End { get; }
        }

        /// <summary>
        /// Describes the physical streams of one scene, or of one range of an ordered scene, in its
        /// scene data. Stream positions are positions in the streams of the scene, the same as the
        /// positions of its insertions.
        /// </summary>
        public readonly struct SceneSegmentSource
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SceneSegmentSource"/> struct.
            /// </summary>
            /// <param name="pathTags">The path-tag word window. The path tags start at word zero.</param>
            /// <param name="pathData">The path-data word window.</param>
            /// <param name="transforms">The transform word window.</param>
            /// <param name="styles">The style word window.</param>
            /// <param name="drawTags">The draw-tag window.</param>
            /// <param name="drawData">The draw-data word window.</param>
            /// <param name="profileSlotsBase">The word offset of the profile slot table, or zero.</param>
            /// <param name="profileRecordsBase">The word offset of the profile record table, or zero.</param>
            public SceneSegmentSource(
                in SceneStreamWindow pathTags,
                in SceneStreamWindow pathData,
                in SceneStreamWindow transforms,
                in SceneStreamWindow styles,
                in SceneStreamWindow drawTags,
                in SceneStreamWindow drawData,
                int profileSlotsBase,
                int profileRecordsBase)
            {
                this.PathTags = pathTags;
                this.PathData = pathData;
                this.Transforms = transforms;
                this.Styles = styles;
                this.DrawTags = drawTags;
                this.DrawData = drawData;
                this.ProfileSlotsBase = profileSlotsBase;
                this.ProfileRecordsBase = profileRecordsBase;
            }

            /// <summary>
            /// Gets the path-tag word window. The path tags start at word zero.
            /// </summary>
            public SceneStreamWindow PathTags { get; }

            /// <summary>
            /// Gets the path-data word window.
            /// </summary>
            public SceneStreamWindow PathData { get; }

            /// <summary>
            /// Gets the transform word window.
            /// </summary>
            public SceneStreamWindow Transforms { get; }

            /// <summary>
            /// Gets the style word window.
            /// </summary>
            public SceneStreamWindow Styles { get; }

            /// <summary>
            /// Gets the draw-tag window.
            /// </summary>
            public SceneStreamWindow DrawTags { get; }

            /// <summary>
            /// Gets the draw-data word window.
            /// </summary>
            public SceneStreamWindow DrawData { get; }

            /// <summary>
            /// Gets the word offset of the profile slot table, or zero when the scene has none.
            /// </summary>
            public int ProfileSlotsBase { get; }

            /// <summary>
            /// Gets the word offset of the profile record table, or zero when the scene has none.
            /// </summary>
            public int ProfileRecordsBase { get; }

            /// <summary>
            /// Creates the source of every stream of an encoded scene. The draw-tag and draw-data
            /// extents come from the layout, because the counts of a scene with retained scenes
            /// are joined totals.
            /// </summary>
            /// <param name="scene">The encoded scene.</param>
            /// <returns>The physical stream source.</returns>
            public static SceneSegmentSource From(WebGPUEncodedScene scene)
            {
                GpuSceneLayout layout = scene.Layout;
                return new SceneSegmentSource(
                    new SceneStreamWindow(0, 0, scene.PathTagWordCount),
                    new SceneStreamWindow((int)layout.PathDataBase, 0, scene.PathDataWordCount),
                    new SceneStreamWindow((int)layout.TransformBase, 0, scene.TransformWordCount),
                    new SceneStreamWindow((int)layout.StyleBase, 0, scene.StyleWordCount),
                    new SceneStreamWindow((int)layout.DrawTagBase, 0, (int)(layout.DrawDataBase - layout.DrawTagBase)),
                    new SceneStreamWindow((int)layout.DrawDataBase, 0, (int)(layout.TransformBase - layout.DrawDataBase)),
                    (int)scene.ProfileSlotsBase,
                    (int)scene.ProfileRecordsBase);
            }

            /// <summary>
            /// Creates the source of the own streams of one range of this scene.
            /// </summary>
            /// <param name="range">The range. It reads retained scenes in place.</param>
            /// <returns>The physical stream source of the range.</returns>
            public SceneSegmentSource Slice(in WebGPUSceneRange range)
            {
                // The joined counts of the range size the dispatch. The own extents of the joined
                // streams are in its segments.
                WebGPUSceneRangeSegments segments = range.Segments!;
                return new SceneSegmentSource(
                    new SceneStreamWindow(this.PathTags.Base, range.PathTagWordStart, range.PathTagWordStart + segments.PathTagWordCount),
                    new SceneStreamWindow(this.PathData.Base, range.PathDataWordStart, range.PathDataWordStart + range.PathDataWordCount),
                    new SceneStreamWindow(this.Transforms.Base, range.TransformWordStart, range.TransformWordStart + range.TransformWordCount),
                    new SceneStreamWindow(this.Styles.Base, range.StyleWordStart, range.StyleWordStart + range.StyleWordCount),
                    new SceneStreamWindow(this.DrawTags.Base, range.DrawTagStart, range.DrawTagStart + segments.DrawTagCount),
                    new SceneStreamWindow(this.DrawData.Base, range.DrawDataWordStart, range.DrawDataWordStart + segments.DrawDataWordCount),
                    this.ProfileSlotsBase,
                    this.ProfileRecordsBase);
            }
        }

        /// <summary>
        /// Builds the resident blocks and the scene segment tables of one scene that reads retained
        /// scenes in place. scene_segment.wgsl reads the tables.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The segments are the pieces of every virtual stream, in stream order:
        /// <list type="number">
        /// <item><description>the outer elements before the first retained scene;</description></item>
        /// <item><description>the retained scene, as its own pieces when it reads more retained scenes;</description></item>
        /// <item><description>the next outer elements, and so on.</description></item>
        /// </list>
        /// Each entry stores the virtual start of its path tags and draw objects, and a wrapping bias
        /// for each stream. A shader then reads scene[bias + virtual offset]. A retained scene drawn
        /// several times has one block.
        /// </para>
        /// <para>
        /// A scene with no ordered operations has one table for all its streams. An ordered scene has
        /// one table for each range that reads retained scenes, because each dispatch reads one range.
        /// The blocks are shared by all tables.
        /// </para>
        /// </remarks>
        public sealed class SceneSegmentTableWriter
        {
            private readonly List<WebGPUSceneBlock> blocks = [];
            private readonly int firstBlockGradientRow;
            private int nextBlockWord;
            private int nextBrushDataOffset;
            private int nextGradientRow;
            private int nextAtlasRowOffset;

            // The bases of the block brush data and of the block atlas rows for the table that is
            // written now. Each table has its own info prefix and its own atlas rows before them.
            private int blockBrushDataBase;
            private int blockAtlasYBase;

            // The virtual start of the next segment in each stream. The transform cursor counts
            // transform records, because the shaders index transforms by record.
            private uint virtualPathTag;
            private uint virtualPathData;
            private uint virtualTransform;
            private uint virtualStyle;
            private uint virtualDrawTag;
            private uint virtualDrawData;
            private int entryIndex;

            /// <summary>
            /// Initializes a new instance of the <see cref="SceneSegmentTableWriter"/> class and
            /// assigns one block to each distinct retained scene, in first-use order.
            /// </summary>
            /// <param name="insertions">The retained scenes of the root scene, in stream order.</param>
            /// <param name="firstBlockWord">The scene-buffer word of the first block, after the root scene data.</param>
            /// <param name="rootGradientRowCount">The root gradient-ramp row count.</param>
            public SceneSegmentTableWriter(ReadOnlySpan<WebGPUSceneInsertion> insertions, int firstBlockWord, int rootGradientRowCount)
            {
                this.firstBlockGradientRow = rootGradientRowCount;
                this.nextBlockWord = firstBlockWord;
                this.nextGradientRow = rootGradientRowCount;
                this.AddBlocks(insertions);
                this.Blocks = [.. this.blocks];
            }

            /// <summary>
            /// Gets the resident blocks, in scene-buffer order.
            /// </summary>
            public WebGPUSceneBlock[] Blocks { get; }

            /// <summary>
            /// Gets the word length of the scene buffer: the root scene data, then the blocks.
            /// </summary>
            public int SceneBufferWordCount => this.nextBlockWord;

            /// <summary>
            /// Gets the brush-data word count of the blocks.
            /// </summary>
            public int BlockBrushDataWordCount => this.nextBrushDataOffset;

            /// <summary>
            /// Gets the gradient-ramp row count of the blocks.
            /// </summary>
            public int BlockGradientRowCount => this.nextGradientRow - this.firstBlockGradientRow;

            /// <summary>
            /// Counts the segments of a scene or range that reads the given retained scenes in place.
            /// </summary>
            /// <param name="insertions">The retained scenes, in stream order.</param>
            /// <returns>The segment count.</returns>
            public static int CountSegments(ReadOnlySpan<WebGPUSceneInsertion> insertions)
            {
                // N retained scenes divide the outer streams into N + 1 pieces. Each retained
                // scene adds its own pieces.
                int count = insertions.Length + 1;
                for (int i = 0; i < insertions.Length; i++)
                {
                    count = checked(count + CountSegments(insertions[i].Scene.EncodedScene.Insertions));
                }

                return count;
            }

            /// <summary>
            /// Writes one table: the table of a scene, or the table of one range of an ordered scene.
            /// </summary>
            /// <param name="table">The table words, <see cref="SceneSegmentEntryWordCount"/> for each segment.</param>
            /// <param name="source">The physical streams of the scene or range.</param>
            /// <param name="insertions">The retained scenes of the scene or range, in stream order.</param>
            /// <param name="brushDataBase">The info-buffer word of the brush data of the scene: the info word count of the dispatch.</param>
            /// <param name="blockBrushDataBase">The info-buffer word of the brush data of the first block.</param>
            /// <param name="blockAtlasYBase">The first image-atlas row of the first block.</param>
            /// <returns>The virtual path-tag word count.</returns>
            public int Write(
                Span<uint> table,
                in SceneSegmentSource source,
                ReadOnlySpan<WebGPUSceneInsertion> insertions,
                int brushDataBase,
                int blockBrushDataBase,
                int blockAtlasYBase)
            {
                // Each table indexes its virtual streams from zero, because each dispatch reads
                // one scene or one range from its start.
                this.virtualPathTag = 0;
                this.virtualPathData = 0;
                this.virtualTransform = 0;
                this.virtualStyle = 0;
                this.virtualDrawTag = 0;
                this.virtualDrawData = 0;
                this.entryIndex = 0;
                this.blockBrushDataBase = blockBrushDataBase;
                this.blockAtlasYBase = blockAtlasYBase;
                this.WriteScene(table, source, 0, brushDataBase, 0, 0, insertions);
                return checked((int)this.virtualPathTag);
            }

            /// <summary>
            /// Assigns a block to each distinct retained scene that is not yet in a block, depth first.
            /// </summary>
            /// <param name="insertions">The retained scenes, in stream order.</param>
            private void AddBlocks(ReadOnlySpan<WebGPUSceneInsertion> insertions)
            {
                for (int i = 0; i < insertions.Length; i++)
                {
                    WebGPUEncodedScene scene = insertions[i].Scene.EncodedScene;
                    if (this.FindBlock(scene) < 0)
                    {
                        // Even block offsets keep half the block offset exact in the profile slot
                        // origin (SCENE_SEGMENT_PROFILE_SLOTS).
                        int offset = AlignUp(this.nextBlockWord, 2);
                        this.blocks.Add(new WebGPUSceneBlock(scene, offset, this.nextBrushDataOffset, this.nextGradientRow, this.nextAtlasRowOffset));
                        this.nextBlockWord = checked(offset + scene.SceneWordCount);
                        this.nextBrushDataOffset = checked(this.nextBrushDataOffset + scene.PathGradientDataWordCount);
                        this.nextGradientRow = checked(this.nextGradientRow + scene.GradientRowCount);
                        this.nextAtlasRowOffset = checked(this.nextAtlasRowOffset + WebGPUSceneResources.GetImageAtlasHeight(scene.Images, null));
                    }

                    this.AddBlocks(scene.Insertions);
                }
            }

            /// <summary>
            /// Gets the block index of a retained scene, or -1 when it has no block.
            /// </summary>
            /// <param name="scene">The retained scene.</param>
            /// <returns>The block index.</returns>
            private int FindBlock(WebGPUEncodedScene scene)
            {
                for (int i = 0; i < this.blocks.Count; i++)
                {
                    if (ReferenceEquals(this.blocks[i].Scene, scene))
                    {
                        return i;
                    }
                }

                return -1;
            }

            /// <summary>
            /// Writes the entries of one scene or range: one for each piece of its own streams, and
            /// the entries of each retained scene between them.
            /// </summary>
            /// <param name="table">The table words.</param>
            /// <param name="source">The physical streams of the scene or range.</param>
            /// <param name="blockWord">The scene-buffer word of the scene data.</param>
            /// <param name="brushDataBase">The info-buffer word of the scene brush data.</param>
            /// <param name="gradientRowBase">The first gradient-ramp row of the scene.</param>
            /// <param name="atlasYBase">The first image-atlas row of the scene.</param>
            /// <param name="insertions">The retained scenes of the scene or range, in stream order.</param>
            private void WriteScene(
                Span<uint> table,
                in SceneSegmentSource source,
                int blockWord,
                int brushDataBase,
                int gradientRowBase,
                int atlasYBase,
                ReadOnlySpan<WebGPUSceneInsertion> insertions)
            {
                // The start of the next piece in the scene's own streams.
                int pathTag = source.PathTags.Start;
                int pathData = source.PathData.Start;
                int transform = source.Transforms.Start;
                int style = source.Styles.Start;
                int drawTag = source.DrawTags.Start;
                int drawData = source.DrawData.Start;

                // The extra iteration writes the piece after the last retained scene.
                for (int i = 0; i <= insertions.Length; i++)
                {
                    bool isTail = i == insertions.Length;
                    WebGPUSceneInsertion insertion = isTail ? default : insertions[i];
                    int pathTagEnd = isTail ? source.PathTags.End : insertion.PathTagWord;
                    int pathDataEnd = isTail ? source.PathData.End : insertion.PathDataWord;
                    int transformEnd = isTail ? source.Transforms.End : insertion.TransformWord;
                    int styleEnd = isTail ? source.Styles.End : insertion.StyleWord;
                    int drawTagEnd = isTail ? source.DrawTags.End : insertion.DrawTag;
                    int drawDataEnd = isTail ? source.DrawData.End : insertion.DrawDataWord;

                    // Each bias is the physical start minus the virtual start, in wrapping arithmetic.
                    Span<uint> entry = table.Slice(this.entryIndex * SceneSegmentEntryWordCount, SceneSegmentEntryWordCount);
                    entry[0] = this.virtualPathTag;
                    entry[1] = unchecked((uint)(blockWord + source.PathTags.Base + pathTag) - this.virtualPathTag);
                    entry[2] = unchecked((uint)(blockWord + source.PathData.Base + pathData) - this.virtualPathData);
                    entry[3] = unchecked((uint)(blockWord + source.Styles.Base + style) - this.virtualStyle);
                    entry[4] = unchecked((uint)(blockWord + source.Transforms.Base + transform) - (this.virtualTransform * TransformWordCount));
                    entry[5] = this.virtualDrawTag;
                    entry[6] = unchecked((uint)(blockWord + source.DrawTags.Base + drawTag) - this.virtualDrawTag);
                    entry[7] = unchecked((uint)(blockWord + source.DrawData.Base + drawData) - this.virtualDrawData);
                    entry[8] = (uint)brushDataBase;
                    entry[9] = (uint)gradientRowBase;
                    entry[10] = (uint)atlasYBase;

                    // The profile slot of a line is the slot base plus half its path-data word in
                    // the scene's own data. In the scene buffer that word moves by blockWord, so the
                    // origin moves by half of it. blockWord is even.
                    entry[11] = source.ProfileSlotsBase == 0 ? 0U : (uint)(source.ProfileSlotsBase + (blockWord / 2));
                    entry[12] = source.ProfileRecordsBase == 0 ? 0U : (uint)(blockWord + source.ProfileRecordsBase);
                    this.entryIndex++;

                    this.virtualPathTag += (uint)(pathTagEnd - pathTag);
                    this.virtualPathData += (uint)(pathDataEnd - pathData);
                    this.virtualTransform += (uint)((transformEnd - transform) / TransformWordCount);
                    this.virtualStyle += (uint)(styleEnd - style);
                    this.virtualDrawTag += (uint)(drawTagEnd - drawTag);
                    this.virtualDrawData += (uint)(drawDataEnd - drawData);
                    pathTag = pathTagEnd;
                    pathData = pathDataEnd;
                    transform = transformEnd;
                    style = styleEnd;
                    drawTag = drawTagEnd;
                    drawData = drawDataEnd;

                    if (!isTail)
                    {
                        // The block bases of this table place the brush data and atlas rows of the
                        // block after the info prefix and atlas rows of the dispatch.
                        WebGPUEncodedScene scene = insertion.Scene.EncodedScene;
                        WebGPUSceneBlock block = this.blocks[this.FindBlock(scene)];
                        this.WriteScene(
                            table,
                            SceneSegmentSource.From(scene),
                            block.SceneWordOffset,
                            this.blockBrushDataBase + block.BrushDataOffset,
                            block.GradientRowBase,
                            this.blockAtlasYBase + block.AtlasYOffset,
                            scene.Insertions);
                    }
                }
            }
        }

        /// <summary>
        /// Calculates the segment-to-profile table size and the number of profile records.
        /// </summary>
        /// <param name="fills">The aliased fills that have profile data.</param>
        /// <param name="dataRegionBase">The word offset of path data in the scene.</param>
        /// <param name="dataWordCount">The path-data length in words.</param>
        /// <param name="slotWordCount">Receives the segment-to-profile table length in words.</param>
        /// <param name="xProfileCount">Receives the addressable X-profile count.</param>
        /// <param name="yProfileCount">Receives the addressable Y-profile count.</param>
        private static void GetProfileRegionLayout(
            List<GpuProfileFillRecord> fills,
            int dataRegionBase,
            int dataWordCount,
            out int slotWordCount,
            out int xProfileCount,
            out int yProfileCount)
        {
            // Every line starts at a two-word point. The shader divides that point's absolute scene
            // offset by two to find its profile tag. Include the words before path data so a scene
            // rendered in several ranges still uses one stable index.
            slotWordCount = (dataRegionBase + dataWordCount + 1) >> 1;
            xProfileCount = 0;
            yProfileCount = 0;
            for (int fillIndex = 0; fillIndex < fills.Count; fillIndex++)
            {
                LinearGeometryProfiles profiles = fills[fillIndex].Profiles;

                // Each axis identifier has 16 bits. The largest value means "no profile", so the
                // record count stops before that value.
                xProfileCount = (int)Math.Min((long)xProfileCount + profiles.XProfileCount, LinearGeometryProfiles.SentinelId);
                yProfileCount = (int)Math.Min((long)yProfileCount + profiles.YProfileCount, LinearGeometryProfiles.SentinelId);
            }
        }

        /// <summary>
        /// Calculates profile storage for ordered partitions without copying their fill lists.
        /// </summary>
        /// <param name="partitions">The ordered partition encodings.</param>
        /// <param name="dataRegionBase">The word offset of path data in the scene.</param>
        /// <param name="dataWordCount">The path-data length in words.</param>
        /// <param name="slotWordCount">Receives the segment-to-profile table length in words.</param>
        /// <param name="xProfileCount">Receives the addressable X-profile count.</param>
        /// <param name="yProfileCount">Receives the addressable Y-profile count.</param>
        private static void GetProfileRegionLayout(
            SceneEncodingPartition?[] partitions,
            int dataRegionBase,
            int dataWordCount,
            out int slotWordCount,
            out int xProfileCount,
            out int yProfileCount)
        {
            slotWordCount = (dataRegionBase + dataWordCount + 1) >> 1;
            xProfileCount = 0;
            yProfileCount = 0;
            for (int partitionIndex = 0; partitionIndex < partitions.Length; partitionIndex++)
            {
                List<GpuProfileFillRecord>? fills = partitions[partitionIndex]!.ProfileFills;
                if (fills is null)
                {
                    continue;
                }

                for (int fillIndex = 0; fillIndex < fills.Count; fillIndex++)
                {
                    LinearGeometryProfiles profiles = fills[fillIndex].Profiles;

                    // Profile identifiers are scene-wide because all render ranges share these
                    // records. The largest 16-bit value remains reserved for "no profile".
                    xProfileCount = (int)Math.Min((long)xProfileCount + profiles.XProfileCount, LinearGeometryProfiles.SentinelId);
                    yProfileCount = (int)Math.Min((long)yProfileCount + profiles.YProfileCount, LinearGeometryProfiles.SentinelId);
                }
            }
        }

        /// <summary>
        /// Writes one axis of profile records in scene-global identifier order.
        /// </summary>
        /// <param name="records">The destination record list.</param>
        /// <param name="profiles">The geometry-space profile data.</param>
        /// <param name="xAxis">Whether to write the X-axis profile table.</param>
        /// <param name="count">The number of addressable records to write.</param>
        /// <param name="idBase">The scene-global identifier base for this fill's axis profiles.</param>
        /// <param name="translate">The translation from geometry space to absolute target space.</param>
        private static void WriteProfileRecords(
            Span<uint> records,
            LinearGeometryProfiles profiles,
            bool xAxis,
            int count,
            int idBase,
            float translate)
        {
            if (xAxis)
            {
                // Select the source table outside the loop so this hot packing pass does not test
                // the axis again for every record.
                for (int i = 0; i < count; i++)
                {
                    profiles.GetXProfile(i, out float minimum, out float maximum, out int link);
                    WriteProfileRecord(records, i, minimum, maximum, link, idBase, translate);
                }

                return;
            }

            for (int i = 0; i < count; i++)
            {
                profiles.GetYProfile(i, out float minimum, out float maximum, out int link);
                WriteProfileRecord(records, i, minimum, maximum, link, idBase, translate);
            }
        }

        /// <summary>
        /// Writes one three-word profile record.
        /// </summary>
        /// <param name="records">The destination record list.</param>
        /// <param name="index">The destination profile index.</param>
        /// <param name="minimum">The lowest coordinate reached by the profile.</param>
        /// <param name="maximum">The highest coordinate reached by the profile.</param>
        /// <param name="link">The profile's contour connection data.</param>
        /// <param name="idBase">The scene-global identifier base.</param>
        /// <param name="translate">The translation from geometry space to absolute target space.</param>
        private static void WriteProfileRecord(
            Span<uint> records,
            int index,
            float minimum,
            float maximum,
            int link,
            int idBase,
            float translate)
        {
            // Words 0 and 1 are the inclusive axis range in absolute target-space 24.8 format.
            records[index * 3] = unchecked((uint)FloatToProfileFixed(minimum + translate));
            records[(index * 3) + 1] = unchecked((uint)FloatToProfileFixed(maximum + translate));

            // Bit 0 records a connection to the previous profile. Bits 1..31 contain the one-based
            // identifier joined across a closed contour's final point. Rebase only that identifier;
            // zero continues to mean that there is no closing connection.
            int partner = link >> 1;
            if (partner != 0)
            {
                partner += idBase;
            }

            records[(index * 3) + 2] = unchecked((uint)((link & 1) | (partner << 1)));
        }

        /// <summary>
        /// Converts a geometry-local segment tag to scene-global profile identifiers.
        /// </summary>
        /// <param name="tag">The per-geometry profile tag.</param>
        /// <param name="xBase">The scene-global x identifier base.</param>
        /// <param name="yBase">The scene-global y identifier base.</param>
        /// <returns>The rebased tag.</returns>
        private static uint RebaseProfileTag(uint tag, int xBase, int yBase)
        {
            uint sentinel = LinearGeometryProfiles.SentinelId;
            uint xId = tag & sentinel;
            uint yId = tag >> 16;

            // Adding the shared-table base also preserves an existing sentinel: the sum remains at
            // or above the reserved value and is replaced with the same sentinel.
            xId = xId + (uint)xBase >= sentinel ? sentinel : xId + (uint)xBase;
            yId = yId + (uint)yBase >= sentinel ? sentinel : yId + (uint)yBase;
            return xId | (yId << 16);
        }

        /// <summary>
        /// Converts one geometry-space coordinate to absolute target-space 24.8 fixed-point with
        /// the same clamp and rounding the CPU rasterizer's profile tables use.
        /// </summary>
        /// <param name="value">The coordinate to convert.</param>
        /// <returns>The 24.8 fixed-point value.</returns>
        private static int FloatToProfileFixed(float value) => (int)MathF.Round(Math.Clamp(value, -8388608F, 8388607F) * 256F);

        /// <summary>
        /// Writes both profile tables for a single encoding stream.
        /// </summary>
        /// <param name="sceneWords">The full scene word span.</param>
        /// <param name="fills">The aliased fills that have profile data, in path-data order.</param>
        /// <param name="dataRegionBase">The word offset of path data in the scene.</param>
        /// <param name="slotsBase">The word offset of the segment-to-profile table.</param>
        /// <param name="slotWordCount">The segment-to-profile table length in words.</param>
        /// <param name="recordsBase">The word offset of the profile range and contour-link table.</param>
        /// <param name="xProfileCount">The addressable X-profile count.</param>
        /// <param name="yProfileCount">The addressable Y-profile count.</param>
        private static void WriteProfileRegions(
            Span<uint> sceneWords,
            List<GpuProfileFillRecord> fills,
            int dataRegionBase,
            int slotsBase,
            int slotWordCount,
            int recordsBase,
            int xProfileCount,
            int yProfileCount)
        {
            Span<uint> slots = sceneWords.Slice(slotsBase, slotWordCount);

            // Not every encoded point starts a line. Mark all point pairs as having no profile,
            // then write tags only at the line starts of the recorded aliased fills.
            slots.Fill(uint.MaxValue);
            sceneWords[recordsBase] = (uint)xProfileCount;
            sceneWords[recordsBase + 1] = (uint)yProfileCount;

            int xBase = 0;
            int yBase = 0;
            int xRecordOffset = recordsBase + 2;
            int yRecordOffset = xRecordOffset + (xProfileCount * 3);
            for (int fillIndex = 0; fillIndex < fills.Count; fillIndex++)
            {
                GpuProfileFillRecord fill = fills[fillIndex];
                WriteProfileFill(
                    sceneWords,
                    slots,
                    fill,
                    dataRegionBase + fill.PathDataOffset,
                    xProfileCount,
                    yProfileCount,
                    ref xBase,
                    ref yBase,
                    ref xRecordOffset,
                    ref yRecordOffset);
            }
        }

        /// <summary>
        /// Writes both profile tables from ordered partitions without copying their fill lists.
        /// </summary>
        /// <param name="sceneWords">The full scene word span.</param>
        /// <param name="partitions">The ordered partition encodings.</param>
        /// <param name="dataRegionBase">The word offset of path data in the scene.</param>
        /// <param name="slotsBase">The word offset of the segment-to-profile table.</param>
        /// <param name="slotWordCount">The segment-to-profile table length in words.</param>
        /// <param name="recordsBase">The word offset of the profile range and contour-link table.</param>
        /// <param name="xProfileCount">The addressable X-profile count.</param>
        /// <param name="yProfileCount">The addressable Y-profile count.</param>
        private static void WriteProfileRegions(
            Span<uint> sceneWords,
            SceneEncodingPartition?[] partitions,
            int dataRegionBase,
            int slotsBase,
            int slotWordCount,
            int recordsBase,
            int xProfileCount,
            int yProfileCount)
        {
            Span<uint> slots = sceneWords.Slice(slotsBase, slotWordCount);

            // All partitions share one table indexed by absolute scene position. Initialize
            // padding, non-line points, and fills without profiles to the sentinel tag.
            slots.Fill(uint.MaxValue);
            sceneWords[recordsBase] = (uint)xProfileCount;
            sceneWords[recordsBase + 1] = (uint)yProfileCount;

            int xBase = 0;
            int yBase = 0;
            int xRecordOffset = recordsBase + 2;
            int yRecordOffset = xRecordOffset + (xProfileCount * 3);
            int partitionDataOffset = 0;
            for (int partitionIndex = 0; partitionIndex < partitions.Length; partitionIndex++)
            {
                SceneEncodingPartition partition = partitions[partitionIndex]!;
                List<GpuProfileFillRecord>? fills = partition.ProfileFills;
                if (fills is not null)
                {
                    for (int fillIndex = 0; fillIndex < fills.Count; fillIndex++)
                    {
                        GpuProfileFillRecord fill = fills[fillIndex];
                        WriteProfileFill(
                            sceneWords,
                            slots,
                            fill,
                            dataRegionBase + partitionDataOffset + fill.PathDataOffset,
                            xProfileCount,
                            yProfileCount,
                            ref xBase,
                            ref yBase,
                            ref xRecordOffset,
                            ref yRecordOffset);
                    }
                }

                partitionDataOffset += partition.PathDataWordCount;
            }
        }

        /// <summary>
        /// Writes one fill's profile records and maps each line start to its profile identifiers.
        /// </summary>
        /// <param name="sceneWords">The full scene word span.</param>
        /// <param name="slots">The segment-to-profile table.</param>
        /// <param name="fill">The aliased fill that has profile data.</param>
        /// <param name="contourStart">The fill's first absolute path-data word.</param>
        /// <param name="xProfileCount">The addressable X-profile count.</param>
        /// <param name="yProfileCount">The addressable Y-profile count.</param>
        /// <param name="xBase">The next scene-global X-profile identifier.</param>
        /// <param name="yBase">The next scene-global Y-profile identifier.</param>
        /// <param name="xRecordOffset">The next X-profile record word offset.</param>
        /// <param name="yRecordOffset">The next Y-profile record word offset.</param>
        private static void WriteProfileFill(
            Span<uint> sceneWords,
            Span<uint> slots,
            in GpuProfileFillRecord fill,
            int contourStart,
            int xProfileCount,
            int yProfileCount,
            ref int xBase,
            ref int yBase,
            ref int xRecordOffset,
            ref int yRecordOffset)
        {
            LinearGeometryProfiles profiles = fill.Profiles;

            // The scene-wide 16-bit limit can truncate the final fill's profile tables. Tags for
            // records beyond the limit become sentinels when they are rebased below.
            int xCount = Math.Min(profiles.XProfileCount, xProfileCount - xBase);
            int yCount = Math.Min(profiles.YProfileCount, yProfileCount - yBase);
            WriteProfileRecords(sceneWords.Slice(xRecordOffset, xCount * 3), profiles, xAxis: true, xCount, xBase, fill.TranslateX);
            WriteProfileRecords(sceneWords.Slice(yRecordOffset, yCount * 3), profiles, xAxis: false, yCount, yBase, fill.TranslateY);

            int segmentOrdinal = 0;
            LinearContour[] contours = (LinearContour[])fill.Geometry.Contours;

            // A profile tag is stored at the start-point pair of its line. Walk the contour layout
            // to find those pairs and write directly to the final table. This second linear pass
            // avoids a separate offset array for every fill.
            for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
            {
                LinearContour contour = contours[contourIndex];
                for (int j = 0; j < contour.SegmentCount; j++)
                {
                    slots[(contourStart >> 1) + j] = RebaseProfileTag(profiles.GetSegmentTag(segmentOrdinal++), xBase, yBase);
                }

                contourStart += 2 + (contour.SegmentCount * 2);
            }

            xBase += xCount;
            yBase += yCount;
            xRecordOffset += xCount * 3;
            yRecordOffset += yCount * 3;
        }

        /// <summary>
        /// Resolves ordered partition encodings into the final packed scene buffers.
        /// </summary>
        /// <param name="partitions">The ordered partition encodings.</param>
        /// <param name="targetBounds">The target bounds.</param>
        /// <param name="allocator">The allocator used for scene buffers.</param>
        /// <returns>The resolved encoded scene.</returns>
        public static WebGPUEncodedScene Resolve(
            SceneEncodingPartition?[] partitions,
            in Rectangle targetBounds,
            MemoryAllocator allocator)
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
            int solidColorCount = 0;
            int gradientRampCount = 0;
            int gradientRowCount = 0;
            int fillCount = 0;
            int pathCount = 0;
            int lineCount = 0;
            int infoWordCount = 0;
            int clipCount = 0;
            int profileFillCount = 0;
            long estimatedPathRowCount = 0;
            long estimatedTileCrossings = 0;
            long estimatedPtclWords = 0;
            long estimatedBinFootprint = 0;
            long estimatedPathTiles = 0;
            List<Rectangle> blendDepthRegions = [];

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
                solidColorCount += partition.SolidColors.Count;
                gradientRampCount += partition.GradientRamps.Count;
                gradientRowCount += partition.GradientRowCount;
                fillCount += partition.FillCount;
                pathCount += partition.PathCount;
                lineCount += partition.LineCount;
                infoWordCount += partition.InfoWordCount;
                clipCount += partition.ClipCount;
                profileFillCount += partition.ProfileFills?.Count ?? 0;
                estimatedPathRowCount = Math.Min(estimatedPathRowCount + partition.EstimatedPathRowCount, int.MaxValue);
                estimatedTileCrossings += partition.EstimatedTileCrossings;
                estimatedPtclWords += partition.EstimatedPtclWords;
                estimatedBinFootprint += partition.EstimatedBinFootprint;
                estimatedPathTiles += partition.EstimatedPathTiles;

                // Partitions do not cut through layers, and each partition replays the clips open
                // at its boundary, so each partition counts the true depth of its clips. All
                // partitions use the same tile grid, so the regions of one depth join by union.
                Rectangle[] partitionRegions = partition.BlendDepthRegions;
                for (int depth = 0; depth < partitionRegions.Length; depth++)
                {
                    if (blendDepthRegions.Count <= depth)
                    {
                        blendDepthRegions.Add(partitionRegions[depth]);
                    }
                    else if (!partitionRegions[depth].IsEmpty)
                    {
                        blendDepthRegions[depth] = blendDepthRegions[depth].IsEmpty
                            ? partitionRegions[depth]
                            : Rectangle.Union(blendDepthRegions[depth], partitionRegions[depth]);
                    }
                }
            }

            // Path tags are reduced in 256-word scan segments (pathtag_reduce WG_SIZE), so the
            // word count is padded up to a whole segment before the path-data stream begins.
            int pathTagWordCount = AlignUp(DivideRoundUp(pathTagByteCount, 4), 256);
            int drawTagBase = pathTagWordCount + pathDataWordCount;
            int drawDataBase = drawTagBase + drawTagCount;
            int transformBase = drawDataBase + drawDataWordCount;
            int styleBase = transformBase + transformWordCount;
            int sceneWordCount = styleBase + styleWordCount;

            int profileSlotWordCount = 0;
            int xProfileCount = 0;
            int yProfileCount = 0;
            int profileSlotsBase = 0;
            int profileRecordsBase = 0;
            if (profileFillCount != 0)
            {
                GetProfileRegionLayout(
                    partitions,
                    pathTagWordCount,
                    pathDataWordCount,
                    out profileSlotWordCount,
                    out xProfileCount,
                    out yProfileCount);

                profileSlotsBase = sceneWordCount;
                profileRecordsBase = checked(profileSlotsBase + profileSlotWordCount);
                sceneWordCount = checked(profileRecordsBase + 2 + ((xProfileCount + yProfileCount) * 3));
            }

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
                List<GpuSolidColorDescriptor> solidColors = new(solidColorCount);
                List<GpuGradientRampDescriptor> gradientRamps = new(gradientRampCount);
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

                    for (int solidColorIndex = 0; solidColorIndex < partition.SolidColors.Count; solidColorIndex++)
                    {
                        GpuSolidColorDescriptor solidColor = partition.SolidColors[solidColorIndex];

                        solidColors.Add(new GpuSolidColorDescriptor(
                            solidColor.Color,
                            drawDataOffset + solidColor.DrawDataWordOffset));
                    }

                    for (int gradientRampIndex = 0; gradientRampIndex < partition.GradientRamps.Count; gradientRampIndex++)
                    {
                        GpuGradientRampDescriptor gradientRamp = partition.GradientRamps[gradientRampIndex];

                        gradientRamps.Add(new GpuGradientRampDescriptor(
                            gradientRamp.ColorStops,
                            gradientRowOffset + gradientRamp.RowIndex));
                    }

                    drawDataOffset += partition.DrawDataWordCount;

                    partition.Transforms.CopyTo(sceneWords.Slice((int)layout.TransformBase + transformOffset, partition.TransformWordCount));
                    transformOffset += partition.TransformWordCount;

                    partition.Styles.CopyTo(sceneWords.Slice((int)layout.StyleBase + styleOffset, partition.StyleWordCount));
                    styleOffset += partition.StyleWordCount;

                    if (partition.GradientRowCount > 0)
                    {
                        partition.GradientPixels.CopyTo(
                            gradientPixelsOwner!.Memory.Span.Slice(gradientPixelOffset, partition.GradientRowCount * GradientRowWordCount));

                        gradientPixelOffset += partition.GradientRowCount * GradientRowWordCount;
                    }

                    if (partition.PathGradientDataWordCount > 0)
                    {
                        partition.PathGradientData.CopyTo(
                            pathGradientDataOwner!.Memory.Span.Slice(pathGradientDataOffset, partition.PathGradientDataWordCount));
                    }

                    gradientRowOffset += partition.GradientRowCount;
                    pathGradientDataOffset += partition.PathGradientDataWordCount;
                }

                sceneBytes[pathTagByteCount..(pathTagWordCount * sizeof(uint))].Clear();
                if (profileFillCount != 0)
                {
                    WriteProfileRegions(
                        sceneWords,
                        partitions,
                        pathTagWordCount,
                        profileSlotsBase,
                        profileSlotWordCount,
                        profileRecordsBase,
                        xProfileCount,
                        yProfileCount);
                }

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
                    solidColors,
                    gradientRamps,
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
                    estimatedTileCrossings,
                    estimatedPtclWords,
                    estimatedBinFootprint,
                    []);

                resolved.SetProfileRegions((uint)profileSlotsBase, (uint)profileRecordsBase);
                resolved.SetScratchBounds(
                    estimatedPathTiles,
                    WebGPUEncodedScene.GetBlendSpillSlots(CollectionsMarshal.AsSpan(blendDepthRegions), int.MinValue, int.MaxValue),
                    [.. blendDepthRegions]);

                // All partitions use the grid of the whole target, so their row bounds add.
                WebGPUSceneRowBounds rowBounds = new(DivideRoundUp(targetBounds.Height, TileHeight), 0);
                for (int i = 0; i < partitions.Length; i++)
                {
                    if (partitions[i]!.RowBounds is { } partitionRowBounds)
                    {
                        rowBounds.Add(partitionRowBounds);
                    }
                }

                resolved.SetRowBounds(rowBounds, [rowBounds]);
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
    /// <param name="geometry">The linear geometry to encode.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the number of non-horizontal line segments encoded.</param>
    /// <param name="tileCrossings">
    /// Receives the summed conservative tile-crossing bound over every encoded segment, used to seed the
    /// segment and path-tile scratch buffers far more tightly than the draw's bounding-box diagonal.
    /// </param>
    /// <param name="rowCrossings">Receives an upper bound for the tile crossings of the segments in one tile row.</param>
    /// <returns>The number of encoded path objects: 1, or 0 when the geometry has no segments.</returns>
    private static int EncodePath(
        Point destinationOffset,
        LinearGeometry geometry,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out long tileCrossings,
        out long rowCrossings)
    {
        Vector2 translate = new(destinationOffset.X - rootTargetBounds.X, destinationOffset.Y - rootTargetBounds.Y);
        lineCount = 0;
        tileCrossings = 0;
        rowCrossings = 0;

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

                // path_lowering takes a line slot for every segment of nonzero length,
                // horizontal ones too, so all of them count toward the line bound.
                tileCrossings += CountLineTileCrossings(current, end, out long columnCrossings);
                if (end != current)
                {
                    // In one tile row a segment visits at most all of its tile columns. One more
                    // column covers a translation of the tile alignment, and one more the f32
                    // rounding of the GPU walk. This bound needs no division in this loop.
                    lineCount++;
                    rowCrossings += columnCrossings + 3;
                }

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
    /// <param name="columnCrossings">Receives the tile column boundaries crossed between the two endpoints.</param>
    /// <returns>The number of tiles the segment covers.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CountLineTileCrossings(Vector2 start, Vector2 end, out long columnCrossings)
    {
        Vector2 tileSize = new(TileWidth, TileHeight);
        Vector2 spanned = Vector2.Abs(Floor(end / tileSize) - Floor(start / tileSize));
        columnCrossings = (long)spanned.X;
        return 1 + (long)Sum(spanned);
    }

    /// <summary>
    /// Returns an upper bound for the tiles that one straight segment visits in a single tile row.
    /// </summary>
    /// <remarks>
    /// In a 16-pixel row band a segment spans at most min(|dx|, 16 |dx| / |dy|) pixels horizontally,
    /// and a span of w pixels touches at most floor(w / 16) + 2 tile columns at any alignment. One
    /// more tile absorbs the f32 rounding of the GPU walk.
    /// </remarks>
    /// <param name="start">The segment start in pixels.</param>
    /// <param name="end">The segment end in pixels.</param>
    /// <returns>The tile bound for one tile row.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetRowTileCrossings(Vector2 start, Vector2 end)
    {
        // A horizontal segment divides by zero, which gives +Infinity, so Min keeps the |dx| term.
        Vector2 delta = Vector2.Abs(end - start);
        return (long)(MathF.Min(delta.X / TileWidth, delta.X / delta.Y) + 3F);
    }

    /// <summary>
    /// Returns an upper bound for the tile span of the stroke of one segment in a single tile row.
    /// </summary>
    /// <remarks>
    /// The stroke lies inside the capsule of the segment with the outline radius. The capsule part
    /// in one 16-pixel row band comes from the centerline part in that band widened by the radius
    /// on both sides, which spans at most min(|dx|, (16 + 2r) |dx| / |dy|) pixels horizontally.
    /// Widened by 2r, that span touches at most its width / 16 + 2 tile columns, and one more tile
    /// covers the backdrop tile at x + 1 and the f32 rounding of the GPU walk.
    /// </remarks>
    /// <param name="start">The segment start in pixels.</param>
    /// <param name="end">The segment end in pixels.</param>
    /// <param name="radius">The outline radius in pixels.</param>
    /// <returns>The tile span bound for one tile row.</returns>
    private static int GetSegmentRowSpanTiles(Vector2 start, Vector2 end, float radius)
    {
        // A horizontal segment divides by zero, which gives +Infinity, so Min keeps the |dx| term.
        Vector2 delta = Vector2.Abs(end - start);
        float centerlineWidth = MathF.Min(delta.X, (TileHeight + (2F * radius)) * delta.X / delta.Y);
        return (int)MathF.Min(((centerlineWidth + (2F * radius)) / TileWidth) + 3F, int.MaxValue);
    }

    /// <summary>
    /// Returns an upper bound for the path tiles that tile_alloc allocates for one draw.
    /// </summary>
    /// <remarks>
    /// path_row_span gives each tile row the span from the first to the last tile that a line
    /// touches. When a line passes outside the clipped bbox on one side, the span widens to that bbox
    /// edge. The lines of a draw lie inside the capsules of its source segments with the stroke
    /// radius, so the hull of the capsule extents in each row bounds that span. One extra tile on each side
    /// covers the top-edge backdrop tile at x + 1 and the f32 rounding of the GPU walk.
    /// </remarks>
    /// <param name="source">The source segments of the draw.</param>
    /// <param name="gridTranslate">The translation from source points to tile-grid pixels.</param>
    /// <param name="tileX0">The first tile column of the clipped bounds.</param>
    /// <param name="tileX1">The tile column after the clipped bounds.</param>
    /// <param name="tileY0">The first tile row of the clipped bounds.</param>
    /// <param name="tileY1">The tile row after the clipped bounds.</param>
    /// <param name="rowBounds">The row bounds that receive the span of each row.</param>
    /// <param name="ptclWordsPerTile">The largest PTCL word count that the draw writes in one tile.</param>
    /// <returns>The sum of the row-span tiles.</returns>
    private static long BoundRowSpanTiles(
        in RowSpanSource source,
        Vector2 gridTranslate,
        int tileX0,
        int tileX1,
        int tileY0,
        int tileY1,
        WebGPUSceneRowBounds rowBounds,
        int ptclWordsPerTile)
    {
        int rowCount = tileY1 - tileY0;
        int[]? rented = null;
        Span<int> spans = rowCount <= 256
            ? stackalloc int[512]
            : (rented = ArrayPool<int>.Shared.Rent(rowCount * 2));

        // Each row keeps its first and last tile column in two adjacent slots.
        spans = spans[..(rowCount * 2)];
        for (int i = 0; i < rowCount; i++)
        {
            spans[i * 2] = int.MaxValue;
            spans[(i * 2) + 1] = int.MinValue;
        }

        if (source.Geometry is LinearGeometry geometry)
        {
            IReadOnlyList<LinearContour> contours = geometry.Contours;
            IReadOnlyList<PointF> points = geometry.Points;
            for (int i = 0; i < contours.Count; i++)
            {
                LinearContour contour = contours[i];
                if (contour.PointCount == 0)
                {
                    continue;
                }

                // The segment walk matches EncodePath: a closed contour ends at its first point.
                Vector2 current = (Vector2)points[contour.PointStart] + gridTranslate;
                for (int j = 0; j < contour.SegmentCount; j++)
                {
                    int endPointIndex = contour.PointStart + ((j + 1) == contour.PointCount ? 0 : j + 1);
                    Vector2 end = (Vector2)points[endPointIndex] + gridTranslate;
                    AccumulateSegmentRowSpans(spans, current, end, source.Radius, tileX0, tileX1, tileY0, tileY1);
                    current = end;
                }

                if (contour.SegmentCount == 0)
                {
                    AccumulateSegmentRowSpans(spans, current, current, source.Radius, tileX0, tileX1, tileY0, tileY1);
                }
            }
        }
        else
        {
            AccumulateSegmentRowSpans(spans, source.Start + gridTranslate, source.End + gridTranslate, source.Radius, tileX0, tileX1, tileY0, tileY1);
        }

        // Each row span also goes to the row bounds, so a chunk window gets the spans of its rows.
        long tiles = 0;
        for (int i = 0; i < rowCount; i++)
        {
            int first = spans[i * 2];
            int last = spans[(i * 2) + 1];
            if (last >= first)
            {
                int rowTiles = last - first + 1;
                tiles += rowTiles;
                rowBounds.AddRowTiles(tileY0 + i, rowTiles, (long)rowTiles * ptclWordsPerTile);
            }
        }

        if (rented is not null)
        {
            ArrayPool<int>.Shared.Return(rented);
        }

        return tiles;
    }

    /// <summary>
    /// Widens the row spans with the capsule of one segment.
    /// </summary>
    /// <param name="spans">The first and last tile column of each row, in adjacent slots.</param>
    /// <param name="a">The segment start in tile-grid pixels.</param>
    /// <param name="b">The segment end in tile-grid pixels.</param>
    /// <param name="radius">The capsule radius in pixels.</param>
    /// <param name="tileX0">The first tile column of the clipped bounds.</param>
    /// <param name="tileX1">The tile column after the clipped bounds.</param>
    /// <param name="tileY0">The first tile row of the clipped bounds.</param>
    /// <param name="tileY1">The tile row after the clipped bounds.</param>
    private static void AccumulateSegmentRowSpans(Span<int> spans, Vector2 a, Vector2 b, float radius, int tileX0, int tileX1, int tileY0, int tileY1)
    {
        float yMin = MathF.Min(a.Y, b.Y);
        float yMax = MathF.Max(a.Y, b.Y);
        int rowFirst = Math.Max((int)MathF.Floor((yMin - radius) / TileHeight), tileY0);
        int rowLast = Math.Min((int)MathF.Floor((yMax + radius) / TileHeight), tileY1 - 1);
        if (rowFirst > rowLast)
        {
            return;
        }

        float dy = b.Y - a.Y;
        bool flat = MathF.Abs(dy) < 1E-6F;
        float inverseDy = flat ? 0F : (b.X - a.X) / dy;
        for (int row = rowFirst; row <= rowLast; row++)
        {
            // The part of the segment whose capsule reaches this row lies between the row band
            // widened by the radius, clamped to the y range of the segment.
            float xa;
            float xb;
            if (flat)
            {
                xa = MathF.Min(a.X, b.X);
                xb = MathF.Max(a.X, b.X);
            }
            else
            {
                float ya = Math.Clamp((row * TileHeight) - radius, yMin, yMax);
                float yb = Math.Clamp(((row + 1) * TileHeight) + radius, yMin, yMax);
                float x0 = a.X + ((ya - a.Y) * inverseDy);
                float x1 = a.X + ((yb - a.Y) * inverseDy);
                xa = MathF.Min(x0, x1);
                xb = MathF.Max(x0, x1);
            }

            int first = Math.Clamp((int)MathF.Floor((xa - radius) / TileWidth) - 1, tileX0, tileX1 - 1);
            int last = Math.Clamp((int)MathF.Floor((xb + radius) / TileWidth) + 1, tileX0, tileX1 - 1);
            int slot = (row - tileY0) * 2;
            spans[slot] = Math.Min(spans[slot], first);
            spans[slot + 1] = Math.Max(spans[slot + 1], last);
        }
    }

    /// <summary>
    /// Returns the radius around the stroke centerline that contains the GPU stroke outline: the
    /// outline radius of the pen plus one pixel.
    /// </summary>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The radius in pixels.</returns>
    private static float GetStrokeOutlineRadius(Pen pen, float widthScale)
    {
        // One pixel absorbs the f32 differences of the GPU stroker.
        return pen.GetOutlineRadius(widthScale) + 1F;
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
    /// Encodes the final line segments of a stroke centreline in target-local space.
    /// </summary>
    /// <remarks>
    /// The encoder removes duplicate and sub-1/64-pixel segments and applies the same open/closed
    /// contour rules as the CPU stroker. The shader receives lines only and expands those lines into
    /// stroke bodies, joins, and caps.
    /// </remarks>
    /// <param name="geometry">The linear centreline geometry to encode.</param>
    /// <param name="destinationOffset">The absolute destination offset applied to every point.</param>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <param name="residualStretch">The largest length scale of the residual transform the GPU applies to the points.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the estimated final line workload for the stroke.</param>
    /// <param name="tileCrossings">Receives the bound on the tile crossings of the emitted stroke lines.</param>
    /// <param name="rowCrossings">Receives an upper bound for the tile crossings of the emitted body lines in one tile row.</param>
    /// <param name="endCrossings">Receives the tile crossings of the emitted join and cap lines.</param>
    /// <returns>The number of encoded path objects: 1, or 0 when no contour survived preprocessing.</returns>
    private static int EncodeStrokePath(
        LinearGeometry geometry,
        Point destinationOffset,
        Pen pen,
        float widthScale,
        float residualStretch,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out long tileCrossings,
        out long rowCrossings,
        out long endCrossings)
    {
        long bodyRowCrossings = 0;
        long innerJoinCrossings = 0;
        int bodySegmentCount = 0;
        float outlineRadius = GetStrokeOutlineRadius(pen, widthScale);
        float pointTranslateX = destinationOffset.X - rootTargetBounds.X;
        float pointTranslateY = destinationOffset.Y - rootTargetBounds.Y;
        lineCount = EstimateStrokeLineCount(geometry, pen, widthScale);
        float strokeWidth = pen.StrokeWidth * widthScale;
        float halfWidth = strokeWidth * 0.5F;
        float capChainLength = GetStrokeCapChainLength(pen, halfWidth);
        double outlineLength = 0D;
        int encodedContourCount = 0;
        LinearContour[] contours = (LinearContour[])geometry.Contours;
        PointF[] geometryPoints = (PointF[])geometry.Points;

        for (int i = 0; i < contours.Length; i++)
        {
            LinearContour contour = contours[i];
            if (contour.PointCount == 0)
            {
                continue;
            }

            ReadOnlySpan<PointF> contourPoints = geometryPoints.AsSpan(contour.PointStart, contour.PointCount);
            PointF firstPoint = contourPoints[0];
            PointF previousKept = firstPoint;
            PointF pointLike = firstPoint;
            PointF secondPoint = default;
            PointF lastKept = firstPoint;
            int pointIndex = 1;
            int keptCount = 1;
            Vector2 firstDirection = default;
            float firstLength = 0F;
            Vector2 previousDirection = default;
            float previousLength = 0F;
            double contourOutlineLength = 0D;

            // Count the filtered points and retain only the few values needed to classify the
            // contour. The second pass writes them directly to the scene streams, so no temporary
            // point buffer is rented for each stroke geometry.
            while (true)
            {
                PointF segmentStart = previousKept;
                if (!TryGetNextStrokePoint(contourPoints, ref pointIndex, ref previousKept, ref pointLike, out PointF point))
                {
                    break;
                }

                Vector2 segment = (Vector2)point - (Vector2)segmentStart;
                float segmentLength = segment.Length();
                Vector2 direction = segment / segmentLength;
                contourOutlineLength += 2D * segmentLength;
                bodyRowCrossings += 2L * GetRowTileCrossings(segmentStart, point);
                bodySegmentCount++;
                if (keptCount == 1)
                {
                    secondPoint = point;
                    firstDirection = direction;
                    firstLength = segmentLength;
                }
                else
                {
                    contourOutlineLength += GetStrokeJoinChainLength(
                        pen,
                        halfWidth,
                        previousDirection,
                        previousLength,
                        direction,
                        segmentLength,
                        out float innerExtent);

                    innerJoinCrossings += GetInnerJoinTileCrossings(innerExtent, halfWidth, outlineRadius);
                }

                previousDirection = direction;
                previousLength = segmentLength;
                lastKept = point;
                keptCount++;
            }

            int segmentCount = keptCount - 1;

            // Match the CPU closure rule. A contour with at least three source points is closed
            // when it is declared closed or its endpoints are within one scaled stroke width.
            bool isClosed = false;
            if (contourPoints.Length >= 3)
            {
                PointF last = contourPoints[^1];
                float closeThreshold = MathF.Max(strokeWidth, 1E-3F);
                isClosed = contour.IsClosed ||
                    firstPoint == last ||
                    Vector2.DistanceSquared(firstPoint, last) <= closeThreshold * closeThreshold;
            }

            bool duplicateClosingPoint = keptCount > 1 && lastKept == firstPoint;
            int distinctPointCount = keptCount - (isClosed && duplicateClosingPoint ? 1 : 0);

            if (segmentCount == 0)
            {
                EncodePointStrokeContour(pointLike, pointTranslateX, pointTranslateY, ref pathTags, ref pathData);
                outlineLength += (4D * PointStrokeSegmentHalfLength) + (2D * capChainLength);
                encodedContourCount++;
                continue;
            }

            if (segmentCount == 1 || distinctPointCount == 2)
            {
                // The CPU stroker emits these as one capped open segment even when declared closed.
                Span<PointF> segmentPoints = [firstPoint, secondPoint];
                EncodeOpenStrokeContour(segmentPoints, pointTranslateX, pointTranslateY, ref pathTags, ref pathData);
                outlineLength += (2D * Vector2.Distance(firstPoint, secondPoint)) + (2D * capChainLength);
                encodedContourCount++;
                continue;
            }

            if (isClosed)
            {
                int emitCount = duplicateClosingPoint ? keptCount - 1 : keptCount;

                // Emit the final line back to the first point only when that gap survived the
                // micro-segment filter. A smaller gap is closed by the join alone.
                bool closingSegment = duplicateClosingPoint ||
                    (segmentCount > 1 &&
                     Vector2.DistanceSquared(lastKept, firstPoint) > StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon);

                if (closingSegment && !duplicateClosingPoint)
                {
                    bodyRowCrossings += 2L * GetRowTileCrossings(lastKept, firstPoint);
                    bodySegmentCount++;
                    Vector2 closingVector = (Vector2)firstPoint - (Vector2)lastKept;
                    float closingLength = closingVector.Length();
                    Vector2 closingDirection = closingVector / closingLength;
                    float closingJoinLength = GetStrokeJoinChainLength(
                        pen,
                        halfWidth,
                        previousDirection,
                        previousLength,
                        closingDirection,
                        closingLength,
                        out float closingInnerExtent);

                    float firstJoinLength = GetStrokeJoinChainLength(
                        pen,
                        halfWidth,
                        closingDirection,
                        closingLength,
                        firstDirection,
                        firstLength,
                        out float firstInnerExtent);

                    contourOutlineLength += (2D * closingLength) + closingJoinLength + firstJoinLength;
                    innerJoinCrossings += GetInnerJoinTileCrossings(closingInnerExtent, halfWidth, outlineRadius) +
                        GetInnerJoinTileCrossings(firstInnerExtent, halfWidth, outlineRadius);
                }
                else
                {
                    contourOutlineLength += GetStrokeJoinChainLength(
                        pen,
                        halfWidth,
                        previousDirection,
                        previousLength,
                        firstDirection,
                        firstLength,
                        out float innerExtent);

                    innerJoinCrossings += GetInnerJoinTileCrossings(innerExtent, halfWidth, outlineRadius);
                }

                outlineLength += contourOutlineLength;
                int linetoCount = (emitCount - 1) + (closingSegment ? 1 : 0);
                Span<uint> contourData = pathData.GetAppendSpan(2 + (linetoCount * 2) + 2);
                Span<byte> contourTags = pathTags.GetAppendSpan(linetoCount + 1);

                int dataIndex = 0;
                int tagIndex = 0;
                contourData[dataIndex++] = BitcastSingle(firstPoint.X + pointTranslateX);
                contourData[dataIndex++] = BitcastSingle(firstPoint.Y + pointTranslateY);

                previousKept = firstPoint;
                pointLike = firstPoint;
                pointIndex = 1;
                PointF lastEndpoint = firstPoint;
                for (int j = 1; j < emitCount; j++)
                {
                    _ = TryGetNextStrokePoint(contourPoints, ref pointIndex, ref previousKept, ref pointLike, out PointF point);
                    contourData[dataIndex++] = BitcastSingle(point.X + pointTranslateX);
                    contourData[dataIndex++] = BitcastSingle(point.Y + pointTranslateY);
                    contourTags[tagIndex++] = PackPathTag(PathTag.LineToF32);
                    lastEndpoint = point;
                }

                if (closingSegment)
                {
                    contourData[dataIndex++] = BitcastSingle(firstPoint.X + pointTranslateX);
                    contourData[dataIndex++] = BitcastSingle(firstPoint.Y + pointTranslateY);
                    contourTags[tagIndex++] = PackPathTag(PathTag.LineToF32);
                    lastEndpoint = firstPoint;
                }

                // The closing marker extends from the last emitted endpoint in the direction of
                // the first segment. The shader uses this synthetic line only to calculate the
                // wrap join; it does not emit another stroke body.
                contourData[dataIndex++] = BitcastSingle(lastEndpoint.X + (secondPoint.X - firstPoint.X) + pointTranslateX);
                contourData[dataIndex] = BitcastSingle(lastEndpoint.Y + (secondPoint.Y - firstPoint.Y) + pointTranslateY);
                contourTags[tagIndex] = PackPathTag(PathTag.LineToF32 | PathTag.SubpathEnd);

                pathData.Advance(contourData.Length);
                pathTags.Advance(contourTags.Length);

                encodedContourCount++;
                continue;
            }

            int openLinetoCount = keptCount - 1;
            outlineLength += contourOutlineLength + (2D * capChainLength);
            Span<uint> openData = pathData.GetAppendSpan(2 + (openLinetoCount * 2) + 4);
            Span<byte> openTags = pathTags.GetAppendSpan(openLinetoCount + 1);
            int openDataIndex = 0;
            int openTagIndex = 0;
            openData[openDataIndex++] = BitcastSingle(firstPoint.X + pointTranslateX);
            openData[openDataIndex++] = BitcastSingle(firstPoint.Y + pointTranslateY);

            previousKept = firstPoint;
            pointLike = firstPoint;
            pointIndex = 1;
            while (TryGetNextStrokePoint(contourPoints, ref pointIndex, ref previousKept, ref pointLike, out PointF point))
            {
                openData[openDataIndex++] = BitcastSingle(point.X + pointTranslateX);
                openData[openDataIndex++] = BitcastSingle(point.Y + pointTranslateY);
                openTags[openTagIndex++] = PackPathTag(PathTag.LineToF32);
            }

            // The open marker stores the start point and one point on its outgoing tangent. QuadTo
            // selects this four-word marker layout; it does not represent a curve in the shader.
            PointF tangentPoint = GetStrokeMarkerTangentPoint(firstPoint, secondPoint);
            openData[openDataIndex++] = BitcastSingle(firstPoint.X + pointTranslateX);
            openData[openDataIndex++] = BitcastSingle(firstPoint.Y + pointTranslateY);
            openData[openDataIndex++] = BitcastSingle(tangentPoint.X + pointTranslateX);
            openData[openDataIndex] = BitcastSingle(tangentPoint.Y + pointTranslateY);
            openTags[openTagIndex] = PackPathTag(PathTag.QuadToF32 | PathTag.SubpathEnd);

            pathData.Advance(openData.Length);
            pathTags.Advance(openTags.Length);
            encodedContourCount++;
        }

        tileCrossings = BoundStrokeTileCrossings(outlineLength, residualStretch, lineCount);
        rowCrossings = bodyRowCrossings;
        endCrossings = GetStrokeEndTileCrossings(lineCount, bodySegmentCount, pen, widthScale) + innerJoinCrossings;
        if (encodedContourCount == 0)
        {
            return 0;
        }

        pathTags.Add(PackPathTag(PathTag.Path));
        return 1;
    }

    /// <summary>
    /// Gets the next stroke contour point that survives exact-duplicate and micro-segment collapse.
    /// </summary>
    /// <param name="points">The source contour points.</param>
    /// <param name="index">The next source point index.</param>
    /// <param name="previousKept">The last point that survived filtering.</param>
    /// <param name="pointLike">The last non-duplicate source point, used when the contour collapses to a point.</param>
    /// <param name="point">Receives the next retained point.</param>
    /// <returns><see langword="true"/> when another point survives filtering.</returns>
    private static bool TryGetNextStrokePoint(
        ReadOnlySpan<PointF> points,
        ref int index,
        ref PointF previousKept,
        ref PointF pointLike,
        out PointF point)
    {
        while (index < points.Length)
        {
            point = points[index++];

            // Exact duplicates do not update pointLike because they provide no direction and do
            // not change the location represented by a fully collapsed contour.
            if (point == previousKept)
            {
                continue;
            }

            pointLike = point;

            // A non-duplicate micro segment updates the representative point but does not become
            // a stroke segment. Measure the next candidate from the last point that was retained.
            if (Vector2.DistanceSquared(previousKept, point) <= StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon)
            {
                continue;
            }

            previousKept = point;
            return true;
        }

        point = default;
        return false;
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

        // QuadTo selects the open-marker layout. These two points define a line tangent; no curve
        // reaches the shader.
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
        // A zero-length centreline has no tangent. Use the same short horizontal segment as the
        // CPU point-stroke path so cap construction has a stable direction.
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
    /// <param name="residualStretch">The largest length scale of the residual transform the GPU applies to the points.</param>
    /// <param name="rootTargetBounds">The root target bounds used for target-local conversion.</param>
    /// <param name="pathTags">The path-tag stream.</param>
    /// <param name="pathData">The path-data stream.</param>
    /// <param name="lineCount">Receives the estimated final line workload for the stroke.</param>
    /// <param name="tileCrossings">Receives the bound on the tile crossings of the emitted stroke lines.</param>
    /// <param name="rowCrossings">Receives an upper bound for the tile crossings of the emitted body lines in one tile row.</param>
    /// <param name="endCrossings">Receives the tile crossings of the emitted join and cap lines.</param>
    /// <returns>The number of encoded path objects; always 1.</returns>
    private static int EncodeOpenSegmentStrokePath(
        PointF start,
        PointF end,
        Point destinationOffset,
        Pen pen,
        float widthScale,
        float residualStretch,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out long tileCrossings,
        out long rowCrossings,
        out long endCrossings)
    {
        float pointTranslateX = destinationOffset.X - rootTargetBounds.X;
        float pointTranslateY = destinationOffset.Y - rootTargetBounds.Y;
        NormalizePointStrokeSegment(ref start, ref end);
        Span<PointF> segmentPoints = [start, end];
        EncodeOpenStrokeContour(segmentPoints, pointTranslateX, pointTranslateY, ref pathTags, ref pathData);
        pathTags.Add(PackPathTag(PathTag.Path));
        lineCount = EstimateStrokeLineCountForOpenSegment(pen, widthScale);
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;
        double outlineLength = (2D * Vector2.Distance(start, end)) + (2D * GetStrokeCapChainLength(pen, halfWidth));
        tileCrossings = BoundStrokeTileCrossings(outlineLength, residualStretch, lineCount);
        rowCrossings = 2L * GetRowTileCrossings(start, end);
        endCrossings = GetStrokeEndTileCrossings(lineCount, 1, pen, widthScale);
        return 1;
    }

    /// <summary>
    /// Returns an upper bound for the tile crossings of the join and cap lines of a stroke.
    /// </summary>
    /// <remarks>
    /// Each body of a centerline segment is two lines parallel to it, which the per-row bound of
    /// the segment covers. The other lines are joins and caps. Each fits in a square of twice the
    /// outline radius r. It crosses at most 2r / 16 + 1 tile boundaries on each axis, so it visits at
    /// most 4r / 16 + 3 tiles. They are near the segment ends, so all of them can be in one tile row.
    /// </remarks>
    /// <param name="lineCount">The estimated GPU line count of the stroke.</param>
    /// <param name="segmentCount">The number of centerline segments with a body.</param>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The tile-crossing bound of the join and cap lines.</returns>
    private static long GetStrokeEndTileCrossings(int lineCount, int segmentCount, Pen pen, float widthScale)
    {
        long otherLines = Math.Max(lineCount - (2L * segmentCount), 0L);
        long otherLineCrossings = (long)(4F * GetStrokeOutlineRadius(pen, widthScale) / TileWidth) + 3L;
        return otherLines * otherLineCrossings;
    }

    /// <summary>
    /// Bounds the tile crossings of the inner chain of one join when it does not fit in the square
    /// of twice the outline radius that <see cref="GetStrokeEndTileCrossings"/> assumes.
    /// </summary>
    /// <remarks>
    /// An inner corner draws two lines: from the offset point of one segment to the inner miter
    /// apex, and from the apex to the offset point of the next segment. The offset points are one
    /// half width from the vertex, so each line is at most innerExtent + halfWidth long. A line
    /// with length L crosses at most sqrt(2) * L / 16 + 3 tiles. This is a bound on the total of the
    /// lines, so it holds in every tile-row window.
    /// </remarks>
    /// <param name="innerExtent">The largest distance of the inner chain from the join vertex.</param>
    /// <param name="halfWidth">Half the transform-scaled stroke width.</param>
    /// <param name="outlineRadius">The outline radius from <see cref="GetStrokeOutlineRadius"/>.</param>
    /// <returns>The added tile-crossing bound, or zero when the lines fit the square.</returns>
    private static long GetInnerJoinTileCrossings(float innerExtent, float halfWidth, float outlineRadius)
    {
        float lineLength = innerExtent + halfWidth;
        return lineLength > 2F * outlineRadius
            ? (long)Math.Ceiling(Math.Sqrt(2D) * 2D * lineLength / TileWidth) + 6L
            : 0L;
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
        lineCount = 4;
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
                    lineCount = 4;
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

                    AppendRectangleContour(
                        new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height),
                        translateX,
                        translateY,
                        ref pathTags,
                        ref pathData);

                    lineCount += 4;
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
                    lineCount += 4;
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
    /// Estimates the final line workload for one stroke.
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

            // The stroke encoder can close an open contour whose ends meet, which adds one body
            // and one join, and it draws a two-point closed contour as a capped open segment. So
            // every contour gets the closing body, a join for each body and two caps.
            int bodies = contour.IsClosed ? contour.SegmentCount : contour.SegmentCount + 1;
            total += (bodies * 2) + (bodies * joinCost) + (capCost * 2);
        }

        return Math.Max(total, 1);
    }

    /// <summary>
    /// Estimates the final line workload for one explicit open two-point stroke segment.
    /// </summary>
    /// <param name="pen">The pen that defines stroke width, joins, and caps.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The conservative line count; at least 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateStrokeLineCountForOpenSegment(Pen pen, float widthScale)
        => Math.Max(2 + (GetStrokeCapLineCost(pen, widthScale) * 2), 1);

    /// <summary>
    /// Bounds the tile crossings of the lines the GPU stroker emits for one stroke. One line crosses at most
    /// (|dx| + |dy|) / 16 + 3 tiles, so the emitted outline crosses at most sqrt(2) times its device-space
    /// length in tiles plus three per line.
    /// </summary>
    /// <param name="outlineLength">The bound on the emitted outline length before the residual transform.</param>
    /// <param name="residualStretch">The largest length scale of the residual transform the GPU applies to the points.</param>
    /// <param name="lineCount">The estimated emitted line count.</param>
    /// <returns>The tile-crossing bound.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long BoundStrokeTileCrossings(double outlineLength, float residualStretch, int lineCount)
        => (long)Math.Ceiling(outlineLength * residualStretch * Math.Sqrt(2D) / TileWidth) + (3L * lineCount);

    /// <summary>
    /// Returns the largest length scale of a residual transform: the Frobenius norm of its 2x2 part, which is
    /// at least its largest singular value.
    /// </summary>
    /// <param name="residual">The residual transform the GPU applies to the points.</param>
    /// <returns>The length scale; 1 for the identity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetResidualStretch(Matrix4x4 residual)
        => residual.IsIdentity
            ? 1F
            : MathF.Sqrt((residual.M11 * residual.M11) + (residual.M12 * residual.M12) + (residual.M21 * residual.M21) + (residual.M22 * residual.M22));

    /// <summary>
    /// Returns the length of the line chain the GPU stroker emits for one cap.
    /// </summary>
    /// <param name="pen">The pen that defines the cap style.</param>
    /// <param name="halfWidth">Half the transform-scaled stroke width.</param>
    /// <returns>The chain length in pixels.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetStrokeCapChainLength(Pen pen, float halfWidth)
        => pen.StrokeOptions.LineCap switch
        {
            LineCap.Square => 4F * halfWidth,
            LineCap.Round => MathF.PI * halfWidth,
            _ => 2F * halfWidth
        };

    /// <summary>
    /// Bounds the length of the line chains the GPU stroker emits for one join. The outer chain reaches at most
    /// the miter apex, the miter limit, the round arc, or the bevel chord. The inner chain reaches at most the
    /// miter apex or the inner miter limit of the shader, which is the shorter neighboring segment and at least
    /// 1.01 half widths.
    /// </summary>
    /// <param name="pen">The pen that defines the join style and miter limit.</param>
    /// <param name="halfWidth">Half the transform-scaled stroke width.</param>
    /// <param name="incoming">The unit direction of the segment entering the join.</param>
    /// <param name="incomingLength">The length of the segment entering the join.</param>
    /// <param name="outgoing">The unit direction of the segment leaving the join.</param>
    /// <param name="outgoingLength">The length of the segment leaving the join.</param>
    /// <param name="innerExtent">Receives the largest distance of the inner chain from the join vertex.</param>
    /// <returns>The chain length bound in pixels.</returns>
    private static float GetStrokeJoinChainLength(
        Pen pen,
        float halfWidth,
        Vector2 incoming,
        float incomingLength,
        Vector2 outgoing,
        float outgoingLength,
        out float innerExtent)
    {
        float cosTheta = -Vector2.Dot(incoming, outgoing);
        float sinHalfTheta = MathF.Sqrt(MathF.Max(0F, (1F - cosTheta) * 0.5F));
        float miterApex = sinHalfTheta > 1E-6F ? halfWidth / sinHalfTheta : float.PositiveInfinity;

        // stroke_side_join limits the inner miter to max(min(len1, len2), 1.01 * half_width).
        innerExtent = MathF.Min(miterApex, MathF.Max(MathF.Min(incomingLength, outgoingLength), 1.01F * halfWidth));
        float miterLimit = (float)Math.Max(pen.StrokeOptions.MiterLimit, 1D);
        float outerExtent = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter => halfWidth * (1F + miterLimit),
            LineJoin.MiterRevert => MathF.Max(MathF.Min(miterApex, halfWidth * miterLimit), 2F * halfWidth),
            LineJoin.MiterRound => MathF.Max(MathF.Min(miterApex, halfWidth * miterLimit), MathF.PI * halfWidth),
            LineJoin.Round => MathF.PI * halfWidth,
            _ => 2F * halfWidth
        };

        return (2F * (outerExtent + halfWidth)) + (2F * (innerExtent + halfWidth));
    }

    /// <summary>
    /// Returns the conservative final line cost of one stroke join.
    /// </summary>
    /// <param name="pen">The pen that defines the join style.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The line cost of one join.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeJoinLineCost(Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;

        // The GPU stroker writes at most 2 inner points, 2 outer points for bevel and miter joins
        // or the arc points plus 2 for round joins, and one connector on each side. At a reversal
        // both sides are outer.
        return pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Round or LineJoin.MiterRound => (2 * (GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale) + 1)) + 4,
            _ => 6
        };
    }

    /// <summary>
    /// Returns the conservative final line cost of one stroke cap.
    /// </summary>
    /// <param name="pen">The pen that defines the cap style.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The line cost of one cap.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeCapLineCost(Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;

        // A round cap writes the arc points plus one. The shader clamps the arc points to the arc
        // limit in the style flags, which is this arc cost.
        return pen.StrokeOptions.LineCap switch
        {
            LineCap.Square => 3,
            LineCap.Round => GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale) + 1,
            _ => 1
        };
    }

    /// <summary>
    /// Returns the conservative final line cost of one round arc in the stroke shaders.
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
            GpuSceneDrawTag.FillLinGradient => 11,
            GpuSceneDrawTag.FillRadGradient => 13,
            GpuSceneDrawTag.FillEllipticGradient => 13,
            GpuSceneDrawTag.FillSweepGradient => 11,
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
    /// Word layout consumed by path_lowering.wgsl: 0 = style flags, 1 = device-space stroke width,
    /// 2 = packed draw flags, 3 = miter limit, 4 = arc detail scale, 5 = zero,
    /// 6-9 = target-local interest rectangle as left, top, right, bottom.
    /// Strokes do not use antialiased text coverage boost data, so word 5 is zero in both modes.
    /// <para>
    /// Bits 0-10 of word 0 hold the arc limit: the interior vertex count of a round arc of
    /// half a turn, from <see cref="GetStrokeArcLineCost"/>. The shader clamps the vertex count of
    /// every arc to it. The f32 arc math on the GPU can give a larger count than the CPU, for
    /// example for a cap radius rounded at a large coordinate. The clamp makes the line estimates
    /// of round joins and caps upper bounds.
    /// </para>
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

        // Only round joins and caps draw arcs, so other pens skip the acos.
        uint arcLimit = 0U;
        if (pen.StrokeOptions.LineJoin is LineJoin.Round or LineJoin.MiterRound || pen.StrokeOptions.LineCap == LineCap.Round)
        {
            int arcCount = GetStrokeArcLineCost(pen.StrokeWidth * widthScale * 0.5F, MathF.PI, pen.StrokeOptions.ArcDetailScale);
            arcLimit = (uint)Math.Min(arcCount, MaxStrokeArcSubdivisions);
        }

        return (
            (uint)styleFlags | arcLimit,
            BitcastSingle(pen.StrokeWidth * widthScale),
            PackStyleDrawFlags(options),
            BitcastSingle((float)pen.StrokeOptions.MiterLimit),
            BitcastSingle((float)pen.StrokeOptions.ArcDetailScale),
            0U,
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
    /// Packs the stroke join flags consumed by the staged path-lowering shader.
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
    /// Packs the start and end cap flags consumed by the staged path-lowering shader.
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
    /// Antialiased fills carry the perceptual coverage boost applied by fine in word 5
    /// (zero for plain fills, non-zero only for text). Aliased fills leave word 5 zero.
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

        // Antialiased fills carry the perceptual coverage boost as a float. Aliased fills sample
        // the shape at pixel centres, a rule with no threshold and no boost, so their coverage
        // word carries nothing.
        uint coverageWord = options.Antialias ? BitcastSingle(coverageBoost) : 0U;

        return (
            (uint)styleFlags,
            0U,
            PackStyleDrawFlags(options),
            0U,
            0U,
            coverageWord,
            BitcastSingle(interestBounds.Left),
            BitcastSingle(interestBounds.Top),
            BitcastSingle(interestBounds.Right),
            BitcastSingle(interestBounds.Bottom));
    }

    /// <summary>
    /// Packs the draw-flags word carried through path lowering into coarse and fine.
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
    public static int DivideRoundUp(int value, int divisor)
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
    /// <param name="solidColors">Receives the target-specialized color patch site.</param>
    private static void AppendSolidColor(
        SolidBrush solidBrush,
        ref OwnedStream<uint> drawData,
        List<GpuSolidColorDescriptor> solidColors)
    {
        // The CPU renderer blends a source snapped through TPixel, which is unknown here
        // because retained scenes bind to any pixel format later. The continuous words keep
        // the stream layout fixed; the descriptor records where the snapped value is patched.
        solidColors.Add(new GpuSolidColorDescriptor(solidBrush.Color, drawData.Count));
        AppendColor(solidBrush.Color.ToScaledVector4(PixelAlphaRepresentation.Associated), ref drawData);
    }

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
    /// <param name="solidColors">Receives deferred solid color patch sites.</param>
    /// <param name="gradientRamps">Receives deferred gradient ramp patch sites.</param>
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
        List<GpuSolidColorDescriptor> solidColors,
        List<GpuGradientRampDescriptor> gradientRamps,
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
                AppendSolidColor((SolidBrush)brush, ref drawData, solidColors);
                break;
            case GpuSceneDrawTag.FillRecolor:
                AppendRecolorData((RecolorBrush)brush, ref drawData, ref pathGradientData, recolors);
                break;
            case GpuSceneDrawTag.FillLinGradient:
                AppendLinearGradientData((LinearGradientBrush)brush, ref drawData, ref gradientPixels, gradientRamps, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillRadGradient:
                AppendRadialGradientData((RadialGradientBrush)brush, ref drawData, ref gradientPixels, gradientRamps, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillEllipticGradient:
                AppendEllipticGradientData((EllipticGradientBrush)brush, ref drawData, ref gradientPixels, gradientRamps, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillSweepGradient:
                AppendSweepGradientData((SweepGradientBrush)brush, ref drawData, ref gradientPixels, gradientRamps, ref gradientRowCount);
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
    /// <param name="clipBlendMode">The base clip blend word.</param>
    /// <param name="drawData">The draw-data stream.</param>
    private static void AppendClipBeginData(
        ClipOperation operation,
        uint clipBlendMode,
        ref OwnedStream<uint> drawData)
    {
        // Difference is ImageSharp-specific clip state, not a Vello blend mode; encode it
        // in a spare bit while ordinary clips keep Vello's exact MIX_CLIP|SRC_OVER marker.
        uint blendMode = operation == ClipOperation.Difference
            ? clipBlendMode | ClipDifferenceMaskBit
            : clipBlendMode;

        drawData.Add(blendMode);
        drawData.Add(BitcastSingle(1F));
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
            PixelColorBlendingMode.ColorDodge => 6U,
            PixelColorBlendingMode.ColorBurn => 7U,
            PixelColorBlendingMode.SoftLight => 9U,
            PixelColorBlendingMode.Difference => 10U,
            PixelColorBlendingMode.Exclusion => 11U,
            PixelColorBlendingMode.Hue => 12U,
            PixelColorBlendingMode.Saturation => 13U,
            PixelColorBlendingMode.Color => 14U,
            PixelColorBlendingMode.Luminosity => 15U,
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
            PixelAlphaCompositionMode.Plus => 12U,
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
    /// <param name="gradientRamps">Receives the target-specialized ramp patch site.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
    private static void AppendLinearGradientData(
        LinearGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        List<GpuGradientRampDescriptor> gradientRamps,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(ref gradientPixels);
        gradientRamps.Add(new GpuGradientRampDescriptor(brush.ColorStops.ToArray(), gradientRowCount));
        gradientRowCount++;

        drawData.Add(indexMode);
        AppendGradientTransform(brush, ref drawData);
        drawData.Add(BitcastSingle(brush.StartPoint.X));
        drawData.Add(BitcastSingle(brush.StartPoint.Y));
        drawData.Add(BitcastSingle(brush.EndPoint.X));
        drawData.Add(BitcastSingle(brush.EndPoint.Y));
    }

    /// <summary>
    /// Appends the transform from drawing coordinates to the gradient's coordinate space, which
    /// is the affine part of the brush's gradient transform inverted.
    /// </summary>
    /// <param name="brush">The gradient brush.</param>
    /// <param name="drawData">The draw-data stream.</param>
    private static void AppendGradientTransform(GradientBrush brush, ref OwnedStream<uint> drawData)
    {
        // A transform that cannot be inverted has no gradient space. The zero matrix maps every
        // sample to the origin, so the brush paints one color where the CPU renderer paints nothing.
        if (!brush.TryGetInverseTransform(out Matrix3x2 m))
        {
            m = default;
        }

        drawData.Add(BitcastSingle(m.M11));
        drawData.Add(BitcastSingle(m.M12));
        drawData.Add(BitcastSingle(m.M21));
        drawData.Add(BitcastSingle(m.M22));
        drawData.Add(BitcastSingle(m.M31));
        drawData.Add(BitcastSingle(m.M32));
    }

    /// <summary>
    /// Appends the radial gradient payload and its packed ramp row.
    /// </summary>
    /// <param name="brush">The radial gradient brush.</param>
    /// <param name="drawData">The draw-data stream.</param>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <param name="gradientRamps">Receives the target-specialized ramp patch site.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
    private static void AppendRadialGradientData(
        RadialGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        List<GpuGradientRampDescriptor> gradientRamps,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(ref gradientPixels);
        gradientRamps.Add(new GpuGradientRampDescriptor(brush.ColorStops.ToArray(), gradientRowCount));
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
        AppendGradientTransform(brush, ref drawData);
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
    /// <param name="gradientRamps">Receives the target-specialized ramp patch site.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
    private static void AppendEllipticGradientData(
        EllipticGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        List<GpuGradientRampDescriptor> gradientRamps,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(ref gradientPixels);
        gradientRamps.Add(new GpuGradientRampDescriptor(brush.ColorStops.ToArray(), gradientRowCount));
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
        AppendGradientTransform(brush, ref drawData);
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
    /// <param name="gradientRamps">Receives the target-specialized ramp patch site.</param>
    /// <param name="gradientRowCount">The gradient row counter, advanced by one.</param>
    private static void AppendSweepGradientData(
        SweepGradientBrush brush,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        List<GpuGradientRampDescriptor> gradientRamps,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(ref gradientPixels);
        gradientRamps.Add(new GpuGradientRampDescriptor(brush.ColorStops.ToArray(), gradientRowCount));
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
        AppendGradientTransform(brush, ref drawData);
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
    /// Reserves one zeroed gradient ramp row across the fixed gradient width.
    /// </summary>
    /// <param name="gradientPixels">The gradient-pixel stream.</param>
    /// <remarks>
    /// The texels are written by <see cref="WebGPUEncodedScene.SpecializeBrushColors{TPixel}"/>
    /// when a flush binds the scene to its pixel format: every ramp sample must snap through
    /// the target pixel type, so values baked here would be discarded. The row advances zeroed
    /// to keep the stream layout fixed.
    /// </remarks>
    private static void AppendGradientRamp(ref OwnedStream<uint> gradientPixels)
    {
        Span<uint> row = gradientPixels.GetAppendSpan(GradientRowWordCount);
        row.Clear();
        gradientPixels.Advance(GradientRowWordCount);
    }

    /// <summary>
    /// Evaluates the premultiplied gradient color at the normalized parameter.
    /// </summary>
    /// <param name="colorStops">The gradient color stops, ordered by ratio.</param>
    /// <param name="t">The normalized sample position in the range [0, 1].</param>
    /// <returns>The associated floating-point sample.</returns>
    public static Vector4 EvaluateGradientColor(ReadOnlySpan<ColorStop> colorStops, float t)
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
    public static uint PackHalf2(float x, float y)
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
/// One retained scene that an encoded scene reads in place, at one position inside a layer.
/// </summary>
/// <remarks>
/// The positions are the counts of the outer stream elements before the retained scene. The
/// shaders read the retained streams through the scene segment table (see scene_segment.wgsl).
/// </remarks>
internal readonly struct WebGPUSceneInsertion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneInsertion"/> struct.
    /// </summary>
    /// <param name="scene">The retained scene.</param>
    /// <param name="pathTagWord">The number of outer path-tag words before the retained scene.</param>
    /// <param name="pathDataWord">The number of outer path-data words before the retained scene.</param>
    /// <param name="transformWord">The number of outer transform words before the retained scene.</param>
    /// <param name="styleWord">The number of outer style words before the retained scene.</param>
    /// <param name="drawTag">The number of outer draw tags before the retained scene.</param>
    /// <param name="drawDataWord">The number of outer draw-data words before the retained scene.</param>
    public WebGPUSceneInsertion(
        WebGPUDrawingBackendScene scene,
        int pathTagWord,
        int pathDataWord,
        int transformWord,
        int styleWord,
        int drawTag,
        int drawDataWord)
    {
        this.Scene = scene;
        this.PathTagWord = pathTagWord;
        this.PathDataWord = pathDataWord;
        this.TransformWord = transformWord;
        this.StyleWord = styleWord;
        this.DrawTag = drawTag;
        this.DrawDataWord = drawDataWord;
    }

    /// <summary>
    /// Gets the retained scene.
    /// </summary>
    public WebGPUDrawingBackendScene Scene { get; }

    /// <summary>
    /// Gets the number of outer path-tag words before the retained scene.
    /// </summary>
    public int PathTagWord { get; }

    /// <summary>
    /// Gets the number of outer path-data words before the retained scene.
    /// </summary>
    public int PathDataWord { get; }

    /// <summary>
    /// Gets the number of outer transform words before the retained scene.
    /// </summary>
    public int TransformWord { get; }

    /// <summary>
    /// Gets the number of outer style words before the retained scene.
    /// </summary>
    public int StyleWord { get; }

    /// <summary>
    /// Gets the number of outer draw tags before the retained scene.
    /// </summary>
    public int DrawTag { get; }

    /// <summary>
    /// Gets the number of outer draw-data words before the retained scene.
    /// </summary>
    public int DrawDataWord { get; }
}

/// <summary>
/// One retained scene stored as a resident block in the scene buffer of a dispatch.
/// </summary>
/// <remarks>
/// A retained scene drawn several times has one block. The scene data and gradient rows of the
/// blocks follow those of the outer scene. The brush data and atlas rows of the blocks follow the
/// info prefix and atlas rows of each dispatch. Those change from range to range, so the block
/// stores offsets in those block areas.
/// </remarks>
internal readonly struct WebGPUSceneBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneBlock"/> struct.
    /// </summary>
    /// <param name="scene">The retained scene.</param>
    /// <param name="sceneWordOffset">The word offset of the scene data in the scene buffer.</param>
    /// <param name="brushDataOffset">The word offset of the brush data in the brush data of the blocks.</param>
    /// <param name="gradientRowBase">The first gradient-ramp row.</param>
    /// <param name="atlasYOffset">The first image-atlas row in the atlas rows of the blocks.</param>
    public WebGPUSceneBlock(
        WebGPUEncodedScene scene,
        int sceneWordOffset,
        int brushDataOffset,
        int gradientRowBase,
        int atlasYOffset)
    {
        this.Scene = scene;
        this.SceneWordOffset = sceneWordOffset;
        this.BrushDataOffset = brushDataOffset;
        this.GradientRowBase = gradientRowBase;
        this.AtlasYOffset = atlasYOffset;
    }

    /// <summary>
    /// Gets the retained scene.
    /// </summary>
    public WebGPUEncodedScene Scene { get; }

    /// <summary>
    /// Gets the word offset of the scene data in the scene buffer.
    /// </summary>
    public int SceneWordOffset { get; }

    /// <summary>
    /// Gets the word offset of the brush data in the brush data of the blocks.
    /// </summary>
    public int BrushDataOffset { get; }

    /// <summary>
    /// Gets the first gradient-ramp row.
    /// </summary>
    public int GradientRowBase { get; }

    /// <summary>
    /// Gets the first image-atlas row in the atlas rows of the blocks.
    /// </summary>
    public int AtlasYOffset { get; }
}

/// <summary>
/// Flush-scoped encoded scene payload.
/// </summary>
internal sealed class WebGPUEncodedScene : IDisposable
{
    /// <summary>
    /// Gets the word offset of the segment-to-profile table, or zero when absent.
    /// </summary>
    public uint ProfileSlotsBase { get; private set; }

    /// <summary>
    /// Gets the word offset of the profile range and contour-link table, or zero when absent.
    /// </summary>
    public uint ProfileRecordsBase { get; private set; }

    /// <summary>
    /// Sets the two profile-table offsets for the encoded scene.
    /// </summary>
    /// <param name="slotsBase">The word offset of the segment-to-profile table.</param>
    /// <param name="recordsBase">The word offset of the profile range and contour-link table.</param>
    public void SetProfileRegions(uint slotsBase, uint recordsBase)
    {
        this.ProfileSlotsBase = slotsBase;
        this.ProfileRecordsBase = recordsBase;
    }

    /// <summary>
    /// Gets the path-tile bound of a dispatch of the whole scene.
    /// </summary>
    public long EstimatedPathTiles { get; private set; }

    /// <summary>
    /// Gets the blend-spill slot bound of a dispatch of the whole scene.
    /// </summary>
    public long EstimatedBlendSpillSlots { get; private set; }

    /// <summary>
    /// Gets the union region of each clip and layer depth of the scene, starting at depth 1, in
    /// root-target-local pixels. An encoding that reads this scene in place adds these depths to
    /// its own depth at that position.
    /// </summary>
    public Rectangle[] BlendDepthRegions { get; private set; } = [];

    /// <summary>
    /// Sets the scratch bounds that the counts of the scene do not carry.
    /// </summary>
    /// <param name="pathTiles">The path-tile bound.</param>
    /// <param name="blendSpillSlots">The blend-spill slot bound.</param>
    /// <param name="blendDepthRegions">The union region of each clip and layer depth, starting at depth 1.</param>
    public void SetScratchBounds(long pathTiles, long blendSpillSlots, Rectangle[] blendDepthRegions)
    {
        this.EstimatedPathTiles = pathTiles;
        this.EstimatedBlendSpillSlots = blendSpillSlots;
        this.BlendDepthRegions = blendDepthRegions;
    }

    /// <summary>
    /// Gets the row bounds of a dispatch of the whole scene, or <see langword="null"/> when the
    /// scene has no draws outside its ranges.
    /// </summary>
    public WebGPUSceneRowBounds? RowBounds { get; private set; }

    /// <summary>
    /// Gets the blend-spill slot bound of the tiles in a root-target-local row interval.
    /// </summary>
    /// <param name="y0">The top of the interval in pixels.</param>
    /// <param name="y1">The bottom of the interval in pixels.</param>
    /// <returns>The blend-spill slot bound.</returns>
    public long GetBlendSpillSlots(int y0, int y1) => GetBlendSpillSlots(this.BlendDepthRegions, y0, y1);

    /// <summary>
    /// Gets the blend-spill slot bound of the tiles of blend depth regions in a row interval.
    /// </summary>
    /// <param name="regions">The union region of each blend depth in tile-grid pixels, starting at depth 1.</param>
    /// <param name="y0">The top of the interval in pixels.</param>
    /// <param name="y1">The bottom of the interval in pixels.</param>
    /// <returns>The blend-spill slot bound.</returns>
    public static long GetBlendSpillSlots(ReadOnlySpan<Rectangle> regions, int y0, int y1)
    {
        // Only the depths past the blend stack split spill: index i is depth i + 1.
        long slots = 0;
        for (int i = WebGPUSceneEncoder.BlendStackSplit; i < regions.Length; i++)
        {
            Rectangle region = regions[i];
            int top = Math.Max(region.Top, y0);
            int bottom = Math.Min(region.Bottom, y1);
            if (region.Width > 0 && bottom > top)
            {
                long columns = WebGPUSceneEncoder.DivideRoundUp(region.Right, WebGPUSceneEncoder.TileWidth) - (region.Left / WebGPUSceneEncoder.TileWidth);
                long rows = WebGPUSceneEncoder.DivideRoundUp(bottom, WebGPUSceneEncoder.TileHeight) - (top / WebGPUSceneEncoder.TileHeight);
                slots += columns * rows * WebGPUSceneEncoder.TileWidth * WebGPUSceneEncoder.TileHeight;
            }
        }

        return slots;
    }

    /// <summary>
    /// Sets the row bounds of the scene and takes ownership of every row bounds instance that its
    /// encoding created, including those of its ranges. Each instance is sealed for queries.
    /// </summary>
    /// <param name="rowBounds">The row bounds of a dispatch of the whole scene.</param>
    /// <param name="ownedRowBounds">The instances that this scene disposes.</param>
    public void SetRowBounds(WebGPUSceneRowBounds? rowBounds, WebGPUSceneRowBounds[] ownedRowBounds)
    {
        for (int i = 0; i < ownedRowBounds.Length; i++)
        {
            ownedRowBounds[i].Seal();
        }

        this.RowBounds = rowBounds;
        this.ownedRowBounds = ownedRowBounds;
    }

    /// <summary>
    /// Gets the retained scenes that this scene reads in place inside its layers, in stream order.
    /// </summary>
    public ReadOnlySpan<WebGPUSceneInsertion> Insertions => this.insertions;

    /// <summary>
    /// Gets the resident blocks of the retained scenes, in scene-buffer order. A nested retained
    /// scene has its own block.
    /// </summary>
    public ReadOnlySpan<WebGPUSceneBlock> Blocks => this.blocks;

    /// <summary>
    /// Gets the number of entries of the scene segment table of all streams, or zero when the scene
    /// has no such table. An ordered scene has no such table: each of its ranges that reads retained
    /// scenes has a table of its own (see <see cref="WebGPUSceneRangeSegments"/>).
    /// </summary>
    public int SegmentCount { get; private set; }

    /// <summary>
    /// Gets the word offset of the scene segment tables. The tables are the end of <see cref="SceneData"/>.
    /// </summary>
    public int SegmentTableBase { get; private set; }

    /// <summary>
    /// Gets the word length of the scene buffer: <see cref="SceneData"/>, then the resident blocks.
    /// </summary>
    public int SceneBufferWordCount => this.blocks.Length == 0 ? this.SceneWordCount : this.sceneBufferWordCount;

    /// <summary>
    /// Gets the brush-data word count of the resident blocks, which follows the brush data of this scene.
    /// </summary>
    public int BlockBrushDataWordCount { get; private set; }

    /// <summary>
    /// Gets the gradient-ramp row count of the resident blocks, which follows the rows of this scene.
    /// </summary>
    public int BlockGradientRowCount { get; private set; }

    /// <summary>
    /// Records the retained scenes that this scene reads in place, and the scene buffer layout of
    /// their blocks and segment table.
    /// </summary>
    /// <param name="insertions">The retained scenes, in stream order.</param>
    /// <param name="blocks">The resident blocks, in scene-buffer order.</param>
    /// <param name="segmentCount">The number of entries of the table of all streams, or zero for an ordered scene.</param>
    /// <param name="segmentTableBase">The word offset of the segment tables in <see cref="SceneData"/>.</param>
    /// <param name="sceneBufferWordCount">The word length of the scene buffer.</param>
    /// <param name="blockBrushDataWordCount">The brush-data word count of the blocks.</param>
    /// <param name="blockGradientRowCount">The gradient-ramp row count of the blocks.</param>
    public void SetSegments(
        WebGPUSceneInsertion[] insertions,
        WebGPUSceneBlock[] blocks,
        int segmentCount,
        int segmentTableBase,
        int sceneBufferWordCount,
        int blockBrushDataWordCount,
        int blockGradientRowCount)
    {
        this.insertions = insertions;
        this.blocks = blocks;
        this.SegmentCount = segmentCount;
        this.SegmentTableBase = segmentTableBase;
        this.sceneBufferWordCount = sceneBufferWordCount;
        this.BlockBrushDataWordCount = blockBrushDataWordCount;
        this.BlockGradientRowCount = blockGradientRowCount;
    }

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
        0L,
        0L,
        0L,
        []);

    private readonly IMemoryOwner<uint>? sceneDataOwner;
    private readonly IMemoryOwner<uint>? gradientPixelsOwner;
    private readonly IMemoryOwner<uint>? pathGradientDataOwner;
    private readonly List<GpuImageDescriptor> images;
    private readonly List<GpuRecolorDescriptor> recolors;
    private readonly List<GpuSolidColorDescriptor> solidColors;
    private readonly List<GpuGradientRampDescriptor> gradientRamps;
    private readonly WebGPUSceneOperation[] operations;
    private WebGPUSceneInsertion[] insertions = [];
    private WebGPUSceneBlock[] blocks = [];
    private int sceneBufferWordCount;
    private WebGPUSceneRowBounds[] ownedRowBounds = [];
    private bool disposed;

    /// <summary>
    /// The pixel type the solid color words and gradient ramp texels are specialized for, or
    /// <see langword="null"/> while they hold the encoder's continuous values.
    /// </summary>
    private Type? specializedPixelType;

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
    /// <param name="solidColors">The solid color descriptors referenced by draw data.</param>
    /// <param name="gradientRamps">The gradient ramp descriptors referenced by the ramp texture rows.</param>
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
    /// <param name="estimatedTileCrossings">The CPU-side upper bound for tile-boundary crossings.</param>
    /// <param name="estimatedPtclWords">The CPU-side upper bound for the dynamic PTCL words.</param>
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
        List<GpuSolidColorDescriptor> solidColors,
        List<GpuGradientRampDescriptor> gradientRamps,
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
        long estimatedTileCrossings,
        long estimatedPtclWords,
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
        this.solidColors = solidColors;
        this.gradientRamps = gradientRamps;
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
        this.EstimatedTileCrossings = estimatedTileCrossings;
        this.EstimatedPtclWords = estimatedPtclWords;
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
    /// final lines. Bounds the GPU segment, segment-count, and path-tile demand.
    /// </summary>
    public long EstimatedTileCrossings { get; }

    /// <summary>
    /// Gets the CPU-side upper-bound estimate of the dynamic PTCL words the scene's tile crossings
    /// can write: one fill command plus the draw's paint command per crossing.
    /// </summary>
    public long EstimatedPtclWords { get; }

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
        => this.gradientPixelsOwner is null
            ? ReadOnlyMemory<uint>.Empty
            : this.gradientPixelsOwner.Memory[..(this.GradientRowCount * WebGPUSceneEncoder.GradientRowWordCount)];

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
    public int InfoBufferWordCount => this.InfoWordCount + this.PathGradientDataWordCount + this.BlockBrushDataWordCount;

    /// <summary>
    /// Gets the deferred image descriptors that must be patched after atlas creation.
    /// </summary>
    public IReadOnlyList<GpuImageDescriptor> Images => this.images;

    /// <summary>
    /// Gets the deferred recolor descriptors that must be specialized for the target pixel type.
    /// </summary>
    public IReadOnlyList<GpuRecolorDescriptor> Recolors => this.recolors;

    /// <summary>
    /// Gets the deferred solid color descriptors that must be specialized for the target pixel type.
    /// </summary>
    public IReadOnlyList<GpuSolidColorDescriptor> SolidColors => this.solidColors;

    /// <summary>
    /// Gets the deferred gradient ramp descriptors that must be specialized for the target pixel type.
    /// </summary>
    public IReadOnlyList<GpuGradientRampDescriptor> GradientRamps => this.gradientRamps;

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
    /// Gets the path-tag word count that this scene adds to the virtual stream of a scene that reads
    /// it in place: its padded words, or the virtual words of its own segment table when it reads
    /// retained scenes in place.
    /// </summary>
    public int VirtualPathTagWordCount => this.insertions.Length == 0 ? this.PathTagWordCount : this.PathTagByteCount / 4;

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
    /// Rewrites the retained solid color words and gradient ramp texels with values snapped
    /// through <typeparamref name="TPixel"/>, so the shader blends the same sources the CPU
    /// renderer blends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pixel type is unknown until a flush binds the scene to a target, so the snap cannot
    /// happen at encode time. Mutating the retained streams lets the snapped values ride the
    /// scene upload and the ramp texture write that happen anyway; patching them GPU-side would
    /// cost one queue call per solid draw per flush. <see cref="SetSceneWord"/> skips identical
    /// rewrites, so repeated flushes at one pixel type upload nothing new, and a pixel type
    /// change re-uploads only the words that differ.
    /// </para>
    /// <para>
    /// Precision contract: the wire format is associated binary16, so straight-alpha snapped
    /// components multiply by alpha and the product rounds. Opaque values are exempt because
    /// multiplying by one changes nothing and every 8-bit value is exactly representable, so
    /// they blend bit-identically to the CPU. Translucent values carry the product rounding
    /// into the blend, and with output rounding stacked on top a rare pixel can land two 8-bit
    /// steps from the CPU result. Removing that requires straight transport plus an in-shader
    /// premultiply, which for ramps also changes how the sampler interpolates between texels.
    /// </para>
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format of the target.</typeparam>
    public void SpecializeBrushColors<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (this.specializedPixelType == typeof(TPixel))
        {
            return;
        }

        PixelAlphaRepresentation alphaRepresentation = TPixel.GetPixelTypeInfo().AlphaRepresentation;
        this.SpecializeSolidColors<TPixel>(alphaRepresentation);
        this.SpecializeGradientRamps<TPixel>(alphaRepresentation);
        this.specializedPixelType = typeof(TPixel);
    }

    /// <summary>
    /// Rewrites each solid fill's packed draw-data words with the color snapped through
    /// <typeparamref name="TPixel"/>, mirroring the conversion <c>SolidBrush</c> performs when
    /// it creates its CPU renderer.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target.</typeparam>
    /// <param name="alphaRepresentation">The alpha representation of <typeparamref name="TPixel"/>.</param>
    private void SpecializeSolidColors<TPixel>(PixelAlphaRepresentation alphaRepresentation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        for (int i = 0; i < this.solidColors.Count; i++)
        {
            GpuSolidColorDescriptor descriptor = this.solidColors[i];
            Vector4 color = descriptor.Color.ToPixel<TPixel>().ToScaledVector4();
            if (alphaRepresentation != PixelAlphaRepresentation.Associated)
            {
                // Associating rounds the product into binary16; see the precision contract on
                // SpecializeBrushColors.
                float alpha = color.W;
                color *= alpha;
                color.W = alpha;
            }

            int wordIndex = checked((int)this.Layout.DrawDataBase + descriptor.DrawDataWordOffset);
            this.SetSceneWord(wordIndex, WebGPUSceneEncoder.PackHalf2(color.X, color.Y));
            this.SetSceneWord(wordIndex + 1, WebGPUSceneEncoder.PackHalf2(color.Z, color.W));
        }
    }

    /// <summary>
    /// Rewrites each recorded gradient ramp row with texels snapped through
    /// <typeparamref name="TPixel"/>, mirroring the per-pixel conversion the CPU gradient
    /// encoders perform before blending.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target.</typeparam>
    /// <param name="alphaRepresentation">The alpha representation of <typeparamref name="TPixel"/>.</param>
    private void SpecializeGradientRamps<TPixel>(PixelAlphaRepresentation alphaRepresentation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (this.gradientPixelsOwner is null)
        {
            return;
        }

        Span<uint> gradientPixels = this.gradientPixelsOwner.Memory.Span;
        for (int i = 0; i < this.gradientRamps.Count; i++)
        {
            GpuGradientRampDescriptor descriptor = this.gradientRamps[i];
            int rowBase = descriptor.RowIndex * WebGPUSceneEncoder.GradientRowWordCount;
            for (int x = 0; x < WebGPUSceneEncoder.GradientWidth; x++)
            {
                float t = x / (float)(WebGPUSceneEncoder.GradientWidth - 1);
                Vector4 color = WebGPUSceneEncoder.EvaluateGradientColor(descriptor.ColorStops, t);
                if (alphaRepresentation == PixelAlphaRepresentation.Associated)
                {
                    color = TPixel.FromScaledVector4(color).ToScaledVector4();
                }
                else
                {
                    // The CPU's straight-alpha encoder undoes the interpolation-time
                    // premultiplication before converting to TPixel, so the same division
                    // precedes the snap. Re-associating rounds the product into binary16;
                    // see the precision contract on SpecializeBrushColors.
                    float alpha = color.W;
                    if (alpha > 0)
                    {
                        color /= alpha;
                        color.W = alpha;
                    }
                    else
                    {
                        color = Vector4.Zero;
                    }

                    color = TPixel.FromScaledVector4(color).ToScaledVector4();
                    float snappedAlpha = color.W;
                    color *= snappedAlpha;
                    color.W = snappedAlpha;
                }

                gradientPixels[rowBase + (x * 2)] = WebGPUSceneEncoder.PackHalf2(color.X, color.Y);
                gradientPixels[rowBase + (x * 2) + 1] = WebGPUSceneEncoder.PackHalf2(color.Z, color.W);
            }
        }
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
        for (int i = 0; i < this.ownedRowBounds.Length; i++)
        {
            this.ownedRowBounds[i].Dispose();
        }
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
/// Describes one solid brush color whose packed draw-data words must be converted for the
/// render target pixel type.
/// </summary>
internal readonly struct GpuSolidColorDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSolidColorDescriptor"/> struct.
    /// </summary>
    /// <param name="color">The full-precision brush color.</param>
    /// <param name="drawDataWordOffset">The draw-data word offset of the packed color to patch.</param>
    public GpuSolidColorDescriptor(Color color, int drawDataWordOffset)
    {
        this.Color = color;
        this.DrawDataWordOffset = drawDataWordOffset;
    }

    /// <summary>
    /// Gets the full-precision brush color.
    /// </summary>
    public Color Color { get; }

    /// <summary>
    /// Gets the draw-data word offset of the packed color to patch for the render target.
    /// </summary>
    public int DrawDataWordOffset { get; }
}

/// <summary>
/// Describes one gradient ramp row whose texels must be converted for the render target pixel type.
/// </summary>
internal readonly struct GpuGradientRampDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuGradientRampDescriptor"/> struct.
    /// </summary>
    /// <param name="colorStops">The gradient color stops, ordered by ratio.</param>
    /// <param name="rowIndex">The ramp texture row the gradient occupies.</param>
    public GpuGradientRampDescriptor(ColorStop[] colorStops, int rowIndex)
    {
        this.ColorStops = colorStops;
        this.RowIndex = rowIndex;
    }

    /// <summary>
    /// Gets the gradient color stops, ordered by ratio.
    /// </summary>
    public ColorStop[] ColorStops { get; }

    /// <summary>
    /// Gets the ramp texture row the gradient occupies.
    /// </summary>
    public int RowIndex { get; }
}

/// <summary>
/// Identifies one aliased fill that needs profile data in the final GPU scene.
/// </summary>
internal readonly struct GpuProfileFillRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuProfileFillRecord"/> struct.
    /// </summary>
    /// <param name="pathDataOffset">The fill's first path-data word, relative to its partition's path-data stream.</param>
    /// <param name="geometry">The fill's linear geometry.</param>
    /// <param name="profiles">The fill's partition-owned profile view.</param>
    /// <param name="translateX">The X translation from geometry space to absolute target space.</param>
    /// <param name="translateY">The Y translation from geometry space to absolute target space.</param>
    public GpuProfileFillRecord(
        int pathDataOffset,
        LinearGeometry geometry,
        LinearGeometryProfiles profiles,
        float translateX,
        float translateY)
    {
        this.PathDataOffset = pathDataOffset;
        this.Geometry = geometry;
        this.Profiles = profiles;
        this.TranslateX = translateX;
        this.TranslateY = translateY;
    }

    /// <summary>
    /// Gets the fill's first path-data word, relative to its partition's path-data stream.
    /// </summary>
    public int PathDataOffset { get; }

    /// <summary>
    /// Gets the fill's linear geometry.
    /// </summary>
    public LinearGeometry Geometry { get; }

    /// <summary>
    /// Gets the fill's profile data in its partition-owned arena.
    /// </summary>
    public LinearGeometryProfiles Profiles { get; }

    /// <summary>
    /// Gets the X translation from geometry space to absolute target space.
    /// </summary>
    public float TranslateX { get; }

    /// <summary>
    /// Gets the Y translation from geometry space to absolute target space.
    /// </summary>
    public float TranslateY { get; }
}

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
    private IMemoryOwner<T>? owner;
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
    /// Disposes the current owner and clears the stream state. The owner slot is nulled so a
    /// dispose after <see cref="DetachOwner"/> cannot return the transferred owner to the
    /// allocator a second time.
    /// </summary>
    public void Dispose()
    {
        this.owner?.Dispose();
        this.owner = null;
        this.span = default;
        this.Count = 0;
    }

    /// <summary>
    /// Detaches the current owner from the stream without copying. The owner slot is nulled so
    /// a later dispose cannot release the transferred owner.
    /// </summary>
    /// <returns>The detached owner.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<T> DetachOwner()
    {
        IMemoryOwner<T> detached = this.owner!;
        this.owner = null;
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
        this.owner!.Dispose();
        this.owner = next;
        this.span = next.Memory.Span;
    }
}

#pragma warning restore SA1201
