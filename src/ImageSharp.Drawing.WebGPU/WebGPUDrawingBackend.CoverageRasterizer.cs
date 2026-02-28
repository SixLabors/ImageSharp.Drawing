// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal sealed unsafe partial class WebGPUDrawingBackend
{
    private const int TileWidth = 16;
    private const int TileHeight = 16;
    private const float TileScale = 1F / TileWidth;
    private const int LineStrideBytes = 24;
    private const int PathStrideBytes = 32;
    private const int TileStrideBytes = 8;
    private const int SegmentCountStrideBytes = 8;
    private const int SegmentStrideBytes = 24;
    private const int SegmentAllocWorkgroupSize = 256;

    private readonly Dictionary<CoverageDefinitionIdentity, CachedCoverageGeometry> coverageGeometryCache = new();

    private delegate uint BindGroupEntryWriter(Span<BindGroupEntry> entries);

    private unsafe delegate void ComputePassDispatch(ComputePassEncoder* pass);

    private bool TryCreateCoverageTextureFromFlattened<TPixel>(
        WebGPUFlushContext flushContext,
        List<CompositionCoverageDefinition> definitions,
        Configuration configuration,
        out TextureView* coverageView,
        out CoveragePlacement[] coveragePlacements,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        coverageView = null;
        coveragePlacements = Array.Empty<CoveragePlacement>();
        error = null;
        if (definitions.Count == 0)
        {
            return true;
        }

        CoveragePathBuild[] pathBuilds = new CoveragePathBuild[definitions.Count];
        coveragePlacements = new CoveragePlacement[definitions.Count];
        int totalLineCount = 0;
        int totalTileCount = 0;
        ulong totalEstimatedSegments = 0;
        int atlasWidthInTiles = 0;
        int atlasHeightInTiles = 0;
        int currentTileY = 0;
        uint? fillRuleValue = null;
        uint? aliasedValue = null;
        for (int i = 0; i < definitions.Count; i++)
        {
            CompositionCoverageDefinition definition = definitions[i];
            Rectangle interest = definition.RasterizerOptions.Interest;
            if (interest.Width <= 0 || interest.Height <= 0)
            {
                error = "Invalid coverage bounds.";
                return false;
            }

            uint fillRule = definition.RasterizerOptions.IntersectionRule == IntersectionRule.EvenOdd ? 1u : 0u;
            uint isAliased = definition.RasterizerOptions.RasterizationMode == RasterizationMode.Aliased ? 1u : 0u;
            if ((fillRuleValue.HasValue && fillRuleValue.Value != fillRule) ||
                (aliasedValue.HasValue && aliasedValue.Value != isAliased))
            {
                error = "Mixed rasterization modes are not supported in one flush coverage pass.";
                return false;
            }

            fillRuleValue ??= fillRule;
            aliasedValue ??= isAliased;

            int widthInTiles = (int)DivideRoundUp(interest.Width, TileWidth);
            int heightInTiles = (int)DivideRoundUp(interest.Height, TileHeight);
            int originTileX = 0;
            int originTileY = currentTileY;
            int originX = originTileX * TileWidth;
            int originY = originTileY * TileHeight;

            CoverageDefinitionIdentity identity = new(definition);
            if (!this.coverageGeometryCache.TryGetValue(identity, out CachedCoverageGeometry? geometry))
            {
                IMemoryOwner<byte>? lineOwner = null;
                try
                {
                    if (!TryBuildLineBuffer(
                            definition.Path,
                            in interest,
                            definition.RasterizerOptions.SamplingOrigin,
                            configuration.MemoryAllocator,
                            out lineOwner,
                            out int lineCount,
                            out _,
                            out _,
                            out _,
                            out _,
                            out uint estimatedSegments,
                            out error))
                    {
                        return false;
                    }

                    geometry = new CachedCoverageGeometry(
                        lineOwner,
                        lineCount,
                        estimatedSegments,
                        widthInTiles,
                        heightInTiles,
                        interest.Width,
                        interest.Height);
                    lineOwner = null;
                    this.coverageGeometryCache[identity] = geometry;
                }
                finally
                {
                    lineOwner?.Dispose();
                }
            }

            if (geometry is null)
            {
                error = "Failed to resolve cached coverage geometry.";
                return false;
            }

            pathBuilds[i] = new CoveragePathBuild(
                geometry,
                originTileX,
                originTileY,
                originX,
                originY);
            coveragePlacements[i] = new CoveragePlacement(originX, originY, interest.Width, interest.Height);

            totalLineCount = checked(totalLineCount + geometry.LineCount);
            totalEstimatedSegments += geometry.EstimatedSegments;
            atlasWidthInTiles = Math.Max(atlasWidthInTiles, geometry.WidthInTiles);
            atlasHeightInTiles = Math.Max(atlasHeightInTiles, originTileY + geometry.HeightInTiles);
            currentTileY += geometry.HeightInTiles;
        }

        totalTileCount = checked(atlasWidthInTiles * atlasHeightInTiles);

        int atlasWidth = Math.Max(1, atlasWidthInTiles * TileWidth);
        int atlasHeight = Math.Max(1, atlasHeightInTiles * TileHeight);
        if (!TryCreateCoverageTexture(
                flushContext,
                atlasWidth,
                atlasHeight,
                configuration.MemoryAllocator,
                totalLineCount == 0,
                out Texture* coverageTexture,
                out coverageView,
                out error))
        {
            return false;
        }

        flushContext.TrackTexture(coverageTexture);
        flushContext.TrackTextureView(coverageView);
        if (totalLineCount == 0)
        {
            return true;
        }

        int lineBufferBytes = checked(totalLineCount * LineStrideBytes);
        using IMemoryOwner<byte> lineUploadOwner = configuration.MemoryAllocator.Allocate<byte>(lineBufferBytes);
        Span<byte> lineUpload = lineUploadOwner.Memory.Span[..lineBufferBytes];
        int mergedLineIndex = 0;
        for (int pathIndex = 0; pathIndex < pathBuilds.Length; pathIndex++)
        {
            CoveragePathBuild build = pathBuilds[pathIndex];
            CachedCoverageGeometry geometry = build.Geometry;
            if (geometry.LineCount == 0 || geometry.LineOwner is null)
            {
                continue;
            }

            ReadOnlySpan<byte> sourceLines = geometry.LineOwner.Memory.Span[..(geometry.LineCount * LineStrideBytes)];
            for (int lineIndex = 0; lineIndex < geometry.LineCount; lineIndex++)
            {
                int sourceOffset = lineIndex * LineStrideBytes;
                float x0 = ReadFloat(sourceLines, sourceOffset + 8) + build.OriginX;
                float y0 = ReadFloat(sourceLines, sourceOffset + 12) + build.OriginY;
                float x1 = ReadFloat(sourceLines, sourceOffset + 16) + build.OriginX;
                float y1 = ReadFloat(sourceLines, sourceOffset + 20) + build.OriginY;
                WriteLine(lineUpload, mergedLineIndex, (uint)pathIndex, x0, y0, x1, y1);
                mergedLineIndex++;
            }
        }

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-lines",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)lineBufferBytes,
                out WgpuBuffer* lineBuffer,
                out error))
        {
            return false;
        }

        fixed (byte* lineUploadPtr = lineUpload)
        {
            flushContext.Api.QueueWriteBuffer(
                flushContext.Queue,
                lineBuffer,
                0,
                lineUploadPtr,
                (nuint)lineBufferBytes);
        }

        int pathBufferBytes = checked(pathBuilds.Length * PathStrideBytes);
        using IMemoryOwner<byte> pathUploadOwner = configuration.MemoryAllocator.Allocate<byte>(pathBufferBytes);
        Span<byte> pathUpload = pathUploadOwner.Memory.Span[..pathBufferBytes];
        int tileBase = 0;
        for (int i = 0; i < pathBuilds.Length; i++)
        {
            CoveragePathBuild build = pathBuilds[i];
            WritePath(
                pathUpload.Slice(i * PathStrideBytes, PathStrideBytes),
                (uint)build.OriginTileX,
                (uint)build.OriginTileY,
                (uint)(build.OriginTileX + atlasWidthInTiles),
                (uint)(build.OriginTileY + build.Geometry.HeightInTiles),
                (uint)tileBase);
            tileBase = checked(tileBase + (atlasWidthInTiles * build.Geometry.HeightInTiles));
        }

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-paths",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)pathBufferBytes,
                out WgpuBuffer* pathBuffer,
                out error))
        {
            return false;
        }

        fixed (byte* pathUploadPtr = pathUpload)
        {
            flushContext.Api.QueueWriteBuffer(
                flushContext.Queue,
                pathBuffer,
                0,
                pathUploadPtr,
                (nuint)pathBufferBytes);
        }

        int tileBufferBytes = checked(totalTileCount * TileStrideBytes);
        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-tiles",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)tileBufferBytes,
                out WgpuBuffer* tileBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.CommandEncoderClearBuffer(
            flushContext.CommandEncoder,
            tileBuffer,
            0,
            (nuint)tileBufferBytes);

        int tileCountsBytes = checked(totalTileCount * sizeof(uint));
        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-tile-counts",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)tileCountsBytes,
                out WgpuBuffer* tileCountsBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.CommandEncoderClearBuffer(
            flushContext.CommandEncoder,
            tileCountsBuffer,
            0,
            (nuint)tileCountsBytes);

        if (totalEstimatedSegments > int.MaxValue)
        {
            error = "Coverage segment estimate overflow.";
            return false;
        }

        uint segCountsCapacity = totalEstimatedSegments == 0 ? 1u : checked((uint)totalEstimatedSegments);
        uint segmentsCapacity = segCountsCapacity;
        int segCountsBytes = checked((int)segCountsCapacity * SegmentCountStrideBytes);
        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-segment-counts",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)segCountsBytes,
                out WgpuBuffer* segCountsBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.CommandEncoderClearBuffer(
            flushContext.CommandEncoder,
            segCountsBuffer,
            0,
            (nuint)segCountsBytes);

        int segmentsBytes = checked((int)segmentsCapacity * SegmentStrideBytes);
        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-segments",
                BufferUsage.Storage,
                (nuint)segmentsBytes,
                out WgpuBuffer* segmentsBuffer,
                out error))
        {
            return false;
        }

        RasterConfig config = new()
        {
            WidthInTiles = (uint)atlasWidthInTiles,
            HeightInTiles = (uint)atlasHeightInTiles,
            TargetWidth = (uint)atlasWidth,
            TargetHeight = (uint)atlasHeight,
            BaseColor = 0,
            NDrawObj = 0,
            NPath = (uint)pathBuilds.Length,
            NClip = 0,
            BinDataStart = 0,
            PathtagBase = 0,
            PathdataBase = 0,
            DrawtagBase = 0,
            DrawdataBase = 0,
            TransformBase = 0,
            StyleBase = 0,
            LinesSize = (uint)totalLineCount,
            BinningSize = (uint)pathBuilds.Length,
            TilesSize = (uint)totalTileCount,
            SegCountsSize = segCountsCapacity,
            SegmentsSize = segmentsCapacity,
            BlendSize = 1,
            PtclSize = 1
        };

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-raster-config",
                BufferUsage.Uniform | BufferUsage.CopyDst,
                (nuint)Unsafe.SizeOf<RasterConfig>(),
                out WgpuBuffer* configBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.QueueWriteBuffer(flushContext.Queue, configBuffer, 0, &config, (nuint)Unsafe.SizeOf<RasterConfig>());

        BumpAllocatorsData bumpData = new()
        {
            Failed = 0,
            Binning = 0,
            Ptcl = 0,
            Tile = 0,
            SegCounts = 0,
            Segments = 0,
            Blend = 0,
            Lines = (uint)totalLineCount
        };

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-bump",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)Unsafe.SizeOf<BumpAllocatorsData>(),
                out WgpuBuffer* bumpBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.QueueWriteBuffer(flushContext.Queue, bumpBuffer, 0, &bumpData, (nuint)Unsafe.SizeOf<BumpAllocatorsData>());

        IndirectCountData indirectData = default;
        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-indirect",
                BufferUsage.Storage | BufferUsage.Indirect | BufferUsage.CopyDst,
                (nuint)Unsafe.SizeOf<IndirectCountData>(),
                out WgpuBuffer* indirectBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.QueueWriteBuffer(flushContext.Queue, indirectBuffer, 0, &indirectData, (nuint)Unsafe.SizeOf<IndirectCountData>());

        SegmentAllocConfig segmentAllocConfig = new() { TileCount = (uint)totalTileCount };
        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-segment-alloc",
                BufferUsage.Uniform | BufferUsage.CopyDst,
                (nuint)Unsafe.SizeOf<SegmentAllocConfig>(),
                out WgpuBuffer* segmentAllocBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.QueueWriteBuffer(flushContext.Queue, segmentAllocBuffer, 0, &segmentAllocConfig, (nuint)Unsafe.SizeOf<SegmentAllocConfig>());

        CoverageConfig coverageConfig = new()
        {
            TargetWidth = (uint)atlasWidth,
            TargetHeight = (uint)atlasHeight,
            TileOriginX = 0,
            TileOriginY = 0,
            TileWidthInTiles = (uint)atlasWidthInTiles,
            TileHeightInTiles = (uint)atlasHeightInTiles,
            FillRule = fillRuleValue.GetValueOrDefault(0),
            IsAliased = aliasedValue.GetValueOrDefault(0)
        };

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-coverage-config",
                BufferUsage.Uniform | BufferUsage.CopyDst,
                (nuint)Unsafe.SizeOf<CoverageConfig>(),
                out WgpuBuffer* coverageConfigBuffer,
                out error))
        {
            return false;
        }

        flushContext.Api.QueueWriteBuffer(flushContext.Queue, coverageConfigBuffer, 0, &coverageConfig, (nuint)Unsafe.SizeOf<CoverageConfig>());

        if (!this.DispatchPathCountSetup(flushContext, bumpBuffer, indirectBuffer, out error) ||
            !this.DispatchPathCount(flushContext, configBuffer, bumpBuffer, lineBuffer, pathBuffer, tileBuffer, segCountsBuffer, indirectBuffer, out error) ||
            !this.DispatchBackdrop(flushContext, configBuffer, tileBuffer, atlasHeightInTiles, out error) ||
            !this.DispatchSegmentAlloc(flushContext, bumpBuffer, tileBuffer, tileCountsBuffer, segmentAllocBuffer, totalTileCount, out error) ||
            !this.DispatchPathTilingSetup(flushContext, bumpBuffer, indirectBuffer, out error) ||
            !this.DispatchPathTiling(flushContext, bumpBuffer, segCountsBuffer, lineBuffer, pathBuffer, tileBuffer, segmentsBuffer, indirectBuffer, out error) ||
            !this.DispatchCoverageFine(flushContext, coverageConfigBuffer, tileBuffer, tileCountsBuffer, segmentsBuffer, coverageView, atlasWidthInTiles, atlasHeightInTiles, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetOrCreateCoverageBuffer(
        WebGPUFlushContext flushContext,
        string bufferKey,
        BufferUsage usage,
        nuint requiredSize,
        out WgpuBuffer* buffer,
        out string? error)
        => flushContext.DeviceState.TryGetOrCreateSharedBuffer(
            bufferKey,
            usage,
            requiredSize,
            out buffer,
            out _,
            out error);

    private void DisposeCoverageResources()
    {
        foreach (CachedCoverageGeometry geometry in this.coverageGeometryCache.Values)
        {
            geometry.Dispose();
        }

        this.coverageGeometryCache.Clear();
    }

    private static bool TryBuildLineBuffer(
        IPath path,
        in Rectangle interest,
        RasterizerSamplingOrigin samplingOrigin,
        MemoryAllocator allocator,
        out IMemoryOwner<byte>? lineOwner,
        out int lineCount,
        out float minX,
        out float minY,
        out float maxX,
        out float maxY,
        out uint estimatedSegments,
        out string? error)
    {
        error = null;
        lineOwner = null;
        lineCount = 0;
        estimatedSegments = 0;
        minX = float.PositiveInfinity;
        minY = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        maxY = float.NegativeInfinity;
        bool samplePixelCenter = samplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;

        List<ISimplePath> simplePaths = [];
        foreach (ISimplePath simplePath in path.Flatten())
        {
            simplePaths.Add(simplePath);
        }

        for (int i = 0; i < simplePaths.Count; i++)
        {
            ReadOnlySpan<PointF> points = simplePaths[i].Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            for (int j = 0; j < points.Length; j++)
            {
                float x = (points[j].X - interest.X) + samplingOffsetX;
                float y = (points[j].Y - interest.Y) + samplingOffsetY;
                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }

            int contourSegmentCount = simplePaths[i].IsClosed
                ? points.Length
                : points.Length - 1;
            if (contourSegmentCount <= 0)
            {
                continue;
            }

            lineCount += contourSegmentCount;
        }

        if (lineCount == 0)
        {
            minX = 0;
            minY = 0;
            maxX = 0;
            maxY = 0;
            return true;
        }

        int lineBufferBytes = checked(lineCount * LineStrideBytes);
        lineOwner = allocator.Allocate<byte>(lineBufferBytes);
        Span<byte> lineBytes = lineOwner.Memory.Span[..lineBufferBytes];
        lineBytes.Clear();

        int lineIndex = 0;
        for (int i = 0; i < simplePaths.Count; i++)
        {
            ReadOnlySpan<PointF> points = simplePaths[i].Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            bool contourClosed = simplePaths[i].IsClosed;
            int segmentCount = contourClosed
                ? points.Length
                : points.Length - 1;
            if (segmentCount <= 0)
            {
                continue;
            }

            for (int j = 0; j < segmentCount; j++)
            {
                PointF p0 = points[j];
                int nextIndex = j + 1;
                if (nextIndex == points.Length)
                {
                    nextIndex = 0;
                }

                PointF p1 = points[nextIndex];
                float x0 = (p0.X - interest.X) + samplingOffsetX;
                float y0 = (p0.Y - interest.Y) + samplingOffsetY;
                float x1 = (p1.X - interest.X) + samplingOffsetX;
                float y1 = (p1.Y - interest.Y) + samplingOffsetY;
                WriteLine(lineBytes, lineIndex, x0, y0, x1, y1);
                estimatedSegments += EstimateSegmentCount(x0, y0, x1, y1);
                lineIndex++;
            }
        }

        return true;
    }

    private static void WriteLine(Span<byte> destination, int lineIndex, float x0, float y0, float x1, float y1)
        => WriteLine(destination, lineIndex, 0u, x0, y0, x1, y1);

    private static void WriteLine(Span<byte> destination, int lineIndex, uint pathIndex, float x0, float y0, float x1, float y1)
    {
        int offset = lineIndex * LineStrideBytes;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), pathIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 4, 4), 0u);
        WriteFloat(destination, offset + 8, x0);
        WriteFloat(destination, offset + 12, y0);
        WriteFloat(destination, offset + 16, x1);
        WriteFloat(destination, offset + 20, y1);
    }

    private static void WritePath(Span<byte> destination, uint x0, uint y0, uint x1, uint y1)
        => WritePath(destination, x0, y0, x1, y1, 0u);

    private static void WritePath(Span<byte> destination, uint x0, uint y0, uint x1, uint y1, uint tiles)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), x0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), y0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), x1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), y1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), tiles);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), 0u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadFloat(ReadOnlySpan<byte> source, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4)));

    private static void WriteFloat(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), (uint)BitConverter.SingleToInt32Bits(value));

    private static uint EstimateSegmentCount(float x0, float y0, float x1, float y1)
    {
        float s0x = x0 * TileScale;
        float s0y = y0 * TileScale;
        float s1x = x1 * TileScale;
        float s1y = y1 * TileScale;
        uint countX = SpanTiles(s0x, s1x);
        uint countY = SpanTiles(s0y, s1y);
        if (countX > 0)
        {
            countX -= 1;
        }

        return countX + countY;
    }

    private static uint SpanTiles(float a, float b)
    {
        float max = MathF.Max(a, b);
        float min = MathF.Min(a, b);
        float span = MathF.Ceiling(max) - MathF.Floor(min);
        if (span < 1F)
        {
            span = 1F;
        }

        return (uint)span;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static bool TryCreateCoverageTexture(
        WebGPUFlushContext flushContext,
        int width,
        int height,
        MemoryAllocator allocator,
        bool clearOnCreate,
        out Texture* coverageTexture,
        out TextureView* coverageView,
        out string? error)
    {
        TextureDescriptor descriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.StorageBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = TextureFormat.R32float,
            MipLevelCount = 1,
            SampleCount = 1
        };

        coverageTexture = flushContext.Api.DeviceCreateTexture(flushContext.Device, in descriptor);
        if (coverageTexture is null)
        {
            coverageView = null;
            error = "Failed to create coverage texture.";
            return false;
        }

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = descriptor.Format,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        coverageView = flushContext.Api.TextureCreateView(coverageTexture, in viewDescriptor);
        if (coverageView is null)
        {
            flushContext.Api.TextureRelease(coverageTexture);
            error = "Failed to create coverage texture view.";
            return false;
        }

        if (clearOnCreate)
        {
            int rowBytes = checked(width * sizeof(float));
            int byteCount = checked(rowBytes * height);
            using IMemoryOwner<byte> zeroOwner = allocator.Allocate<byte>(byteCount);
            Span<byte> zeroData = zeroOwner.Memory.Span[..byteCount];
            zeroData.Clear();
            ImageCopyTexture destination = new()
            {
                Texture = coverageTexture,
                MipLevel = 0,
                Origin = new Origin3D(0, 0, 0),
                Aspect = TextureAspect.All
            };

            Extent3D writeSize = new((uint)width, (uint)height, 1);
            TextureDataLayout layout = new()
            {
                Offset = 0,
                BytesPerRow = (uint)rowBytes,
                RowsPerImage = (uint)height
            };

            fixed (byte* zeroPtr = zeroData)
            {
                flushContext.Api.QueueWriteTexture(
                    flushContext.Queue,
                    in destination,
                    zeroPtr,
                    (nuint)byteCount,
                    in layout,
                    in writeSize);
            }
        }

        error = null;
        return true;
    }

    private bool DispatchPathCountSetup(
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* indirectBuffer,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "path-count-setup",
            PathCountSetupComputeShader.Code,
            TryCreatePathCountSetupBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = bumpBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = indirectBuffer, Offset = 0, Size = nuint.MaxValue };
                return 2;
            },
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, 1, 1, 1),
            out error);

    private bool DispatchPathCount(
        WebGPUFlushContext flushContext,
        WgpuBuffer* configBuffer,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* lineBuffer,
        WgpuBuffer* pathBuffer,
        WgpuBuffer* tileBuffer,
        WgpuBuffer* segCountsBuffer,
        WgpuBuffer* indirectBuffer,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "path-count",
            PathCountComputeShader.Code,
            TryCreatePathCountBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = configBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<RasterConfig>() };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = bumpBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = lineBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = pathBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = tileBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[5] = new BindGroupEntry { Binding = 5, Buffer = segCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                return 6;
            },
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroupsIndirect(pass, indirectBuffer, (nuint)0),
            out error);

    private bool DispatchSegmentAlloc(
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* tileBuffer,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* segmentAllocBuffer,
        int tileCount,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "segment-alloc",
            SegmentAllocComputeShader.Code,
            TryCreateSegmentAllocBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = bumpBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = segmentAllocBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<SegmentAllocConfig>() };
                return 4;
            },
            (pass) =>
            {
                uint dispatchX = DivideRoundUp(tileCount, SegmentAllocWorkgroupSize);
                flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, dispatchX, 1, 1);
            },
            out error);

    private bool DispatchBackdrop(
        WebGPUFlushContext flushContext,
        WgpuBuffer* configBuffer,
        WgpuBuffer* tileBuffer,
        int heightInTiles,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "backdrop",
            BackdropComputeShader.Code,
            TryCreateBackdropBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = configBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<RasterConfig>() };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileBuffer, Offset = 0, Size = nuint.MaxValue };
                return 2;
            },
            (pass) =>
            {
                flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)heightInTiles, 1, 1);
            },
            out error);

    private bool DispatchPathTilingSetup(
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* indirectBuffer,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "path-tiling-setup",
            PathTilingSetupComputeShader.Code,
            TryCreatePathTilingSetupBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = bumpBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = indirectBuffer, Offset = 0, Size = nuint.MaxValue };
                return 2;
            },
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, 1, 1, 1),
            out error);

    private bool DispatchPathTiling(
        WebGPUFlushContext flushContext,
        WgpuBuffer* bumpBuffer,
        WgpuBuffer* segCountsBuffer,
        WgpuBuffer* lineBuffer,
        WgpuBuffer* pathBuffer,
        WgpuBuffer* tileBuffer,
        WgpuBuffer* segmentsBuffer,
        WgpuBuffer* indirectBuffer,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "path-tiling",
            PathTilingComputeShader.Code,
            TryCreatePathTilingBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = bumpBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = segCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = lineBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = pathBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = tileBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[5] = new BindGroupEntry { Binding = 5, Buffer = segmentsBuffer, Offset = 0, Size = nuint.MaxValue };
                return 6;
            },
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroupsIndirect(pass, indirectBuffer, (nuint)0),
            out error);

    private bool DispatchCoverageFine(
        WebGPUFlushContext flushContext,
        WgpuBuffer* coverageConfigBuffer,
        WgpuBuffer* tileBuffer,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* segmentsBuffer,
        TextureView* coverageView,
        int tileWidth,
        int tileHeight,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "coverage-fine",
            CoverageFineComputeShader.Code,
            TryCreateCoverageFineBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = coverageConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<CoverageConfig>() };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = segmentsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, TextureView = coverageView };
                return 5;
            },
            (pass) =>
            {
                flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)tileWidth, (uint)tileHeight, 1);
            },
            out error);

    private bool DispatchComputePass(
        WebGPUFlushContext flushContext,
        string pipelineKey,
        ReadOnlySpan<byte> shaderCode,
        WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
        BindGroupEntryWriter entryWriter,
        ComputePassDispatch dispatch,
        out string? error)
    {
        if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                pipelineKey,
                shaderCode,
                bindGroupLayoutFactory,
                out BindGroupLayout* bindGroupLayout,
                out ComputePipeline* pipeline,
                out error))
        {
            return false;
        }

        BindGroupEntry* entries = stackalloc BindGroupEntry[8];
        uint entryCount = entryWriter(new Span<BindGroupEntry>(entries, 8));

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = entryCount,
            Entries = entries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            error = $"Failed to create bind group for pipeline '{pipelineKey}'.";
            return false;
        }

        flushContext.TrackBindGroup(bindGroup);
        ComputePassDescriptor passDescriptor = default;
        ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(flushContext.CommandEncoder, in passDescriptor);
        if (passEncoder is null)
        {
            error = $"Failed to begin compute pass for pipeline '{pipelineKey}'.";
            return false;
        }

        try
        {
            flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
            flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
            dispatch(passEncoder);
        }
        finally
        {
            flushContext.Api.ComputePassEncoderEnd(passEncoder);
            flushContext.Api.ComputePassEncoderRelease(passEncoder);
        }

        error = null;
        return true;
    }

    private static bool TryCreatePathCountSetupBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<BumpAllocatorsData>()
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<IndirectCountData>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 2,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create path count setup bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePathCountBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[6];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<RasterConfig>()
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<BumpAllocatorsData>()
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)LineStrideBytes
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)PathStrideBytes
            }
        };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)TileStrideBytes
            }
        };
        entries[5] = new BindGroupLayoutEntry
        {
            Binding = 5,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)SegmentCountStrideBytes
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 6,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create path count bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreateSegmentAllocBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<BumpAllocatorsData>()
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)TileStrideBytes
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = sizeof(uint)
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<SegmentAllocConfig>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 4,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create segment allocation bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreateBackdropBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<RasterConfig>()
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 2,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create backdrop bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePathTilingSetupBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<BumpAllocatorsData>()
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<IndirectCountData>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 2,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create path tiling setup bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePathTilingBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[6];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<BumpAllocatorsData>()
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)SegmentCountStrideBytes
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)LineStrideBytes
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)PathStrideBytes
            }
        };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)TileStrideBytes
            }
        };
        entries[5] = new BindGroupLayoutEntry
        {
            Binding = 5,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)SegmentStrideBytes
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 6,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create path tiling bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreateCoverageFineBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[5];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<CoverageConfig>()
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)TileStrideBytes
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = sizeof(uint)
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)SegmentStrideBytes
            }
        };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Compute,
            StorageTexture = new StorageTextureBindingLayout
            {
                Access = StorageTextureAccess.WriteOnly,
                Format = TextureFormat.R32float,
                ViewDimension = TextureViewDimension.Dimension2D
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 5,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create coverage fine bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private readonly struct CoveragePathBuild
    {
        public CoveragePathBuild(
            CachedCoverageGeometry geometry,
            int originTileX,
            int originTileY,
            int originX,
            int originY)
        {
            this.Geometry = geometry;
            this.OriginTileX = originTileX;
            this.OriginTileY = originTileY;
            this.OriginX = originX;
            this.OriginY = originY;
        }

        public CachedCoverageGeometry Geometry { get; }

        public int OriginTileX { get; }

        public int OriginTileY { get; }

        public int OriginX { get; }

        public int OriginY { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RasterConfig
    {
        public uint WidthInTiles;
        public uint HeightInTiles;
        public uint TargetWidth;
        public uint TargetHeight;
        public uint BaseColor;
        public uint NDrawObj;
        public uint NPath;
        public uint NClip;
        public uint BinDataStart;
        public uint PathtagBase;
        public uint PathdataBase;
        public uint DrawtagBase;
        public uint DrawdataBase;
        public uint TransformBase;
        public uint StyleBase;
        public uint LinesSize;
        public uint BinningSize;
        public uint TilesSize;
        public uint SegCountsSize;
        public uint SegmentsSize;
        public uint BlendSize;
        public uint PtclSize;
        public uint Pad0;
        public uint Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BumpAllocatorsData
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
    private struct IndirectCountData
    {
        public uint CountX;
        public uint CountY;
        public uint CountZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SegmentAllocConfig
    {
        public uint TileCount;
        public uint Pad0;
        public uint Pad1;
        public uint Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CoverageConfig
    {
        public uint TargetWidth;
        public uint TargetHeight;
        public uint TileOriginX;
        public uint TileOriginY;
        public uint TileWidthInTiles;
        public uint TileHeightInTiles;
        public uint FillRule;
        public uint IsAliased;
    }

    private sealed class CachedCoverageGeometry : IDisposable
    {
        public CachedCoverageGeometry(
            IMemoryOwner<byte>? lineOwner,
            int lineCount,
            uint estimatedSegments,
            int widthInTiles,
            int heightInTiles,
            int coverageWidth,
            int coverageHeight)
        {
            this.LineOwner = lineOwner;
            this.LineCount = lineCount;
            this.EstimatedSegments = estimatedSegments;
            this.WidthInTiles = widthInTiles;
            this.HeightInTiles = heightInTiles;
            this.CoverageWidth = coverageWidth;
            this.CoverageHeight = coverageHeight;
        }

        public IMemoryOwner<byte>? LineOwner { get; }

        public int LineCount { get; }

        public uint EstimatedSegments { get; }

        public int WidthInTiles { get; }

        public int HeightInTiles { get; }

        public int CoverageWidth { get; }

        public int CoverageHeight { get; }

        public void Dispose()
            => this.LineOwner?.Dispose();
    }
}
