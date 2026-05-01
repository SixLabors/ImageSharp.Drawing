// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Staged scene types are grouped by pipeline role.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Phase 1 shell for the staged WebGPU scene pipeline.
/// </summary>
internal static class WebGPUSceneDispatch
{
    // Fixed bootstrap PTCL reservation per tile. The coarse stage writes each tile's initial
    // command list into this prefix, while dynamic PTCL growth starts after the tileCount * 64 area.
    internal const uint PtclInitialAlloc = 64U;

    private const string PreparePipelineKey = "scene/prepare";
    private const string PathtagReducePipelineKey = "scene/pathtag-reduce";
    private const string PathtagReduce2PipelineKey = "scene/pathtag-reduce2";
    private const string PathtagScan1PipelineKey = "scene/pathtag-scan1";
    private const string PathtagScanPipelineKey = "scene/pathtag-scan";
    private const string PathtagScanSmallPipelineKey = "scene/pathtag-scan-small";
    private const string BboxClearPipelineKey = "scene/bbox-clear";
    private const string FlattenPipelineKey = "scene/flatten";
    private const string DrawReducePipelineKey = "scene/draw-reduce";
    private const string DrawLeafPipelineKey = "scene/draw-leaf";
    private const string ClipReducePipelineKey = "scene/clip-reduce";
    private const string ClipLeafPipelineKey = "scene/clip-leaf";
    private const string BinningPipelineKey = "scene/binning";
    private const string PathRowAllocPipelineKey = "scene/path-row-alloc";
    private const string PathRowSpanPipelineKey = "scene/path-row-span";
    private const string TileAllocPipelineKey = "scene/tile-alloc";
    private const string BackdropPipelineKey = "scene/backdrop";
    private const string PathCountSetupPipelineKey = "scene/path-count-setup";
    private const string PathCountPipelineKey = "scene/path-count";
    private const string CoarsePipelineKey = "scene/coarse";
    private const string PathTilingSetupPipelineKey = "scene/path-tiling-setup";
    private const string PathTilingPipelineKey = "scene/path-tiling";
    private const string FineAreaPipelineKey = "scene/fine-area";
    private const string FineAliasedThresholdPipelineKey = "scene/fine-aliased-threshold";
    private const string ChunkResetPipelineKey = "scene/chunk-reset";

    /// <summary>
    /// Identifies the staged-scene storage binding that exceeded the device limit for one flush attempt.
    /// </summary>
    public enum BindingLimitBuffer
    {
        /// <summary>
        /// No binding-limit failure was reported.
        /// </summary>
        None = 0,

        /// <summary>
        /// The sparse path-row buffer exceeded the maximum bindable storage-buffer size.
        /// </summary>
        PathRows = 1,

        /// <summary>
        /// The path-tiling tile buffer exceeded the maximum bindable storage-buffer size.
        /// </summary>
        PathTiles = 2,

        /// <summary>
        /// The path-tiling segment-count buffer exceeded the maximum bindable storage-buffer size.
        /// </summary>
        SegCounts = 3,

        /// <summary>
        /// The path-tiling segment buffer exceeded the maximum bindable storage-buffer size.
        /// </summary>
        Segments = 4,

        /// <summary>
        /// The coarse-stage blend-spill buffer exceeded the maximum bindable storage-buffer size.
        /// </summary>
        BlendSpill = 5,

        /// <summary>
        /// The coarse/fine PTCL buffer exceeded the maximum bindable storage-buffer size.
        /// </summary>
        Ptcl = 6
    }

    /// <summary>
    /// Describes one binding-limit failure reported while planning a staged scene.
    /// </summary>
    public readonly struct BindingLimitFailure
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BindingLimitFailure"/> struct.
        /// </summary>
        /// <param name="buffer">The staged-scene binding that exceeded the current device limit.</param>
        /// <param name="requiredBytes">The number of bytes required by that binding.</param>
        /// <param name="limitBytes">The maximum number of bytes the current device allows for that binding.</param>
        public BindingLimitFailure(BindingLimitBuffer buffer, nuint requiredBytes, nuint limitBytes)
        {
            this.Buffer = buffer;
            this.RequiredBytes = requiredBytes;
            this.LimitBytes = limitBytes;
        }

        /// <summary>
        /// Gets the empty binding-limit result.
        /// </summary>
        public static BindingLimitFailure None { get; } = new(BindingLimitBuffer.None, 0, 0);

        /// <summary>
        /// Gets the staged-scene binding that exceeded the current device limit.
        /// </summary>
        public BindingLimitBuffer Buffer { get; }

        /// <summary>
        /// Gets the number of bytes required by the binding.
        /// </summary>
        public nuint RequiredBytes { get; }

        /// <summary>
        /// Gets the maximum number of bytes the current device allows for the binding.
        /// </summary>
        public nuint LimitBytes { get; }

        /// <summary>
        /// Gets a value indicating whether one binding exceeded the current device limit.
        /// </summary>
        public bool IsExceeded => this.Buffer != BindingLimitBuffer.None;
    }

    /// <summary>
    /// Builds flush-scoped GPU resources for a retained encoded scene.
    /// </summary>
    public static WebGPUStagedScene CreateStagedScene<TPixel>(
        Configuration configuration,
        NativeCanvasFrame<TPixel> target,
        WebGPUEncodedScene encodedScene,
        TextureFormat textureFormat,
        FeatureName requiredFeature,
        WebGPUSceneBumpSizes bumpSizes,
        ref WebGPUSceneResourceArena? resourceArena)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPUFlushContext flushContext = WebGPUFlushContext.Create(
            target,
            textureFormat,
            requiredFeature,
            configuration.MemoryAllocator);

        try
        {
            WebGPUSceneBumpSizes seededBumpSizes = SeedSceneBumpSizes(bumpSizes, encodedScene);
            WebGPUSceneConfig config = WebGPUSceneConfig.Create(encodedScene, seededBumpSizes);
            uint baseColor = 0U;
            bool chunkingRequired = false;

            if (!TryValidateBindingSizes(encodedScene, config, flushContext.DeviceState.MaxStorageBufferBindingSize, out BindingLimitFailure bindingLimitFailure, out string? error))
            {
                if (!IsChunkableBindingFailure(bindingLimitFailure.Buffer))
                {
                    throw new InvalidOperationException(error ?? "The staged WebGPU scene exceeded the current binding limits.");
                }

                chunkingRequired = true;
            }

            if (encodedScene.FillCount == 0)
            {
                return new WebGPUStagedScene(
                    flushContext,
                    encodedScene,
                    config,
                    default,
                    chunkingRequired ? bindingLimitFailure : BindingLimitFailure.None,
                    ownsEncodedScene: false);
            }

            if (!WebGPUSceneResources.TryCreate<TPixel>(flushContext, encodedScene, config, baseColor, ref resourceArena, out WebGPUSceneResourceSet resources, out error))
            {
                throw new InvalidOperationException(error ?? "Failed to create WebGPU scene resources.");
            }

            return new WebGPUStagedScene(
                flushContext,
                encodedScene,
                config,
                resources,
                chunkingRequired ? bindingLimitFailure : BindingLimitFailure.None,
                ownsEncodedScene: false);
        }
        catch
        {
            flushContext.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Checks whether one encoded scene can be bound to the current WebGPU staged pipeline.
    /// </summary>
    /// <remarks>
    /// This validates the API-level binding ceiling for each individual buffer. It is separate from
    /// the staged pipeline's scratch-capacity checks, which decide whether the current bump sizes are
    /// large enough for this scene.
    /// </remarks>
    /// <param name="encodedScene">The encoded scene whose planned bindings are being checked.</param>
    /// <param name="config">The buffer plan for the current staged-scene attempt.</param>
    /// <param name="maxStorageBufferBindingSize">The device-reported maximum size of one storage-buffer binding.</param>
    /// <param name="bindingLimitFailure">Receives the exact binding-limit failure when one binding is too large.</param>
    /// <param name="error">Receives the validation failure reason when a binding is too large.</param>
    /// <returns><see langword="true"/> when every planned binding fits within the device limit; otherwise, <see langword="false"/>.</returns>
    public static bool TryValidateBindingSizes(
        WebGPUEncodedScene encodedScene,
        WebGPUSceneConfig config,
        nuint maxStorageBufferBindingSize,
        out BindingLimitFailure bindingLimitFailure,
        out string? error)
    {
        bindingLimitFailure = BindingLimitFailure.None;
        WebGPUSceneBufferSizes bufferSizes = config.BufferSizes;
        nuint infoBinDataByteLength = checked(GetBindingByteLength<uint>(encodedScene.InfoBufferWordCount) + config.BufferSizes.BinData.ByteLength + config.BufferSizes.BinHeaders.ByteLength);
        if (!TryValidateBufferSize(GetBindingByteLength<GpuSceneConfig>(1), "scene config", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(GetBindingByteLength<uint>(encodedScene.SceneWordCount), "scene data", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.PathReduced.ByteLength, "path reduced", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.PathReduced2.ByteLength, "path reduced2", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.PathReducedScan.ByteLength, "path reduced scan", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.PathMonoids.ByteLength, "path monoids", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.PathBboxes.ByteLength, "path bboxes", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.DrawReduced.ByteLength, "draw reduced", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.DrawMonoids.ByteLength, "draw monoids", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(infoBinDataByteLength, "scene info/bin data", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.ClipInputs.ByteLength, "clip inputs", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.ClipElements.ByteLength, "clip elements", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.ClipBics.ByteLength, "clip bics", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.ClipBboxes.ByteLength, "clip bboxes", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.DrawBboxes.ByteLength, "draw bboxes", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.Paths.ByteLength, "scene paths", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.Lines.ByteLength, "scene lines", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.BinHeaders.ByteLength, "bin headers", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.IndirectCount.ByteLength, "indirect count", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.PathRows.ByteLength, "path rows", BindingLimitBuffer.PathRows, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.PathTiles.ByteLength, "path tiles", BindingLimitBuffer.PathTiles, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.SegCounts.ByteLength, "segment counts", BindingLimitBuffer.SegCounts, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.Segments.ByteLength, "segments", BindingLimitBuffer.Segments, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.BlendSpill.ByteLength, "blend spill", BindingLimitBuffer.BlendSpill, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.Ptcl.ByteLength, "ptcl", BindingLimitBuffer.Ptcl, maxStorageBufferBindingSize, out bindingLimitFailure, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Checks the encoded-scene stats that must fit inside the staged pipeline's fixed scratch buffers.
    /// </summary>
    /// <remarks>
    /// These are the pipeline's current bump-allocator capacities. Unlike the API binding limit above,
    /// these sizes are expected to become growable state as the robust dynamic-memory path is completed.
    /// </remarks>
    /// <param name="encodedScene">The encoded scene whose aggregate scheduling counts are being checked.</param>
    /// <param name="config">The staged-scene buffer plan that provides the current scratch capacities.</param>
    /// <param name="error">Receives the validation failure reason when a required scratch buffer would overflow.</param>
    /// <returns><see langword="true"/> when the encoded scene fits inside the current scratch capacities; otherwise, <see langword="false"/>.</returns>
    public static bool TryValidateScratchCapacities(
        WebGPUEncodedScene encodedScene,
        WebGPUSceneConfig config,
        out string? error)
    {
        WebGPUSceneBufferSizes bufferSizes = config.BufferSizes;
        uint lineCount = checked((uint)encodedScene.LineCount);
        uint pathRowEstimate = checked((uint)encodedScene.TotalPathRowCount);
        uint pathTileFloor = checked((uint)Math.Max(encodedScene.TotalPathRowCount, encodedScene.PathCount));
        uint initialPtclWords = checked((uint)encodedScene.TileCountX * (uint)encodedScene.TileCountY * PtclInitialAlloc);

        if (lineCount > bufferSizes.Lines.Length)
        {
            error = $"The staged-scene line buffer reserves {bufferSizes.Lines.Length} entries, but this scene encodes {lineCount} lines.";
            return false;
        }

        if (lineCount > bufferSizes.SegCounts.Length)
        {
            error = $"The staged-scene segment-count buffer reserves {bufferSizes.SegCounts.Length} entries, but this scene needs at least {lineCount}.";
            return false;
        }

        if (pathRowEstimate > bufferSizes.PathRows.Length)
        {
            error = $"The staged-scene path-row buffer reserves {bufferSizes.PathRows.Length} entries, but this scene needs at least {pathRowEstimate}.";
            return false;
        }

        if (pathTileFloor > bufferSizes.PathTiles.Length)
        {
            error = $"The staged-scene path-tile buffer reserves {bufferSizes.PathTiles.Length} entries, but this scene needs at least {pathTileFloor} sparse tiles.";
            return false;
        }

        if (initialPtclWords > bufferSizes.Ptcl.Length)
        {
            error = $"The staged-scene PTCL buffer reserves {bufferSizes.Ptcl.Length} words, but the tile-grid bootstrap alone requires {initialPtclWords}.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Dispatches the early Vello-style scheduling stages for one staged scene.
    /// </summary>
    public static bool TryDispatchSchedulingStages(
        ref WebGPUStagedScene stagedScene,
        WebGPUSceneSchedulingArena arena,
        out WebGPUSceneSchedulingResources scheduling,
        out string? error)
    {
        scheduling = default;

        if (!TryDispatchSharedSchedulingStages(ref stagedScene, arena, out error))
        {
            return false;
        }

        return TryDispatchChunkLocalSchedulingStages(ref stagedScene, arena, resetChunkLocalBumpAllocators: false, out scheduling, out error);
    }

    /// <summary>
    /// Records the chunk-invariant scheduling stages that depend only on the full encoded scene and can be reused across all oversized-scene tile windows in the same flush.
    /// </summary>
    /// <param name="stagedScene">The flush-scoped staged scene whose full-scene scheduling state is being prepared for later chunk reuse.</param>
    /// <param name="arena">The reusable scheduling arena that owns the transient buffers written by the shared passes.</param>
    /// <param name="error">Receives the recording or dispatch failure reason when the shared scheduling stages cannot be staged successfully.</param>
    /// <returns><see langword="true"/> when the shared scheduling stages were recorded successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryDispatchSharedSchedulingStages(
        ref WebGPUStagedScene stagedScene,
        WebGPUSceneSchedulingArena arena,
        out string? error)
    {
        WebGPUFlushContext flushContext = stagedScene.FlushContext;
        WebGPUEncodedScene encodedScene = stagedScene.EncodedScene;
        WebGPUSceneConfig config = stagedScene.Config;
        WebGPUSceneBufferSizes bufferSizes = config.BufferSizes;
        WebGPUSceneWorkgroupCounts workgroupCounts = config.WorkgroupCounts;
        nuint sceneBufferSize = GetBindingByteLength<uint>(encodedScene.SceneWordCount);
        nuint pathReducedBufferSize = bufferSizes.PathReduced.ByteLength;
        nuint pathReduced2BufferSize = bufferSizes.PathReduced2.ByteLength;
        nuint pathReducedScanBufferSize = bufferSizes.PathReducedScan.ByteLength;
        nuint pathMonoidBufferSize = bufferSizes.PathMonoids.ByteLength;
        nuint pathBboxBufferSize = bufferSizes.PathBboxes.ByteLength;
        nuint drawReducedBufferSize = bufferSizes.DrawReduced.ByteLength;
        nuint drawMonoidBufferSize = bufferSizes.DrawMonoids.ByteLength;
        nuint infoBinDataBufferSize = checked(GetBindingByteLength<uint>(encodedScene.InfoBufferWordCount) + bufferSizes.BinData.ByteLength + bufferSizes.BinHeaders.ByteLength);
        nuint clipInputBufferSize = bufferSizes.ClipInputs.ByteLength;
        nuint clipElementBufferSize = bufferSizes.ClipElements.ByteLength;
        nuint clipBicBufferSize = bufferSizes.ClipBics.ByteLength;
        nuint clipBboxBufferSize = bufferSizes.ClipBboxes.ByteLength;
        nuint drawBboxBufferSize = bufferSizes.DrawBboxes.ByteLength;
        nuint lineBufferSize = bufferSizes.Lines.ByteLength;
        nuint binHeaderBufferSize = bufferSizes.BinHeaders.ByteLength;
        if (encodedScene.FillCount == 0)
        {
            error = null;
            return true;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged WebGPU scene.";
            return false;
        }

        WgpuBuffer* binHeaderBuffer = arena.BinHeaderBuffer;
        WgpuBuffer* bumpBuffer = arena.BumpBuffer;

        WebGPUSceneResourceRegistry resourceRegistry = WebGPUSceneResourceRegistry.Create(stagedScene.Resources);
        resourceRegistry.RegisterSchedulingBuffers(
            binHeaderBuffer,
            arena.IndirectCountBuffer,
            arena.PathRowBuffer,
            arena.PathTileBuffer,
            arena.SegCountBuffer,
            arena.SegmentBuffer,
            arena.BlendBuffer,
            arena.PtclBuffer,
            bumpBuffer);

        WebGPUSceneComputeRecording recording = new(resourceRegistry);

        if (!TryDispatchPrepare(
                recording,
                stagedScene.Resources.HeaderBuffer,
                bumpBuffer,
                PrepareComputeShader.GetDispatchX(),
                out error))
        {
            return false;
        }

        if (!TryDispatchPathtagReduce(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                pathReducedBufferSize,
                workgroupCounts.PathReduceX,
                out error))
        {
            return false;
        }

        if (workgroupCounts.UseLargePathScan)
        {
            if (!TryDispatchPathtagReduce2(
                    recording,
                    flushContext,
                    stagedScene.Resources,
                    pathReducedBufferSize,
                    pathReduced2BufferSize,
                    workgroupCounts.PathReduce2X,
                    out error))
            {
                return false;
            }

            if (!TryDispatchPathtagScan1(
                    recording,
                    flushContext,
                    stagedScene.Resources,
                    pathReducedBufferSize,
                    pathReduced2BufferSize,
                    pathReducedScanBufferSize,
                    workgroupCounts.PathScan1X,
                    out error))
            {
                return false;
            }

            if (!TryDispatchPathtagScan(
                    recording,
                    flushContext,
                    stagedScene.Resources,
                    sceneBufferSize,
                    pathReducedScanBufferSize,
                    pathMonoidBufferSize,
                    workgroupCounts.PathScanX,
                    useSmallVariant: false,
                    out error))
            {
                return false;
            }
        }
        else if (!TryDispatchPathtagScan(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                pathReducedBufferSize,
                pathMonoidBufferSize,
                workgroupCounts.PathScanX,
                useSmallVariant: true,
                out error))
        {
            return false;
        }

        if (!TryDispatchBboxClear(
                recording,
                flushContext,
                stagedScene.Resources,
                pathBboxBufferSize,
                workgroupCounts.BboxClearX,
                out error))
        {
            return false;
        }

        if (!TryDispatchFlatten(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                pathMonoidBufferSize,
                pathBboxBufferSize,
                bumpBuffer,
                lineBufferSize,
                workgroupCounts.FlattenX,
                out error))
        {
            return false;
        }

        if (!TryDispatchDrawReduce(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                drawReducedBufferSize,
                workgroupCounts.DrawReduceX,
                out error))
        {
            return false;
        }

        if (!TryDispatchDrawLeaf(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                drawReducedBufferSize,
                pathBboxBufferSize,
                drawMonoidBufferSize,
                infoBinDataBufferSize,
                clipInputBufferSize,
                workgroupCounts.DrawLeafX,
                out error))
        {
            return false;
        }

        if (encodedScene.ClipCount > 0)
        {
            if (workgroupCounts.ClipReduceX > 0 &&
                !TryDispatchClipReduce(
                    recording,
                    flushContext,
                    stagedScene.Resources,
                    clipInputBufferSize,
                    pathBboxBufferSize,
                    clipBicBufferSize,
                    clipElementBufferSize,
                    workgroupCounts.ClipReduceX,
                    out error))
            {
                return false;
            }

            if (!TryDispatchClipLeaf(
                    recording,
                    flushContext,
                    stagedScene.Resources,
                    clipInputBufferSize,
                    pathBboxBufferSize,
                    clipBicBufferSize,
                    clipElementBufferSize,
                    drawMonoidBufferSize,
                    clipBboxBufferSize,
                    workgroupCounts.ClipLeafX,
                    out error))
            {
                return false;
            }
        }

        if (!TryDispatchBinning(
                recording,
                flushContext,
                stagedScene.Resources,
                drawMonoidBufferSize,
                pathBboxBufferSize,
                clipBboxBufferSize,
                drawBboxBufferSize,
                infoBinDataBufferSize,
                bumpBuffer,
                workgroupCounts.BinningX,
                workgroupCounts.BinningY,
                out error))
        {
            return false;
        }

        return TryExecuteComputeRecording(flushContext, recording, out error);
    }

    /// <summary>
    /// Records the chunk-local scheduling stages that must be rerun for each oversized-scene tile window after the shared full-scene preparation has populated reusable buffers.
    /// </summary>
    /// <param name="stagedScene">The chunk-scoped staged scene describing the current tile-row window.</param>
    /// <param name="arena">The reusable scheduling arena that owns the transient chunk-local scheduling buffers.</param>
    /// <param name="resetChunkLocalBumpAllocators">Whether the chunk-local allocator counters should be cleared while preserving the shared full-scene scheduling state already stored in the arena.</param>
    /// <param name="scheduling">Receives the transient scheduling resources bound by the later fine and readback stages.</param>
    /// <param name="error">Receives the recording or dispatch failure reason when the chunk-local scheduling stages cannot be staged successfully.</param>
    /// <returns><see langword="true"/> when the chunk-local scheduling stages were recorded successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryDispatchChunkLocalSchedulingStages(
        ref WebGPUStagedScene stagedScene,
        WebGPUSceneSchedulingArena arena,
        bool resetChunkLocalBumpAllocators,
        out WebGPUSceneSchedulingResources scheduling,
        out string? error)
    {
        scheduling = default;

        WebGPUFlushContext flushContext = stagedScene.FlushContext;
        WebGPUEncodedScene encodedScene = stagedScene.EncodedScene;
        WebGPUSceneConfig config = stagedScene.Config;
        WebGPUSceneBufferSizes bufferSizes = config.BufferSizes;
        WebGPUSceneWorkgroupCounts workgroupCounts = config.WorkgroupCounts;
        nuint sceneBufferSize = GetBindingByteLength<uint>(encodedScene.SceneWordCount);
        nuint drawMonoidBufferSize = bufferSizes.DrawMonoids.ByteLength;
        nuint infoBinDataBufferSize = checked(GetBindingByteLength<uint>(encodedScene.InfoBufferWordCount) + bufferSizes.BinData.ByteLength + bufferSizes.BinHeaders.ByteLength);
        nuint drawBboxBufferSize = bufferSizes.DrawBboxes.ByteLength;
        nuint pathBufferSize = bufferSizes.Paths.ByteLength;
        nuint lineBufferSize = bufferSizes.Lines.ByteLength;
        nuint binHeaderBufferSize = bufferSizes.BinHeaders.ByteLength;
        nuint pathRowBufferSize = bufferSizes.PathRows.ByteLength;
        nuint pathTileBufferSize = bufferSizes.PathTiles.ByteLength;
        nuint segCountBufferSize = bufferSizes.SegCounts.ByteLength;
        nuint segmentBufferSize = bufferSizes.Segments.ByteLength;
        nuint ptclBufferSize = bufferSizes.Ptcl.ByteLength;
        if (encodedScene.FillCount == 0)
        {
            error = null;
            return true;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged WebGPU scene.";
            return false;
        }

        WgpuBuffer* binHeaderBuffer = arena.BinHeaderBuffer;
        WgpuBuffer* indirectCountBuffer = arena.IndirectCountBuffer;
        WgpuBuffer* pathRowBuffer = arena.PathRowBuffer;
        WgpuBuffer* pathTileBuffer = arena.PathTileBuffer;
        WgpuBuffer* segCountBuffer = arena.SegCountBuffer;
        WgpuBuffer* segmentBuffer = arena.SegmentBuffer;
        WgpuBuffer* blendBuffer = arena.BlendBuffer;
        WgpuBuffer* ptclBuffer = arena.PtclBuffer;
        WgpuBuffer* bumpBuffer = arena.BumpBuffer;

        WebGPUSceneResourceRegistry resourceRegistry = WebGPUSceneResourceRegistry.Create(stagedScene.Resources);
        resourceRegistry.RegisterSchedulingBuffers(
            binHeaderBuffer,
            indirectCountBuffer,
            pathRowBuffer,
            pathTileBuffer,
            segCountBuffer,
            segmentBuffer,
            blendBuffer,
            ptclBuffer,
            bumpBuffer);

        WebGPUSceneComputeRecording recording = new(resourceRegistry);

        if (resetChunkLocalBumpAllocators &&
            !TryDispatchChunkReset(recording, bumpBuffer, ChunkResetComputeShader.GetDispatchX(), out error))
        {
            return false;
        }

        if (!TryDispatchPathRowAlloc(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                drawBboxBufferSize,
                pathBufferSize,
                pathRowBuffer,
                pathRowBufferSize,
                bumpBuffer,
                workgroupCounts.PathRowAllocX,
                out error))
        {
            return false;
        }

        if (!TryDispatchPathCountSetup(
                recording,
                flushContext,
                bumpBuffer,
                indirectCountBuffer,
                workgroupCounts.PathCountSetupX,
                out error))
        {
            return false;
        }

        if (!TryDispatchPathRowSpan(
                recording,
                flushContext,
                stagedScene.Resources,
                bumpBuffer,
                pathBufferSize,
                pathRowBuffer,
                pathRowBufferSize,
                lineBufferSize,
                indirectCountBuffer,
                out error))
        {
            return false;
        }

        if (!TryDispatchTileAlloc(
                recording,
                flushContext,
                stagedScene.Resources,
                bumpBuffer,
                pathBufferSize,
                pathRowBuffer,
                pathRowBufferSize,
                pathTileBuffer,
                pathTileBufferSize,
                workgroupCounts.TileAllocX,
                out error))
        {
            return false;
        }

        if (!TryDispatchPathCount(
                recording,
                flushContext,
                stagedScene.Resources,
                bumpBuffer,
                pathBufferSize,
                pathRowBuffer,
                pathRowBufferSize,
                pathTileBuffer,
                pathTileBufferSize,
                segCountBuffer,
                segCountBufferSize,
                lineBufferSize,
                indirectCountBuffer,
                out error))
        {
            return false;
        }

        if (!TryDispatchBackdrop(
                recording,
                flushContext,
                stagedScene.Resources,
                bumpBuffer,
                pathBufferSize,
                pathRowBuffer,
                pathRowBufferSize,
                pathTileBuffer,
                pathTileBufferSize,
                workgroupCounts.BackdropX,
                out error))
        {
            return false;
        }

        if (!TryDispatchCoarse(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                drawMonoidBufferSize,
                infoBinDataBufferSize,
                pathBufferSize,
                pathRowBuffer,
                pathRowBufferSize,
                pathTileBuffer,
                pathTileBufferSize,
                ptclBuffer,
                ptclBufferSize,
                bumpBuffer,
                workgroupCounts.CoarseX,
                workgroupCounts.CoarseY,
                out error))
        {
            return false;
        }

        if (!TryDispatchPathTilingSetup(
                recording,
                flushContext,
                stagedScene.Resources.HeaderBuffer,
                bumpBuffer,
                indirectCountBuffer,
                ptclBuffer,
                ptclBufferSize,
                workgroupCounts.PathTilingSetupX,
                out error))
        {
            return false;
        }

        if (!TryDispatchPathTiling(
                recording,
                flushContext,
                stagedScene.Resources,
                bumpBuffer,
                segCountBuffer,
                segCountBufferSize,
                lineBufferSize,
                pathBufferSize,
                pathRowBuffer,
                pathRowBufferSize,
                pathTileBuffer,
                pathTileBufferSize,
                segmentBuffer,
                segmentBufferSize,
                indirectCountBuffer,
                out error))
        {
            return false;
        }

        if (!TryExecuteComputeRecording(flushContext, recording, out error))
        {
            return false;
        }

        scheduling = new WebGPUSceneSchedulingResources(
            binHeaderBuffer,
            indirectCountBuffer,
            pathRowBuffer,
            pathTileBuffer,
            segCountBuffer,
            segmentBuffer,
            blendBuffer,
            ptclBuffer,
            bumpBuffer);
        error = null;
        return true;
    }

    /// <summary>
    /// Reuses the existing scheduling arena if every buffer is large enough for this scene,
    /// otherwise disposes it and creates a new one.
    /// </summary>
    /// <remarks>
    /// The caller passes the rented arena by reference and must return the final value left in
    /// that reference. When this method replaces an undersized arena, it clears the reference
    /// before storing the new arena so the caller never returns a disposed arena to a cache.
    /// </remarks>
    private static WebGPUSceneSchedulingArena EnsureSchedulingArena(
        WebGPUFlushContext flushContext,
        WebGPUSceneBufferSizes bufferSizes,
        nuint readbackByteLength,
        ref WebGPUSceneSchedulingArena? arena)
    {
        if (arena is not null && arena.CanReuse(flushContext, bufferSizes, readbackByteLength))
        {
            return arena;
        }

        WebGPUSceneSchedulingArena.Dispose(arena);
        arena = null;

        arena = CreateSchedulingArena(flushContext, bufferSizes, readbackByteLength);
        return arena;
    }

    private static unsafe WebGPUSceneSchedulingArena CreateSchedulingArena(
        WebGPUFlushContext flushContext,
        WebGPUSceneBufferSizes bufferSizes,
        nuint readbackByteLength)
    {
        WgpuBuffer* binHeaderBuffer = CreateArenaStorageBuffer(flushContext, bufferSizes.BinHeaders.ByteLength);
        WgpuBuffer* indirectCountBuffer = CreateArenaIndirectStorageBuffer(flushContext, bufferSizes.IndirectCount.ByteLength);
        WgpuBuffer* pathRowBuffer = CreateArenaStorageBuffer(flushContext, bufferSizes.PathRows.ByteLength);
        WgpuBuffer* pathTileBuffer = CreateArenaStorageBuffer(flushContext, bufferSizes.PathTiles.ByteLength);
        WgpuBuffer* segCountBuffer = CreateArenaStorageBuffer(flushContext, bufferSizes.SegCounts.ByteLength);
        WgpuBuffer* segmentBuffer = CreateArenaStorageBuffer(flushContext, bufferSizes.Segments.ByteLength);
        WgpuBuffer* blendBuffer = CreateArenaStorageBuffer(flushContext, bufferSizes.BlendSpill.ByteLength);
        WgpuBuffer* ptclBuffer = CreateArenaStorageBuffer(flushContext, bufferSizes.Ptcl.ByteLength);

        GpuSceneBumpAllocators bumpAllocators = default;
        WgpuBuffer* bumpBuffer = CreateAndUploadArenaStorageBuffer(flushContext, in bumpAllocators);

        BufferDescriptor readbackDescriptor = new()
        {
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
            Size = readbackByteLength,
            MappedAtCreation = false
        };

        WgpuBuffer* readbackBuffer;
        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            readbackBuffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in readbackDescriptor);
        }

        return new WebGPUSceneSchedulingArena(
            flushContext.Api,
            flushContext.DeviceHandle,
            bufferSizes,
            readbackByteLength,
            binHeaderBuffer,
            indirectCountBuffer,
            pathRowBuffer,
            pathTileBuffer,
            segCountBuffer,
            segmentBuffer,
            blendBuffer,
            ptclBuffer,
            bumpBuffer,
            readbackBuffer);
    }

    /// <summary>
    /// Executes the staged scene pipeline against the current flush target.
    /// </summary>
    public static unsafe bool TryRenderStagedScene(
        ref WebGPUStagedScene stagedScene,
        ref WebGPUSceneSchedulingArena? schedulingArena,
        out bool requiresGrowth,
        out WebGPUSceneBumpSizes grownBumpSizes,
        out string? error)
    {
        requiresGrowth = false;
        grownBumpSizes = stagedScene.Config.BumpSizes;
        error = null;

        WebGPUEncodedScene encodedScene = stagedScene.EncodedScene;
        if (encodedScene.FillCount == 0)
        {
            return true;
        }

        // Oversized scenes that exceed the device binding limit use tile-row chunking.
        // The chunked path ensures its own arena per chunk with chunk-local sizes.
        if (IsChunkableBindingFailure(stagedScene.BindingLimitFailure.Buffer))
        {
            return TryRenderSegmentChunkedStagedScene(ref stagedScene, ref schedulingArena, out requiresGrowth, out grownBumpSizes, out error);
        }

        // Normal path: ensure arena with full-scene sizes.
        WebGPUSceneSchedulingArena currentArena = EnsureSchedulingArena(
            stagedScene.FlushContext,
            stagedScene.Config.BufferSizes,
            (nuint)sizeof(GpuSceneBumpAllocators),
            ref schedulingArena);

        if (!TryDispatchSchedulingStages(ref stagedScene, currentArena, out WebGPUSceneSchedulingResources scheduling, out error))
        {
            return false;
        }

        WebGPUFlushContext flushContext = stagedScene.FlushContext;
        int targetWidth = encodedScene.TargetSize.Width;
        int targetHeight = encodedScene.TargetSize.Height;

        if (!WebGPUDrawingBackend.TryCreateCompositionTexture(flushContext, targetWidth, targetHeight, out Texture* outputTexture, out TextureView* outputTextureView, out error))
        {
            return false;
        }

        if (!TryDispatchFineArea(
                flushContext,
                stagedScene.Resources,
                encodedScene,
                stagedScene.Config.BufferSizes,
                scheduling,
                outputTextureView,
                (uint)encodedScene.TileCountX,
                (uint)encodedScene.TileCountY,
                out error))
        {
            return false;
        }

        // Single submit: scheduling + fine + readback all in one command encoder.
        // The readback map blocks until the GPU finishes everything.
        if (!TryEnqueueSchedulingStatusReadback(flushContext, scheduling.BumpBuffer, currentArena.ReadbackBuffer, 0, out error) ||
            !WebGPUDrawingBackend.TrySubmit(flushContext) ||
            !TryReadSchedulingStatus(flushContext, currentArena.ReadbackBuffer, out GpuSceneBumpAllocators bumpAllocators, out error))
        {
            return false;
        }

        // Overflow: the fine output is discarded, but the scheduling readback still reports
        // the scratch usage visible to this pass. Later-stage demand can stay hidden until
        // earlier overflows are resolved, so the backend retries with larger buffers until
        // the capacities converge or the bounded attempt budget is exhausted.
        if (RequiresScratchReallocation(in bumpAllocators, stagedScene.Config.BumpSizes))
        {
            requiresGrowth = true;
            grownBumpSizes = GrowBumpSizes(stagedScene.Config.BumpSizes, in bumpAllocators);
            error = "The staged WebGPU scene needs larger scratch buffers and will be retried.";
            return false;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged-scene final copy.";
            return false;
        }

        using (WebGPUHandle.HandleReference targetTextureReference = flushContext.TargetTextureHandle.AcquireReference())
        {
            WebGPUDrawingBackend.CopyTextureRegion(
                flushContext,
                outputTexture,
                0,
                0,
                (Texture*)targetTextureReference.Handle,
                flushContext.TargetBounds.X,
                flushContext.TargetBounds.Y,
                targetWidth,
                targetHeight);
        }

        if (!WebGPUDrawingBackend.TrySubmit(flushContext))
        {
            error = "Failed to submit the staged-scene final copy.";
            return false;
        }

        // Return actual GPU usage so the caller can cache known-good sizes for later renders.
        grownBumpSizes = new WebGPUSceneBumpSizes(
            Math.Max(bumpAllocators.Lines, stagedScene.Config.BumpSizes.Lines),
            Math.Max(bumpAllocators.Binning, stagedScene.Config.BumpSizes.Binning),
            Math.Max(bumpAllocators.PathRows, 1U),
            Math.Max(bumpAllocators.Tile, 1U),
            Math.Max(bumpAllocators.SegCounts, stagedScene.Config.BumpSizes.SegCounts),
            Math.Max(bumpAllocators.Segments, stagedScene.Config.BumpSizes.Segments),
            Math.Max(bumpAllocators.BlendSpill, stagedScene.Config.BumpSizes.BlendSpill),
            Math.Max(bumpAllocators.Ptcl, stagedScene.Config.BumpSizes.Ptcl));

        return true;
    }

    /// <summary>
    /// Executes the oversized-scene path by rerunning the GPU scheduling and fine stages in tile-row chunks.
    /// </summary>
    /// <remarks>
    /// This path only activates when the monolithic segments binding would exceed the device limit.
    /// The CPU-encoded scene stays whole; only the tile-dependent GPU scheduling buffers are reduced
    /// and rerun per chunk so normal scenes keep the existing single-pass fast path.
    /// </remarks>
    /// <param name="stagedScene">The flush-scoped scene whose full-scene segments buffer exceeded the device binding limit.</param>
    /// <param name="schedulingArena">The flush-local reusable scheduling scratch and readback arena.</param>
    /// <param name="requiresGrowth">Receives whether the chunked path needs the caller to retry with larger global scratch capacities.</param>
    /// <param name="grownBumpSizes">Receives the enlarged scratch capacities when <paramref name="requiresGrowth"/> is <see langword="true"/>.</param>
    /// <param name="error">Receives the chunked-render failure reason when the oversized scene cannot be completed.</param>
    /// <returns><see langword="true"/> when every chunk rendered successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryRenderSegmentChunkedStagedScene(
        ref WebGPUStagedScene stagedScene,
        ref WebGPUSceneSchedulingArena? schedulingArena,
        out bool requiresGrowth,
        out WebGPUSceneBumpSizes grownBumpSizes,
        out string? error)
    {
        requiresGrowth = false;
        grownBumpSizes = stagedScene.Config.BumpSizes;
        error = null;

        WebGPUEncodedScene encodedScene = stagedScene.EncodedScene;
        WebGPUFlushContext flushContext = stagedScene.FlushContext;
        int targetWidth = encodedScene.TargetSize.Width;
        int targetHeight = encodedScene.TargetSize.Height;
        nuint maxStorageBufferBindingSize = flushContext.DeviceState.MaxStorageBufferBindingSize;

        if (!WebGPUDrawingBackend.TryCreateCompositionTexture(flushContext, targetWidth, targetHeight, out Texture* outputTexture, out TextureView* outputTextureView, out error))
        {
            return false;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged-scene chunk prefill pass.";
            return false;
        }

        using (WebGPUHandle.HandleReference targetTextureReference = flushContext.TargetTextureHandle.AcquireReference())
        {
            WebGPUDrawingBackend.CopyTextureRegion(
                flushContext,
                (Texture*)targetTextureReference.Handle,
                flushContext.TargetBounds.X,
                flushContext.TargetBounds.Y,
                outputTexture,
                0,
                0,
                targetWidth,
                targetHeight);
        }

        if (!WebGPUDrawingBackend.TrySubmit(flushContext))
        {
            error = "Failed to submit the staged-scene chunk prefill copy.";
            return false;
        }

        uint nextChunkTileHeight = GetInitialChunkTileHeight(encodedScene, stagedScene.BindingLimitFailure);
        uint tileYStart = 0U;
        uint totalTileHeight = checked((uint)encodedScene.TileCountY);

        // Readback buffer holds one BumpAllocators per chunk so we can batch-read after all chunks.
        nuint readbackByteLength = checked(Math.Max(totalTileHeight, 1U) * (uint)sizeof(GpuSceneBumpAllocators));

        // Track each chunk's config sizes and tile heights for the batch readback at the end.
        using IMemoryOwner<WebGPUSceneBumpSizes> chunkBumpSizeOwner = flushContext.MemoryAllocator.Allocate<WebGPUSceneBumpSizes>(checked((int)Math.Max(totalTileHeight, 1U)));
        using IMemoryOwner<uint> chunkTileHeightOwner = flushContext.MemoryAllocator.Allocate<uint>(checked((int)Math.Max(totalTileHeight, 1U)));
        Span<WebGPUSceneBumpSizes> chunkAttemptBumpSizes = chunkBumpSizeOwner.Memory.Span;
        Span<uint> chunkAttemptTileHeights = chunkTileHeightOwner.Memory.Span;

        int chunkReadbackCount = 0;
        bool sharedSchedulingPrepared = false;

        // Outer loop: advance through tile rows. Each iteration renders one chunk.
        while (tileYStart < totalTileHeight)
        {
            uint remainingTileHeight = totalTileHeight - tileYStart;
            uint requestedTileHeight = Math.Min(nextChunkTileHeight, remainingTileHeight);

            // Scale the full-scene bump sizes down proportionally for this chunk's tile height.
            WebGPUSceneBumpSizes chunkBumpSize = ScaleChunkBumpSizes(stagedScene.Config.BumpSizes, encodedScene, requestedTileHeight);

            // Inner loop: shrink the chunk if its buffers still exceed the device binding limit.
            while (true)
            {
                WebGPUSceneChunkWindow chunkWindow = CreateChunkWindow(tileYStart, requestedTileHeight, remainingTileHeight);
                WebGPUSceneConfig chunkConfig = WebGPUSceneConfig.Create(encodedScene, chunkBumpSize, chunkWindow);
                if (!TryValidateBindingSizes(encodedScene, chunkConfig, maxStorageBufferBindingSize, out BindingLimitFailure bindingLimitFailure, out error))
                {
                    if (IsChunkableBindingFailure(bindingLimitFailure.Buffer))
                    {
                        uint smallerTileHeight = ShrinkChunkTileHeight(requestedTileHeight, remainingTileHeight, bindingLimitFailure);
                        if (smallerTileHeight >= requestedTileHeight)
                        {
                            return false;
                        }

                        requestedTileHeight = smallerTileHeight;
                        chunkBumpSize = ScaleChunkBumpSizes(stagedScene.Config.BumpSizes, encodedScene, requestedTileHeight);
                        continue;
                    }

                    return false;
                }

                // Chunk fits. Re-ensure the arena for this chunk's buffer sizes (may be
                // different from the previous chunk if the scene has non-uniform density).
                WebGPUStagedScene chunkScene = new(flushContext, encodedScene, chunkConfig, stagedScene.Resources, BindingLimitFailure.None);
                WebGPUSceneSchedulingArena currentArena = EnsureSchedulingArena(flushContext, chunkConfig.BufferSizes, readbackByteLength, ref schedulingArena);

                bool useSharedSchedulingState = sharedSchedulingPrepared || chunkWindow.TileHeight < totalTileHeight;
                if (!sharedSchedulingPrepared && useSharedSchedulingState)
                {
                    if (!TryDispatchSharedSchedulingStages(ref stagedScene, currentArena, out error))
                    {
                        return false;
                    }

                    if (!WebGPUDrawingBackend.TrySubmit(flushContext))
                    {
                        error = "Failed to submit the staged-scene shared chunk scheduling passes.";
                        return false;
                    }

                    sharedSchedulingPrepared = true;
                }

                // Render this chunk: scheduling + fine into the shared output texture.
                // Readback is deferred - each chunk copies its bump status into a different
                // offset in the readback buffer. All chunks are checked in one batch below.
                bool recordedChunk = useSharedSchedulingState
                    ? TryRecordChunkAttempt(
                        ref chunkScene,
                        currentArena,
                        outputTextureView,
                        (nuint)chunkReadbackCount * (nuint)sizeof(GpuSceneBumpAllocators),
                        chunkReadbackCount > 0,
                        out error)
                    : TryRenderChunkAttempt(
                        ref chunkScene,
                        currentArena,
                        outputTextureView,
                        (nuint)chunkReadbackCount * (nuint)sizeof(GpuSceneBumpAllocators),
                        useSharedSchedulingState,
                        resetChunkLocalBumpAllocators: false,
                        out error);
                if (recordedChunk)
                {
                    chunkAttemptBumpSizes[chunkReadbackCount++] = chunkConfig.BumpSizes;
                    chunkAttemptTileHeights[chunkReadbackCount - 1] = chunkWindow.TileHeight;
                    tileYStart += chunkWindow.TileHeight;
                    nextChunkTileHeight = requestedTileHeight;
                    break;
                }
            }
        }

        if (sharedSchedulingPrepared && !WebGPUDrawingBackend.TrySubmit(flushContext))
        {
            error = "Failed to submit the staged-scene chunk passes.";
            return false;
        }

        // All chunks submitted. Map the readback buffer once and check every chunk's
        // bump allocators. If any overflowed, grow sizes and the outer retry loop re-runs.
        if (!TryReadChunkSchedulingStatuses(
                flushContext,
                schedulingArena,
                chunkReadbackCount,
                chunkAttemptBumpSizes,
                chunkAttemptTileHeights,
                totalTileHeight,
                out requiresGrowth,
                out grownBumpSizes,
                out error))
        {
            return false;
        }

        if (requiresGrowth)
        {
            error = "One or more staged WebGPU scene chunks need larger scratch buffers and will be retried.";
            return false;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged-scene final copy.";
            return false;
        }

        using (WebGPUHandle.HandleReference targetTextureReference = flushContext.TargetTextureHandle.AcquireReference())
        {
            WebGPUDrawingBackend.CopyTextureRegion(
                flushContext,
                outputTexture,
                0,
                0,
                (Texture*)targetTextureReference.Handle,
                flushContext.TargetBounds.X,
                flushContext.TargetBounds.Y,
                targetWidth,
                targetHeight);
        }

        return WebGPUDrawingBackend.TrySubmit(flushContext);
    }

    /// <summary>
    /// Executes one chunk attempt by uploading the chunk header, running scheduling, and fine-rendering into the shared output texture.
    /// </summary>
    /// <param name="stagedScene">The chunk-scoped staged scene describing the tile-row window being rendered.</param>
    /// <param name="schedulingArena">The flush-local reusable scheduling scratch and readback arena.</param>
    /// <param name="outputTextureView">The shared output texture view receiving fine-pass results for every chunk.</param>
    /// <param name="readbackOffset">The byte offset inside the shared readback buffer where this chunk should copy its bump allocators.</param>
    /// <param name="reuseSharedSchedulingState">Whether this chunk should reuse the flush-scoped shared scheduling results instead of rerunning the full scheduling pipeline.</param>
    /// <param name="resetChunkLocalBumpAllocators">Whether the chunk-local bump counters should be cleared while preserving the shared full-scene scheduling state.</param>
    /// <param name="error">Receives the chunk failure reason when scheduling or fine dispatch cannot complete.</param>
    /// <returns><see langword="true"/> when this chunk rendered successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryRenderChunkAttempt(
        ref WebGPUStagedScene stagedScene,
        WebGPUSceneSchedulingArena schedulingArena,
        TextureView* outputTextureView,
        nuint readbackOffset,
        bool reuseSharedSchedulingState,
        bool resetChunkLocalBumpAllocators,
        out string? error)
    {
        error = null;
        WebGPUSceneSchedulingResources scheduling;

        // Upload the chunk-specific config header (tile window, buffer sizes).
        if (!TryWriteSceneHeader(stagedScene.FlushContext, stagedScene.Resources.HeaderBuffer, WebGPUSceneResources.CreateHeader(stagedScene.EncodedScene, stagedScene.Config, 0U), out error))
        {
            return false;
        }

        if (reuseSharedSchedulingState)
        {
            if (!TryDispatchChunkLocalSchedulingStages(ref stagedScene, schedulingArena, resetChunkLocalBumpAllocators, out WebGPUSceneSchedulingResources sharedScheduling, out error))
            {
                return false;
            }

            scheduling = sharedScheduling;
        }
        else if (!TryDispatchSchedulingStages(ref stagedScene, schedulingArena, out scheduling, out error))
        {
            return false;
        }

        if (!TryDispatchFineArea(
                stagedScene.FlushContext,
                stagedScene.Resources,
                stagedScene.EncodedScene,
                stagedScene.Config.BufferSizes,
                scheduling,
                outputTextureView,
                (uint)stagedScene.EncodedScene.TileCountX,
                stagedScene.Config.ChunkWindow.TileHeight,
                out error))
        {
            return false;
        }

        if (!TryEnqueueSchedulingStatusReadback(stagedScene.FlushContext, scheduling.BumpBuffer, schedulingArena.ReadbackBuffer, readbackOffset, out error) ||
            !WebGPUDrawingBackend.TrySubmit(stagedScene.FlushContext))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Records one chunk attempt into the current command encoder by copying a dedicated chunk header, replaying the chunk-local scheduling stages, and appending the scheduling-status readback copy without submitting yet.
    /// </summary>
    /// <param name="stagedScene">The chunk-scoped staged scene describing the tile-row window being recorded.</param>
    /// <param name="schedulingArena">The flush-local reusable scheduling scratch and readback arena.</param>
    /// <param name="outputTextureView">The shared output texture view receiving fine-pass results for this chunk.</param>
    /// <param name="readbackOffset">The byte offset inside the shared readback buffer where this chunk should copy its bump allocators.</param>
    /// <param name="resetChunkLocalBumpAllocators">Whether the chunk-local allocator counters should be cleared while preserving the shared full-scene scheduling state.</param>
    /// <param name="error">Receives the chunk failure reason when scheduling, fine dispatch, or readback staging cannot complete.</param>
    /// <returns><see langword="true"/> when this chunk was recorded successfully into the active command encoder; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryRecordChunkAttempt(
        ref WebGPUStagedScene stagedScene,
        WebGPUSceneSchedulingArena schedulingArena,
        TextureView* outputTextureView,
        nuint readbackOffset,
        bool resetChunkLocalBumpAllocators,
        out string? error)
    {
        error = null;

        GpuSceneConfig header = WebGPUSceneResources.CreateHeader(stagedScene.EncodedScene, stagedScene.Config, 0U);
        if (!stagedScene.FlushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged-scene chunk pass.";
            return false;
        }

        WgpuBuffer* headerSourceBuffer = CreateAndUploadCopySourceBuffer(stagedScene.FlushContext, in header);

        if (!TryEnqueueSceneHeaderCopy(stagedScene.FlushContext, headerSourceBuffer, stagedScene.Resources.HeaderBuffer, out error) ||
            !TryDispatchChunkLocalSchedulingStages(ref stagedScene, schedulingArena, resetChunkLocalBumpAllocators, out WebGPUSceneSchedulingResources scheduling, out error))
        {
            return false;
        }

        if (!TryDispatchFineArea(
                stagedScene.FlushContext,
                stagedScene.Resources,
                stagedScene.EncodedScene,
                stagedScene.Config.BufferSizes,
                scheduling,
                outputTextureView,
                (uint)stagedScene.EncodedScene.TileCountX,
                stagedScene.Config.ChunkWindow.TileHeight,
                out error))
        {
            return false;
        }

        return TryEnqueueSchedulingStatusReadback(stagedScene.FlushContext, scheduling.BumpBuffer, schedulingArena.ReadbackBuffer, readbackOffset, out error);
    }

    /// <summary>
    /// Uploads one staged-scene config header into the shared header buffer before a chunked render attempt.
    /// </summary>
    /// <param name="flushContext">The flush context that owns the queue used to upload the updated header.</param>
    /// <param name="headerBuffer">The shared root config buffer bound at slot zero by most staged-scene shaders.</param>
    /// <param name="header">The chunk-specific config header to upload.</param>
    /// <param name="error">Receives the upload failure reason when the header cannot be staged.</param>
    /// <returns><see langword="true"/> when the header upload completed; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryWriteSceneHeader(
        WebGPUFlushContext flushContext,
        WgpuBuffer* headerBuffer,
        GpuSceneConfig header,
        out string? error)
    {
        nuint headerSize = (nuint)sizeof(GpuSceneConfig);
        using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
        flushContext.Api.QueueWriteBuffer((Queue*)queueReference.Handle, headerBuffer, 0, &header, headerSize);
        error = null;
        return true;
    }

    /// <summary>
    /// Records one copy from a per-chunk header upload buffer into the shared staged-scene header buffer so subsequent chunk-local passes see the correct tile window.
    /// </summary>
    /// <param name="flushContext">The flush context that owns the active command encoder receiving the copy command.</param>
    /// <param name="sourceBuffer">The flush-scoped buffer containing the already uploaded chunk header data.</param>
    /// <param name="destinationBuffer">The shared staged-scene header buffer consumed by subsequent chunk-local passes.</param>
    /// <param name="error">Receives the recording failure reason when the copy cannot be appended to the active command encoder.</param>
    /// <returns><see langword="true"/> when the header copy was recorded successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryEnqueueSceneHeaderCopy(
        WebGPUFlushContext flushContext,
        WgpuBuffer* sourceBuffer,
        WgpuBuffer* destinationBuffer,
        out string? error)
    {
        flushContext.EndComputePassIfOpen();
        if (flushContext.CommandEncoder is null)
        {
            error = "Failed to record the staged-scene chunk header copy because no command encoder is active.";
            return false;
        }

        flushContext.Api.CommandEncoderCopyBufferToBuffer(
            flushContext.CommandEncoder,
            sourceBuffer,
            0,
            destinationBuffer,
            0,
            (nuint)sizeof(GpuSceneConfig));
        error = null;
        return true;
    }

    /// <summary>
    /// Resets the shared scheduling bump-allocator buffer so one chunk attempt does not inherit
    /// the previous chunk's failure state through the prepare shader's previous-run path.
    /// </summary>
    private static unsafe bool TryResetSceneBumpAllocators(
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        out string? error)
    {
        GpuSceneBumpAllocators reset = default;
        using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
        flushContext.Api.QueueWriteBuffer((Queue*)queueReference.Handle, bumpBuffer, 0, &reset, (nuint)sizeof(GpuSceneBumpAllocators));
        error = null;
        return true;
    }

    /// <summary>
    /// Chooses the first chunk height from the exact full-scene chunkable binding overflow.
    /// </summary>
    /// <param name="scene">The encoded scene whose full-scene chunkable binding exceeded the device limit.</param>
    /// <param name="bindingLimitFailure">The exact chunk-local binding overflow reported during full-scene planning.</param>
    /// <returns>The initial tile-row chunk height to try for the oversized scene.</returns>
    private static uint GetInitialChunkTileHeight(WebGPUEncodedScene scene, BindingLimitFailure bindingLimitFailure)
    {
        uint fullTileHeight = checked((uint)scene.TileCountY);
        if (!IsChunkableBindingFailure(bindingLimitFailure.Buffer) || bindingLimitFailure.RequiredBytes == 0)
        {
            return fullTileHeight;
        }

        ulong usableBytes = Math.Max(bindingLimitFailure.LimitBytes - (bindingLimitFailure.LimitBytes / 8UL), 1UL);
        uint estimatedTileHeight = checked((uint)Math.Max(1UL, (usableBytes * fullTileHeight) / bindingLimitFailure.RequiredBytes));
        return AlignChunkTileHeight(Math.Min(estimatedTileHeight, fullTileHeight), fullTileHeight);
    }

    /// <summary>
    /// Shrinks the current chunk height after an exact chunkable binding-limit failure.
    /// </summary>
    /// <param name="currentTileHeight">The chunk height that just overflowed the device binding limit.</param>
    /// <param name="remainingTileHeight">The number of tile rows still left to render in the oversized scene.</param>
    /// <param name="bindingLimitFailure">The exact chunk-local binding-limit failure reported for the overflowing chunk.</param>
    /// <returns>A smaller tile-row chunk height to retry for the same scene region.</returns>
    private static uint ShrinkChunkTileHeight(uint currentTileHeight, uint remainingTileHeight, BindingLimitFailure bindingLimitFailure)
    {
        if (currentTileHeight <= 1U || bindingLimitFailure.RequiredBytes == 0)
        {
            return currentTileHeight;
        }

        ulong usableBytes = Math.Max(bindingLimitFailure.LimitBytes - (bindingLimitFailure.LimitBytes / 8UL), 1UL);
        uint estimatedTileHeight = checked((uint)Math.Max(1UL, (usableBytes * currentTileHeight) / bindingLimitFailure.RequiredBytes));
        uint alignedTileHeight = AlignChunkTileHeight(Math.Min(estimatedTileHeight, remainingTileHeight), remainingTileHeight);
        if (alignedTileHeight < currentTileHeight)
        {
            return alignedTileHeight;
        }

        // Chunk starts must remain 16-row aligned because coarse indexes the full-scene
        // bin grid from chunk_tile_y_start / N_TILE_Y. A sub-bin start would read the
        // wrong bin headers for that chunk and drop coverage.
        return currentTileHeight > 16U ? (currentTileHeight - 1U) & ~15U : currentTileHeight;
    }

    /// <summary>
    /// Creates one tile-row chunk window, keeping the scratch tile buffers bin-aligned while the fine pass renders only the real rows.
    /// </summary>
    /// <param name="tileYStart">The first global tile row covered by the chunk.</param>
    /// <param name="requestedTileHeight">The preferred number of tile rows to render in this chunk.</param>
    /// <param name="remainingTileHeight">The number of scene tile rows still left to render.</param>
    /// <returns>The chunk window describing the real rows and the aligned scratch-buffer height.</returns>
    private static WebGPUSceneChunkWindow CreateChunkWindow(uint tileYStart, uint requestedTileHeight, uint remainingTileHeight)
    {
        uint tileHeight = Math.Min(requestedTileHeight, remainingTileHeight);
        uint tileBufferHeight = AlignUp(tileHeight, 16U);
        return new WebGPUSceneChunkWindow(tileYStart, tileHeight, tileBufferHeight);
    }

    /// <summary>
    /// Scales the chunk-local scheduling capacities from the last known-good full-scene budget.
    /// </summary>
    /// <param name="sourceBumpSizes">The full-scene scratch budget used as the source for chunk-local sizing.</param>
    /// <param name="scene">The encoded scene whose aggregate tile and line counts provide lower bounds.</param>
    /// <param name="chunkTileHeight">The real chunk height, in tile rows.</param>
    /// <returns>The chunk-local scratch capacities to use for this tile-row window.</returns>
    private static WebGPUSceneBumpSizes ScaleChunkBumpSizes(WebGPUSceneBumpSizes sourceBumpSizes, WebGPUEncodedScene scene, uint chunkTileHeight)
    {
        uint fullTileHeight = checked((uint)scene.TileCountY);
        uint pathRowFloor = ScaleCount((uint)Math.Max(scene.TotalPathRowCount, scene.PathCount), chunkTileHeight, fullTileHeight);
        uint pathTileFloor = pathRowFloor;
        uint segmentFloor = AddSizingSlack(ScaleCount((uint)scene.LineCount, chunkTileHeight, fullTileHeight));

        return new WebGPUSceneBumpSizes(
            sourceBumpSizes.Lines,
            sourceBumpSizes.Binning,
            Math.Max(ScaleCount(sourceBumpSizes.PathRows, chunkTileHeight, fullTileHeight), pathRowFloor),
            Math.Max(ScaleCount(sourceBumpSizes.PathTiles, chunkTileHeight, fullTileHeight), pathTileFloor),
            Math.Max(ScaleCount(sourceBumpSizes.SegCounts, chunkTileHeight, fullTileHeight), segmentFloor),
            Math.Max(ScaleCount(sourceBumpSizes.Segments, chunkTileHeight, fullTileHeight), segmentFloor),
            ScaleCount(sourceBumpSizes.BlendSpill, chunkTileHeight, fullTileHeight),
            ScaleCount(sourceBumpSizes.Ptcl, chunkTileHeight, fullTileHeight));
    }

    /// <summary>
    /// Raises the caller-provided scratch capacities to the scene-specific lower bounds already known on the CPU so the first GPU attempt does not waste a full-scene pass on allocations that cannot possibly fit.
    /// </summary>
    /// <param name="currentSizes">The persisted scratch capacities carried into this flush.</param>
    /// <param name="scene">The encoded scene whose line count and path-tile estimate provide guaranteed minimum capacities.</param>
    /// <returns>The retained capacities raised to the scene's known CPU-side lower bounds.</returns>
    private static WebGPUSceneBumpSizes SeedSceneBumpSizes(WebGPUSceneBumpSizes currentSizes, WebGPUEncodedScene scene)
    {
        uint lineFloor = AddSizingSlack(checked((uint)Math.Max(scene.LineCount, 1)));
        uint pathRowFloor = checked((uint)Math.Max(scene.TotalPathRowCount, scene.PathCount));
        uint pathTileFloor = pathRowFloor;

        return new WebGPUSceneBumpSizes(
            Math.Max(currentSizes.Lines, lineFloor),
            currentSizes.Binning,
            Math.Max(currentSizes.PathRows, pathRowFloor),
            Math.Max(currentSizes.PathTiles, pathTileFloor),
            Math.Max(currentSizes.SegCounts, lineFloor),
            Math.Max(currentSizes.Segments, lineFloor),
            currentSizes.BlendSpill,
            currentSizes.Ptcl);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsChunkableBindingFailure(BindingLimitBuffer buffer)
        => buffer is BindingLimitBuffer.PathRows or BindingLimitBuffer.PathTiles or BindingLimitBuffer.SegCounts or BindingLimitBuffer.Segments or BindingLimitBuffer.BlendSpill or BindingLimitBuffer.Ptcl;

    /// <summary>
    /// Scales one count by the chunk's tile-height ratio while keeping the result non-zero.
    /// </summary>
    /// <param name="value">The full-scene count to scale.</param>
    /// <param name="numerator">The chunk-local tile height used as the scale numerator.</param>
    /// <param name="denominator">The full-scene tile height used as the scale denominator.</param>
    /// <returns>The scaled non-zero count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ScaleCount(uint value, uint numerator, uint denominator)
        => Math.Max(1U, checked((uint)Math.Max(1UL, ((ulong)Math.Max(value, 1U) * numerator) / Math.Max(denominator, 1U))));

    /// <summary>
    /// Aligns a chunk height to one coarse bin (16 tile rows) so the next chunk's tile-y start stays bin-aligned.
    /// </summary>
    /// <param name="tileHeight">The candidate real chunk height, in tile rows.</param>
    /// <param name="maximumTileHeight">The maximum tile height allowed for this chunk.</param>
    /// <returns>The aligned chunk height, preserving short tail chunks only when there are no further rows to render.</returns>
    /// <remarks>
    /// <para>
    /// Coarse rasterization reads the global binning grid using <c>chunk_tile_y_start / N_TILE_Y</c> and lays its
    /// per-bin tiles out starting at <c>chunk_tile_y_start</c>. If a chunk's height is not a multiple of
    /// <c>N_TILE_Y</c> (16), the next chunk's <c>chunk_tile_y_start</c> ends up bin-misaligned and the coarse
    /// dispatch reads the wrong bin's content for the misaligned rows, dropping coverage in those rows.
    /// </para>
    /// <para>
    /// This method therefore rounds non-final chunks up to the next multiple of 16 (the smallest legal chunk size
    /// that preserves bin alignment) and only permits a sub-bin tail when <paramref name="maximumTileHeight"/>
    /// itself is a sub-bin remainder; this is the final chunk and there are no further rows to render.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignChunkTileHeight(uint tileHeight, uint maximumTileHeight)
    {
        if (tileHeight >= maximumTileHeight)
        {
            return maximumTileHeight;
        }

        // Round up to a full bin row so the next chunk starts on a bin boundary.
        uint alignedTileHeight = AlignUp(Math.Max(tileHeight, 1U), 16U);
        return alignedTileHeight >= maximumTileHeight ? maximumTileHeight : alignedTileHeight;
    }

    /// <summary>
    /// Adds the same sizing slack used elsewhere in staged-scene planning so chunk-local floors do not sit on the edge.
    /// </summary>
    /// <param name="required">The required count before slack and alignment are applied.</param>
    /// <returns>The required count plus slack, aligned to the next 1024-element boundary.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AddSizingSlack(uint required)
        => AlignUp(checked(required + Math.Max(required / 8U, 1024U)), 1024U);

    /// <summary>
    /// Rounds one unsigned count up to the next multiple of the requested alignment.
    /// </summary>
    /// <param name="value">The value to align.</param>
    /// <param name="alignment">The required power-of-two alignment.</param>
    /// <returns><paramref name="value"/> rounded up to the next aligned boundary.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignUp(uint value, uint alignment)
        => value + (uint)(-(int)value & (alignment - 1U));

    /// <summary>
    /// Determines whether the scheduling stages reported scratch usage beyond the current capacities.
    /// </summary>
    /// <param name="bumpAllocators">The bump allocator counters read back from the GPU.</param>
    /// <param name="currentSizes">The capacities used for the current attempt.</param>
    /// <returns><see langword="true"/> when the flush should be retried with larger scratch buffers; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RequiresScratchReallocation(in GpuSceneBumpAllocators bumpAllocators, WebGPUSceneBumpSizes currentSizes)
        => bumpAllocators.Failed != 0 ||
           bumpAllocators.Binning > currentSizes.Binning ||
           bumpAllocators.Ptcl > currentSizes.Ptcl ||
           bumpAllocators.PathRows > currentSizes.PathRows ||
           bumpAllocators.Tile > currentSizes.PathTiles ||
           bumpAllocators.SegCounts > currentSizes.SegCounts ||
           bumpAllocators.Segments > currentSizes.Segments ||
           bumpAllocators.BlendSpill > currentSizes.BlendSpill ||
           bumpAllocators.Lines > currentSizes.Lines;

    /// <summary>
    /// Produces the next scratch-capacity budget from the usage reported by the GPU scheduling stages.
    /// </summary>
    /// <param name="currentSizes">The capacities used for the current attempt.</param>
    /// <param name="bumpAllocators">The bump allocator counters read back from the GPU.</param>
    /// <returns>The enlarged capacities to use for the next retry.</returns>
    private static WebGPUSceneBumpSizes GrowBumpSizes(WebGPUSceneBumpSizes currentSizes, in GpuSceneBumpAllocators bumpAllocators)
    {
        WebGPUSceneBumpSizes grownSizes = new(
            GrowBumpSize(currentSizes.Lines, bumpAllocators.Lines),
            GrowBumpSize(currentSizes.Binning, bumpAllocators.Binning),
            GrowBumpSize(currentSizes.PathRows, bumpAllocators.PathRows),
            GrowBumpSize(currentSizes.PathTiles, bumpAllocators.Tile),
            GrowBumpSize(currentSizes.SegCounts, bumpAllocators.SegCounts),
            GrowBumpSize(currentSizes.Segments, bumpAllocators.Segments),
            GrowBumpSize(currentSizes.BlendSpill, bumpAllocators.BlendSpill),
            GrowBumpSize(currentSizes.Ptcl, bumpAllocators.Ptcl));

        if (bumpAllocators.Failed == 0 || !AreBumpSizesEqual(grownSizes, currentSizes))
        {
            return grownSizes;
        }

        return new WebGPUSceneBumpSizes(
            ForceGrowBumpSize(currentSizes.Lines),
            ForceGrowBumpSize(currentSizes.Binning),
            ForceGrowBumpSize(currentSizes.PathRows),
            ForceGrowBumpSize(currentSizes.PathTiles),
            ForceGrowBumpSize(currentSizes.SegCounts),
            ForceGrowBumpSize(currentSizes.Segments),
            ForceGrowBumpSize(currentSizes.BlendSpill),
            ForceGrowBumpSize(currentSizes.Ptcl));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static WebGPUSceneBumpSizes MaxBumpSizes(WebGPUSceneBumpSizes left, WebGPUSceneBumpSizes right)
        => new(
            Math.Max(left.Lines, right.Lines),
            Math.Max(left.Binning, right.Binning),
            Math.Max(left.PathRows, right.PathRows),
            Math.Max(left.PathTiles, right.PathTiles),
            Math.Max(left.SegCounts, right.SegCounts),
            Math.Max(left.Segments, right.Segments),
            Math.Max(left.BlendSpill, right.BlendSpill),
            Math.Max(left.Ptcl, right.Ptcl));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreBumpSizesEqual(WebGPUSceneBumpSizes left, WebGPUSceneBumpSizes right)
        => left.Lines == right.Lines &&
           left.Binning == right.Binning &&
           left.PathRows == right.PathRows &&
           left.PathTiles == right.PathTiles &&
           left.SegCounts == right.SegCounts &&
           left.Segments == right.Segments &&
           left.BlendSpill == right.BlendSpill &&
           left.Ptcl == right.Ptcl;

    /// <summary>
    /// Grows one scratch-capacity counter enough to cover the reported usage with a larger retry margin.
    /// </summary>
    /// <param name="currentSize">The capacity used for the current attempt.</param>
    /// <param name="requiredSize">The usage reported by the GPU for that allocator.</param>
    /// <returns>The retained or enlarged capacity for the next retry.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GrowBumpSize(uint currentSize, uint requiredSize)
    {
        if (requiredSize <= currentSize)
        {
            return currentSize;
        }

        // Use the GPU-reported size doubled so one retry is always enough.
        return checked(requiredSize * 2U);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ForceGrowBumpSize(uint currentSize)
        => checked(currentSize + Math.Max(currentSize / 2U, 4096U));

    /// <summary>
    /// Records the first pathtag reduction pass that collapses raw path tags into workgroup monoids.
    /// </summary>
    private static unsafe bool TryDispatchPathtagReduce(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint pathReducedBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[3];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.SceneBuffer, sceneBufferSize);
        entries[2] = CreateBufferBinding(2, resources.PathReducedBuffer, pathReducedBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.PathtagReduce, entries, 3, dispatchX, 1, 1, out error);
    }

    /// <summary>
    /// Records the second pathtag reduction pass used by the large-scan variant.
    /// </summary>
    private static unsafe bool TryDispatchPathtagReduce2(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint pathReducedBufferSize,
        nuint pathReduced2BufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = CreateBufferBinding(0, resources.PathReducedBuffer, pathReducedBufferSize);
        entries[1] = CreateBufferBinding(1, resources.PathReduced2Buffer, pathReduced2BufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.PathtagReduce2, entries, 2, dispatchX, 1, 1, out error);
    }

    /// <summary>
    /// Records the prefix-scan setup pass used by the large pathtag scan path.
    /// </summary>
    private static unsafe bool TryDispatchPathtagScan1(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint pathReducedBufferSize,
        nuint pathReduced2BufferSize,
        nuint pathReducedScanBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[3];
        entries[0] = CreateBufferBinding(0, resources.PathReducedBuffer, pathReducedBufferSize);
        entries[1] = CreateBufferBinding(1, resources.PathReduced2Buffer, pathReduced2BufferSize);
        entries[2] = CreateBufferBinding(2, resources.PathReducedScanBuffer, pathReducedScanBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.PathtagScan1, entries, 3, dispatchX, 1, 1, out error);
    }

    /// <summary>
    /// Records the final pathtag prefix-scan pass, selecting the small or large shader variant.
    /// </summary>
    private static unsafe bool TryDispatchPathtagScan(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint parentBufferSize,
        nuint pathMonoidBufferSize,
        uint dispatchX,
        bool useSmallVariant,
        out string? error)
    {
        WgpuBuffer* parentBuffer = useSmallVariant ? resources.PathReducedBuffer : resources.PathReducedScanBuffer;

        BindGroupEntry* entries = stackalloc BindGroupEntry[4];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.SceneBuffer, sceneBufferSize);
        entries[2] = CreateBufferBinding(2, parentBuffer, parentBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathMonoidBuffer, pathMonoidBufferSize);

        return recording.TryRecord(useSmallVariant ? WebGPUSceneShaderId.PathtagScanSmall : WebGPUSceneShaderId.PathtagScan, entries, 4, dispatchX, 1, 1, out error);
    }

    private static unsafe bool TryDispatchBboxClear(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint pathBboxBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.PathBboxBuffer, pathBboxBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.BboxClear, entries, 2, dispatchX, 1, 1, out error);
    }

    private static unsafe bool TryDispatchFlatten(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint pathMonoidBufferSize,
        nuint pathBboxBufferSize,
        WgpuBuffer* bumpBuffer,
        nuint lineBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[6];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.SceneBuffer, sceneBufferSize);
        entries[2] = CreateBufferBinding(2, resources.PathMonoidBuffer, pathMonoidBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathBboxBuffer, pathBboxBufferSize);
        entries[4] = CreateBufferBinding(4, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[5] = CreateBufferBinding(5, resources.LineBuffer, lineBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.Flatten, entries, 6, dispatchX, 1, 1, out error);
    }

    private static unsafe bool TryDispatchDrawReduce(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint drawReducedBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[3];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.SceneBuffer, sceneBufferSize);
        entries[2] = CreateBufferBinding(2, resources.DrawReducedBuffer, drawReducedBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.DrawReduce, entries, 3, dispatchX, 1, 1, out error);
    }

    private static unsafe bool TryDispatchDrawLeaf(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint drawReducedBufferSize,
        nuint pathBboxBufferSize,
        nuint drawMonoidBufferSize,
        nuint infoBinDataBufferSize,
        nuint clipInputBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[7];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.SceneBuffer, sceneBufferSize);
        entries[2] = CreateBufferBinding(2, resources.DrawReducedBuffer, drawReducedBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathBboxBuffer, pathBboxBufferSize);
        entries[4] = CreateBufferBinding(4, resources.DrawMonoidBuffer, drawMonoidBufferSize);
        entries[5] = CreateBufferBinding(5, resources.InfoBinDataBuffer, infoBinDataBufferSize);
        entries[6] = CreateBufferBinding(6, resources.ClipInputBuffer, clipInputBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.DrawLeaf, entries, 7, dispatchX, 1, 1, out error);
    }

    private static unsafe bool TryDispatchClipReduce(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint clipInputBufferSize,
        nuint pathBboxBufferSize,
        nuint clipBicBufferSize,
        nuint clipElementBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[4];
        entries[0] = CreateBufferBinding(0, resources.ClipInputBuffer, clipInputBufferSize);
        entries[1] = CreateBufferBinding(1, resources.PathBboxBuffer, pathBboxBufferSize);
        entries[2] = CreateBufferBinding(2, resources.ClipBicBuffer, clipBicBufferSize);
        entries[3] = CreateBufferBinding(3, resources.ClipElementBuffer, clipElementBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.ClipReduce, entries, 4, dispatchX, 1, 1, out error);
    }

    private static unsafe bool TryDispatchClipLeaf(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint clipInputBufferSize,
        nuint pathBboxBufferSize,
        nuint clipBicBufferSize,
        nuint clipElementBufferSize,
        nuint drawMonoidBufferSize,
        nuint clipBboxBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[7];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.ClipInputBuffer, clipInputBufferSize);
        entries[2] = CreateBufferBinding(2, resources.PathBboxBuffer, pathBboxBufferSize);
        entries[3] = CreateBufferBinding(3, resources.ClipBicBuffer, clipBicBufferSize);
        entries[4] = CreateBufferBinding(4, resources.ClipElementBuffer, clipElementBufferSize);
        entries[5] = CreateBufferBinding(5, resources.DrawMonoidBuffer, drawMonoidBufferSize);
        entries[6] = CreateBufferBinding(6, resources.ClipBboxBuffer, clipBboxBufferSize);

        return recording.TryRecord(WebGPUSceneShaderId.ClipLeaf, entries, 7, dispatchX, 1, 1, out error);
    }

    private static unsafe bool TryDispatchBinning(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint drawMonoidBufferSize,
        nuint pathBboxBufferSize,
        nuint clipBboxBufferSize,
        nuint drawBboxBufferSize,
        nuint infoBinDataBufferSize,
        WgpuBuffer* bumpBuffer,
        uint dispatchX,
        uint dispatchY,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[7];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.DrawMonoidBuffer, drawMonoidBufferSize);
        entries[2] = CreateBufferBinding(2, resources.PathBboxBuffer, pathBboxBufferSize);
        entries[3] = CreateBufferBinding(3, resources.ClipBboxBuffer, clipBboxBufferSize);
        entries[4] = CreateBufferBinding(4, resources.DrawBboxBuffer, drawBboxBufferSize);
        entries[5] = CreateBufferBinding(5, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[6] = CreateBufferBinding(6, resources.InfoBinDataBuffer, infoBinDataBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.Binning, entries, 7, dispatchX, dispatchY, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Appends a copy of the scheduling bump allocators into the flush-local readback buffer after the current compute work.
    /// </summary>
    /// <param name="flushContext">The flush context that owns the active command encoder.</param>
    /// <param name="bumpBuffer">The scheduling bump allocator buffer to copy from.</param>
    /// <param name="readbackBuffer">The flush-local readback buffer receiving the copied allocator state.</param>
    /// <param name="destinationOffset">The byte offset inside <paramref name="readbackBuffer"/> for this copy.</param>
    /// <param name="error">Receives the copy-recording failure reason.</param>
    private static unsafe bool TryEnqueueSchedulingStatusReadback(
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* readbackBuffer,
        nuint destinationOffset,
        out string? error)
    {
        flushContext.EndComputePassIfOpen();
        if (flushContext.CommandEncoder is null)
        {
            error = "Failed to record the staged-scene scheduling readback copy because no command encoder is active.";
            return false;
        }

        flushContext.Api.CommandEncoderCopyBufferToBuffer(
            flushContext.CommandEncoder,
            bumpBuffer,
            0,
            readbackBuffer,
            destinationOffset,
            (nuint)sizeof(GpuSceneBumpAllocators));
        error = null;
        return true;
    }

    /// <summary>
    /// Maps the already-copied scheduling status from the flush-local readback buffer.
    /// </summary>
    private static unsafe bool TryReadSchedulingStatus(
        WebGPUFlushContext flushContext,
        WgpuBuffer* readbackBuffer,
        out GpuSceneBumpAllocators bumpAllocators,
        out string? error)
    {
        bumpAllocators = default;

        BufferMapAsyncStatus mapStatus = BufferMapAsyncStatus.Unknown;
        using ManualResetEventSlim mapReady = new(false);

        void Callback(BufferMapAsyncStatus status, void* userData)
        {
            _ = userData;
            mapStatus = status;
            mapReady.Set();
        }

        using PfnBufferMapCallback callback = PfnBufferMapCallback.From(Callback);
        flushContext.Api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, (nuint)sizeof(GpuSceneBumpAllocators), callback, null);
        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            if (!WaitForMapSignal(flushContext.WgpuExtension, (Device*)deviceReference.Handle, mapReady) || mapStatus != BufferMapAsyncStatus.Success)
            {
                error = $"Failed to map staged-scene scheduling status with status '{mapStatus}'.";
                return false;
            }
        }

        void* mapped = flushContext.Api.BufferGetConstMappedRange(readbackBuffer, 0, (nuint)sizeof(GpuSceneBumpAllocators));
        if (mapped is null)
        {
            flushContext.Api.BufferUnmap(readbackBuffer);
            error = "Failed to map the staged-scene scheduling status range.";
            return false;
        }

        try
        {
            bumpAllocators = Unsafe.Read<GpuSceneBumpAllocators>(mapped);
            error = null;
            return true;
        }
        finally
        {
            flushContext.Api.BufferUnmap(readbackBuffer);
        }
    }

    /// <summary>
    /// Maps the chunked scheduling-status buffer once, then aggregates the retry budget required by all chunk submissions.
    /// </summary>
    private static unsafe bool TryReadChunkSchedulingStatuses(
        WebGPUFlushContext flushContext,
        WebGPUSceneSchedulingArena? arena,
        int chunkCount,
        ReadOnlySpan<WebGPUSceneBumpSizes> chunkBumpSizes,
        ReadOnlySpan<uint> chunkTileHeights,
        uint fullTileHeight,
        out bool requiresGrowth,
        out WebGPUSceneBumpSizes grownBumpSizes,
        out string? error)
    {
        requiresGrowth = false;
        grownBumpSizes = default;

        if (arena is null)
        {
            error = "The staging arena was unexpectedly null when reading back chunk scheduling statuses.";
            return false;
        }

        WgpuBuffer* readbackBuffer = arena.ReadbackBuffer;

        if (chunkCount == 0)
        {
            error = null;
            return true;
        }

        BufferMapAsyncStatus mapStatus = BufferMapAsyncStatus.Unknown;
        using ManualResetEventSlim mapReady = new(false);

        void Callback(BufferMapAsyncStatus status, void* userData)
        {
            _ = userData;
            mapStatus = status;
            mapReady.Set();
        }

        nuint mappedByteLength = checked((nuint)chunkCount * (nuint)sizeof(GpuSceneBumpAllocators));
        using PfnBufferMapCallback callback = PfnBufferMapCallback.From(Callback);
        flushContext.Api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, mappedByteLength, callback, null);
        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            if (!WaitForMapSignal(flushContext.WgpuExtension, (Device*)deviceReference.Handle, mapReady) || mapStatus != BufferMapAsyncStatus.Success)
            {
                error = $"Failed to map staged-scene chunk scheduling status with status '{mapStatus}'.";
                return false;
            }
        }

        void* mapped = flushContext.Api.BufferGetConstMappedRange(readbackBuffer, 0, mappedByteLength);
        if (mapped is null)
        {
            flushContext.Api.BufferUnmap(readbackBuffer);
            error = "Failed to map the staged-scene chunk scheduling status range.";
            return false;
        }

        try
        {
            GpuSceneBumpAllocators* statuses = (GpuSceneBumpAllocators*)mapped;
            for (int i = 0; i < chunkCount; i++)
            {
                GpuSceneBumpAllocators bumpAllocators = statuses[i];
                WebGPUSceneBumpSizes currentSizes = chunkBumpSizes[i];
                WebGPUSceneBumpSizes chunkActuals = new(
                    Math.Max(bumpAllocators.Lines, currentSizes.Lines),
                    Math.Max(bumpAllocators.Binning, currentSizes.Binning),
                    Math.Max(bumpAllocators.PathRows, 1U),
                    Math.Max(bumpAllocators.Tile, 1U),
                    Math.Max(bumpAllocators.SegCounts, currentSizes.SegCounts),
                    Math.Max(bumpAllocators.Segments, currentSizes.Segments),
                    Math.Max(bumpAllocators.BlendSpill, currentSizes.BlendSpill),
                    Math.Max(bumpAllocators.Ptcl, currentSizes.Ptcl));
                WebGPUSceneBumpSizes expandedActuals = ExpandChunkBumpSizesToSceneBudget(chunkActuals, fullTileHeight, chunkTileHeights[i]);
                grownBumpSizes = MaxBumpSizes(grownBumpSizes, expandedActuals);

                if (!RequiresScratchReallocation(in bumpAllocators, currentSizes))
                {
                    continue;
                }

                WebGPUSceneBumpSizes grownChunkBumpSizes = GrowBumpSizes(currentSizes, in bumpAllocators);
                WebGPUSceneBumpSizes grownSourceBumpSizes = ExpandChunkBumpSizesToSceneBudget(grownChunkBumpSizes, fullTileHeight, chunkTileHeights[i]);
                grownBumpSizes = MaxBumpSizes(grownBumpSizes, grownSourceBumpSizes);
                requiresGrowth = true;
            }

            error = null;
            return true;
        }
        finally
        {
            flushContext.Api.BufferUnmap(readbackBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static WebGPUSceneBumpSizes ExpandChunkBumpSizesToSceneBudget(WebGPUSceneBumpSizes chunkSizes, uint fullTileHeight, uint chunkTileHeight)
        => new(
            chunkSizes.Lines,
            chunkSizes.Binning,
            ScaleChunkRequirementToSceneBudget(chunkSizes.PathRows, fullTileHeight, chunkTileHeight),
            ScaleChunkRequirementToSceneBudget(chunkSizes.PathTiles, fullTileHeight, chunkTileHeight),
            ScaleChunkRequirementToSceneBudget(chunkSizes.SegCounts, fullTileHeight, chunkTileHeight),
            ScaleChunkRequirementToSceneBudget(chunkSizes.Segments, fullTileHeight, chunkTileHeight),
            ScaleChunkRequirementToSceneBudget(chunkSizes.BlendSpill, fullTileHeight, chunkTileHeight),
            ScaleChunkRequirementToSceneBudget(chunkSizes.Ptcl, fullTileHeight, chunkTileHeight));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ScaleChunkRequirementToSceneBudget(uint chunkRequired, uint fullTileHeight, uint chunkTileHeight)
    {
        ulong numerator = (ulong)Math.Max(chunkRequired, 1U) * Math.Max(fullTileHeight, 1U);
        ulong denominator = Math.Max(chunkTileHeight, 1U);
        return checked((uint)Math.Max(1UL, (numerator + denominator - 1UL) / denominator));
    }

    /// <summary>
    /// Records the sparse path-row allocation stage that reserves one row descriptor per active tile row in each path.
    /// </summary>
    /// <param name="recording">The compute recording that receives the staged dispatch.</param>
    /// <param name="flushContext">The flush context that owns the current device.</param>
    /// <param name="resources">The staged-scene resource set that provides the scene, draw-bbox, and path buffers.</param>
    /// <param name="sceneBufferSize">The byte length of the packed scene buffer binding.</param>
    /// <param name="drawBboxBufferSize">The byte length of the draw-bbox buffer binding.</param>
    /// <param name="pathBufferSize">The byte length of the per-path buffer binding.</param>
    /// <param name="pathRowBuffer">The sparse path-row buffer to populate.</param>
    /// <param name="pathRowBufferSize">The byte length of the sparse path-row buffer binding.</param>
    /// <param name="bumpBuffer">The scheduling bump-allocator buffer.</param>
    /// <param name="dispatchX">The X workgroup count for the stage.</param>
    /// <param name="error">Receives the recording failure reason when the dispatch cannot be staged.</param>
    /// <returns><see langword="true"/> when the dispatch was recorded successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryDispatchPathRowAlloc(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint drawBboxBufferSize,
        nuint pathBufferSize,
        WgpuBuffer* pathRowBuffer,
        nuint pathRowBufferSize,
        WgpuBuffer* bumpBuffer,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[6];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.SceneBuffer, sceneBufferSize);
        entries[2] = CreateBufferBinding(2, resources.DrawBboxBuffer, drawBboxBufferSize);
        entries[3] = CreateBufferBinding(3, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[4] = CreateBufferBinding(4, resources.PathBuffer, pathBufferSize);
        entries[5] = CreateBufferBinding(5, pathRowBuffer, pathRowBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.PathRowAlloc, entries, 6, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Records the line-driven pass that discovers each sparse row's active x span.
    /// </summary>
    /// <param name="recording">The compute recording that receives the staged dispatch.</param>
    /// <param name="flushContext">The flush context that owns the current device.</param>
    /// <param name="resources">The staged-scene resource set that provides the line and path buffers.</param>
    /// <param name="bumpBuffer">The scheduling bump-allocator buffer.</param>
    /// <param name="pathBufferSize">The byte length of the per-path buffer binding.</param>
    /// <param name="pathRowBuffer">The sparse path-row buffer to update.</param>
    /// <param name="pathRowBufferSize">The byte length of the sparse path-row buffer binding.</param>
    /// <param name="lineBufferSize">The byte length of the flattened line buffer binding.</param>
    /// <param name="indirectCountBuffer">The indirect-dispatch argument buffer seeded from the line count.</param>
    /// <param name="error">Receives the recording failure reason when the dispatch cannot be staged.</param>
    /// <returns><see langword="true"/> when the dispatch was recorded successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryDispatchPathRowSpan(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        WgpuBuffer* bumpBuffer,
        nuint pathBufferSize,
        WgpuBuffer* pathRowBuffer,
        nuint pathRowBufferSize,
        nuint lineBufferSize,
        WgpuBuffer* indirectCountBuffer,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[5];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateBufferBinding(2, resources.LineBuffer, lineBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathBuffer, pathBufferSize);
        entries[4] = CreateBufferBinding(4, pathRowBuffer, pathRowBufferSize);

        if (!recording.TryRecordIndirect(WebGPUSceneShaderId.PathRowSpan, entries, 5, indirectCountBuffer, 0, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchTileAlloc(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        WgpuBuffer* bumpBuffer,
        nuint pathBufferSize,
        WgpuBuffer* pathRowBuffer,
        nuint pathRowBufferSize,
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[5];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateBufferBinding(2, resources.PathBuffer, pathBufferSize);
        entries[3] = CreateBufferBinding(3, pathRowBuffer, pathRowBufferSize);
        entries[4] = CreateBufferBinding(4, pathTileBuffer, pathTileBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.TileAlloc, entries, 5, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchBackdrop(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        WgpuBuffer* bumpBuffer,
        nuint pathBufferSize,
        WgpuBuffer* pathRowBuffer,
        nuint pathRowBufferSize,
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[5];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateBufferBinding(2, resources.PathBuffer, pathBufferSize);
        entries[3] = CreateBufferBinding(3, pathRowBuffer, pathRowBufferSize);
        entries[4] = CreateBufferBinding(4, pathTileBuffer, pathTileBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.Backdrop, entries, 5, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchPathCountSetup(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* indirectCountBuffer,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = CreateBufferBinding(0, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = CreateBufferBinding(1, indirectCountBuffer, (nuint)sizeof(GpuSceneIndirectCount));

        if (!recording.TryRecord(WebGPUSceneShaderId.PathCountSetup, entries, 2, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Records the one-workgroup prepare stage that resets bump counters and can cancel the
    /// remaining scheduling pipeline when the prior run already proved the current scratch
    /// capacities are too small.
    /// </summary>
    /// <param name="recording">The flush-scoped compute recording that receives the staged dispatch.</param>
    /// <param name="headerBuffer">The scene config buffer shared by all staged-scene passes.</param>
    /// <param name="bumpBuffer">The scratch bump allocator buffer that tracks dynamic scheduling usage.</param>
    /// <param name="dispatchX">The X workgroup count for the prepare stage.</param>
    /// <param name="error">Receives the recording failure reason when the dispatch cannot be staged.</param>
    /// <returns><see langword="true"/> when the prepare dispatch was recorded successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryDispatchPrepare(
        WebGPUSceneComputeRecording recording,
        WgpuBuffer* headerBuffer,
        WgpuBuffer* bumpBuffer,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = CreateBufferBinding(0, headerBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));

        if (!recording.TryRecord(WebGPUSceneShaderId.Prepare, entries, 2, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Records the one-workgroup reset pass that keeps the shared full-scene bump counters while clearing the chunk-local allocators between oversized-scene tile windows.
    /// </summary>
    /// <param name="recording">The flush-scoped compute recording that receives the reset dispatch.</param>
    /// <param name="bumpBuffer">The scheduling bump-allocator buffer whose shared counters must be preserved while chunk-local counters are cleared.</param>
    /// <param name="dispatchX">The X workgroup count for the reset pass.</param>
    /// <param name="error">Receives the recording failure reason when the reset dispatch cannot be staged.</param>
    /// <returns><see langword="true"/> when the reset dispatch was recorded successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryDispatchChunkReset(
        WebGPUSceneComputeRecording recording,
        WgpuBuffer* bumpBuffer,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[1];
        entries[0] = CreateBufferBinding(0, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));

        if (!recording.TryRecord(WebGPUSceneShaderId.ChunkReset, entries, 1, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchPathCount(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        WgpuBuffer* bumpBuffer,
        nuint pathBufferSize,
        WgpuBuffer* pathRowBuffer,
        nuint pathRowBufferSize,
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        WgpuBuffer* segCountBuffer,
        nuint segCountBufferSize,
        nuint lineBufferSize,
        WgpuBuffer* indirectCountBuffer,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[7];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateBufferBinding(2, resources.LineBuffer, lineBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathBuffer, pathBufferSize);
        entries[4] = CreateBufferBinding(4, pathRowBuffer, pathRowBufferSize);
        entries[5] = CreateBufferBinding(5, pathTileBuffer, pathTileBufferSize);
        entries[6] = CreateBufferBinding(6, segCountBuffer, segCountBufferSize);

        if (!recording.TryRecordIndirect(WebGPUSceneShaderId.PathCount, entries, 7, indirectCountBuffer, 0, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchCoarse(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint drawMonoidBufferSize,
        nuint infoBinDataBufferSize,
        nuint pathBufferSize,
        WgpuBuffer* pathRowBuffer,
        nuint pathRowBufferSize,
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        WgpuBuffer* ptclBuffer,
        nuint ptclBufferSize,
        WgpuBuffer* bumpBuffer,
        uint dispatchX,
        uint dispatchY,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[9];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.SceneBuffer, sceneBufferSize);
        entries[2] = CreateBufferBinding(2, resources.DrawMonoidBuffer, drawMonoidBufferSize);
        entries[3] = CreateBufferBinding(3, resources.InfoBinDataBuffer, infoBinDataBufferSize);
        entries[4] = CreateBufferBinding(4, resources.PathBuffer, pathBufferSize);
        entries[5] = CreateBufferBinding(5, pathRowBuffer, pathRowBufferSize);
        entries[6] = CreateBufferBinding(6, pathTileBuffer, pathTileBufferSize);
        entries[7] = CreateBufferBinding(7, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[8] = CreateBufferBinding(8, ptclBuffer, ptclBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.Coarse, entries, 9, dispatchX, dispatchY, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchPathTilingSetup(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WgpuBuffer* headerBuffer,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* indirectCountBuffer,
        WgpuBuffer* ptclBuffer,
        nuint ptclBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[4];
        entries[0] = CreateBufferBinding(0, headerBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateBufferBinding(2, indirectCountBuffer, (nuint)sizeof(GpuSceneIndirectCount));
        entries[3] = CreateBufferBinding(3, ptclBuffer, ptclBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.PathTilingSetup, entries, 4, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchPathTiling(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* segCountBuffer,
        nuint segCountBufferSize,
        nuint lineBufferSize,
        nuint pathBufferSize,
        WgpuBuffer* pathRowBuffer,
        nuint pathRowBufferSize,
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        WgpuBuffer* segmentBuffer,
        nuint segmentBufferSize,
        WgpuBuffer* indirectCountBuffer,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[7];
        entries[0] = CreateBufferBinding(0, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = CreateBufferBinding(1, segCountBuffer, segCountBufferSize);
        entries[2] = CreateBufferBinding(2, resources.LineBuffer, lineBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathBuffer, pathBufferSize);
        entries[4] = CreateBufferBinding(4, pathRowBuffer, pathRowBufferSize);
        entries[5] = CreateBufferBinding(5, pathTileBuffer, pathTileBufferSize);
        entries[6] = CreateBufferBinding(6, segmentBuffer, segmentBufferSize);

        if (!recording.TryRecordIndirect(WebGPUSceneShaderId.PathTiling, entries, 7, indirectCountBuffer, 0, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static unsafe bool TryDispatchFineArea(
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        WebGPUEncodedScene encodedScene,
        WebGPUSceneBufferSizes bufferSizes,
        WebGPUSceneSchedulingResources scheduling,
        TextureView* outputTextureView,
        uint groupCountX,
        uint groupCountY,
        out string? error)
    {
        bool useAliasedThreshold = encodedScene.FineRasterizationMode == RasterizationMode.Aliased;
        byte[] shaderCode = useAliasedThreshold
            ? FineAliasedThresholdComputeShader.GetCode(flushContext.TextureFormat)
            : FineAreaComputeShader.GetCode(flushContext.TextureFormat);

        bool LayoutFactory(WebGPU api, Device* device, out BindGroupLayout* layout, out string? layoutError)
            => useAliasedThreshold
                ? FineAliasedThresholdComputeShader.TryCreateBindGroupLayout(
                    api,
                    device,
                    flushContext.TextureFormat,
                    out layout,
                    out layoutError)
                : FineAreaComputeShader.TryCreateBindGroupLayout(
                    api,
                    device,
                    flushContext.TextureFormat,
                    out layout,
                    out layoutError);

        if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                $"{(useAliasedThreshold ? FineAliasedThresholdPipelineKey : FineAreaPipelineKey)}/{flushContext.TextureFormat}",
                shaderCode,
                useAliasedThreshold ? FineAliasedThresholdComputeShader.EntryPoint : FineAreaComputeShader.EntryPoint,
                LayoutFactory,
                out BindGroupLayout* bindGroupLayout,
                out ComputePipeline* pipeline,
                out error))
        {
            return false;
        }

        BindGroupEntry* entries = stackalloc BindGroupEntry[9];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, scheduling.SegmentBuffer, bufferSizes.Segments.ByteLength);
        entries[2] = CreateBufferBinding(2, scheduling.PtclBuffer, bufferSizes.Ptcl.ByteLength);
        entries[3] = CreateBufferBinding(3, resources.InfoBinDataBuffer, checked(GetBindingByteLength<uint>(encodedScene.InfoBufferWordCount) + bufferSizes.BinData.ByteLength + bufferSizes.BinHeaders.ByteLength));
        entries[4] = CreateBufferBinding(4, scheduling.BlendBuffer, bufferSizes.BlendSpill.ByteLength);
        entries[5] = new BindGroupEntry { Binding = 5, TextureView = outputTextureView };
        entries[6] = new BindGroupEntry { Binding = 6, TextureView = resources.GradientTextureView };
        entries[7] = new BindGroupEntry { Binding = 7, TextureView = resources.ImageAtlasTextureView };

        using WebGPUHandle.HandleReference targetViewReference = flushContext.TargetTextureViewHandle.AcquireReference();
        entries[8] = new BindGroupEntry { Binding = 8, TextureView = (TextureView*)targetViewReference.Handle };

        if (!TryDispatchComputePass(flushContext, bindGroupLayout, pipeline, entries, 9, groupCountX, groupCountY, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates a bind group and dispatches one direct compute pass immediately.
    /// </summary>
    internal static unsafe bool TryDispatchComputePass(
        WebGPUFlushContext flushContext,
        BindGroupLayout* bindGroupLayout,
        ComputePipeline* pipeline,
        BindGroupEntry* entries,
        uint entryCount,
        uint groupCountX,
        uint groupCountY,
        uint groupCountZ,
        out string? error)
    {
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0)
        {
            error = null;
            return true;
        }

        BindGroupDescriptor descriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = entryCount,
            Entries = entries
        };

        BindGroup* bindGroup;
        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            bindGroup = flushContext.Api.DeviceCreateBindGroup((Device*)deviceReference.Handle, in descriptor);
        }

        if (bindGroup is null)
        {
            error = "Failed to create a staged-scene compute bind group.";
            return false;
        }

        flushContext.TrackBindGroup(bindGroup);
        bool ownsPassEncoder = false;
        ComputePassEncoder* passEncoder = flushContext.ComputePassEncoder;
        if (passEncoder is null)
        {
            if (!flushContext.BeginComputePass())
            {
                error = "Failed to begin a staged-scene compute pass.";
                return false;
            }

            passEncoder = flushContext.ComputePassEncoder;
            ownsPassEncoder = true;
        }

        try
        {
            flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
            flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
            flushContext.Api.ComputePassEncoderDispatchWorkgroups(passEncoder, groupCountX, groupCountY, groupCountZ);
        }
        finally
        {
            if (ownsPassEncoder)
            {
                flushContext.EndComputePassIfOpen();
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates a bind group and dispatches one indirect compute pass immediately.
    /// </summary>
    internal static unsafe bool TryDispatchComputePassIndirect(
        WebGPUFlushContext flushContext,
        BindGroupLayout* bindGroupLayout,
        ComputePipeline* pipeline,
        BindGroupEntry* entries,
        uint entryCount,
        WgpuBuffer* indirectBuffer,
        ulong indirectOffset,
        out string? error)
    {
        BindGroupDescriptor descriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = entryCount,
            Entries = entries
        };

        BindGroup* bindGroup;
        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            bindGroup = flushContext.Api.DeviceCreateBindGroup((Device*)deviceReference.Handle, in descriptor);
        }

        if (bindGroup is null)
        {
            error = "Failed to create a staged-scene compute bind group.";
            return false;
        }

        flushContext.TrackBindGroup(bindGroup);
        bool ownsPassEncoder = false;
        ComputePassEncoder* passEncoder = flushContext.ComputePassEncoder;
        if (passEncoder is null)
        {
            if (!flushContext.BeginComputePass())
            {
                error = "Failed to begin a staged-scene compute pass.";
                return false;
            }

            passEncoder = flushContext.ComputePassEncoder;
            ownsPassEncoder = true;
        }

        try
        {
            flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
            flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
            flushContext.Api.ComputePassEncoderDispatchWorkgroupsIndirect(passEncoder, indirectBuffer, indirectOffset);
        }
        finally
        {
            if (ownsPassEncoder)
            {
                flushContext.EndComputePassIfOpen();
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Replays the recorded scheduling commands, resolving bind groups and pipelines just before submission.
    /// </summary>
    private static unsafe bool TryExecuteComputeRecording(
        WebGPUFlushContext flushContext,
        WebGPUSceneComputeRecording recording,
        out string? error)
    {
        foreach (WebGPUSceneComputeCommand command in recording.Commands)
        {
            string shaderName = GetShaderDebugName(command.ShaderId);
            if (!TryResolveComputeShader(flushContext, command.ShaderId, out BindGroupLayout* bindGroupLayout, out ComputePipeline* pipeline, out error))
            {
                error = error is null ? null : $"{error} Stage: {shaderName}.";
                return false;
            }

            if (!command.IsIndirect &&
                (command.GroupCountX == 0 || command.GroupCountY == 0 || command.GroupCountZ == 0))
            {
                continue;
            }

            if (!flushContext.BeginComputePass())
            {
                error = $"Failed to begin the staged-scene compute pass for '{shaderName}'.";
                return false;
            }

            try
            {
                BindGroupEntry[] entries = command.ResolveEntries(recording.ResourceRegistry);
                fixed (BindGroupEntry* entriesPtr = entries)
                {
                    BindGroupDescriptor descriptor = new()
                    {
                        Layout = bindGroupLayout,
                        EntryCount = (uint)entries.Length,
                        Entries = entriesPtr
                    };

                    BindGroup* bindGroup;
                    using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
                    {
                        bindGroup = flushContext.Api.DeviceCreateBindGroup((Device*)deviceReference.Handle, in descriptor);
                    }

                    if (bindGroup is null)
                    {
                        error = $"Failed to create a staged-scene compute bind group for '{shaderName}'.";
                        return false;
                    }

                    flushContext.TrackBindGroup(bindGroup);
                    flushContext.Api.ComputePassEncoderSetPipeline(flushContext.ComputePassEncoder, pipeline);
                    flushContext.Api.ComputePassEncoderSetBindGroup(flushContext.ComputePassEncoder, 0, bindGroup, 0, null);

                    if (command.IsIndirect)
                    {
                        flushContext.Api.ComputePassEncoderDispatchWorkgroupsIndirect(
                            flushContext.ComputePassEncoder,
                            command.IndirectBuffer,
                            command.IndirectOffset);
                    }
                    else
                    {
                        flushContext.Api.ComputePassEncoderDispatchWorkgroups(
                            flushContext.ComputePassEncoder,
                            command.GroupCountX,
                            command.GroupCountY,
                            command.GroupCountZ);
                    }
                }
            }
            finally
            {
                flushContext.EndComputePassIfOpen();
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Maps one staged-scene shader identifier to the short debug name used in failure messages.
    /// </summary>
    /// <param name="shaderId">The staged-scene shader identifier.</param>
    /// <returns>The short debug name for <paramref name="shaderId"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetShaderDebugName(WebGPUSceneShaderId shaderId)
        => shaderId switch
        {
            WebGPUSceneShaderId.Prepare => "prepare",
            WebGPUSceneShaderId.PathtagReduce => "pathtag_reduce",
            WebGPUSceneShaderId.PathtagReduce2 => "pathtag_reduce2",
            WebGPUSceneShaderId.PathtagScan1 => "pathtag_scan1",
            WebGPUSceneShaderId.PathtagScan => "pathtag_scan",
            WebGPUSceneShaderId.PathtagScanSmall => "pathtag_scan_small",
            WebGPUSceneShaderId.BboxClear => "bbox_clear",
            WebGPUSceneShaderId.Flatten => "flatten",
            WebGPUSceneShaderId.DrawReduce => "draw_reduce",
            WebGPUSceneShaderId.DrawLeaf => "draw_leaf",
            WebGPUSceneShaderId.ClipReduce => "clip_reduce",
            WebGPUSceneShaderId.ClipLeaf => "clip_leaf",
            WebGPUSceneShaderId.Binning => "binning",
            WebGPUSceneShaderId.PathRowAlloc => "path_row_alloc",
            WebGPUSceneShaderId.PathRowSpan => "path_row_span",
            WebGPUSceneShaderId.TileAlloc => "tile_alloc",
            WebGPUSceneShaderId.Backdrop => "backdrop",
            WebGPUSceneShaderId.PathCountSetup => "path_count_setup",
            WebGPUSceneShaderId.PathCount => "path_count",
            WebGPUSceneShaderId.Coarse => "coarse",
            WebGPUSceneShaderId.PathTilingSetup => "path_tiling_setup",
            WebGPUSceneShaderId.PathTiling => "path_tiling",
            WebGPUSceneShaderId.ChunkReset => "chunk_reset",
            _ => "unknown",
        };

    /// <summary>
    /// Resolves the cached bind-group layout and compute pipeline for one staged-scene shader identifier.
    /// </summary>
    private static unsafe bool TryResolveComputeShader(
        WebGPUFlushContext flushContext,
        WebGPUSceneShaderId shaderId,
        out BindGroupLayout* bindGroupLayout,
        out ComputePipeline* pipeline,
        out string? error)
    {
        bindGroupLayout = null;
        pipeline = null;

        bool LayoutFactory(WebGPU api, Device* device, out BindGroupLayout* layout, out string? layoutError) =>
            shaderId switch
            {
                WebGPUSceneShaderId.Prepare => PrepareComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathtagReduce => PathtagReduceComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathtagReduce2 => PathtagReduce2ComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathtagScan1 => PathtagScan1ComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathtagScan or WebGPUSceneShaderId.PathtagScanSmall => PathtagScanComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.BboxClear => BboxClearComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.Flatten => FlattenComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.DrawReduce => DrawReduceComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.DrawLeaf => DrawLeafComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.ClipReduce => ClipReduceComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.ClipLeaf => ClipLeafComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.Binning => BinningComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathRowAlloc => PathRowAllocComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathRowSpan => PathRowSpanComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.TileAlloc => TileAllocComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.Backdrop => BackdropComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathCountSetup => PathCountSetupComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathCount => PathCountComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.Coarse => CoarseComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathTiling => PathTilingComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.ChunkReset => ChunkResetComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                _ => throw new UnreachableException()
            };

        ReadOnlySpan<byte> shaderCode = shaderId switch
        {
            WebGPUSceneShaderId.Prepare => PrepareComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathtagReduce => PathtagReduceComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathtagReduce2 => PathtagReduce2ComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathtagScan1 => PathtagScan1ComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathtagScan => PathtagScanComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathtagScanSmall => PathtagScanComputeShader.SmallShaderCode,
            WebGPUSceneShaderId.BboxClear => BboxClearComputeShader.ShaderCode,
            WebGPUSceneShaderId.Flatten => FlattenComputeShader.ShaderCode,
            WebGPUSceneShaderId.DrawReduce => DrawReduceComputeShader.ShaderCode,
            WebGPUSceneShaderId.DrawLeaf => DrawLeafComputeShader.ShaderCode,
            WebGPUSceneShaderId.ClipReduce => ClipReduceComputeShader.ShaderCode,
            WebGPUSceneShaderId.ClipLeaf => ClipLeafComputeShader.ShaderCode,
            WebGPUSceneShaderId.Binning => BinningComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathRowAlloc => PathRowAllocComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathRowSpan => PathRowSpanComputeShader.ShaderCode,
            WebGPUSceneShaderId.TileAlloc => TileAllocComputeShader.ShaderCode,
            WebGPUSceneShaderId.Backdrop => BackdropComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathCountSetup => PathCountSetupComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathCount => PathCountComputeShader.ShaderCode,
            WebGPUSceneShaderId.Coarse => CoarseComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathTiling => PathTilingComputeShader.ShaderCode,
            WebGPUSceneShaderId.ChunkReset => ChunkResetComputeShader.ShaderCode,
            _ => throw new UnreachableException()
        };

        ReadOnlySpan<byte> entryPoint = shaderId switch
        {
            WebGPUSceneShaderId.Prepare => PrepareComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathtagReduce => PathtagReduceComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathtagReduce2 => PathtagReduce2ComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathtagScan1 => PathtagScan1ComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathtagScan or WebGPUSceneShaderId.PathtagScanSmall => PathtagScanComputeShader.EntryPoint,
            WebGPUSceneShaderId.BboxClear => BboxClearComputeShader.EntryPoint,
            WebGPUSceneShaderId.Flatten => FlattenComputeShader.EntryPoint,
            WebGPUSceneShaderId.DrawReduce => DrawReduceComputeShader.EntryPoint,
            WebGPUSceneShaderId.DrawLeaf => DrawLeafComputeShader.EntryPoint,
            WebGPUSceneShaderId.ClipReduce => ClipReduceComputeShader.EntryPoint,
            WebGPUSceneShaderId.ClipLeaf => ClipLeafComputeShader.EntryPoint,
            WebGPUSceneShaderId.Binning => BinningComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathRowAlloc => PathRowAllocComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathRowSpan => PathRowSpanComputeShader.EntryPoint,
            WebGPUSceneShaderId.TileAlloc => TileAllocComputeShader.EntryPoint,
            WebGPUSceneShaderId.Backdrop => BackdropComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathCountSetup => PathCountSetupComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathCount => PathCountComputeShader.EntryPoint,
            WebGPUSceneShaderId.Coarse => CoarseComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathTiling => PathTilingComputeShader.EntryPoint,
            WebGPUSceneShaderId.ChunkReset => ChunkResetComputeShader.EntryPoint,
            _ => throw new UnreachableException()
        };

        string pipelineKey = shaderId switch
        {
            WebGPUSceneShaderId.Prepare => PreparePipelineKey,
            WebGPUSceneShaderId.PathtagReduce => PathtagReducePipelineKey,
            WebGPUSceneShaderId.PathtagReduce2 => PathtagReduce2PipelineKey,
            WebGPUSceneShaderId.PathtagScan1 => PathtagScan1PipelineKey,
            WebGPUSceneShaderId.PathtagScan => PathtagScanPipelineKey,
            WebGPUSceneShaderId.PathtagScanSmall => PathtagScanSmallPipelineKey,
            WebGPUSceneShaderId.BboxClear => BboxClearPipelineKey,
            WebGPUSceneShaderId.Flatten => FlattenPipelineKey,
            WebGPUSceneShaderId.DrawReduce => DrawReducePipelineKey,
            WebGPUSceneShaderId.DrawLeaf => DrawLeafPipelineKey,
            WebGPUSceneShaderId.ClipReduce => ClipReducePipelineKey,
            WebGPUSceneShaderId.ClipLeaf => ClipLeafPipelineKey,
            WebGPUSceneShaderId.Binning => BinningPipelineKey,
            WebGPUSceneShaderId.PathRowAlloc => PathRowAllocPipelineKey,
            WebGPUSceneShaderId.PathRowSpan => PathRowSpanPipelineKey,
            WebGPUSceneShaderId.TileAlloc => TileAllocPipelineKey,
            WebGPUSceneShaderId.Backdrop => BackdropPipelineKey,
            WebGPUSceneShaderId.PathCountSetup => PathCountSetupPipelineKey,
            WebGPUSceneShaderId.PathCount => PathCountPipelineKey,
            WebGPUSceneShaderId.Coarse => CoarsePipelineKey,
            WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupPipelineKey,
            WebGPUSceneShaderId.PathTiling => PathTilingPipelineKey,
            WebGPUSceneShaderId.ChunkReset => ChunkResetPipelineKey,
            _ => throw new UnreachableException()
        };

        return flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
            pipelineKey,
            shaderCode,
            entryPoint,
            LayoutFactory,
            out bindGroupLayout,
            out pipeline,
            out error);
    }

    /// <summary>
    /// Creates one buffer binding entry covering the full bound range of the target buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe BindGroupEntry CreateBufferBinding(uint binding, WgpuBuffer* buffer, nuint size)
        => new()
        {
            Binding = binding,
            Buffer = buffer,
            Offset = 0,
            Size = size
        };

    /// <summary>
    /// Creates one reusable scheduling storage buffer that is owned outside the flush-context tracking lists.
    /// </summary>
    private static unsafe WgpuBuffer* CreateArenaStorageBuffer(
        WebGPUFlushContext flushContext,
        nuint size)
        => CreateArenaBuffer(
            flushContext,
            size,
            BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst);

    /// <summary>
    /// Creates one reusable scheduling buffer that can also serve as an indirect dispatch argument buffer.
    /// </summary>
    private static unsafe WgpuBuffer* CreateArenaIndirectStorageBuffer(
        WebGPUFlushContext flushContext,
        nuint size)
        => CreateArenaBuffer(
            flushContext,
            size,
            BufferUsage.Storage | BufferUsage.Indirect | BufferUsage.CopyDst);

    /// <summary>
    /// Creates one flush-scoped buffer, promoting zero-byte requests to a one-word allocation for WebGPU validation.
    /// </summary>
    private static unsafe WgpuBuffer* CreateBuffer(
        WebGPUFlushContext flushContext,
        nuint size,
        BufferUsage usage)
    {
        if (size == 0)
        {
            size = sizeof(uint);
        }

        BufferDescriptor descriptor = new()
        {
            Usage = usage,
            Size = size
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            WgpuBuffer* buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
            flushContext.TrackBuffer(buffer);
            return buffer;
        }
    }

    /// <summary>
    /// Creates one reusable scheduling buffer without attaching it to the current flush-context tracking lists.
    /// </summary>
    private static unsafe WgpuBuffer* CreateArenaBuffer(
        WebGPUFlushContext flushContext,
        nuint size,
        BufferUsage usage)
    {
        if (size == 0)
        {
            size = sizeof(uint);
        }

        BufferDescriptor descriptor = new()
        {
            Usage = usage,
            Size = size
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            return flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
        }
    }

    /// <summary>
    /// Creates one flush-scoped copy-source buffer and uploads a single unmanaged value into it.
    /// </summary>
    /// <param name="flushContext">The flush context that owns the device and queue used to create and populate the copy-source buffer.</param>
    /// <param name="value">The unmanaged value to upload into the new copy-source buffer.</param>
    /// <returns>The populated copy-source buffer.</returns>
    /// <typeparam name="T">The unmanaged payload type uploaded into the new copy-source buffer.</typeparam>
    private static unsafe WgpuBuffer* CreateAndUploadCopySourceBuffer<T>(
        WebGPUFlushContext flushContext,
        in T value)
        where T : unmanaged
    {
        WgpuBuffer* buffer = CreateBuffer(flushContext, (nuint)sizeof(T), BufferUsage.CopySrc | BufferUsage.CopyDst);

        using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
        flushContext.Api.QueueWriteBuffer(
            (Queue*)queueReference.Handle,
            buffer,
            0,
            Unsafe.AsPointer(ref Unsafe.AsRef(in value)),
            (nuint)sizeof(T));

        return buffer;
    }

    /// <summary>
    /// Creates one reusable scheduling storage buffer and uploads a single unmanaged value into it.
    /// </summary>
    private static unsafe WgpuBuffer* CreateAndUploadArenaStorageBuffer<T>(
        WebGPUFlushContext flushContext,
        in T value)
        where T : unmanaged
    {
        WgpuBuffer* buffer = CreateArenaStorageBuffer(flushContext, (nuint)sizeof(T));

        using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
        flushContext.Api.QueueWriteBuffer(
            (Queue*)queueReference.Handle,
            buffer,
            0,
            Unsafe.AsPointer(ref Unsafe.AsRef(in value)),
            (nuint)sizeof(T));

        return buffer;
    }

    /// <summary>
    /// Gets the byte length required to bind <paramref name="count"/> unmanaged elements, preserving WebGPU's non-zero binding rule.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint GetBindingByteLength<T>(int count)
        where T : unmanaged
        => checked((nuint)Math.Max(count, 1) * (nuint)Unsafe.SizeOf<T>());

    /// <summary>
    /// Checks one planned binding size against the current staged-scene storage-buffer limit.
    /// </summary>
    /// <param name="byteLength">The planned size of the binding in bytes.</param>
    /// <param name="bufferName">The human-readable buffer name used in diagnostics.</param>
    /// <param name="bindingLimitBuffer">The binding identifier to report when this check fails.</param>
    /// <param name="maxStorageBufferBindingSize">The device-reported storage-buffer binding limit.</param>
    /// <param name="bindingLimitFailure">Receives the exact binding-limit failure when the planned binding is too large.</param>
    /// <param name="error">Receives the validation failure reason when the planned binding is too large.</param>
    /// <returns><see langword="true"/> when the binding fits within the device limit; otherwise, <see langword="false"/>.</returns>
    private static bool TryValidateBufferSize(
        nuint byteLength,
        string bufferName,
        BindingLimitBuffer bindingLimitBuffer,
        nuint maxStorageBufferBindingSize,
        out BindingLimitFailure bindingLimitFailure,
        out string? error)
    {
        if (byteLength > maxStorageBufferBindingSize)
        {
            bindingLimitFailure = new BindingLimitFailure(bindingLimitBuffer, byteLength, maxStorageBufferBindingSize);
            error = $"The staged-scene {bufferName} buffer requires {byteLength} bytes, exceeding the current WebGPU binding limit of {maxStorageBufferBindingSize} bytes.";
            return false;
        }

        bindingLimitFailure = BindingLimitFailure.None;
        error = null;
        return true;
    }

    /// <summary>
    /// Pumps the WebGPU device while waiting for one asynchronous map callback to signal completion.
    /// </summary>
    /// <param name="extension">The optional native WGPU extension used to advance callback delivery.</param>
    /// <param name="device">The device that owns the mapped readback buffer.</param>
    /// <param name="signal">The event that the map callback sets when the copy is ready to read.</param>
    /// <returns><see langword="true"/> when the callback completed before the timeout; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool WaitForMapSignal(Wgpu? extension, Device* device, ManualResetEventSlim signal)
    {
        if (extension is null)
        {
            return signal.Wait(5000);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!signal.IsSet && stopwatch.ElapsedMilliseconds < 5000)
        {
            _ = extension.DevicePoll(device, true, (WrappedSubmissionIndex*)null);
        }

        return signal.IsSet;
    }
}

/// <summary>
/// Flush-scoped recording of staged-scene compute dispatches.
/// This mirrors the upstream split between command recording and later execution,
/// without introducing another runtime abstraction layer.
/// </summary>
internal sealed unsafe class WebGPUSceneComputeRecording
{
    private readonly List<WebGPUSceneComputeCommand> commands = [];

    public WebGPUSceneComputeRecording(WebGPUSceneResourceRegistry resourceRegistry)
        => this.ResourceRegistry = resourceRegistry;

    public WebGPUSceneResourceRegistry ResourceRegistry { get; }

    /// <summary>
    /// Gets the recorded compute commands in submission order.
    /// </summary>
    public IReadOnlyList<WebGPUSceneComputeCommand> Commands => this.commands;

    /// <summary>
    /// Records one direct compute dispatch.
    /// </summary>
    public bool TryRecord(
        WebGPUSceneShaderId shaderId,
        BindGroupEntry* entries,
        uint entryCount,
        uint groupCountX,
        uint groupCountY,
        uint groupCountZ,
        out string? error)
    {
        this.commands.Add(new WebGPUSceneComputeCommand(
            shaderId,
            groupCountX,
            groupCountY,
            groupCountZ,
            this.CopyResources(entries, entryCount),
            null,
            0,
            isIndirect: false));
        error = null;
        return true;
    }

    /// <summary>
    /// Records one indirect compute dispatch.
    /// </summary>
    public bool TryRecordIndirect(
        WebGPUSceneShaderId shaderId,
        BindGroupEntry* entries,
        uint entryCount,
        WgpuBuffer* indirectBuffer,
        ulong indirectOffset,
        out string? error)
    {
        this.commands.Add(new WebGPUSceneComputeCommand(
            shaderId,
            0,
            0,
            0,
            this.CopyResources(entries, entryCount),
            indirectBuffer,
            indirectOffset,
            isIndirect: true));
        error = null;
        return true;
    }

    private WebGPUSceneResourceProxy[] CopyResources(BindGroupEntry* entries, uint entryCount)
    {
        WebGPUSceneResourceProxy[] resources = new WebGPUSceneResourceProxy[entryCount];
        for (int i = 0; i < resources.Length; i++)
        {
            resources[i] = this.ResourceRegistry.CreateProxy(entries[i]);
        }

        return resources;
    }
}

/// <summary>
/// One recorded staged-scene compute dispatch.
/// </summary>
internal readonly unsafe struct WebGPUSceneComputeCommand
{
    public WebGPUSceneComputeCommand(
        WebGPUSceneShaderId shaderId,
        uint groupCountX,
        uint groupCountY,
        uint groupCountZ,
        WebGPUSceneResourceProxy[] resources,
        WgpuBuffer* indirectBuffer,
        ulong indirectOffset,
        bool isIndirect)
    {
        this.ShaderId = shaderId;
        this.Resources = resources;
        this.GroupCountX = groupCountX;
        this.GroupCountY = groupCountY;
        this.GroupCountZ = groupCountZ;
        this.IndirectBuffer = indirectBuffer;
        this.IndirectOffset = indirectOffset;
        this.IsIndirect = isIndirect;
    }

    public WebGPUSceneShaderId ShaderId { get; }

    public WebGPUSceneResourceProxy[] Resources { get; }

    public uint GroupCountX { get; }

    public uint GroupCountY { get; }

    public uint GroupCountZ { get; }

    public WgpuBuffer* IndirectBuffer { get; }

    public ulong IndirectOffset { get; }

    public bool IsIndirect { get; }

    public BindGroupEntry[] ResolveEntries(WebGPUSceneResourceRegistry resourceRegistry)
    {
        BindGroupEntry[] entries = new BindGroupEntry[this.Resources.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = resourceRegistry.Resolve(this.Resources[i]);
        }

        return entries;
    }
}

/// <summary>
/// Identifiers for the generated staged-scene compute shaders cached by the dispatch layer.
/// </summary>
internal enum WebGPUSceneShaderId
{
    Prepare = 0,
    PathtagReduce = 1,
    PathtagReduce2 = 2,
    PathtagScan1 = 3,
    PathtagScan = 4,
    PathtagScanSmall = 5,
    BboxClear = 6,
    Flatten = 7,
    DrawReduce = 8,
    DrawLeaf = 9,
    ClipReduce = 10,
    ClipLeaf = 11,
    Binning = 12,
    PathRowAlloc = 13,
    PathRowSpan = 14,
    TileAlloc = 15,
    Backdrop = 16,
    PathCountSetup = 17,
    PathCount = 18,
    Coarse = 19,
    PathTilingSetup = 20,
    PathTiling = 21,
    ChunkReset = 22
}

/// <summary>
/// Serializable placeholder for one buffer or texture-view binding recorded before execution.
/// </summary>
internal readonly struct WebGPUSceneResourceProxy
{
    private WebGPUSceneResourceProxy(
        uint binding,
        nuint offset,
        nuint size,
        uint resourceId,
        WebGPUSceneResourceProxyKind kind)
    {
        this.Binding = binding;
        this.Offset = offset;
        this.Size = size;
        this.ResourceId = resourceId;
        this.Kind = kind;
    }

    public uint Binding { get; }

    public nuint Offset { get; }

    public nuint Size { get; }

    public uint ResourceId { get; }

    public WebGPUSceneResourceProxyKind Kind { get; }

    public static WebGPUSceneResourceProxy CreateBuffer(uint binding, uint resourceId, nuint offset, nuint size)
        => new(binding, offset, size, resourceId, WebGPUSceneResourceProxyKind.Buffer);

    public static WebGPUSceneResourceProxy CreateTextureView(uint binding, uint resourceId)
        => new(binding, 0, 0, resourceId, WebGPUSceneResourceProxyKind.TextureView);
}

/// <summary>
/// Distinguishes whether a recorded binding proxy resolves to a buffer or a texture view.
/// </summary>
internal enum WebGPUSceneResourceProxyKind
{
    Buffer = 0,
    TextureView = 1
}

/// <summary>
/// Assigns stable integer ids to flush-scoped resources so recorded commands can be replayed later.
/// </summary>
internal sealed unsafe class WebGPUSceneResourceRegistry
{
    private uint nextResourceId = 1;
    private readonly Dictionary<nint, uint> bufferIds = [];
    private readonly Dictionary<nint, uint> textureViewIds = [];
    private readonly Dictionary<uint, nint> buffers = [];
    private readonly Dictionary<uint, nint> textureViews = [];

    private WebGPUSceneResourceRegistry()
    {
    }

    /// <summary>
    /// Creates a registry preloaded with the persistent resources owned by the staged scene.
    /// </summary>
    public static WebGPUSceneResourceRegistry Create(WebGPUSceneResourceSet resources)
    {
        WebGPUSceneResourceRegistry registry = new();
        registry.RegisterBuffer(resources.HeaderBuffer);
        registry.RegisterBuffer(resources.SceneBuffer);
        registry.RegisterBuffer(resources.PathReducedBuffer);
        registry.RegisterBuffer(resources.PathReduced2Buffer);
        registry.RegisterBuffer(resources.PathReducedScanBuffer);
        registry.RegisterBuffer(resources.PathMonoidBuffer);
        registry.RegisterBuffer(resources.PathBboxBuffer);
        registry.RegisterBuffer(resources.DrawReducedBuffer);
        registry.RegisterBuffer(resources.DrawMonoidBuffer);
        registry.RegisterBuffer(resources.InfoBinDataBuffer);
        registry.RegisterBuffer(resources.ClipInputBuffer);
        registry.RegisterBuffer(resources.ClipElementBuffer);
        registry.RegisterBuffer(resources.ClipBicBuffer);
        registry.RegisterBuffer(resources.ClipBboxBuffer);
        registry.RegisterBuffer(resources.DrawBboxBuffer);
        registry.RegisterBuffer(resources.PathBuffer);
        registry.RegisterBuffer(resources.LineBuffer);
        registry.RegisterTextureView(resources.GradientTextureView);
        registry.RegisterTextureView(resources.ImageAtlasTextureView);
        return registry;
    }

    /// <summary>
    /// Registers the transient buffers produced by the scheduling passes.
    /// </summary>
    public void RegisterSchedulingBuffers(
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* indirectCountBuffer,
        WgpuBuffer* pathRowBuffer,
        WgpuBuffer* pathTileBuffer,
        WgpuBuffer* segCountBuffer,
        WgpuBuffer* segmentBuffer,
        WgpuBuffer* blendBuffer,
        WgpuBuffer* ptclBuffer,
        WgpuBuffer* bumpBuffer)
    {
        this.RegisterBuffer(binHeaderBuffer);
        this.RegisterBuffer(indirectCountBuffer);
        this.RegisterBuffer(pathRowBuffer);
        this.RegisterBuffer(pathTileBuffer);
        this.RegisterBuffer(segCountBuffer);
        this.RegisterBuffer(segmentBuffer);
        this.RegisterBuffer(blendBuffer);
        this.RegisterBuffer(ptclBuffer);
        this.RegisterBuffer(bumpBuffer);
    }

    /// <summary>
    /// Converts one live bind-group entry into a stable proxy that can be resolved later.
    /// </summary>
    public WebGPUSceneResourceProxy CreateProxy(BindGroupEntry entry)
        => entry.TextureView is not null
            ? WebGPUSceneResourceProxy.CreateTextureView(entry.Binding, this.GetTextureViewId(entry.TextureView))
            : WebGPUSceneResourceProxy.CreateBuffer(entry.Binding, this.GetBufferId(entry.Buffer), checked((nuint)entry.Offset), checked((nuint)entry.Size));

    /// <summary>
    /// Resolves one previously recorded proxy back to the live bind-group entry for execution.
    /// </summary>
    public BindGroupEntry Resolve(WebGPUSceneResourceProxy proxy)
        => proxy.Kind == WebGPUSceneResourceProxyKind.TextureView
            ? new BindGroupEntry { Binding = proxy.Binding, TextureView = (TextureView*)this.textureViews[proxy.ResourceId] }
            : new BindGroupEntry
            {
                Binding = proxy.Binding,
                Buffer = (WgpuBuffer*)this.buffers[proxy.ResourceId],
                Offset = proxy.Offset,
                Size = proxy.Size
            };

    private void RegisterBuffer(WgpuBuffer* buffer)
    {
        nint handle = (nint)buffer;
        if (this.bufferIds.ContainsKey(handle))
        {
            return;
        }

        uint id = this.nextResourceId++;
        this.bufferIds[handle] = id;
        this.buffers[id] = (nint)buffer;
    }

    private void RegisterTextureView(TextureView* textureView)
    {
        nint handle = (nint)textureView;
        if (this.textureViewIds.ContainsKey(handle))
        {
            return;
        }

        uint id = this.nextResourceId++;
        this.textureViewIds[handle] = id;
        this.textureViews[id] = (nint)textureView;
    }

    private uint GetBufferId(WgpuBuffer* buffer) => this.bufferIds[(nint)buffer];

    private uint GetTextureViewId(TextureView* textureView) => this.textureViewIds[(nint)textureView];
}

/// <summary>
/// One flush-scoped staged-scene instance produced during the WebGPU rasterizer replacement.
/// </summary>
internal readonly struct WebGPUStagedScene : IDisposable
{
    public WebGPUStagedScene(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene encodedScene,
        WebGPUSceneConfig config,
        WebGPUSceneResourceSet resources,
        WebGPUSceneDispatch.BindingLimitFailure bindingLimitFailure)
        : this(flushContext, encodedScene, config, resources, bindingLimitFailure, ownsEncodedScene: true)
    {
    }

    public WebGPUStagedScene(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene encodedScene,
        WebGPUSceneConfig config,
        WebGPUSceneResourceSet resources,
        WebGPUSceneDispatch.BindingLimitFailure bindingLimitFailure,
        bool ownsEncodedScene)
    {
        this.FlushContext = flushContext;
        this.EncodedScene = encodedScene;
        this.Config = config;
        this.Resources = resources;
        this.BindingLimitFailure = bindingLimitFailure;
        this.OwnsEncodedScene = ownsEncodedScene;
    }

    /// <summary>
    /// Gets the flush context that owns the device, queue, encoder, and tracked native resources.
    /// </summary>
    public WebGPUFlushContext FlushContext { get; }

    /// <summary>
    /// Gets the encoded scene payload owned by this staged scene.
    /// </summary>
    public WebGPUEncodedScene EncodedScene { get; }

    /// <summary>
    /// Gets the dispatch and buffer plan derived from <see cref="EncodedScene"/>.
    /// </summary>
    public WebGPUSceneConfig Config { get; }

    /// <summary>
    /// Gets the flush-scoped GPU resources created for <see cref="EncodedScene"/>.
    /// </summary>
    public WebGPUSceneResourceSet Resources { get; }

    /// <summary>
    /// Gets the binding-limit overflow that must be handled by an alternate render path.
    /// </summary>
    public WebGPUSceneDispatch.BindingLimitFailure BindingLimitFailure { get; }

    /// <summary>
    /// Gets a value indicating whether this staged scene owns the encoded scene.
    /// </summary>
    public bool OwnsEncodedScene { get; }

    /// <summary>
    /// Releases the encoded scene and the flush context that owns the tracked native resources.
    /// </summary>
    public void Dispose()
    {
        if (this.OwnsEncodedScene)
        {
            this.EncodedScene.Dispose();
        }

        this.FlushContext.Dispose();
    }
}

/// <summary>
/// Flush-scoped GPU buffers produced by the early scheduling stages.
/// </summary>
internal readonly unsafe struct WebGPUSceneSchedulingResources
{
    public WebGPUSceneSchedulingResources(
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* indirectCountBuffer,
        WgpuBuffer* pathRowBuffer,
        WgpuBuffer* pathTileBuffer,
        WgpuBuffer* segCountBuffer,
        WgpuBuffer* segmentBuffer,
        WgpuBuffer* blendBuffer,
        WgpuBuffer* ptclBuffer,
        WgpuBuffer* bumpBuffer)
    {
        this.BinHeaderBuffer = binHeaderBuffer;
        this.IndirectCountBuffer = indirectCountBuffer;
        this.PathRowBuffer = pathRowBuffer;
        this.PathTileBuffer = pathTileBuffer;
        this.SegCountBuffer = segCountBuffer;
        this.SegmentBuffer = segmentBuffer;
        this.BlendBuffer = blendBuffer;
        this.PtclBuffer = ptclBuffer;
        this.BumpBuffer = bumpBuffer;
    }

    /// <summary>
    /// Gets the bin-header buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* BinHeaderBuffer { get; }

    /// <summary>
    /// Gets the indirect dispatch-count buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* IndirectCountBuffer { get; }

    /// <summary>
    /// Gets the sparse path-row buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* PathRowBuffer { get; }

    /// <summary>
    /// Gets the path-tile buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* PathTileBuffer { get; }

    /// <summary>
    /// Gets the segment-count buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* SegCountBuffer { get; }

    /// <summary>
    /// Gets the segment buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* SegmentBuffer { get; }

    /// <summary>
    /// Gets the blend-spill buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* BlendBuffer { get; }

    /// <summary>
    /// Gets the PTCL buffer produced by the scheduling passes.
    /// </summary>
    public WgpuBuffer* PtclBuffer { get; }

    /// <summary>
    /// Gets the bump allocator buffer used to coordinate scratch allocation across scheduling passes.
    /// </summary>
    public WgpuBuffer* BumpBuffer { get; }
}

/// <summary>
/// Reusable scheduling scratch and readback buffers.
/// </summary>
/// <remarks>
/// The buffers are mutable scratch state written by scheduling passes, so an arena is exclusive
/// to one render while rented. The owner may cache it after render completion for later reuse.
/// </remarks>
internal sealed unsafe class WebGPUSceneSchedulingArena
{
    public WebGPUSceneSchedulingArena(
        WebGPU api,
        WebGPUDeviceHandle device,
        WebGPUSceneBufferSizes capacitySizes,
        nuint readbackByteLength,
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* indirectCountBuffer,
        WgpuBuffer* pathRowBuffer,
        WgpuBuffer* pathTileBuffer,
        WgpuBuffer* segCountBuffer,
        WgpuBuffer* segmentBuffer,
        WgpuBuffer* blendBuffer,
        WgpuBuffer* ptclBuffer,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* readbackBuffer)
    {
        this.Api = api;
        this.Device = device;
        this.CapacitySizes = capacitySizes;
        this.ReadbackByteLength = readbackByteLength;
        this.BinHeaderBuffer = binHeaderBuffer;
        this.IndirectCountBuffer = indirectCountBuffer;
        this.PathRowBuffer = pathRowBuffer;
        this.PathTileBuffer = pathTileBuffer;
        this.SegCountBuffer = segCountBuffer;
        this.SegmentBuffer = segmentBuffer;
        this.BlendBuffer = blendBuffer;
        this.PtclBuffer = ptclBuffer;
        this.BumpBuffer = bumpBuffer;
        this.ReadbackBuffer = readbackBuffer;
    }

    /// <summary>
    /// Gets the API facade used to release arena-owned buffers after the flush ends.
    /// </summary>
    public WebGPU Api { get; }

    /// <summary>
    /// Gets the device that created the arena buffers so reuse never crosses device boundaries.
    /// </summary>
    public WebGPUDeviceHandle Device { get; }

    public WebGPUSceneBufferSizes CapacitySizes { get; }

    public nuint ReadbackByteLength { get; }

    public WgpuBuffer* BinHeaderBuffer { get; }

    public WgpuBuffer* IndirectCountBuffer { get; }

    public WgpuBuffer* PathRowBuffer { get; }

    public WgpuBuffer* PathTileBuffer { get; }

    public WgpuBuffer* SegCountBuffer { get; }

    public WgpuBuffer* SegmentBuffer { get; }

    public WgpuBuffer* BlendBuffer { get; }

    public WgpuBuffer* PtclBuffer { get; }

    public WgpuBuffer* BumpBuffer { get; }

    public WgpuBuffer* ReadbackBuffer { get; }

    /// <summary>
    /// Returns true if every buffer fits the required sizes for this scene.
    /// </summary>
    public bool CanReuse(WebGPUFlushContext flushContext, WebGPUSceneBufferSizes bufferSizes, nuint readbackByteLength)
        => ReferenceEquals(this.Device, flushContext.DeviceHandle) &&
           this.BinHeaderBuffer is not null &&
           this.IndirectCountBuffer is not null &&
           this.PathRowBuffer is not null &&
           this.PathTileBuffer is not null &&
           this.SegCountBuffer is not null &&
           this.SegmentBuffer is not null &&
           this.BlendBuffer is not null &&
           this.PtclBuffer is not null &&
           this.BumpBuffer is not null &&
           this.ReadbackBuffer is not null &&
           bufferSizes.BinHeaders.ByteLength <= this.CapacitySizes.BinHeaders.ByteLength &&
           bufferSizes.IndirectCount.ByteLength <= this.CapacitySizes.IndirectCount.ByteLength &&
           bufferSizes.PathRows.ByteLength <= this.CapacitySizes.PathRows.ByteLength &&
           bufferSizes.PathTiles.ByteLength <= this.CapacitySizes.PathTiles.ByteLength &&
           bufferSizes.SegCounts.ByteLength <= this.CapacitySizes.SegCounts.ByteLength &&
           bufferSizes.Segments.ByteLength <= this.CapacitySizes.Segments.ByteLength &&
           bufferSizes.BlendSpill.ByteLength <= this.CapacitySizes.BlendSpill.ByteLength &&
           bufferSizes.Ptcl.ByteLength <= this.CapacitySizes.Ptcl.ByteLength &&
           readbackByteLength <= this.ReadbackByteLength;

    /// <summary>
    /// Releases all GPU buffers owned by this arena.
    /// </summary>
    public static void Dispose(WebGPUSceneSchedulingArena? arena)
    {
        if (arena is null || arena.BinHeaderBuffer is null)
        {
            return;
        }

        WebGPU api = arena.Api;
        ReleaseArenaBuffer(api, arena.ReadbackBuffer);
        ReleaseArenaBuffer(api, arena.BumpBuffer);
        ReleaseArenaBuffer(api, arena.PtclBuffer);
        ReleaseArenaBuffer(api, arena.BlendBuffer);
        ReleaseArenaBuffer(api, arena.SegmentBuffer);
        ReleaseArenaBuffer(api, arena.SegCountBuffer);
        ReleaseArenaBuffer(api, arena.PathRowBuffer);
        ReleaseArenaBuffer(api, arena.PathTileBuffer);
        ReleaseArenaBuffer(api, arena.IndirectCountBuffer);
        ReleaseArenaBuffer(api, arena.BinHeaderBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReleaseArenaBuffer(WebGPU api, WgpuBuffer* buffer)
    {
        if (buffer is not null)
        {
            api.BufferRelease(buffer);
        }
    }
}

#pragma warning restore SA1201
