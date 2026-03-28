// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Phase-1 staged scene types are grouped by pipeline role.

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    private const string TileAllocPipelineKey = "scene/tile-alloc";
    private const string BackdropPipelineKey = "scene/backdrop";
    private const string PathCountSetupPipelineKey = "scene/path-count-setup";
    private const string PathCountPipelineKey = "scene/path-count";
    private const string CoarsePipelineKey = "scene/coarse";
    private const string PathTilingSetupPipelineKey = "scene/path-tiling-setup";
    private const string PathTilingPipelineKey = "scene/path-tiling";
    private const string FineAreaPipelineKey = "scene/fine-area";
    private const string FineAliasedThresholdPipelineKey = "scene/fine-aliased-threshold";

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
        /// The path-tiling segment buffer exceeded the maximum bindable storage-buffer size.
        /// </summary>
        Segments = 1
    }

    /// <summary>
    /// Describes one binding-limit failure reported while planning a staged scene.
    /// </summary>
    /// <param name="Buffer">The staged-scene binding that exceeded the current device limit.</param>
    /// <param name="RequiredBytes">The number of bytes required by that binding.</param>
    /// <param name="LimitBytes">The maximum number of bytes the current device allows for that binding.</param>
    public readonly record struct BindingLimitFailure(BindingLimitBuffer Buffer, nuint RequiredBytes, nuint LimitBytes)
    {
        /// <summary>
        /// Gets the empty binding-limit result.
        /// </summary>
        public static BindingLimitFailure None { get; } = new(BindingLimitBuffer.None, 0, 0);

        /// <summary>
        /// Gets a value indicating whether one binding exceeded the current device limit.
        /// </summary>
        public bool IsExceeded => this.Buffer != BindingLimitBuffer.None;
    }

    /// <summary>
    /// Builds the flush-scoped encoded scene and uploads its GPU resources.
    /// </summary>
    /// <param name="configuration">The drawing configuration that owns allocators and backend settings.</param>
    /// <param name="target">The flush target that exposes the native WebGPU surface.</param>
    /// <param name="scene">The prepared composition scene to stage.</param>
    /// <param name="bumpSizes">The current dynamic scratch capacities to use for this attempt.</param>
    /// <param name="stagedScene">Receives the flush-scoped staged scene on success.</param>
    /// <param name="error">Receives the staging failure reason when creation fails.</param>
    /// <typeparam name="TPixel">The pixel type of the target surface.</typeparam>
    /// <returns><see langword="true"/> when the staged scene was created successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateStagedScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene scene,
        WebGPUSceneBumpSizes bumpSizes,
        out WebGPUStagedScene stagedScene,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
        => TryCreateStagedScene(configuration, target, scene.Commands, bumpSizes, out _, out _, out stagedScene, out error);

    /// <summary>
    /// Builds the flush-scoped encoded scene and uploads its GPU resources.
    /// </summary>
    /// <param name="configuration">The drawing configuration that owns allocators and backend settings.</param>
    /// <param name="target">The flush target that exposes the native WebGPU surface.</param>
    /// <param name="commands">The prepared composition commands for this flush.</param>
    /// <param name="bumpSizes">The current dynamic scratch capacities to use for this attempt.</param>
    /// <param name="exceedsBindingLimit">Receives whether creation failed because a single WebGPU binding would be too large.</param>
    /// <param name="bindingLimitFailure">Receives the exact binding-limit failure when <paramref name="exceedsBindingLimit"/> is <see langword="true"/>.</param>
    /// <param name="stagedScene">Receives the flush-scoped staged scene on success.</param>
    /// <param name="error">Receives the staging failure reason when creation fails.</param>
    /// <typeparam name="TPixel">The pixel type of the target surface.</typeparam>
    /// <returns><see langword="true"/> when the staged scene was created successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateStagedScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        IReadOnlyList<CompositionCommand> commands,
        WebGPUSceneBumpSizes bumpSizes,
        out bool exceedsBindingLimit,
        out BindingLimitFailure bindingLimitFailure,
        out WebGPUStagedScene stagedScene,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        stagedScene = default;
        exceedsBindingLimit = false;
        bindingLimitFailure = BindingLimitFailure.None;
        WebGPUEncodedScene? encodedScene = null;

        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId, out FeatureName requiredFeature))
        {
            error = $"The staged WebGPU scene pipeline does not support pixel format '{typeof(TPixel).Name}'.";
            return false;
        }

        WebGPUSceneSupportResult support = WebGPUSceneEncoder.ValidateSceneSupport(commands);
        if (!support.IsSupported)
        {
            error = support.Error;
            return false;
        }

        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(formatId);

        WebGPUFlushContext? flushContext = WebGPUFlushContext.Create(
            target,
            textureFormat,
            requiredFeature,
            configuration.MemoryAllocator);

        if (flushContext is null)
        {
            error = "Failed to create a WebGPU flush context for the staged scene pipeline.";
            return false;
        }

        try
        {
            encodedScene = WebGPUSceneEncoder.Encode(commands, support, flushContext.TargetBounds, flushContext.MemoryAllocator);
            WebGPUSceneBumpSizes sceneBumpSizes = bumpSizes.WithSceneLowerBounds(encodedScene);
            WebGPUSceneConfig config = WebGPUSceneConfig.Create(encodedScene, sceneBumpSizes);
            uint baseColor = 0U;
            bool segmentChunkingRequired = false;
            if (!TryValidateBindingSizes(encodedScene, config, flushContext.DeviceState.MaxStorageBufferBindingSize, out bindingLimitFailure, out error))
            {
                if (bindingLimitFailure.Buffer != BindingLimitBuffer.Segments)
                {
                    exceedsBindingLimit = true;
                    encodedScene.Dispose();
                    flushContext.Dispose();
                    stagedScene = default;
                    return false;
                }

                segmentChunkingRequired = true;
            }

            if (encodedScene.FillCount == 0)
            {
                stagedScene = new WebGPUStagedScene(flushContext, encodedScene, config, default, segmentChunkingRequired ? bindingLimitFailure : BindingLimitFailure.None);
                error = null;
                return true;
            }

            if (!WebGPUSceneResources.TryCreate<TPixel>(flushContext, encodedScene, config, baseColor, out WebGPUSceneResourceSet resources, out error))
            {
                encodedScene.Dispose();
                flushContext.Dispose();
                stagedScene = default;
                return false;
            }

            stagedScene = new WebGPUStagedScene(flushContext, encodedScene, config, resources, segmentChunkingRequired ? bindingLimitFailure : BindingLimitFailure.None);
            error = null;
            return true;
        }
        catch
        {
            encodedScene?.Dispose();
            flushContext.Dispose();
            stagedScene = default;
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
        nuint infoBinDataByteLength = checked(GetBindingByteLength<uint>(encodedScene.InfoWordCount) + config.BufferSizes.BinData.ByteLength);
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
            !TryValidateBufferSize(bufferSizes.PathTiles.ByteLength, "path tiles", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.SegCounts.ByteLength, "segment counts", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.Segments.ByteLength, "segments", BindingLimitBuffer.Segments, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.BlendSpill.ByteLength, "blend spill", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error) ||
            !TryValidateBufferSize(bufferSizes.Ptcl.ByteLength, "ptcl", BindingLimitBuffer.None, maxStorageBufferBindingSize, out bindingLimitFailure, out error))
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
        uint tileMembershipCount = checked((uint)encodedScene.TotalTileMembershipCount);
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

        if (tileMembershipCount > bufferSizes.PathTiles.Length)
        {
            error = $"The staged-scene path-tile buffer reserves {bufferSizes.PathTiles.Length} entries, but this scene touches {tileMembershipCount} tiles.";
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
    public static unsafe bool TryDispatchSchedulingStages(
        ref WebGPUStagedScene stagedScene,
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
        nuint pathReducedBufferSize = bufferSizes.PathReduced.ByteLength;
        nuint pathReduced2BufferSize = bufferSizes.PathReduced2.ByteLength;
        nuint pathReducedScanBufferSize = bufferSizes.PathReducedScan.ByteLength;
        nuint pathMonoidBufferSize = bufferSizes.PathMonoids.ByteLength;
        nuint pathBboxBufferSize = bufferSizes.PathBboxes.ByteLength;
        nuint drawReducedBufferSize = bufferSizes.DrawReduced.ByteLength;
        nuint drawMonoidBufferSize = bufferSizes.DrawMonoids.ByteLength;
        nuint infoBinDataBufferSize = checked(GetBindingByteLength<uint>(encodedScene.InfoWordCount) + bufferSizes.BinData.ByteLength);
        nuint clipInputBufferSize = bufferSizes.ClipInputs.ByteLength;
        nuint clipElementBufferSize = bufferSizes.ClipElements.ByteLength;
        nuint clipBicBufferSize = bufferSizes.ClipBics.ByteLength;
        nuint clipBboxBufferSize = bufferSizes.ClipBboxes.ByteLength;
        nuint drawBboxBufferSize = bufferSizes.DrawBboxes.ByteLength;
        nuint pathBufferSize = bufferSizes.Paths.ByteLength;
        nuint lineBufferSize = bufferSizes.Lines.ByteLength;
        nuint binHeaderBufferSize = bufferSizes.BinHeaders.ByteLength;
        nuint indirectCountBufferSize = bufferSizes.IndirectCount.ByteLength;
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

        if (!TryCreateStorageBuffer(flushContext, bufferSizes.BinHeaders.ByteLength, out WgpuBuffer* binHeaderBuffer, out error))
        {
            return false;
        }

        if (!TryCreateIndirectStorageBuffer(flushContext, indirectCountBufferSize, out WgpuBuffer* indirectCountBuffer, out error))
        {
            return false;
        }

        if (!TryCreateStorageBuffer(flushContext, bufferSizes.PathTiles.ByteLength, out WgpuBuffer* pathTileBuffer, out error))
        {
            return false;
        }

        if (!TryCreateStorageBuffer(flushContext, bufferSizes.SegCounts.ByteLength, out WgpuBuffer* segCountBuffer, out error))
        {
            return false;
        }

        if (!TryCreateStorageBuffer(flushContext, bufferSizes.Segments.ByteLength, out WgpuBuffer* segmentBuffer, out error))
        {
            return false;
        }

        if (!TryCreateStorageBuffer(flushContext, bufferSizes.BlendSpill.ByteLength, out WgpuBuffer* blendBuffer, out error))
        {
            return false;
        }

        if (!TryCreateStorageBuffer(flushContext, bufferSizes.Ptcl.ByteLength, out WgpuBuffer* ptclBuffer, out error))
        {
            return false;
        }

        GpuSceneBumpAllocators bumpAllocators = new()
        {
            Failed = 0,
            Binning = 0,
            Ptcl = 0,
            Tile = 0,
            SegCounts = 0,
            Segments = 0,
            BlendSpill = 0,
            Lines = 0
        };

        if (!TryCreateAndUploadStorageBuffer(flushContext, in bumpAllocators, out WgpuBuffer* bumpBuffer, out error))
        {
            return false;
        }

        WebGPUSceneResourceRegistry resourceRegistry = WebGPUSceneResourceRegistry.Create(stagedScene.Resources);
        resourceRegistry.RegisterSchedulingBuffers(
            binHeaderBuffer,
            indirectCountBuffer,
            pathTileBuffer,
            segCountBuffer,
            segmentBuffer,
            blendBuffer,
            ptclBuffer,
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
            if (!TryDispatchClipReduce(
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
                binHeaderBuffer,
                binHeaderBufferSize,
                bumpBuffer,
                workgroupCounts.BinningX,
                out error))
        {
            return false;
        }

        if (!TryDispatchTileAlloc(
                recording,
                flushContext,
                stagedScene.Resources,
                sceneBufferSize,
                drawBboxBufferSize,
                pathBufferSize,
                pathTileBuffer,
                pathTileBufferSize,
                bumpBuffer,
                workgroupCounts.TileAllocX,
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

        if (!TryDispatchPathCount(
                recording,
                flushContext,
                stagedScene.Resources,
                bumpBuffer,
                pathBufferSize,
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
                binHeaderBuffer,
                binHeaderBufferSize,
                pathBufferSize,
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
    /// Executes the staged scene pipeline against the current flush target.
    /// </summary>
    /// <remarks>
    /// Scheduling runs first so the backend can read back the bump allocators and decide whether
    /// this flush needs larger scratch buffers before continuing into the fine raster pass.
    /// </remarks>
    /// <param name="stagedScene">The flush-scoped scene, resources, and config for this attempt.</param>
    /// <param name="requiresGrowth">Receives whether scheduling reported that the scratch capacities were too small.</param>
    /// <param name="grownBumpSizes">Receives the enlarged scratch capacities to retry with when <paramref name="requiresGrowth"/> is <see langword="true"/>.</param>
    /// <param name="error">Receives the render failure reason when the staged path cannot continue.</param>
    /// <returns><see langword="true"/> when the staged scene rendered successfully; otherwise, <see langword="false"/>.</returns>
    public static unsafe bool TryRenderStagedScene(
        ref WebGPUStagedScene stagedScene,
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

        if (stagedScene.BindingLimitFailure.Buffer == BindingLimitBuffer.Segments)
        {
            return TryRenderSegmentChunkedStagedScene(ref stagedScene, out requiresGrowth, out grownBumpSizes, out error);
        }

        if (!TryDispatchSchedulingStages(ref stagedScene, out WebGPUSceneSchedulingResources scheduling, out error))
        {
            return false;
        }

        if (!TryReadSchedulingStatus(stagedScene.FlushContext, scheduling.BumpBuffer, out GpuSceneBumpAllocators bumpAllocators, out error))
        {
            return false;
        }

        if (RequiresScratchReallocation(in bumpAllocators, stagedScene.Config.BumpSizes))
        {
            requiresGrowth = true;
            grownBumpSizes = GrowBumpSizes(stagedScene.Config.BumpSizes, in bumpAllocators);
            error = "The staged WebGPU scene needs larger scratch buffers and will be retried.";
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("IMAGE_SHARP_WEBGPU_DEBUG_SCHED"), "1", StringComparison.Ordinal))
        {
            WebGPUEncodedScene debugScene = stagedScene.EncodedScene;
            error = $"scene fills={debugScene.FillCount} paths={debugScene.PathCount} lines={debugScene.LineCount} pathtag_bytes={debugScene.PathTagByteCount} pathtag_words={debugScene.PathTagWordCount} drawtags={debugScene.DrawTagCount} drawdata={debugScene.DrawDataWordCount} transforms={debugScene.TransformWordCount} styles={debugScene.StyleWordCount}; sched failed={bumpAllocators.Failed} binning={bumpAllocators.Binning} ptcl={bumpAllocators.Ptcl} tile={bumpAllocators.Tile} seg_counts={bumpAllocators.SegCounts} segments={bumpAllocators.Segments} blend={bumpAllocators.BlendSpill} lines={bumpAllocators.Lines}";
            return false;
        }

        WebGPUFlushContext flushContext = stagedScene.FlushContext;
        int targetWidth = encodedScene.TargetSize.Width;
        int targetHeight = encodedScene.TargetSize.Height;

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged-scene fine pass.";
            return false;
        }

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

        WebGPUDrawingBackend.CopyTextureRegion(
            flushContext,
            outputTexture,
            0,
            0,
            flushContext.TargetTexture,
            flushContext.TargetBounds.X,
            flushContext.TargetBounds.Y,
            targetWidth,
            targetHeight);

        return WebGPUDrawingBackend.TrySubmit(flushContext);
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
    /// <param name="requiresGrowth">Receives whether the chunked path needs the caller to retry with larger global scratch capacities.</param>
    /// <param name="grownBumpSizes">Receives the enlarged scratch capacities when <paramref name="requiresGrowth"/> is <see langword="true"/>.</param>
    /// <param name="error">Receives the chunked-render failure reason when the oversized scene cannot be completed.</param>
    /// <returns><see langword="true"/> when every chunk rendered successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryRenderSegmentChunkedStagedScene(
        ref WebGPUStagedScene stagedScene,
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

        WebGPUDrawingBackend.CopyTextureRegion(
            flushContext,
            flushContext.TargetTexture,
            flushContext.TargetBounds.X,
            flushContext.TargetBounds.Y,
            outputTexture,
            0,
            0,
            targetWidth,
            targetHeight);

        if (!WebGPUDrawingBackend.TrySubmit(flushContext))
        {
            error = "Failed to submit the staged-scene chunk prefill copy.";
            return false;
        }

        uint nextChunkTileHeight = GetInitialSegmentChunkTileHeight(encodedScene, stagedScene.BindingLimitFailure);
        WebGPUSceneBumpSizes lastSuccessfulChunkBumpSizes = stagedScene.Config.BumpSizes;
        uint lastSuccessfulChunkTileHeight = 0U;
        uint tileYStart = 0U;
        uint totalTileHeight = checked((uint)encodedScene.TileCountY);
        while (tileYStart < totalTileHeight)
        {
            uint remainingTileHeight = totalTileHeight - tileYStart;
            uint requestedTileHeight = Math.Min(nextChunkTileHeight, remainingTileHeight);
            WebGPUSceneBumpSizes chunkBumpSizes = requestedTileHeight == lastSuccessfulChunkTileHeight
                ? lastSuccessfulChunkBumpSizes
                : ScaleChunkBumpSizes(stagedScene.Config.BumpSizes, encodedScene, requestedTileHeight);
            while (true)
            {
                WebGPUSceneChunkWindow chunkWindow = CreateChunkWindow(tileYStart, requestedTileHeight, remainingTileHeight);
                WebGPUSceneConfig chunkConfig = WebGPUSceneConfig.Create(encodedScene, chunkBumpSizes, chunkWindow);
                if (!TryValidateBindingSizes(encodedScene, chunkConfig, maxStorageBufferBindingSize, out BindingLimitFailure bindingLimitFailure, out error))
                {
                    if (bindingLimitFailure.Buffer == BindingLimitBuffer.Segments)
                    {
                        uint smallerTileHeight = ShrinkChunkTileHeight(requestedTileHeight, remainingTileHeight, bindingLimitFailure);
                        if (smallerTileHeight >= requestedTileHeight)
                        {
                            return false;
                        }

                        requestedTileHeight = smallerTileHeight;
                        chunkBumpSizes = requestedTileHeight == lastSuccessfulChunkTileHeight
                            ? lastSuccessfulChunkBumpSizes
                            : ScaleChunkBumpSizes(stagedScene.Config.BumpSizes, encodedScene, requestedTileHeight);
                        continue;
                    }

                    return false;
                }

                WebGPUStagedScene chunkScene = new(flushContext, encodedScene, chunkConfig, stagedScene.Resources, BindingLimitFailure.None);
                if (TryRenderChunkAttempt(ref chunkScene, outputTextureView, out bool chunkRequiresGrowth, out WebGPUSceneBumpSizes grownChunkBumpSizes, out error))
                {
                    lastSuccessfulChunkBumpSizes = chunkConfig.BumpSizes;
                    lastSuccessfulChunkTileHeight = requestedTileHeight;
                    tileYStart += chunkWindow.TileHeight;
                    nextChunkTileHeight = requestedTileHeight;
                    break;
                }

                if (!chunkRequiresGrowth)
                {
                    return false;
                }

                chunkBumpSizes = grownChunkBumpSizes;
            }
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged-scene final copy.";
            return false;
        }

        WebGPUDrawingBackend.CopyTextureRegion(
            flushContext,
            outputTexture,
            0,
            0,
            flushContext.TargetTexture,
            flushContext.TargetBounds.X,
            flushContext.TargetBounds.Y,
            targetWidth,
            targetHeight);

        return WebGPUDrawingBackend.TrySubmit(flushContext);
    }

    /// <summary>
    /// Executes one chunk attempt by uploading the chunk header, running scheduling, and fine-rendering into the shared output texture.
    /// </summary>
    /// <param name="stagedScene">The chunk-scoped staged scene describing the tile-row window being rendered.</param>
    /// <param name="outputTextureView">The shared output texture view receiving fine-pass results for every chunk.</param>
    /// <param name="requiresGrowth">Receives whether this chunk reported that its scratch capacities were too small.</param>
    /// <param name="grownBumpSizes">Receives the enlarged chunk-local scratch capacities when <paramref name="requiresGrowth"/> is <see langword="true"/>.</param>
    /// <param name="error">Receives the chunk failure reason when scheduling or fine dispatch cannot complete.</param>
    /// <returns><see langword="true"/> when this chunk rendered successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryRenderChunkAttempt(
        ref WebGPUStagedScene stagedScene,
        TextureView* outputTextureView,
        out bool requiresGrowth,
        out WebGPUSceneBumpSizes grownBumpSizes,
        out string? error)
    {
        requiresGrowth = false;
        grownBumpSizes = stagedScene.Config.BumpSizes;
        error = null;

        if (!TryWriteSceneHeader(stagedScene.FlushContext, stagedScene.Resources.HeaderBuffer, WebGPUSceneResources.CreateHeader(stagedScene.EncodedScene, stagedScene.Config, 0U), out error))
        {
            return false;
        }

        if (!TryDispatchSchedulingStages(ref stagedScene, out WebGPUSceneSchedulingResources scheduling, out error))
        {
            return false;
        }

        if (!TryReadSchedulingStatus(stagedScene.FlushContext, scheduling.BumpBuffer, out GpuSceneBumpAllocators bumpAllocators, out error))
        {
            return false;
        }

        if (RequiresScratchReallocation(in bumpAllocators, stagedScene.Config.BumpSizes))
        {
            requiresGrowth = true;
            grownBumpSizes = GrowBumpSizes(stagedScene.Config.BumpSizes, in bumpAllocators);
            error = "The staged WebGPU scene chunk needs larger scratch buffers and will be retried.";
            return false;
        }

        if (!stagedScene.FlushContext.EnsureCommandEncoder())
        {
            error = "Failed to create a command encoder for the staged-scene chunk fine pass.";
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

        return WebGPUDrawingBackend.TrySubmit(stagedScene.FlushContext);
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
        flushContext.Api.QueueWriteBuffer(flushContext.Queue, headerBuffer, 0, &header, headerSize);
        error = null;
        return true;
    }

    /// <summary>
    /// Chooses the first chunk height from the exact full-scene segments binding overflow.
    /// </summary>
    /// <param name="scene">The encoded scene whose full-scene segments binding exceeded the device limit.</param>
    /// <param name="bindingLimitFailure">The exact segments binding overflow reported during full-scene planning.</param>
    /// <returns>The initial tile-row chunk height to try for the oversized scene.</returns>
    private static uint GetInitialSegmentChunkTileHeight(WebGPUEncodedScene scene, BindingLimitFailure bindingLimitFailure)
    {
        uint fullTileHeight = checked((uint)scene.TileCountY);
        if (bindingLimitFailure.Buffer != BindingLimitBuffer.Segments || bindingLimitFailure.RequiredBytes == 0)
        {
            return fullTileHeight;
        }

        ulong usableBytes = Math.Max((ulong)bindingLimitFailure.LimitBytes - ((ulong)bindingLimitFailure.LimitBytes / 8UL), 1UL);
        uint estimatedTileHeight = checked((uint)Math.Max(1UL, (usableBytes * fullTileHeight) / (ulong)bindingLimitFailure.RequiredBytes));
        return AlignChunkTileHeight(Math.Min(estimatedTileHeight, fullTileHeight), fullTileHeight);
    }

    /// <summary>
    /// Shrinks the current chunk height after an exact chunk binding-limit failure.
    /// </summary>
    /// <param name="currentTileHeight">The chunk height that just overflowed the device binding limit.</param>
    /// <param name="remainingTileHeight">The number of tile rows still left to render in the oversized scene.</param>
    /// <param name="bindingLimitFailure">The exact binding-limit failure reported for the overflowing chunk.</param>
    /// <returns>A smaller tile-row chunk height to retry for the same scene region.</returns>
    private static uint ShrinkChunkTileHeight(uint currentTileHeight, uint remainingTileHeight, BindingLimitFailure bindingLimitFailure)
    {
        if (currentTileHeight <= 1U || bindingLimitFailure.RequiredBytes == 0)
        {
            return currentTileHeight;
        }

        ulong usableBytes = Math.Max((ulong)bindingLimitFailure.LimitBytes - ((ulong)bindingLimitFailure.LimitBytes / 8UL), 1UL);
        uint estimatedTileHeight = checked((uint)Math.Max(1UL, (usableBytes * currentTileHeight) / (ulong)bindingLimitFailure.RequiredBytes));
        uint alignedTileHeight = AlignChunkTileHeight(Math.Min(estimatedTileHeight, remainingTileHeight), remainingTileHeight);
        return alignedTileHeight < currentTileHeight ? alignedTileHeight : Math.Max(1U, currentTileHeight / 2U);
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
        uint chunkTileBufferHeight = AlignUp(chunkTileHeight, 16U);
        uint pathTileFloor = AddSizingSlack(ScaleCount((uint)Math.Max(scene.TotalTileMembershipCount, scene.PathCount), chunkTileBufferHeight, fullTileHeight));
        uint segmentFloor = AddSizingSlack(ScaleCount((uint)Math.Max(scene.TotalLineSliceCount, scene.LineCount), chunkTileBufferHeight, fullTileHeight));

        return new WebGPUSceneBumpSizes(
            sourceBumpSizes.Lines,
            sourceBumpSizes.Binning,
            Math.Max(ScaleCount(sourceBumpSizes.PathTiles, chunkTileBufferHeight, fullTileHeight), pathTileFloor),
            Math.Max(ScaleCount(sourceBumpSizes.SegCounts, chunkTileBufferHeight, fullTileHeight), segmentFloor),
            Math.Max(ScaleCount(sourceBumpSizes.Segments, chunkTileBufferHeight, fullTileHeight), segmentFloor),
            ScaleCount(sourceBumpSizes.BlendSpill, chunkTileBufferHeight, fullTileHeight),
            ScaleCount(sourceBumpSizes.Ptcl, chunkTileBufferHeight, fullTileHeight));
    }

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
    /// Aligns a chunk height down to one coarse bin when possible so tile-local scratch buffers stay bin-shaped.
    /// </summary>
    /// <param name="tileHeight">The candidate real chunk height, in tile rows.</param>
    /// <param name="maximumTileHeight">The maximum tile height allowed for this chunk.</param>
    /// <returns>The aligned chunk height, preserving short tail chunks when necessary.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignChunkTileHeight(uint tileHeight, uint maximumTileHeight)
    {
        if (tileHeight >= maximumTileHeight)
        {
            return maximumTileHeight;
        }

        if (tileHeight <= 16U)
        {
            return tileHeight;
        }

        uint alignedTileHeight = tileHeight & ~15U;
        return alignedTileHeight > 0U ? alignedTileHeight : 16U;
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
        => new(
            GrowBumpSize(currentSizes.Lines, bumpAllocators.Lines),
            GrowBumpSize(currentSizes.Binning, bumpAllocators.Binning),
            GrowBumpSize(currentSizes.PathTiles, bumpAllocators.Tile),
            GrowBumpSize(currentSizes.SegCounts, bumpAllocators.SegCounts),
            GrowBumpSize(currentSizes.Segments, bumpAllocators.Segments),
            GrowBumpSize(currentSizes.BlendSpill, bumpAllocators.BlendSpill),
            GrowBumpSize(currentSizes.Ptcl, bumpAllocators.Ptcl));

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

        uint nextSize = checked(requiredSize + Math.Max(requiredSize / 2U, 4096U));
        return nextSize > currentSize ? nextSize : checked(currentSize + 1U);
    }

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
        WgpuBuffer* binHeaderBuffer,
        nuint binHeaderBufferSize,
        WgpuBuffer* bumpBuffer,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[8];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, resources.DrawMonoidBuffer, drawMonoidBufferSize);
        entries[2] = CreateBufferBinding(2, resources.PathBboxBuffer, pathBboxBufferSize);
        entries[3] = CreateBufferBinding(3, resources.ClipBboxBuffer, clipBboxBufferSize);
        entries[4] = CreateBufferBinding(4, resources.DrawBboxBuffer, drawBboxBufferSize);
        entries[5] = CreateBufferBinding(5, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[6] = CreateBufferBinding(6, resources.InfoBinDataBuffer, infoBinDataBufferSize);
        entries[7] = CreateBufferBinding(7, binHeaderBuffer, binHeaderBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.Binning, entries, 8, dispatchX, 1, 1, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Reads back the bump allocators after scheduling so the backend can decide whether to retry with larger scratch buffers.
    /// </summary>
    /// <param name="flushContext">The flush context that owns the command encoder and device.</param>
    /// <param name="bumpBuffer">The GPU buffer storing the scheduling-stage bump allocators.</param>
    /// <param name="bumpAllocators">Receives the counters reported by the GPU.</param>
    /// <param name="error">Receives the readback failure reason when the status cannot be read.</param>
    /// <returns><see langword="true"/> when the scheduling status was read successfully; otherwise, <see langword="false"/>.</returns>
    private static unsafe bool TryReadSchedulingStatus(
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        out GpuSceneBumpAllocators bumpAllocators,
        out string? error)
    {
        bumpAllocators = default;

        BufferDescriptor readbackDescriptor = new()
        {
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
            Size = (nuint)sizeof(GpuSceneBumpAllocators),
            MappedAtCreation = false
        };

        WgpuBuffer* readbackBuffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in readbackDescriptor);
        if (readbackBuffer is null)
        {
            error = "Failed to create the staged-scene scheduling readback buffer.";
            return false;
        }

        try
        {
            flushContext.EndComputePassIfOpen();
            flushContext.Api.CommandEncoderCopyBufferToBuffer(
                flushContext.CommandEncoder,
                bumpBuffer,
                0,
                readbackBuffer,
                0,
                (nuint)sizeof(GpuSceneBumpAllocators));

            if (!WebGPUDrawingBackend.TrySubmit(flushContext))
            {
                error = "Failed to submit staged-scene scheduling work.";
                return false;
            }

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
            if (!WaitForMapSignal(flushContext.RuntimeLease.WgpuExtension, flushContext.Device, mapReady) || mapStatus != BufferMapAsyncStatus.Success)
            {
                error = $"Failed to map staged-scene scheduling status with status '{mapStatus}'.";
                return false;
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
        finally
        {
            flushContext.Api.BufferRelease(readbackBuffer);
        }
    }

    private static unsafe bool TryDispatchTileAlloc(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        nuint sceneBufferSize,
        nuint drawBboxBufferSize,
        nuint pathBufferSize,
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
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
        entries[5] = CreateBufferBinding(5, pathTileBuffer, pathTileBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.TileAlloc, entries, 6, dispatchX, 1, 1, out error))
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
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[4];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateBufferBinding(2, resources.PathBuffer, pathBufferSize);
        entries[3] = CreateBufferBinding(3, pathTileBuffer, pathTileBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.Backdrop, entries, 4, dispatchX, 1, 1, out error))
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

    private static unsafe bool TryDispatchPathCount(
        WebGPUSceneComputeRecording recording,
        WebGPUFlushContext flushContext,
        WebGPUSceneResourceSet resources,
        WgpuBuffer* bumpBuffer,
        nuint pathBufferSize,
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        WgpuBuffer* segCountBuffer,
        nuint segCountBufferSize,
        nuint lineBufferSize,
        WgpuBuffer* indirectCountBuffer,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[6];
        entries[0] = CreateBufferBinding(0, resources.HeaderBuffer, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateBufferBinding(1, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateBufferBinding(2, resources.LineBuffer, lineBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathBuffer, pathBufferSize);
        entries[4] = CreateBufferBinding(4, pathTileBuffer, pathTileBufferSize);
        entries[5] = CreateBufferBinding(5, segCountBuffer, segCountBufferSize);

        if (!recording.TryRecordIndirect(WebGPUSceneShaderId.PathCount, entries, 6, indirectCountBuffer, 0, out error))
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
        WgpuBuffer* binHeaderBuffer,
        nuint binHeaderBufferSize,
        nuint pathBufferSize,
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
        entries[3] = CreateBufferBinding(3, binHeaderBuffer, binHeaderBufferSize);
        entries[4] = CreateBufferBinding(4, resources.InfoBinDataBuffer, infoBinDataBufferSize);
        entries[5] = CreateBufferBinding(5, resources.PathBuffer, pathBufferSize);
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
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* indirectCountBuffer,
        WgpuBuffer* ptclBuffer,
        nuint ptclBufferSize,
        uint dispatchX,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[3];
        entries[0] = CreateBufferBinding(0, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = CreateBufferBinding(1, indirectCountBuffer, (nuint)sizeof(GpuSceneIndirectCount));
        entries[2] = CreateBufferBinding(2, ptclBuffer, ptclBufferSize);

        if (!recording.TryRecord(WebGPUSceneShaderId.PathTilingSetup, entries, 3, dispatchX, 1, 1, out error))
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
        WgpuBuffer* pathTileBuffer,
        nuint pathTileBufferSize,
        WgpuBuffer* segmentBuffer,
        nuint segmentBufferSize,
        WgpuBuffer* indirectCountBuffer,
        out string? error)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[6];
        entries[0] = CreateBufferBinding(0, bumpBuffer, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = CreateBufferBinding(1, segCountBuffer, segCountBufferSize);
        entries[2] = CreateBufferBinding(2, resources.LineBuffer, lineBufferSize);
        entries[3] = CreateBufferBinding(3, resources.PathBuffer, pathBufferSize);
        entries[4] = CreateBufferBinding(4, pathTileBuffer, pathTileBufferSize);
        entries[5] = CreateBufferBinding(5, segmentBuffer, segmentBufferSize);

        if (!recording.TryRecordIndirect(WebGPUSceneShaderId.PathTiling, entries, 6, indirectCountBuffer, 0, out error))
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
        byte[] shaderCode;
        if (useAliasedThreshold)
        {
            if (!FineAliasedThresholdComputeShader.TryGetCode(flushContext.TextureFormat, out shaderCode, out error))
            {
                return false;
            }
        }
        else if (!FineAreaComputeShader.TryGetCode(flushContext.TextureFormat, out shaderCode, out error))
        {
            return false;
        }

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
        entries[3] = CreateBufferBinding(3, resources.InfoBinDataBuffer, checked(GetBindingByteLength<uint>(encodedScene.InfoWordCount) + bufferSizes.BinData.ByteLength));
        entries[4] = CreateBufferBinding(4, scheduling.BlendBuffer, bufferSizes.BlendSpill.ByteLength);
        entries[5] = new BindGroupEntry { Binding = 5, TextureView = outputTextureView };
        entries[6] = new BindGroupEntry { Binding = 6, TextureView = resources.GradientTextureView };
        entries[7] = new BindGroupEntry { Binding = 7, TextureView = resources.ImageAtlasTextureView };
        entries[8] = new BindGroupEntry { Binding = 8, TextureView = flushContext.TargetView };

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
        BindGroupDescriptor descriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = entryCount,
            Entries = entries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in descriptor);
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

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in descriptor);
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

                    BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in descriptor);
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
            WebGPUSceneShaderId.TileAlloc => "tile_alloc",
            WebGPUSceneShaderId.Backdrop => "backdrop",
            WebGPUSceneShaderId.PathCountSetup => "path_count_setup",
            WebGPUSceneShaderId.PathCount => "path_count",
            WebGPUSceneShaderId.Coarse => "coarse",
            WebGPUSceneShaderId.PathTilingSetup => "path_tiling_setup",
            WebGPUSceneShaderId.PathTiling => "path_tiling",
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
                WebGPUSceneShaderId.TileAlloc => TileAllocComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.Backdrop => BackdropComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathCountSetup => PathCountSetupComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathCount => PathCountComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.Coarse => CoarseComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
                WebGPUSceneShaderId.PathTiling => PathTilingComputeShader.TryCreateBindGroupLayout(api, device, out layout, out layoutError),
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
            WebGPUSceneShaderId.TileAlloc => TileAllocComputeShader.ShaderCode,
            WebGPUSceneShaderId.Backdrop => BackdropComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathCountSetup => PathCountSetupComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathCount => PathCountComputeShader.ShaderCode,
            WebGPUSceneShaderId.Coarse => CoarseComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupComputeShader.ShaderCode,
            WebGPUSceneShaderId.PathTiling => PathTilingComputeShader.ShaderCode,
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
            WebGPUSceneShaderId.TileAlloc => TileAllocComputeShader.EntryPoint,
            WebGPUSceneShaderId.Backdrop => BackdropComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathCountSetup => PathCountSetupComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathCount => PathCountComputeShader.EntryPoint,
            WebGPUSceneShaderId.Coarse => CoarseComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupComputeShader.EntryPoint,
            WebGPUSceneShaderId.PathTiling => PathTilingComputeShader.EntryPoint,
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
            WebGPUSceneShaderId.TileAlloc => TileAllocPipelineKey,
            WebGPUSceneShaderId.Backdrop => BackdropPipelineKey,
            WebGPUSceneShaderId.PathCountSetup => PathCountSetupPipelineKey,
            WebGPUSceneShaderId.PathCount => PathCountPipelineKey,
            WebGPUSceneShaderId.Coarse => CoarsePipelineKey,
            WebGPUSceneShaderId.PathTilingSetup => PathTilingSetupPipelineKey,
            WebGPUSceneShaderId.PathTiling => PathTilingPipelineKey,
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
    /// Creates a flush-scoped storage buffer that may later be read back or rewritten by staging passes.
    /// </summary>
    private static unsafe bool TryCreateStorageBuffer(
        WebGPUFlushContext flushContext,
        nuint size,
        out WgpuBuffer* buffer,
        out string? error)
        => TryCreateBuffer(
            flushContext,
            size,
            BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst,
            out buffer,
            out error);

    /// <summary>
    /// Creates a flush-scoped storage buffer that can also serve as an indirect dispatch argument buffer.
    /// </summary>
    private static unsafe bool TryCreateIndirectStorageBuffer(
        WebGPUFlushContext flushContext,
        nuint size,
        out WgpuBuffer* buffer,
        out string? error)
        => TryCreateBuffer(
            flushContext,
            size,
            BufferUsage.Storage | BufferUsage.Indirect | BufferUsage.CopyDst,
            out buffer,
            out error);

    /// <summary>
    /// Creates one flush-scoped buffer, promoting zero-byte requests to a one-word allocation for WebGPU validation.
    /// </summary>
    private static unsafe bool TryCreateBuffer(
        WebGPUFlushContext flushContext,
        nuint size,
        BufferUsage usage,
        out WgpuBuffer* buffer,
        out string? error)
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

        buffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in descriptor);
        if (buffer is null)
        {
            error = "Failed to create a staged-scene buffer.";
            return false;
        }

        flushContext.TrackBuffer(buffer);
        error = null;
        return true;
    }

    /// <summary>
    /// Creates one flush-scoped storage buffer and uploads a single unmanaged value into it.
    /// </summary>
    private static unsafe bool TryCreateAndUploadStorageBuffer<T>(
        WebGPUFlushContext flushContext,
        in T value,
        out WgpuBuffer* buffer,
        out string? error)
        where T : unmanaged
    {
        if (!TryCreateStorageBuffer(flushContext, (nuint)sizeof(T), out buffer, out error))
        {
            return false;
        }

        flushContext.Api.QueueWriteBuffer(
            flushContext.Queue,
            buffer,
            0,
            Unsafe.AsPointer(ref Unsafe.AsRef(in value)),
            (nuint)sizeof(T));
        error = null;
        return true;
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
    TileAlloc = 13,
    Backdrop = 14,
    PathCountSetup = 15,
    PathCount = 16,
    Coarse = 17,
    PathTilingSetup = 18,
    PathTiling = 19
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
        WgpuBuffer* pathTileBuffer,
        WgpuBuffer* segCountBuffer,
        WgpuBuffer* segmentBuffer,
        WgpuBuffer* blendBuffer,
        WgpuBuffer* ptclBuffer,
        WgpuBuffer* bumpBuffer)
    {
        this.RegisterBuffer(binHeaderBuffer);
        this.RegisterBuffer(indirectCountBuffer);
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
        if (buffer is null)
        {
            return;
        }

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
        if (textureView is null)
        {
            return;
        }

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
    {
        this.FlushContext = flushContext;
        this.EncodedScene = encodedScene;
        this.Config = config;
        this.Resources = resources;
        this.BindingLimitFailure = bindingLimitFailure;
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
    /// Releases the encoded scene and the flush context that owns the tracked native resources.
    /// </summary>
    public void Dispose()
    {
        this.EncodedScene.Dispose();
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
        WgpuBuffer* pathTileBuffer,
        WgpuBuffer* segCountBuffer,
        WgpuBuffer* segmentBuffer,
        WgpuBuffer* blendBuffer,
        WgpuBuffer* ptclBuffer,
        WgpuBuffer* bumpBuffer)
    {
        this.BinHeaderBuffer = binHeaderBuffer;
        this.IndirectCountBuffer = indirectCountBuffer;
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

#pragma warning restore SA1201
