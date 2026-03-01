// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
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

    private readonly Dictionary<CoverageDefinitionIdentity, CachedCoverageGeometry> coverageGeometryCache = [];
    private IMemoryOwner<byte>? cachedCoverageLineUpload;
    private int cachedCoverageLineLength;
    private IMemoryOwner<byte>? cachedCoveragePathUpload;
    private int cachedCoveragePathLength;

    /// <summary>
    /// Writes bind-group entries and returns the number of populated entries.
    /// </summary>
    private delegate uint BindGroupEntryWriter(Span<BindGroupEntry> entries);

    /// <summary>
    /// Encapsulates dispatch logic for a compute pass.
    /// </summary>
    private delegate void ComputePassDispatch(ComputePassEncoder* pass);

    /// <summary>
    /// Builds and dispatches the full coverage rasterization pipeline for flattened paths.
    /// </summary>
    /// <typeparam name="TPixel">The canvas pixel type.</typeparam>
    /// <param name="flushContext">The active flush context.</param>
    /// <param name="definitions">Coverage definitions participating in the current flush.</param>
    /// <param name="configuration">The current processing configuration.</param>
    /// <param name="coverageView">Receives the output coverage texture view.</param>
    /// <param name="coveragePlacements">Receives per-definition atlas placement information.</param>
    /// <param name="error">Receives an error message when the operation fails.</param>
    /// <returns><see langword="true"/> when rasterization setup and dispatch succeed; otherwise <see langword="false"/>.</returns>
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
        coveragePlacements = [];
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

        // First pass: validate inputs, resolve/build cached geometry, and pack atlas placements.
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

        // Build a merged line buffer with coordinates translated into atlas space.
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

        if (!this.TryUploadDirtyCoverageRange(
                flushContext,
                lineBuffer,
                lineUpload,
                ref this.cachedCoverageLineUpload,
                ref this.cachedCoverageLineLength,
                out error))
        {
            return false;
        }

        // Build per-path metadata that maps each path into its tile span inside the atlas.
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

        if (!this.TryUploadDirtyCoverageRange(
                flushContext,
                pathBuffer,
                pathUpload,
                ref this.cachedCoveragePathUpload,
                ref this.cachedCoveragePathLength,
                out error))
        {
            return false;
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

        // Dispatch compute stages in pipeline order: count -> backdrop -> alloc -> emit segments -> fine raster.
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

    /// <summary>
    /// Gets or creates a shared GPU buffer used by the coverage rasterization pipeline.
    /// </summary>
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

    /// <summary>
    /// Uploads only the changed byte range of a coverage buffer payload.
    /// </summary>
    private bool TryUploadDirtyCoverageRange(
        WebGPUFlushContext flushContext,
        WgpuBuffer* destinationBuffer,
        ReadOnlySpan<byte> source,
        ref IMemoryOwner<byte>? cachedOwner,
        ref int cachedLength,
        out string? error)
    {
        error = null;
        if (source.Length == 0)
        {
            cachedLength = 0;
            return true;
        }

        if (cachedOwner is null || cachedOwner.Memory.Length < source.Length)
        {
            cachedOwner?.Dispose();
            cachedOwner = flushContext.MemoryAllocator.Allocate<byte>(source.Length);
            cachedLength = 0;
        }

        Span<byte> cached = cachedOwner.Memory.Span[..source.Length];
        int previousLength = cachedLength;
        int commonLength = Math.Min(previousLength, source.Length);

        int firstDifferent = 0;
        while (firstDifferent < commonLength && cached[firstDifferent] == source[firstDifferent])
        {
            firstDifferent++;
        }

        int uploadLength = 0;
        if (firstDifferent < source.Length)
        {
            int lastDifferent = source.Length - 1;
            while (lastDifferent >= firstDifferent &&
                   lastDifferent < commonLength &&
                   cached[lastDifferent] == source[lastDifferent])
            {
                lastDifferent--;
            }

            uploadLength = (lastDifferent - firstDifferent) + 1;
        }

        // Only write the dirty range to reduce queue upload bandwidth on repeated flushes.
        if (uploadLength > 0)
        {
            fixed (byte* sourcePtr = source)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    destinationBuffer,
                    (nuint)firstDifferent,
                    sourcePtr + firstDifferent,
                    (nuint)uploadLength);
            }
        }

        source.CopyTo(cached);
        cachedLength = source.Length;
        return true;
    }

    /// <summary>
    /// Releases cached coverage resources and clears all CPU-side upload caches.
    /// </summary>
    private void DisposeCoverageResources()
    {
        foreach (CachedCoverageGeometry geometry in this.coverageGeometryCache.Values)
        {
            geometry.Dispose();
        }

        this.coverageGeometryCache.Clear();
        this.cachedCoverageLineUpload?.Dispose();
        this.cachedCoverageLineUpload = null;
        this.cachedCoverageLineLength = 0;
        this.cachedCoveragePathUpload?.Dispose();
        this.cachedCoveragePathUpload = null;
        this.cachedCoveragePathLength = 0;
    }

    /// <summary>
    /// Flattens a path into the compact line format consumed by coverage compute shaders.
    /// </summary>
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

    /// <summary>
    /// Writes a single line record using the default path index.
    /// </summary>
    private static void WriteLine(Span<byte> destination, int lineIndex, float x0, float y0, float x1, float y1)
        => WriteLine(destination, lineIndex, 0u, x0, y0, x1, y1);

    /// <summary>
    /// Writes a single line record with an explicit path index.
    /// </summary>
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

    /// <summary>
    /// Writes a path bounding record using a default tile base.
    /// </summary>
    private static void WritePath(Span<byte> destination, uint x0, uint y0, uint x1, uint y1)
        => WritePath(destination, x0, y0, x1, y1, 0u);

    /// <summary>
    /// Writes a path bounding record with an explicit tile base offset.
    /// </summary>
    private static void WritePath(Span<byte> destination, uint x0, uint y0, uint x1, uint y1, uint tiles)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], x0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), y0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), x1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), y1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), tiles);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), 0u);
    }

    /// <summary>
    /// Reads a 32-bit floating-point value from a little-endian byte span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadFloat(ReadOnlySpan<byte> source, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4)));

    /// <summary>
    /// Writes a 32-bit floating-point value to a little-endian byte span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFloat(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), (uint)BitConverter.SingleToInt32Bits(value));

    /// <summary>
    /// Estimates how many tile segments a line contributes during path tiling.
    /// </summary>
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

    /// <summary>
    /// Computes the number of tiles spanned by two coordinates along one axis.
    /// </summary>
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

    /// <summary>
    /// Creates the coverage output texture and view used by the fine rasterization pass.
    /// </summary>
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

    /// <summary>
    /// Dispatches the path-count setup shader that initializes indirect dispatch counts.
    /// </summary>
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

    /// <summary>
    /// Dispatches the path-count shader that computes per-tile segment counts.
    /// </summary>
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
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroupsIndirect(pass, indirectBuffer, 0),
            out error);

    /// <summary>
    /// Dispatches the segment-allocation shader that computes per-tile segment offsets.
    /// </summary>
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

    /// <summary>
    /// Dispatches the backdrop prefix shader that accumulates backdrop values across tile rows.
    /// </summary>
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
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)heightInTiles, 1, 1),
            out error);

    /// <summary>
    /// Dispatches the path-tiling setup shader that prepares indirect counts for segment emission.
    /// </summary>
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

    /// <summary>
    /// Dispatches the path-tiling shader that emits clipped segments into per-tile storage.
    /// </summary>
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
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroupsIndirect(pass, indirectBuffer, 0),
            out error);

    /// <summary>
    /// Dispatches the fine coverage shader that rasterizes tile segments into the output texture.
    /// </summary>
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
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)tileWidth, (uint)tileHeight, 1),
            out error);

    /// <summary>
    /// Creates and executes a compute pass for a coverage pipeline stage.
    /// </summary>
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

    /// <summary>
    /// Creates the bind-group layout used by the path-count setup shader.
    /// </summary>
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

    /// <summary>
    /// Creates the bind-group layout used by the path-count shader.
    /// </summary>
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
                MinBindingSize = LineStrideBytes
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
                MinBindingSize = PathStrideBytes
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
                MinBindingSize = TileStrideBytes
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
                MinBindingSize = SegmentCountStrideBytes
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

    /// <summary>
    /// Creates the bind-group layout used by the segment-allocation shader.
    /// </summary>
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
                MinBindingSize = TileStrideBytes
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

    /// <summary>
    /// Creates the bind-group layout used by the backdrop prefix shader.
    /// </summary>
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

    /// <summary>
    /// Creates the bind-group layout used by the path-tiling setup shader.
    /// </summary>
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

    /// <summary>
    /// Creates the bind-group layout used by the path-tiling shader.
    /// </summary>
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
                MinBindingSize = SegmentCountStrideBytes
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
                MinBindingSize = LineStrideBytes
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
                MinBindingSize = PathStrideBytes
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
                MinBindingSize = TileStrideBytes
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
                MinBindingSize = SegmentStrideBytes
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

    /// <summary>
    /// Creates the bind-group layout used by the fine coverage shader.
    /// </summary>
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
                MinBindingSize = TileStrideBytes
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
                MinBindingSize = SegmentStrideBytes
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

    /// <summary>
    /// Flattened path payload used during coverage rasterization.
    /// </summary>
    private readonly struct CoveragePathBuild
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CoveragePathBuild"/> struct.
        /// </summary>
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

        /// <summary>
        /// Gets the cached geometry payload.
        /// </summary>
        public CachedCoverageGeometry Geometry { get; }

        /// <summary>
        /// Gets the atlas origin in tile coordinates on the X axis.
        /// </summary>
        public int OriginTileX { get; }

        /// <summary>
        /// Gets the atlas origin in tile coordinates on the Y axis.
        /// </summary>
        public int OriginTileY { get; }

        /// <summary>
        /// Gets the atlas origin in pixel coordinates on the X axis.
        /// </summary>
        public int OriginX { get; }

        /// <summary>
        /// Gets the atlas origin in pixel coordinates on the Y axis.
        /// </summary>
        public int OriginY { get; }
    }

    /// <summary>
    /// Rasterizer dispatch configuration for a coverage pass.
    /// </summary>
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

    /// <summary>
    /// GPU bump allocator counters for transient coverage buffers.
    /// </summary>
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

    /// <summary>
    /// Indirect dispatch counts emitted by the coverage setup stage.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IndirectCountData
    {
        public uint CountX;
        public uint CountY;
        public uint CountZ;
    }

    /// <summary>
    /// Segment allocator configuration for coverage path allocation.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SegmentAllocConfig
    {
        public uint TileCount;
        public uint Pad0;
        public uint Pad1;
        public uint Pad2;
    }

    /// <summary>
    /// Coverage pass configuration shared across compute stages.
    /// </summary>
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

    /// <summary>
    /// Cached CPU-side geometry payload reused across coverage flushes.
    /// </summary>
    private sealed class CachedCoverageGeometry : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CachedCoverageGeometry"/> class.
        /// </summary>
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

        /// <summary>
        /// Gets the owned line segment buffer for the cached coverage geometry.
        /// </summary>
        public IMemoryOwner<byte>? LineOwner { get; }

        /// <summary>
        /// Gets the number of lines stored in <see cref="LineOwner"/>.
        /// </summary>
        public int LineCount { get; }

        /// <summary>
        /// Gets the estimated number of segments generated for this geometry.
        /// </summary>
        public uint EstimatedSegments { get; }

        /// <summary>
        /// Gets the coverage width in tiles.
        /// </summary>
        public int WidthInTiles { get; }

        /// <summary>
        /// Gets the coverage height in tiles.
        /// </summary>
        public int HeightInTiles { get; }

        /// <summary>
        /// Gets the coverage texture width in pixels.
        /// </summary>
        public int CoverageWidth { get; }

        /// <summary>
        /// Gets the coverage texture height in pixels.
        /// </summary>
        public int CoverageHeight { get; }

        /// <inheritdoc/>
        public void Dispose()
            => this.LineOwner?.Dispose();
    }
}
