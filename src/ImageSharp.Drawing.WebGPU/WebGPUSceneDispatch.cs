// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Phase-1 staged scene types are grouped by pipeline role.

using System.Diagnostics;
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
    internal const nuint MaxStorageBufferBindingSize = 128U * 1024U * 1024U;

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

    /// <summary>
    /// Builds the flush-scoped encoded scene and uploads its GPU resources.
    /// </summary>
    public static bool TryCreateStagedScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene scene,
        out WebGPUStagedScene stagedScene,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
        => TryCreateStagedScene(configuration, target, scene.Commands, out _, out stagedScene, out error);

    /// <summary>
    /// Builds the flush-scoped encoded scene and uploads its GPU resources.
    /// </summary>
    public static bool TryCreateStagedScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        IReadOnlyList<CompositionCommand> commands,
        out bool exceedsBindingLimit,
        out WebGPUStagedScene stagedScene,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        stagedScene = default;
        exceedsBindingLimit = false;
        WebGPUEncodedScene? encodedScene = null;

        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId, out FeatureName requiredFeature))
        {
            error = $"The staged WebGPU scene pipeline does not support pixel format '{typeof(TPixel).Name}'.";
            return false;
        }

        if (!WebGPUSceneEncoder.TryValidateBrushSupport(commands, out error))
        {
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
            encodedScene = WebGPUSceneEncoder.Encode(commands, flushContext.TargetBounds, flushContext.MemoryAllocator);
            WebGPUSceneConfig config = WebGPUSceneConfig.Create(encodedScene);
            uint baseColor = 0U;
            if (!TryValidateBindingSizes(encodedScene, config, out error))
            {
                exceedsBindingLimit = true;
                encodedScene.Dispose();
                flushContext.Dispose();
                stagedScene = default;
                return false;
            }

            if (encodedScene.FillCount == 0)
            {
                stagedScene = new WebGPUStagedScene(flushContext, encodedScene, config, default);
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

            stagedScene = new WebGPUStagedScene(flushContext, encodedScene, config, resources);
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
    public static bool TryValidateBindingSizes(
        WebGPUEncodedScene encodedScene,
        WebGPUSceneConfig config,
        out string? error)
    {
        WebGPUSceneBufferSizes bufferSizes = config.BufferSizes;
        nuint infoBinDataByteLength = checked(GetBindingByteLength<uint>(encodedScene.InfoWordCount) + config.BufferSizes.BinData.ByteLength);
        if (!TryValidateBufferSize(GetBindingByteLength<GpuSceneConfig>(1), "scene config", out error) ||
            !TryValidateBufferSize(GetBindingByteLength<uint>(encodedScene.SceneWordCount), "scene data", out error) ||
            !TryValidateBufferSize(bufferSizes.PathReduced.ByteLength, "path reduced", out error) ||
            !TryValidateBufferSize(bufferSizes.PathReduced2.ByteLength, "path reduced2", out error) ||
            !TryValidateBufferSize(bufferSizes.PathReducedScan.ByteLength, "path reduced scan", out error) ||
            !TryValidateBufferSize(bufferSizes.PathMonoids.ByteLength, "path monoids", out error) ||
            !TryValidateBufferSize(bufferSizes.PathBboxes.ByteLength, "path bboxes", out error) ||
            !TryValidateBufferSize(bufferSizes.DrawReduced.ByteLength, "draw reduced", out error) ||
            !TryValidateBufferSize(bufferSizes.DrawMonoids.ByteLength, "draw monoids", out error) ||
            !TryValidateBufferSize(infoBinDataByteLength, "scene info/bin data", out error) ||
            !TryValidateBufferSize(bufferSizes.ClipInputs.ByteLength, "clip inputs", out error) ||
            !TryValidateBufferSize(bufferSizes.ClipElements.ByteLength, "clip elements", out error) ||
            !TryValidateBufferSize(bufferSizes.ClipBics.ByteLength, "clip bics", out error) ||
            !TryValidateBufferSize(bufferSizes.ClipBboxes.ByteLength, "clip bboxes", out error) ||
            !TryValidateBufferSize(bufferSizes.DrawBboxes.ByteLength, "draw bboxes", out error) ||
            !TryValidateBufferSize(bufferSizes.Paths.ByteLength, "scene paths", out error) ||
            !TryValidateBufferSize(bufferSizes.Lines.ByteLength, "scene lines", out error) ||
            !TryValidateBufferSize(bufferSizes.BinHeaders.ByteLength, "bin headers", out error) ||
            !TryValidateBufferSize(bufferSizes.IndirectCount.ByteLength, "indirect count", out error) ||
            !TryValidateBufferSize(bufferSizes.PathTiles.ByteLength, "path tiles", out error) ||
            !TryValidateBufferSize(bufferSizes.SegCounts.ByteLength, "segment counts", out error) ||
            !TryValidateBufferSize(bufferSizes.Segments.ByteLength, "segments", out error) ||
            !TryValidateBufferSize(bufferSizes.BlendSpill.ByteLength, "blend spill", out error) ||
            !TryValidateBufferSize(bufferSizes.Ptcl.ByteLength, "ptcl", out error))
        {
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
            Blend = 0,
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
    public static unsafe bool TryRenderStagedScene(
        ref WebGPUStagedScene stagedScene,
        out string? error)
    {
        error = null;

        WebGPUEncodedScene encodedScene = stagedScene.EncodedScene;
        if (encodedScene.FillCount == 0)
        {
            return true;
        }

        if (!TryDispatchSchedulingStages(ref stagedScene, out WebGPUSceneSchedulingResources scheduling, out error))
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("IMAGE_SHARP_WEBGPU_DEBUG_SCHED"), "1", StringComparison.Ordinal))
        {
            if (!TryReadSchedulingStatus(stagedScene.FlushContext, scheduling.BumpBuffer, out GpuSceneBumpAllocators bumpAllocators, out error))
            {
                return false;
            }

            WebGPUEncodedScene debugScene = stagedScene.EncodedScene;
            error = $"scene fills={debugScene.FillCount} paths={debugScene.PathCount} lines={debugScene.LineCount} pathtag_bytes={debugScene.PathTagByteCount} pathtag_words={debugScene.PathTagWordCount} drawtags={debugScene.DrawTagCount} drawdata={debugScene.DrawDataWordCount} transforms={debugScene.TransformWordCount} styles={debugScene.StyleWordCount}; sched failed={bumpAllocators.Failed} binning={bumpAllocators.Binning} ptcl={bumpAllocators.Ptcl} tile={bumpAllocators.Tile} seg_counts={bumpAllocators.SegCounts} segments={bumpAllocators.Segments} blend={bumpAllocators.Blend} lines={bumpAllocators.Lines}";
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

        WebGPUDrawingBackend.CopyTextureRegion(
            flushContext,
            outputTexture,
            0,
            0,
            flushContext.TargetTexture,
            0,
            0,
            targetWidth,
            targetHeight);

        return WebGPUDrawingBackend.TrySubmit(flushContext);
    }

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
        if (!FineAreaComputeShader.TryGetCode(flushContext.TextureFormat, out byte[] shaderCode, out error))
        {
            return false;
        }

        bool LayoutFactory(WebGPU api, Device* device, out BindGroupLayout* layout, out string? layoutError)
            => FineAreaComputeShader.TryCreateBindGroupLayout(
                api,
                device,
                flushContext.TextureFormat,
                out layout,
                out layoutError);

        if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                $"{FineAreaPipelineKey}/{flushContext.TextureFormat}",
                shaderCode,
                FineAreaComputeShader.EntryPoint,
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

    private static unsafe bool TryExecuteComputeRecording(
        WebGPUFlushContext flushContext,
        WebGPUSceneComputeRecording recording,
        out string? error)
    {
        foreach (WebGPUSceneComputeCommand command in recording.Commands)
        {
            if (!TryResolveComputeShader(flushContext, command.ShaderId, out BindGroupLayout* bindGroupLayout, out ComputePipeline* pipeline, out error))
            {
                return false;
            }

            if (!flushContext.BeginComputePass())
            {
                error = "Failed to begin the staged-scene compute pass.";
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
                        error = "Failed to create a staged-scene compute bind group.";
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe BindGroupEntry CreateBufferBinding(uint binding, WgpuBuffer* buffer, nuint size)
        => new()
        {
            Binding = binding,
            Buffer = buffer,
            Offset = 0,
            Size = size
        };

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint GetBindingByteLength<T>(int count)
        where T : unmanaged
        => checked((nuint)Math.Max(count, 1) * (nuint)Unsafe.SizeOf<T>());

    private static bool TryValidateBufferSize(nuint byteLength, string bufferName, out string? error)
    {
        if (byteLength > MaxStorageBufferBindingSize)
        {
            error = $"The staged-scene {bufferName} buffer requires {byteLength} bytes, exceeding the current WebGPU binding limit of {MaxStorageBufferBindingSize} bytes.";
            return false;
        }

        error = null;
        return true;
    }

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

internal enum WebGPUSceneShaderId
{
    PathtagReduce = 0,
    PathtagReduce2 = 1,
    PathtagScan1 = 2,
    PathtagScan = 3,
    PathtagScanSmall = 4,
    BboxClear = 5,
    Flatten = 6,
    DrawReduce = 7,
    DrawLeaf = 8,
    ClipReduce = 9,
    ClipLeaf = 10,
    Binning = 11,
    TileAlloc = 12,
    Backdrop = 13,
    PathCountSetup = 14,
    PathCount = 15,
    Coarse = 16,
    PathTilingSetup = 17,
    PathTiling = 18
}

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

internal enum WebGPUSceneResourceProxyKind
{
    Buffer = 0,
    TextureView = 1
}

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

    public WebGPUSceneResourceProxy CreateProxy(BindGroupEntry entry)
        => entry.TextureView is not null
            ? WebGPUSceneResourceProxy.CreateTextureView(entry.Binding, this.GetTextureViewId(entry.TextureView))
            : WebGPUSceneResourceProxy.CreateBuffer(entry.Binding, this.GetBufferId(entry.Buffer), checked((nuint)entry.Offset), checked((nuint)entry.Size));

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
        WebGPUSceneResourceSet resources)
    {
        this.FlushContext = flushContext;
        this.EncodedScene = encodedScene;
        this.Config = config;
        this.Resources = resources;
    }

    public WebGPUFlushContext FlushContext { get; }

    public WebGPUEncodedScene EncodedScene { get; }

    public WebGPUSceneConfig Config { get; }

    public WebGPUSceneResourceSet Resources { get; }

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

    public WgpuBuffer* BinHeaderBuffer { get; }

    public WgpuBuffer* IndirectCountBuffer { get; }

    public WgpuBuffer* PathTileBuffer { get; }

    public WgpuBuffer* SegCountBuffer { get; }

    public WgpuBuffer* SegmentBuffer { get; }

    public WgpuBuffer* BlendBuffer { get; }

    public WgpuBuffer* PtclBuffer { get; }

    public WgpuBuffer* BumpBuffer { get; }
}

#pragma warning restore SA1201
