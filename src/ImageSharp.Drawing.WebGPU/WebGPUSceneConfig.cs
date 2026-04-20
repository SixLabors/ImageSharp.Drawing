// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU-side sizing and dispatch configuration for one staged WebGPU scene.
/// </summary>
internal readonly struct WebGPUSceneConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneConfig"/> struct.
    /// </summary>
    /// <param name="workgroupCounts">The per-pass dispatch sizes for the encoded scene.</param>
    /// <param name="bufferSizes">The planned GPU buffer sizes for this flush.</param>
    /// <param name="bumpSizes">The current dynamic scratch capacities to expose to the shaders.</param>
    /// <param name="chunkWindow">The tile-row window rendered by this staged-scene attempt.</param>
    public WebGPUSceneConfig(WebGPUSceneWorkgroupCounts workgroupCounts, WebGPUSceneBufferSizes bufferSizes, WebGPUSceneBumpSizes bumpSizes, WebGPUSceneChunkWindow chunkWindow)
    {
        this.WorkgroupCounts = workgroupCounts;
        this.BufferSizes = bufferSizes;
        this.BumpSizes = bumpSizes;
        this.ChunkWindow = chunkWindow;
    }

    /// <summary>
    /// Gets the dispatch sizes for each staged-scene compute pass.
    /// </summary>
    public WebGPUSceneWorkgroupCounts WorkgroupCounts { get; }

    /// <summary>
    /// Gets the planned GPU buffer sizes for this encoded scene.
    /// </summary>
    public WebGPUSceneBufferSizes BufferSizes { get; }

    /// <summary>
    /// Gets the current dynamic scratch capacities that back the staged pipeline's bump allocators.
    /// </summary>
    public WebGPUSceneBumpSizes BumpSizes { get; }

    /// <summary>
    /// Gets the tile-row window rendered by this staged-scene attempt.
    /// </summary>
    public WebGPUSceneChunkWindow ChunkWindow { get; }

    /// <summary>
    /// Creates the dispatch and buffer plan for one encoded scene.
    /// </summary>
    /// <param name="scene">The encoded scene whose dispatch sizes and buffers are being planned.</param>
    /// <param name="bumpSizes">The current dynamic scratch capacities for the staged pipeline.</param>
    /// <returns>A flush-scoped configuration describing dispatch sizes, buffer sizes, and scratch capacities.</returns>
    public static WebGPUSceneConfig Create(WebGPUEncodedScene scene, WebGPUSceneBumpSizes bumpSizes)
        => Create(scene, bumpSizes, WebGPUSceneChunkWindow.FullScene(scene));

    /// <summary>
    /// Creates the dispatch and buffer plan for one encoded scene and one tile-row render window.
    /// </summary>
    /// <param name="scene">The encoded scene whose dispatch sizes and buffers are being planned.</param>
    /// <param name="bumpSizes">The current dynamic scratch capacities for the staged pipeline.</param>
    /// <param name="chunkWindow">The tile-row window rendered by this staged-scene attempt.</param>
    /// <returns>A flush-scoped configuration describing dispatch sizes, buffer sizes, and scratch capacities.</returns>
    public static WebGPUSceneConfig Create(WebGPUEncodedScene scene, WebGPUSceneBumpSizes bumpSizes, WebGPUSceneChunkWindow chunkWindow)
        => new(
            WebGPUSceneWorkgroupCounts.Create(scene, chunkWindow),
            WebGPUSceneBufferSizes.Create(scene, bumpSizes, chunkWindow),
            bumpSizes,
            chunkWindow);
}

/// <summary>
/// Tile-row window rendered by one staged-scene attempt.
/// </summary>
internal readonly struct WebGPUSceneChunkWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneChunkWindow"/> struct.
    /// </summary>
    /// <param name="tileYStart">The first global tile row included in this attempt.</param>
    /// <param name="tileHeight">The number of real tile rows rendered by this attempt.</param>
    /// <param name="tileBufferHeight">The number of tile rows allocated in the scratch tile buffers for this attempt.</param>
    public WebGPUSceneChunkWindow(uint tileYStart, uint tileHeight, uint tileBufferHeight)
    {
        this.TileYStart = tileYStart;
        this.TileHeight = tileHeight;
        this.TileBufferHeight = tileBufferHeight;
    }

    /// <summary>
    /// Gets the first global tile row included in this attempt.
    /// </summary>
    public uint TileYStart { get; }

    /// <summary>
    /// Gets the number of real tile rows rendered by this attempt.
    /// </summary>
    public uint TileHeight { get; }

    /// <summary>
    /// Gets the tile-row height reserved in scratch buffers for this attempt.
    /// </summary>
    public uint TileBufferHeight { get; }

    /// <summary>
    /// Creates the full-scene render window.
    /// </summary>
    /// <param name="scene">The encoded scene whose full tile height is being rendered.</param>
    /// <returns>The tile window spanning the entire encoded scene.</returns>
    public static WebGPUSceneChunkWindow FullScene(WebGPUEncodedScene scene)
    {
        uint tileHeight = checked((uint)scene.TileCountY);
        return new WebGPUSceneChunkWindow(0U, tileHeight, tileHeight);
    }
}

/// <summary>
/// Flush-scoped scratch capacities for the staged scene's bump-allocated GPU buffers.
/// </summary>
internal readonly struct WebGPUSceneBumpSizes
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneBumpSizes"/> struct.
    /// </summary>
    /// <param name="lines">The flattened line buffer capacity.</param>
    /// <param name="binning">The bin-data scratch capacity.</param>
    /// <param name="pathRows">The sparse path-row buffer capacity.</param>
    /// <param name="pathTiles">The path-tile buffer capacity.</param>
    /// <param name="segCounts">The segment-count buffer capacity.</param>
    /// <param name="segments">The segment buffer capacity.</param>
    /// <param name="blendSpill">The blend-spill buffer capacity.</param>
    /// <param name="ptcl">The dynamic PTCL tail capacity.</param>
    public WebGPUSceneBumpSizes(
        uint lines,
        uint binning,
        uint pathRows,
        uint pathTiles,
        uint segCounts,
        uint segments,
        uint blendSpill,
        uint ptcl)
    {
        this.Lines = lines;
        this.Binning = binning;
        this.PathRows = pathRows;
        this.PathTiles = pathTiles;
        this.SegCounts = segCounts;
        this.Segments = segments;
        this.BlendSpill = blendSpill;
        this.Ptcl = ptcl;
    }

    /// <summary>
    /// Gets the flattened line buffer capacity.
    /// </summary>
    public uint Lines { get; }

    /// <summary>
    /// Gets the bin-data scratch capacity.
    /// </summary>
    public uint Binning { get; }

    /// <summary>
    /// Gets the sparse path-row buffer capacity.
    /// </summary>
    public uint PathRows { get; }

    /// <summary>
    /// Gets the path-tile buffer capacity.
    /// </summary>
    public uint PathTiles { get; }

    /// <summary>
    /// Gets the segment-count buffer capacity.
    /// </summary>
    public uint SegCounts { get; }

    /// <summary>
    /// Gets the segment buffer capacity.
    /// </summary>
    public uint Segments { get; }

    /// <summary>
    /// Gets the blend-spill buffer capacity.
    /// </summary>
    public uint BlendSpill { get; }

    /// <summary>
    /// Gets the PTCL buffer capacity.
    /// </summary>
    public uint Ptcl { get; }

    /// <summary>
    /// Creates the initial capacities for the staged scene's bump-allocated scratch buffers.
    /// </summary>
    /// <remarks>
    /// These are startup sizes for the staged pipeline's transient GPU scratch memory, not fixed
    /// correctness limits. The dynamic-memory path will grow them when an earlier run reports that
    /// a scene needed more space.
    /// </remarks>
    public static WebGPUSceneBumpSizes Initial()
        => new(
            1U << 15,
            1U << 12,
            1U << 15,
            1U << 15,
            1U << 15,
            1U << 15,
            1U << 20,
            1U << 17);
}

/// <summary>
/// Dispatch sizes for the currently implemented staged scene passes.
/// </summary>
internal readonly struct WebGPUSceneWorkgroupCounts
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneWorkgroupCounts"/> struct.
    /// </summary>
    /// <param name="useLargePathScan">Whether the large pathtag scan variant is required for this scene.</param>
    /// <param name="pathReduceX">The workgroup count for the first pathtag reduction pass.</param>
    /// <param name="pathReduce2X">The workgroup count for the second pathtag reduction pass.</param>
    /// <param name="pathScan1X">The workgroup count for the large pathtag scan setup pass.</param>
    /// <param name="pathScanX">The workgroup count for the final pathtag scan pass.</param>
    /// <param name="bboxClearX">The workgroup count for the bbox clear pass.</param>
    /// <param name="flattenX">The workgroup count for the flatten pass.</param>
    /// <param name="drawReduceX">The workgroup count for the draw reduction pass.</param>
    /// <param name="drawLeafX">The workgroup count for the draw leaf pass.</param>
    /// <param name="clipReduceX">The workgroup count for the clip reduction pass.</param>
    /// <param name="clipLeafX">The workgroup count for the clip leaf pass.</param>
    /// <param name="binningX">The workgroup count for the binning pass.</param>
    /// <param name="pathRowAllocX">The workgroup count for the sparse path-row allocation pass.</param>
    /// <param name="tileAllocX">The workgroup count for the sparse path-tile allocation pass.</param>
    /// <param name="pathCountSetupX">The workgroup count for the line-driven indirect setup pass.</param>
    /// <param name="pathCountX">The workgroup count for the path-count pass.</param>
    /// <param name="backdropX">The workgroup count for the backdrop pass.</param>
    /// <param name="coarseX">The X workgroup count for the coarse pass.</param>
    /// <param name="coarseY">The Y workgroup count for the coarse pass.</param>
    /// <param name="pathTilingSetupX">The workgroup count for the path-tiling setup pass.</param>
    /// <param name="pathTilingX">The workgroup count for the path-tiling pass.</param>
    /// <param name="fineX">The X workgroup count for the fine pass.</param>
    /// <param name="fineY">The Y workgroup count for the fine pass.</param>
    public WebGPUSceneWorkgroupCounts(
        bool useLargePathScan,
        uint pathReduceX,
        uint pathReduce2X,
        uint pathScan1X,
        uint pathScanX,
        uint bboxClearX,
        uint flattenX,
        uint drawReduceX,
        uint drawLeafX,
        uint clipReduceX,
        uint clipLeafX,
        uint binningX,
        uint pathRowAllocX,
        uint tileAllocX,
        uint pathCountSetupX,
        uint pathCountX,
        uint backdropX,
        uint coarseX,
        uint coarseY,
        uint pathTilingSetupX,
        uint pathTilingX,
        uint fineX,
        uint fineY)
    {
        this.UseLargePathScan = useLargePathScan;
        this.PathReduceX = pathReduceX;
        this.PathReduce2X = pathReduce2X;
        this.PathScan1X = pathScan1X;
        this.PathScanX = pathScanX;
        this.BboxClearX = bboxClearX;
        this.FlattenX = flattenX;
        this.DrawReduceX = drawReduceX;
        this.DrawLeafX = drawLeafX;
        this.ClipReduceX = clipReduceX;
        this.ClipLeafX = clipLeafX;
        this.BinningX = binningX;
        this.PathRowAllocX = pathRowAllocX;
        this.TileAllocX = tileAllocX;
        this.PathCountSetupX = pathCountSetupX;
        this.PathCountX = pathCountX;
        this.BackdropX = backdropX;
        this.CoarseX = coarseX;
        this.CoarseY = coarseY;
        this.PathTilingSetupX = pathTilingSetupX;
        this.PathTilingX = pathTilingX;
        this.FineX = fineX;
        this.FineY = fineY;
    }

    /// <summary>
    /// Gets a value indicating whether the large pathtag scan variant is required for this scene.
    /// </summary>
    public bool UseLargePathScan { get; }

    /// <summary>
    /// Gets the workgroup count for the first pathtag reduction pass.
    /// </summary>
    public uint PathReduceX { get; }

    /// <summary>
    /// Gets the workgroup count for the second pathtag reduction pass.
    /// </summary>
    public uint PathReduce2X { get; }

    /// <summary>
    /// Gets the workgroup count for the large pathtag scan setup pass.
    /// </summary>
    public uint PathScan1X { get; }

    /// <summary>
    /// Gets the workgroup count for the final pathtag scan pass.
    /// </summary>
    public uint PathScanX { get; }

    /// <summary>
    /// Gets the workgroup count for the bbox clear pass.
    /// </summary>
    public uint BboxClearX { get; }

    /// <summary>
    /// Gets the workgroup count for the flatten pass.
    /// </summary>
    public uint FlattenX { get; }

    /// <summary>
    /// Gets the workgroup count for the draw reduction pass.
    /// </summary>
    public uint DrawReduceX { get; }

    /// <summary>
    /// Gets the workgroup count for the draw leaf pass.
    /// </summary>
    public uint DrawLeafX { get; }

    /// <summary>
    /// Gets the workgroup count for the clip reduction pass.
    /// </summary>
    public uint ClipReduceX { get; }

    /// <summary>
    /// Gets the workgroup count for the clip leaf pass.
    /// </summary>
    public uint ClipLeafX { get; }

    /// <summary>
    /// Gets the workgroup count for the binning pass.
    /// </summary>
    public uint BinningX { get; }

    /// <summary>
    /// Gets the workgroup count for the sparse path-row allocation pass.
    /// </summary>
    public uint PathRowAllocX { get; }

    /// <summary>
    /// Gets the workgroup count for the tile allocation pass.
    /// </summary>
    public uint TileAllocX { get; }

    /// <summary>
    /// Gets the workgroup count for the path-count setup pass.
    /// </summary>
    public uint PathCountSetupX { get; }

    /// <summary>
    /// Gets the workgroup count for the path-count pass.
    /// </summary>
    public uint PathCountX { get; }

    /// <summary>
    /// Gets the workgroup count for the backdrop pass.
    /// </summary>
    public uint BackdropX { get; }

    /// <summary>
    /// Gets the X workgroup count for the coarse pass.
    /// </summary>
    public uint CoarseX { get; }

    /// <summary>
    /// Gets the Y workgroup count for the coarse pass.
    /// </summary>
    public uint CoarseY { get; }

    /// <summary>
    /// Gets the workgroup count for the path-tiling setup pass.
    /// </summary>
    public uint PathTilingSetupX { get; }

    /// <summary>
    /// Gets the workgroup count for the path-tiling pass.
    /// </summary>
    public uint PathTilingX { get; }

    /// <summary>
    /// Gets the X workgroup count for the fine pass.
    /// </summary>
    public uint FineX { get; }

    /// <summary>
    /// Gets the Y workgroup count for the fine pass.
    /// </summary>
    public uint FineY { get; }

    /// <summary>
    /// Computes the workgroup counts required to run the staged-scene pipeline for one encoded scene.
    /// </summary>
    /// <param name="scene">The encoded scene whose dispatch sizes are being planned.</param>
    /// <param name="chunkWindow">The tile-row window rendered by this staged-scene attempt.</param>
    /// <returns>The per-pass workgroup counts for this encoded scene and chunk window.</returns>
    public static WebGPUSceneWorkgroupCounts Create(WebGPUEncodedScene scene, WebGPUSceneChunkWindow chunkWindow)
    {
        uint drawObjectCount = checked((uint)scene.DrawTagCount);
        uint pathCount = checked((uint)scene.PathCount);
        uint lineCount = checked((uint)scene.LineCount);
        uint clipCount = checked((uint)scene.ClipCount);
        uint pathTagCount = checked((uint)scene.PathTagByteCount);
        uint pathTagPadded = AlignUp(pathTagCount, 4U * 256U);

        // The pathtag scan has a small and large variant. We choose once here so every later
        // stage sizes its scratch buffers against the exact scan shape we will dispatch.
        uint pathTagWgs = pathTagPadded / (4U * 256U);
        bool useLargePathScan = pathTagWgs > 256U;
        uint reducedSize = useLargePathScan ? AlignUp(pathTagWgs, 256U) : pathTagWgs;
        uint drawObjectWgs = DivideRoundUp(drawObjectCount, 256U);
        uint drawMonoidWgs = Math.Min(drawObjectWgs, 256U);
        uint flattenWgs = DivideRoundUp(pathTagCount, 256U);
        uint clipReduceWgs = clipCount == 0U ? 0U : (clipCount - 1U) / 256U;
        uint clipWgs = DivideRoundUp(clipCount, 256U);
        uint pathWgs = DivideRoundUp(pathCount, 256U);
        uint widthInBins = DivideRoundUp(checked((uint)scene.TileCountX), 16U);
        uint heightInBins = DivideRoundUp(chunkWindow.TileBufferHeight, 16U);

        return new WebGPUSceneWorkgroupCounts(
            useLargePathScan,
            pathTagWgs,
            256U,
            reducedSize / 256U,
            pathTagWgs,
            drawObjectWgs,
            flattenWgs,
            drawMonoidWgs,
            drawMonoidWgs,
            clipReduceWgs,
            clipWgs,
            BinningComputeShader.GetDispatchX(drawObjectCount),
            PathRowAllocComputeShader.GetDispatchX(pathCount),
            TileAllocComputeShader.GetDispatchX(pathCount),
            PathCountSetupComputeShader.GetDispatchX(),
            PathCountComputeShader.GetDispatchX(lineCount),
            BackdropComputeShader.GetDispatchX(pathCount),
            widthInBins,
            heightInBins,
            PathTilingSetupComputeShader.GetDispatchX(),
            1,
            checked((uint)scene.TileCountX),
            chunkWindow.TileHeight);
    }

    /// <summary>
    /// Rounds up integer division for dispatch sizing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DivideRoundUp(uint value, uint divisor)
        => (value + divisor - 1U) / divisor;

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignUp(uint value, uint alignment)
        => value + (uint)(-(int)value & (alignment - 1U));
}

/// <summary>
/// Buffer sizes for the currently implemented staged scene passes.
/// </summary>
internal readonly struct WebGPUSceneBufferSizes
{
    public WebGPUSceneBufferSizes(
        WebGPUSceneBufferSize<GpuTagMonoid> pathReduced,
        WebGPUSceneBufferSize<GpuTagMonoid> pathReduced2,
        WebGPUSceneBufferSize<GpuTagMonoid> pathReducedScan,
        WebGPUSceneBufferSize<GpuTagMonoid> pathMonoids,
        WebGPUSceneBufferSize<GpuPathBbox> pathBboxes,
        WebGPUSceneBufferSize<GpuSceneDrawMonoid> drawReduced,
        WebGPUSceneBufferSize<GpuSceneDrawMonoid> drawMonoids,
        WebGPUSceneBufferSize<uint> info,
        WebGPUSceneBufferSize<GpuClipInp> clipInputs,
        WebGPUSceneBufferSize<GpuClipElement> clipElements,
        WebGPUSceneBufferSize<GpuBic> clipBics,
        WebGPUSceneBufferSize<Vector4> clipBboxes,
        WebGPUSceneBufferSize<GpuDrawBbox> drawBboxes,
        WebGPUSceneBufferSize<GpuSceneBumpAllocators> bumpAlloc,
        WebGPUSceneBufferSize<GpuScenePath> paths,
        WebGPUSceneBufferSize<GpuSceneLine> lines,
        WebGPUSceneBufferSize<GpuSceneBinHeader> binHeaders,
        WebGPUSceneBufferSize<uint> binData,
        WebGPUSceneBufferSize<GpuSceneIndirectCount> indirectCount,
        WebGPUSceneBufferSize<GpuPathRow> pathRows,
        WebGPUSceneBufferSize<GpuPathTile> pathTiles,
        WebGPUSceneBufferSize<GpuSegmentCount> segCounts,
        WebGPUSceneBufferSize<GpuPathSegment> segments,
        WebGPUSceneBufferSize<uint> blendSpill,
        WebGPUSceneBufferSize<uint> ptcl)
    {
        this.PathReduced = pathReduced;
        this.PathReduced2 = pathReduced2;
        this.PathReducedScan = pathReducedScan;
        this.PathMonoids = pathMonoids;
        this.PathBboxes = pathBboxes;
        this.DrawReduced = drawReduced;
        this.DrawMonoids = drawMonoids;
        this.Info = info;
        this.ClipInputs = clipInputs;
        this.ClipElements = clipElements;
        this.ClipBics = clipBics;
        this.ClipBboxes = clipBboxes;
        this.DrawBboxes = drawBboxes;
        this.BumpAlloc = bumpAlloc;
        this.Paths = paths;
        this.Lines = lines;
        this.BinHeaders = binHeaders;
        this.BinData = binData;
        this.IndirectCount = indirectCount;
        this.PathRows = pathRows;
        this.PathTiles = pathTiles;
        this.SegCounts = segCounts;
        this.Segments = segments;
        this.BlendSpill = blendSpill;
        this.Ptcl = ptcl;
    }

    /// <summary>
    /// Gets the size of the first pathtag reduction buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuTagMonoid> PathReduced { get; }

    /// <summary>
    /// Gets the size of the second pathtag reduction buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuTagMonoid> PathReduced2 { get; }

    /// <summary>
    /// Gets the size of the pathtag scan scratch buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuTagMonoid> PathReducedScan { get; }

    /// <summary>
    /// Gets the size of the final pathtag monoid buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuTagMonoid> PathMonoids { get; }

    /// <summary>
    /// Gets the size of the per-path bounding-box buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuPathBbox> PathBboxes { get; }

    /// <summary>
    /// Gets the size of the draw reduction buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuSceneDrawMonoid> DrawReduced { get; }

    /// <summary>
    /// Gets the size of the final draw monoid buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuSceneDrawMonoid> DrawMonoids { get; }

    /// <summary>
    /// Gets the size of the scene info-word buffer.
    /// </summary>
    public WebGPUSceneBufferSize<uint> Info { get; }

    /// <summary>
    /// Gets the size of the clip input buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuClipInp> ClipInputs { get; }

    /// <summary>
    /// Gets the size of the clip element buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuClipElement> ClipElements { get; }

    /// <summary>
    /// Gets the size of the clip bic buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuBic> ClipBics { get; }

    /// <summary>
    /// Gets the size of the clip bounding-box buffer.
    /// </summary>
    public WebGPUSceneBufferSize<Vector4> ClipBboxes { get; }

    /// <summary>
    /// Gets the size of the draw bounding-box buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuDrawBbox> DrawBboxes { get; }

    /// <summary>
    /// Gets the size of the bump allocator buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuSceneBumpAllocators> BumpAlloc { get; }

    /// <summary>
    /// Gets the size of the per-path scheduling buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuScenePath> Paths { get; }

    /// <summary>
    /// Gets the size of the flattened line buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuSceneLine> Lines { get; }

    /// <summary>
    /// Gets the size of the bin-header buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuSceneBinHeader> BinHeaders { get; }

    /// <summary>
    /// Gets the size of the bin-data scratch buffer.
    /// </summary>
    public WebGPUSceneBufferSize<uint> BinData { get; }

    /// <summary>
    /// Gets the size of the indirect dispatch-count buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuSceneIndirectCount> IndirectCount { get; }

    /// <summary>
    /// Gets the size of the sparse path-row buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuPathRow> PathRows { get; }

    /// <summary>
    /// Gets the size of the path-tile buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuPathTile> PathTiles { get; }

    /// <summary>
    /// Gets the size of the segment-count buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuSegmentCount> SegCounts { get; }

    /// <summary>
    /// Gets the size of the segment buffer.
    /// </summary>
    public WebGPUSceneBufferSize<GpuPathSegment> Segments { get; }

    /// <summary>
    /// Gets the size of the blend-spill buffer.
    /// </summary>
    public WebGPUSceneBufferSize<uint> BlendSpill { get; }

    /// <summary>
    /// Gets the size of the PTCL buffer.
    /// </summary>
    public WebGPUSceneBufferSize<uint> Ptcl { get; }

    /// <summary>
    /// Computes the GPU buffer sizes required by the current staged-scene pipeline.
    /// </summary>
    /// <param name="scene">The encoded scene whose buffer plan is being computed.</param>
    /// <param name="bumpSizes">The current dynamic scratch capacities for the staged pipeline.</param>
    /// <param name="chunkWindow">The tile-row window rendered by this staged-scene attempt.</param>
    /// <returns>The planned GPU buffer sizes for this encoded scene and chunk window.</returns>
    public static WebGPUSceneBufferSizes Create(WebGPUEncodedScene scene, WebGPUSceneBumpSizes bumpSizes, WebGPUSceneChunkWindow chunkWindow)
    {
        WebGPUSceneWorkgroupCounts workgroupCounts = WebGPUSceneWorkgroupCounts.Create(scene, chunkWindow);
        uint pathTagWgs = workgroupCounts.PathReduceX;
        uint reducedSize = workgroupCounts.UseLargePathScan ? AlignUp(pathTagWgs, 256U) : pathTagWgs;
        uint pathReducedCount = reducedSize;
        uint pathReduced2Count = 256U;
        uint pathReducedScanCount = reducedSize;
        uint pathMonoidCount = pathTagWgs * 256U;
        uint pathBboxCount = checked((uint)scene.PathCount);
        uint drawObjectCount = checked((uint)scene.DrawTagCount);
        uint drawReducedCount = workgroupCounts.DrawReduceX;
        uint drawMonoidCount = drawObjectCount;
        uint infoCount = checked((uint)scene.InfoWordCount);
        uint clipInputCount = checked((uint)scene.ClipCount);
        uint clipElementCount = checked((uint)scene.ClipCount);
        uint clipBicCount = clipInputCount / 256U;
        uint clipBboxCount = clipInputCount;
        uint drawBboxCount = drawObjectCount;
        uint drawObjectPartitions = BinningComputeShader.GetDispatchX(drawObjectCount);
        uint binHeaderCount = checked(drawObjectPartitions * 256U);
        uint pathCount = AlignUp(checked((uint)scene.PathCount), 256U);
        uint ptclBootstrapCount = checked((uint)scene.TileCountX * chunkWindow.TileBufferHeight * WebGPUSceneDispatch.PtclInitialAlloc);
        uint ptclCount = checked(bumpSizes.Ptcl + ptclBootstrapCount);

        return new WebGPUSceneBufferSizes(
            WebGPUSceneBufferSize<GpuTagMonoid>.Create(pathReducedCount),
            WebGPUSceneBufferSize<GpuTagMonoid>.Create(pathReduced2Count),
            WebGPUSceneBufferSize<GpuTagMonoid>.Create(pathReducedScanCount),
            WebGPUSceneBufferSize<GpuTagMonoid>.Create(pathMonoidCount),
            WebGPUSceneBufferSize<GpuPathBbox>.Create(pathBboxCount),
            WebGPUSceneBufferSize<GpuSceneDrawMonoid>.Create(drawReducedCount),
            WebGPUSceneBufferSize<GpuSceneDrawMonoid>.Create(drawMonoidCount),
            WebGPUSceneBufferSize<uint>.Create(infoCount),
            WebGPUSceneBufferSize<GpuClipInp>.Create(clipInputCount),
            WebGPUSceneBufferSize<GpuClipElement>.Create(clipElementCount),
            WebGPUSceneBufferSize<GpuBic>.Create(clipBicCount),
            WebGPUSceneBufferSize<Vector4>.Create(clipBboxCount),
            WebGPUSceneBufferSize<GpuDrawBbox>.Create(drawBboxCount),
            WebGPUSceneBufferSize<GpuSceneBumpAllocators>.Create(1),
            WebGPUSceneBufferSize<GpuScenePath>.Create(pathCount),
            WebGPUSceneBufferSize<GpuSceneLine>.Create(bumpSizes.Lines),
            WebGPUSceneBufferSize<GpuSceneBinHeader>.Create(binHeaderCount),
            WebGPUSceneBufferSize<uint>.Create(bumpSizes.Binning),
            WebGPUSceneBufferSize<GpuSceneIndirectCount>.Create(1),
            WebGPUSceneBufferSize<GpuPathRow>.Create(bumpSizes.PathRows),
            WebGPUSceneBufferSize<GpuPathTile>.Create(bumpSizes.PathTiles),
            WebGPUSceneBufferSize<GpuSegmentCount>.Create(bumpSizes.SegCounts),
            WebGPUSceneBufferSize<GpuPathSegment>.Create(bumpSizes.Segments),
            WebGPUSceneBufferSize<uint>.Create(bumpSizes.BlendSpill),
            WebGPUSceneBufferSize<uint>.Create(ptclCount));
    }

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignUp(uint value, uint alignment)
        => value + (uint)(-(int)value & (alignment - 1U));
}

/// <summary>
/// Typed buffer size primitive mirroring Vello's exact-count planning style.
/// </summary>
internal readonly struct WebGPUSceneBufferSize<T>
    where T : unmanaged
{
    private WebGPUSceneBufferSize(uint length)

        // Storage bindings must remain non-zero-sized for validation.
        => this.Length = length > 0 ? length : 1;

    /// <summary>
    /// Gets the element count reserved for this buffer binding.
    /// </summary>
    public uint Length { get; }

    /// <summary>
    /// Gets the binding size in bytes for the padded non-zero element count.
    /// </summary>
    public nuint ByteLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => checked(this.Length * (nuint)Unsafe.SizeOf<T>());
    }

    /// <summary>
    /// Creates a buffer-size wrapper that preserves WebGPU's non-zero storage-binding requirement.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WebGPUSceneBufferSize<T> Create(uint length) => new(length);
}
