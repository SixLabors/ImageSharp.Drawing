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
    public WebGPUSceneConfig(WebGPUSceneWorkgroupCounts workgroupCounts, WebGPUSceneBufferSizes bufferSizes)
    {
        this.WorkgroupCounts = workgroupCounts;
        this.BufferSizes = bufferSizes;
    }

    public WebGPUSceneWorkgroupCounts WorkgroupCounts { get; }

    public WebGPUSceneBufferSizes BufferSizes { get; }

    public static WebGPUSceneConfig Create(WebGPUEncodedScene scene)
        => new(
            WebGPUSceneWorkgroupCounts.Create(scene),
            WebGPUSceneBufferSizes.Create(scene));
}

/// <summary>
/// Dispatch sizes for the currently implemented staged scene passes.
/// </summary>
internal readonly struct WebGPUSceneWorkgroupCounts
{
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

    public bool UseLargePathScan { get; }

    public uint PathReduceX { get; }

    public uint PathReduce2X { get; }

    public uint PathScan1X { get; }

    public uint PathScanX { get; }

    public uint BboxClearX { get; }

    public uint FlattenX { get; }

    public uint DrawReduceX { get; }

    public uint DrawLeafX { get; }

    public uint ClipReduceX { get; }

    public uint ClipLeafX { get; }

    public uint BinningX { get; }

    public uint TileAllocX { get; }

    public uint PathCountSetupX { get; }

    public uint PathCountX { get; }

    public uint BackdropX { get; }

    public uint CoarseX { get; }

    public uint CoarseY { get; }

    public uint PathTilingSetupX { get; }

    public uint PathTilingX { get; }

    public uint FineX { get; }

    public uint FineY { get; }

    public static WebGPUSceneWorkgroupCounts Create(WebGPUEncodedScene scene)
    {
        uint drawObjectCount = checked((uint)scene.DrawTagCount);
        uint pathCount = checked((uint)scene.PathCount);
        uint lineCount = checked((uint)scene.LineCount);
        uint clipCount = checked((uint)scene.ClipCount);
        uint pathTagCount = checked((uint)scene.PathTagByteCount);
        uint pathTagPadded = AlignUp(pathTagCount, 4U * 256U);
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
        uint heightInBins = DivideRoundUp(checked((uint)scene.TileCountY), 16U);

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
            TileAllocComputeShader.GetDispatchX(drawObjectCount),
            PathCountSetupComputeShader.GetDispatchX(),
            PathCountComputeShader.GetDispatchX(lineCount),
            BackdropComputeShader.GetDispatchX(pathCount),
            widthInBins,
            heightInBins,
            PathTilingSetupComputeShader.GetDispatchX(),
            1,
            checked((uint)scene.TileCountX),
            checked((uint)scene.TileCountY));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DivideRoundUp(uint value, uint divisor)
        => (value + divisor - 1U) / divisor;

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
        this.PathTiles = pathTiles;
        this.SegCounts = segCounts;
        this.Segments = segments;
        this.BlendSpill = blendSpill;
        this.Ptcl = ptcl;
    }

    public WebGPUSceneBufferSize<GpuTagMonoid> PathReduced { get; }

    public WebGPUSceneBufferSize<GpuTagMonoid> PathReduced2 { get; }

    public WebGPUSceneBufferSize<GpuTagMonoid> PathReducedScan { get; }

    public WebGPUSceneBufferSize<GpuTagMonoid> PathMonoids { get; }

    public WebGPUSceneBufferSize<GpuPathBbox> PathBboxes { get; }

    public WebGPUSceneBufferSize<GpuSceneDrawMonoid> DrawReduced { get; }

    public WebGPUSceneBufferSize<GpuSceneDrawMonoid> DrawMonoids { get; }

    public WebGPUSceneBufferSize<uint> Info { get; }

    public WebGPUSceneBufferSize<GpuClipInp> ClipInputs { get; }

    public WebGPUSceneBufferSize<GpuClipElement> ClipElements { get; }

    public WebGPUSceneBufferSize<GpuBic> ClipBics { get; }

    public WebGPUSceneBufferSize<Vector4> ClipBboxes { get; }

    public WebGPUSceneBufferSize<GpuDrawBbox> DrawBboxes { get; }

    public WebGPUSceneBufferSize<GpuSceneBumpAllocators> BumpAlloc { get; }

    public WebGPUSceneBufferSize<GpuScenePath> Paths { get; }

    public WebGPUSceneBufferSize<GpuSceneLine> Lines { get; }

    public WebGPUSceneBufferSize<GpuSceneBinHeader> BinHeaders { get; }

    public WebGPUSceneBufferSize<uint> BinData { get; }

    public WebGPUSceneBufferSize<GpuSceneIndirectCount> IndirectCount { get; }

    public WebGPUSceneBufferSize<GpuPathTile> PathTiles { get; }

    public WebGPUSceneBufferSize<GpuSegmentCount> SegCounts { get; }

    public WebGPUSceneBufferSize<GpuPathSegment> Segments { get; }

    public WebGPUSceneBufferSize<uint> BlendSpill { get; }

    public WebGPUSceneBufferSize<uint> Ptcl { get; }

    public static WebGPUSceneBufferSizes Create(WebGPUEncodedScene scene)
    {
        WebGPUSceneWorkgroupCounts workgroupCounts = WebGPUSceneWorkgroupCounts.Create(scene);
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
        uint linesCount = 1U << 21;
        uint binDataCount = 1U << 18;
        uint pathTileCount = 1U << 21;
        uint segmentCount = 1U << 21;
        uint segmentCapacity = 1U << 21;
        uint blendSpillCount = 1U << 20;
        uint ptclCapacity = 1U << 23;

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
            WebGPUSceneBufferSize<GpuSceneLine>.Create(linesCount),
            WebGPUSceneBufferSize<GpuSceneBinHeader>.Create(binHeaderCount),
            WebGPUSceneBufferSize<uint>.Create(binDataCount),
            WebGPUSceneBufferSize<GpuSceneIndirectCount>.Create(1),
            WebGPUSceneBufferSize<GpuPathTile>.Create(pathTileCount),
            WebGPUSceneBufferSize<GpuSegmentCount>.Create(segmentCount),
            WebGPUSceneBufferSize<GpuPathSegment>.Create(segmentCapacity),
            WebGPUSceneBufferSize<uint>.Create(blendSpillCount),
            WebGPUSceneBufferSize<uint>.Create(ptclCapacity));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignUp(uint value, uint alignment)
        => value + (uint)(-(int)value & (alignment - 1U));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DivideRoundUp(uint value, uint divisor)
        => value == 0U ? 0U : ((value - 1U) / divisor) + 1U;
}

/// <summary>
/// Typed buffer size primitive mirroring Vello's exact-count planning style.
/// </summary>
internal readonly struct WebGPUSceneBufferSize<T>
    where T : unmanaged
{
    private WebGPUSceneBufferSize(uint length)
    {
        // Storage bindings must remain non-zero-sized for validation.
        this.Length = length > 0 ? length : 1;
    }

    public uint Length { get; }

    public nuint ByteLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => checked((nuint)this.Length * (nuint)Unsafe.SizeOf<T>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WebGPUSceneBufferSize<T> Create(uint length) => new(length);
}
