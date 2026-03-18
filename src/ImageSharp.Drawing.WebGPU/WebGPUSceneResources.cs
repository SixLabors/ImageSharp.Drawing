// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Phase-1 staged scene types are grouped by pipeline role.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Uploads one encoded scene into flush-scoped GPU resources for the staged WebGPU rasterizer.
/// </summary>
internal static unsafe class WebGPUSceneResources
{
    /// <summary>
    /// Creates the flush-scoped GPU resources required by the staged scene pipeline.
    /// </summary>
    public static bool TryCreate<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        WebGPUSceneConfig config,
        out WebGPUSceneResourceSet resources,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        resources = default;

        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId expectedFormatId))
        {
            error = $"The staged WebGPU scene pipeline does not support pixel format '{typeof(TPixel).Name}'.";
            return false;
        }

        TextureFormat expectedTextureFormat = WebGPUTextureFormatMapper.ToSilk(expectedFormatId);
        if (flushContext.TextureFormat != expectedTextureFormat)
        {
            error = $"Scene resource texture format '{flushContext.TextureFormat}' does not match the required '{expectedTextureFormat}' for pixel type '{typeof(TPixel).Name}'.";
            return false;
        }

        if (!TryCreateAndUploadCombinedInfoBinDataBuffer(flushContext, scene.InfoWordCount, config.BufferSizes.BinData.ByteLength, out WgpuBuffer* infoBinDataBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<uint>(flushContext, scene.SceneData.Span, (uint)scene.SceneData.Length, out WgpuBuffer* sceneBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, ReadOnlySpan<GpuTagMonoid>.Empty, config.BufferSizes.PathReduced.Length, out WgpuBuffer* pathReducedBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, ReadOnlySpan<GpuTagMonoid>.Empty, config.BufferSizes.PathReduced2.Length, out WgpuBuffer* pathReduced2Buffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, ReadOnlySpan<GpuTagMonoid>.Empty, config.BufferSizes.PathReducedScan.Length, out WgpuBuffer* pathReducedScanBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, ReadOnlySpan<GpuTagMonoid>.Empty, config.BufferSizes.PathMonoids.Length, out WgpuBuffer* pathMonoidBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuPathBbox>(flushContext, ReadOnlySpan<GpuPathBbox>.Empty, config.BufferSizes.PathBboxes.Length, out WgpuBuffer* pathBboxBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuSceneDrawMonoid>(flushContext, ReadOnlySpan<GpuSceneDrawMonoid>.Empty, config.BufferSizes.DrawReduced.Length, out WgpuBuffer* drawReducedBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuSceneDrawMonoid>(flushContext, ReadOnlySpan<GpuSceneDrawMonoid>.Empty, config.BufferSizes.DrawMonoids.Length, out WgpuBuffer* drawMonoidBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuClipInp>(flushContext, ReadOnlySpan<GpuClipInp>.Empty, config.BufferSizes.ClipInputs.Length, out WgpuBuffer* clipInputBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuClipElement>(flushContext, ReadOnlySpan<GpuClipElement>.Empty, config.BufferSizes.ClipElements.Length, out WgpuBuffer* clipElementBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuBic>(flushContext, ReadOnlySpan<GpuBic>.Empty, config.BufferSizes.ClipBics.Length, out WgpuBuffer* clipBicBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<Vector4>(flushContext, ReadOnlySpan<Vector4>.Empty, config.BufferSizes.ClipBboxes.Length, out WgpuBuffer* clipBboxBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuDrawBbox>(flushContext, ReadOnlySpan<GpuDrawBbox>.Empty, config.BufferSizes.DrawBboxes.Length, out WgpuBuffer* drawBboxBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuScenePath>(flushContext, ReadOnlySpan<GpuScenePath>.Empty, config.BufferSizes.Paths.Length, out WgpuBuffer* pathBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuSceneLine>(flushContext, ReadOnlySpan<GpuSceneLine>.Empty, config.BufferSizes.Lines.Length, out WgpuBuffer* lineBuffer, out error))
        {
            return false;
        }

        if (!TryCreateTransparentSampledTexture(flushContext, TextureFormat.Rgba8Unorm, out Texture* auxiliaryTexture, out TextureView* auxiliaryTextureView, out error))
        {
            return false;
        }

        GpuSceneConfig header = new(
            (uint)scene.TileCountX,
            (uint)scene.TileCountY,
            (uint)scene.TargetSize.Width,
            (uint)scene.TargetSize.Height,
            0U,
            scene.Layout,
            config.BufferSizes.Lines.Length,
            config.BufferSizes.BinData.Length,
            config.BufferSizes.PathTiles.Length,
            config.BufferSizes.SegCounts.Length,
            config.BufferSizes.Segments.Length,
            config.BufferSizes.BlendSpill.Length,
            config.BufferSizes.Ptcl.Length);

        if (!TryCreateAndUploadScalarBuffer(flushContext, in header, out WgpuBuffer* headerBuffer, out error))
        {
            return false;
        }

        resources = new WebGPUSceneResourceSet(
            headerBuffer,
            sceneBuffer,
            pathReducedBuffer,
            pathReduced2Buffer,
            pathReducedScanBuffer,
            pathMonoidBuffer,
            pathBboxBuffer,
            drawReducedBuffer,
            drawMonoidBuffer,
            infoBinDataBuffer,
            clipInputBuffer,
            clipElementBuffer,
            clipBicBuffer,
            clipBboxBuffer,
            drawBboxBuffer,
            pathBuffer,
            lineBuffer,
            auxiliaryTextureView);
        error = null;
        return true;
    }

    private static bool TryCreateAndUploadCombinedInfoBinDataBuffer(
        WebGPUFlushContext flushContext,
        int infoWordCount,
        nuint dynamicBinByteLength,
        out WgpuBuffer* buffer,
        out string? error)
    {
        nuint infoByteLength = checked((nuint)infoWordCount * (nuint)Unsafe.SizeOf<uint>());
        nuint totalByteLength = checked(infoByteLength + dynamicBinByteLength);
        if (totalByteLength == 0)
        {
            totalByteLength = (nuint)Unsafe.SizeOf<uint>();
        }

        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            Size = totalByteLength
        };

        buffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in descriptor);
        if (buffer is null)
        {
            error = "Failed to create the staged-scene info/bin-data buffer.";
            return false;
        }

        flushContext.TrackBuffer(buffer);
        error = null;
        return true;
    }

    private static bool TryCreateTransparentSampledTexture(
        WebGPUFlushContext flushContext,
        TextureFormat textureFormat,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
    {
        TextureDescriptor textureDescriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(1, 1, 1),
            Format = textureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        texture = flushContext.Api.DeviceCreateTexture(flushContext.Device, in textureDescriptor);
        if (texture is null)
        {
            textureView = null;
            error = "Failed to create a sampled scene texture.";
            return false;
        }

        TextureViewDescriptor textureViewDescriptor = new()
        {
            Format = textureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, in textureViewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            texture = null;
            error = "Failed to create a sampled scene texture view.";
            return false;
        }

        uint pixel = 0;
        ImageCopyTexture destination = new()
        {
            Texture = texture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = 4,
            RowsPerImage = 1
        };

        Extent3D size = new(1, 1, 1);
        flushContext.Api.QueueWriteTexture(flushContext.Queue, in destination, &pixel, 4, in layout, in size);
        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(textureView);
        error = null;
        return true;
    }

    private static bool TryCreateAndUploadScalarBuffer<T>(
        WebGPUFlushContext flushContext,
        in T value,
        out WgpuBuffer* buffer,
        out string? error)
        where T : unmanaged
    {
        nuint byteLength = (nuint)Unsafe.SizeOf<T>();
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = byteLength
        };

        buffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in descriptor);
        if (buffer is null)
        {
            error = $"Failed to create a staged-scene scalar buffer for '{typeof(T).Name}'.";
            return false;
        }

        flushContext.TrackBuffer(buffer);
        flushContext.Api.QueueWriteBuffer(
            flushContext.Queue,
            buffer,
            0,
            Unsafe.AsPointer(ref Unsafe.AsRef(in value)),
            byteLength);
        error = null;
        return true;
    }

    private static bool TryCreateAndUploadBuffer<T>(
        WebGPUFlushContext flushContext,
        ReadOnlySpan<T> values,
        uint minimumLength,
        out WgpuBuffer* buffer,
        out string? error)
        where T : unmanaged
    {
        uint elementCount = Math.Max(Math.Max((uint)values.Length, minimumLength), 1U);
        nuint byteLength = checked((nuint)elementCount * (nuint)Unsafe.SizeOf<T>());
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            Size = byteLength
        };

        buffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in descriptor);
        if (buffer is null)
        {
            error = $"Failed to create a staged-scene buffer for '{typeof(T).Name}'.";
            return false;
        }

        flushContext.TrackBuffer(buffer);
        if (!values.IsEmpty)
        {
            nuint uploadByteLength = checked((nuint)values.Length * (nuint)Unsafe.SizeOf<T>());
            fixed (T* dataPtr = values)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    buffer,
                    0,
                    dataPtr,
                    uploadByteLength);
            }
        }

        error = null;
        return true;
    }
}

/// <summary>
/// Flush-scoped GPU resources produced from one encoded scene.
/// </summary>
internal readonly unsafe struct WebGPUSceneResourceSet
{
    public WebGPUSceneResourceSet(
        WgpuBuffer* headerBuffer,
        WgpuBuffer* sceneBuffer,
        WgpuBuffer* pathReducedBuffer,
        WgpuBuffer* pathReduced2Buffer,
        WgpuBuffer* pathReducedScanBuffer,
        WgpuBuffer* pathMonoidBuffer,
        WgpuBuffer* pathBboxBuffer,
        WgpuBuffer* drawReducedBuffer,
        WgpuBuffer* drawMonoidBuffer,
        WgpuBuffer* infoBinDataBuffer,
        WgpuBuffer* clipInputBuffer,
        WgpuBuffer* clipElementBuffer,
        WgpuBuffer* clipBicBuffer,
        WgpuBuffer* clipBboxBuffer,
        WgpuBuffer* drawBboxBuffer,
        WgpuBuffer* pathBuffer,
        WgpuBuffer* lineBuffer,
        TextureView* auxiliaryTextureView)
    {
        this.HeaderBuffer = headerBuffer;
        this.SceneBuffer = sceneBuffer;
        this.PathReducedBuffer = pathReducedBuffer;
        this.PathReduced2Buffer = pathReduced2Buffer;
        this.PathReducedScanBuffer = pathReducedScanBuffer;
        this.PathMonoidBuffer = pathMonoidBuffer;
        this.PathBboxBuffer = pathBboxBuffer;
        this.DrawReducedBuffer = drawReducedBuffer;
        this.DrawMonoidBuffer = drawMonoidBuffer;
        this.InfoBinDataBuffer = infoBinDataBuffer;
        this.ClipInputBuffer = clipInputBuffer;
        this.ClipElementBuffer = clipElementBuffer;
        this.ClipBicBuffer = clipBicBuffer;
        this.ClipBboxBuffer = clipBboxBuffer;
        this.DrawBboxBuffer = drawBboxBuffer;
        this.PathBuffer = pathBuffer;
        this.LineBuffer = lineBuffer;
        this.AuxiliaryTextureView = auxiliaryTextureView;
    }

    public WgpuBuffer* HeaderBuffer { get; }

    public WgpuBuffer* SceneBuffer { get; }

    public WgpuBuffer* PathReducedBuffer { get; }

    public WgpuBuffer* PathReduced2Buffer { get; }

    public WgpuBuffer* PathReducedScanBuffer { get; }

    public WgpuBuffer* PathMonoidBuffer { get; }

    public WgpuBuffer* PathBboxBuffer { get; }

    public WgpuBuffer* DrawReducedBuffer { get; }

    public WgpuBuffer* DrawMonoidBuffer { get; }

    public WgpuBuffer* InfoBinDataBuffer { get; }

    public WgpuBuffer* ClipInputBuffer { get; }

    public WgpuBuffer* ClipElementBuffer { get; }

    public WgpuBuffer* ClipBicBuffer { get; }

    public WgpuBuffer* ClipBboxBuffer { get; }

    public WgpuBuffer* DrawBboxBuffer { get; }

    public WgpuBuffer* PathBuffer { get; }

    public WgpuBuffer* LineBuffer { get; }

    public TextureView* AuxiliaryTextureView { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuSceneBumpAllocators
{
    public uint Failed;
    public uint Binning;
    public uint Ptcl;
    public uint Tile;
    public uint SegCounts;
    public uint Segments;
    public uint Blend;
    public uint Lines;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuTagMonoid
{
    public GpuTagMonoid(uint transIndex, uint pathSegmentIndex, uint pathSegmentOffset, uint styleIndex, uint pathIndex)
    {
        this.TransIndex = transIndex;
        this.PathSegmentIndex = pathSegmentIndex;
        this.PathSegmentOffset = pathSegmentOffset;
        this.StyleIndex = styleIndex;
        this.PathIndex = pathIndex;
    }

    public uint TransIndex { get; }

    public uint PathSegmentIndex { get; }

    public uint PathSegmentOffset { get; }

    public uint StyleIndex { get; }

    public uint PathIndex { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuPathBbox
{
    public GpuPathBbox(int x0, int y0, int x1, int y1, uint drawFlags, uint transIndex)
    {
        this.X0 = x0;
        this.Y0 = y0;
        this.X1 = x1;
        this.Y1 = y1;
        this.DrawFlags = drawFlags;
        this.TransIndex = transIndex;
    }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public uint DrawFlags { get; }

    public uint TransIndex { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuClipInp
{
    public GpuClipInp(uint drawIndex, int pathIndex)
    {
        this.DrawIndex = drawIndex;
        this.PathIndex = pathIndex;
    }

    public uint DrawIndex { get; }

    public int PathIndex { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuBic
{
    public GpuBic(uint a, uint b)
    {
        this.A = a;
        this.B = b;
    }

    public uint A { get; }

    public uint B { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuClipElement
{
    public GpuClipElement(uint parentIndex, Vector4 bbox)
    {
        this.ParentIndex = parentIndex;
        this.Bbox = bbox;
    }

    public uint ParentIndex { get; }

    public Vector4 Bbox { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuDrawBbox
{
    public GpuDrawBbox(Vector4 bbox)
    {
        this.Bbox = bbox;
    }

    public Vector4 Bbox { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneBinHeader
{
    public GpuSceneBinHeader(uint elementCount, uint chunkOffset)
    {
        this.ElementCount = elementCount;
        this.ChunkOffset = chunkOffset;
    }

    public uint ElementCount { get; }

    public uint ChunkOffset { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuSceneIndirectCount
{
    public uint CountX;
    public uint CountY;
    public uint CountZ;
    public uint Pad0;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneLayout
{
    public GpuSceneLayout(
        uint drawObjectCount,
        uint pathCount,
        uint clipCount,
        uint binDataStart,
        uint pathTagBase,
        uint pathDataBase,
        uint drawTagBase,
        uint drawDataBase,
        uint transformBase,
        uint styleBase)
    {
        this.DrawObjectCount = drawObjectCount;
        this.PathCount = pathCount;
        this.ClipCount = clipCount;
        this.BinDataStart = binDataStart;
        this.PathTagBase = pathTagBase;
        this.PathDataBase = pathDataBase;
        this.DrawTagBase = drawTagBase;
        this.DrawDataBase = drawDataBase;
        this.TransformBase = transformBase;
        this.StyleBase = styleBase;
    }

    public uint DrawObjectCount { get; }

    public uint PathCount { get; }

    public uint ClipCount { get; }

    public uint BinDataStart { get; }

    public uint PathTagBase { get; }

    public uint PathDataBase { get; }

    public uint DrawTagBase { get; }

    public uint DrawDataBase { get; }

    public uint TransformBase { get; }

    public uint StyleBase { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneConfig
{
    public GpuSceneConfig(
        uint widthInTiles,
        uint heightInTiles,
        uint targetWidth,
        uint targetHeight,
        uint baseColor,
        GpuSceneLayout layout,
        uint linesSize,
        uint binningSize,
        uint tilesSize,
        uint segCountsSize,
        uint segmentsSize,
        uint blendSize,
        uint ptclSize)
    {
        this.WidthInTiles = widthInTiles;
        this.HeightInTiles = heightInTiles;
        this.TargetWidth = targetWidth;
        this.TargetHeight = targetHeight;
        this.BaseColor = baseColor;
        this.Layout = layout;
        this.LinesSize = linesSize;
        this.BinningSize = binningSize;
        this.TilesSize = tilesSize;
        this.SegCountsSize = segCountsSize;
        this.SegmentsSize = segmentsSize;
        this.BlendSize = blendSize;
        this.PtclSize = ptclSize;
    }

    public uint WidthInTiles { get; }

    public uint HeightInTiles { get; }

    public uint TargetWidth { get; }

    public uint TargetHeight { get; }

    public uint BaseColor { get; }

    public GpuSceneLayout Layout { get; }

    public uint LinesSize { get; }

    public uint BinningSize { get; }

    public uint TilesSize { get; }

    public uint SegCountsSize { get; }

    public uint SegmentsSize { get; }

    public uint BlendSize { get; }

    public uint PtclSize { get; }
}

internal static class GpuSceneDrawTag
{
    public const uint Nop = 0U;
    public const uint FillColor = 0x44U;
    public const uint FillInfoFlagsFillRuleBit = 1U;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GpuSceneDrawMonoid Map(uint tagWord)
    {
        return new GpuSceneDrawMonoid(
            tagWord != Nop ? 1U : 0U,
            tagWord & 1U,
            (tagWord >> 2) & 0x07U,
            (tagWord >> 6) & 0x0FU);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneDrawMonoid
{
    public GpuSceneDrawMonoid(uint pathIndex, uint clipIndex, uint sceneOffset, uint infoOffset)
    {
        this.PathIndex = pathIndex;
        this.ClipIndex = clipIndex;
        this.SceneOffset = sceneOffset;
        this.InfoOffset = infoOffset;
    }

    public uint PathIndex { get; }

    public uint ClipIndex { get; }

    public uint SceneOffset { get; }

    public uint InfoOffset { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GpuSceneDrawMonoid Combine(in GpuSceneDrawMonoid a, in GpuSceneDrawMonoid b)
        => new(
            a.PathIndex + b.PathIndex,
            a.ClipIndex + b.ClipIndex,
            a.SceneOffset + b.SceneOffset,
            a.InfoOffset + b.InfoOffset);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuScenePath
{
    private readonly uint padding0;
    private readonly uint padding1;
    private readonly uint padding2;

    public GpuScenePath(uint bboxMinX, uint bboxMinY, uint bboxMaxX, uint bboxMaxY, uint tiles)
    {
        this.BboxMinX = bboxMinX;
        this.BboxMinY = bboxMinY;
        this.BboxMaxX = bboxMaxX;
        this.BboxMaxY = bboxMaxY;
        this.Tiles = tiles;
        this.padding0 = 0;
        this.padding1 = 0;
        this.padding2 = 0;
    }

    public uint BboxMinX { get; }

    public uint BboxMinY { get; }

    public uint BboxMaxX { get; }

    public uint BboxMaxY { get; }

    public uint Tiles { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneLine
{
    private readonly uint padding0;

    public GpuSceneLine(uint pathIndex, Vector2 point0, Vector2 point1)
    {
        this.PathIndex = pathIndex;
        this.padding0 = 0;
        this.Point0 = point0;
        this.Point1 = point1;
    }

    public uint PathIndex { get; }

    public Vector2 Point0 { get; }

    public Vector2 Point1 { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuPathTile
{
    public GpuPathTile(int backdrop, uint segmentCountOrIndex)
    {
        this.Backdrop = backdrop;
        this.SegmentCountOrIndex = segmentCountOrIndex;
    }

    public int Backdrop;

    public uint SegmentCountOrIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSegmentCount
{
    public GpuSegmentCount(uint lineIndex, uint counts)
    {
        this.LineIndex = lineIndex;
        this.Counts = counts;
    }

    public uint LineIndex { get; }

    public uint Counts { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuPathSegment
{
    private readonly float padding0;

    public GpuPathSegment(Vector2 point0, Vector2 point1, float yEdge)
    {
        this.Point0 = point0;
        this.Point1 = point1;
        this.YEdge = yEdge;
        this.padding0 = 0;
    }

    public Vector2 Point0 { get; }

    public Vector2 Point1 { get; }

    public float YEdge { get; }
}

#pragma warning restore SA1201
