// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <content>
/// Coverage rasterization helpers.
/// </content>
public sealed unsafe partial class WebGPUDrawingBackend
{
    private const int TileHeight = 16;
    private const int EdgeStrideBytes = 20;
    private const int FixedShift = 8;
    private const int FixedOne = 1 << FixedShift;
    private const int CsrWorkgroupSize = 256;

    private IMemoryOwner<byte>? cachedCoverageLineUpload;
    private int cachedCoverageLineLength;

    /// <summary>
    /// Writes bind-group entries and returns the number of populated entries.
    /// </summary>
    private delegate uint BindGroupEntryWriter(Span<BindGroupEntry> entries);

    /// <summary>
    /// Encapsulates dispatch logic for a compute pass.
    /// </summary>
    private delegate void ComputePassDispatch(ComputePassEncoder* pass);

    /// <summary>
    /// Builds flattened fixed-point edge geometry for all coverage definitions and uploads to a GPU buffer.
    /// Each edge is in 24.8 fixed-point format with min_row/max_row and CSR metadata.
    /// </summary>
    private bool TryCreateEdgeBuffer<TPixel>(
        WebGPUFlushContext flushContext,
        List<CompositionCoverageDefinition> definitions,
        Configuration configuration,
        out WgpuBuffer* edgeBuffer,
        out nuint edgeBufferSize,
        out EdgePlacement[] edgePlacements,
        out int totalEdgeCount,
        out int totalBandOffsetEntries,
        out WgpuBuffer* bandOffsetsBuffer,
        out nuint bandOffsetsBufferSize,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        edgeBuffer = null;
        edgeBufferSize = 0;
        edgePlacements = [];
        totalEdgeCount = 0;
        totalBandOffsetEntries = 0;
        bandOffsetsBuffer = null;
        bandOffsetsBufferSize = 0;
        error = null;
        if (definitions.Count == 0)
        {
            return true;
        }

        edgePlacements = new EdgePlacement[definitions.Count];
        int runningEdgeStart = 0;
        int runningBandOffset = 0;

        // Build pre-split geometry for each definition and compute edge placements.
        DefinitionGeometry[] geometries = new DefinitionGeometry[definitions.Count];
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
            int bandCount = (int)DivideRoundUp(interest.Height, TileHeight);

            // For stroke definitions, expand band assignment so distance-field
            // lookups can find nearby edges beyond the edge's own Y range.
            int strokeExpand = definition.IsStroke
                ? (int)MathF.Ceiling(definition.StrokeWidth * 0.5f) + 1
                : 0;

            IMemoryOwner<GpuEdge>? defEdgeOwner;
            int edgeCount;
            uint[]? defBandOffsets;
            bool edgeSuccess;
            if (definition.IsStroke)
            {
                edgeSuccess = TryBuildStrokeEdges(
                    flushContext.MemoryAllocator,
                    definition.Path,
                    in interest,
                    definition.RasterizerOptions.SamplingOrigin,
                    definition.StrokeWidth * 0.5f,
                    definition.StrokeOptions?.LineJoin ?? LineJoin.Bevel,
                    (float)(definition.StrokeOptions?.MiterLimit ?? 4.0),
                    out defEdgeOwner,
                    out edgeCount,
                    out defBandOffsets,
                    out error,
                    strokeExpand);
            }
            else
            {
                edgeSuccess = TryBuildFixedPointEdges(
                    flushContext.MemoryAllocator,
                    definition.Path,
                    in interest,
                    definition.RasterizerOptions.SamplingOrigin,
                    out defEdgeOwner,
                    out edgeCount,
                    out defBandOffsets,
                    out error);
            }

            if (!edgeSuccess)
            {
                // Dispose any already-built geometry on failure.
                for (int j = 0; j < i; j++)
                {
                    geometries[j].EdgeOwner?.Dispose();
                }

                return false;
            }

            geometries[i] = new DefinitionGeometry(defEdgeOwner, edgeCount, bandCount, defBandOffsets);

            int bandOffsetEntriesForDef = bandCount + 1;
            uint strokeLineCap = definition.IsStroke ? (uint)(definition.StrokeOptions?.LineCap ?? LineCap.Butt) : 0;
            uint strokeLineJoin = definition.IsStroke ? (uint)(definition.StrokeOptions?.LineJoin ?? LineJoin.Bevel) : 0;
            float strokeMiterLimit = definition.IsStroke ? (float)(definition.StrokeOptions?.MiterLimit ?? 4.0) : 0f;

            edgePlacements[i] = new EdgePlacement(
                (uint)runningEdgeStart,
                (uint)edgeCount,
                fillRule,
                (uint)runningBandOffset,
                (uint)bandCount,
                definition.IsStroke,
                definition.StrokeWidth,
                strokeLineCap,
                strokeLineJoin,
                strokeMiterLimit);

            runningEdgeStart += edgeCount;
            runningBandOffset += bandOffsetEntriesForDef;
        }

        totalEdgeCount = runningEdgeStart;
        totalBandOffsetEntries = runningBandOffset;

        if (totalEdgeCount == 0)
        {
            // Provide properly sized buffers even when there are no edges.
            // The shader reads band_offsets[bandOffsetsStart + band], so the
            // buffer must be large enough and zeroed.
            edgeBufferSize = EdgeStrideBytes;
            int emptyOffsetsCount = Math.Max(totalBandOffsetEntries, 1);
            bandOffsetsBufferSize = checked((nuint)(emptyOffsetsCount * sizeof(uint)));
            if (!TryGetOrCreateCoverageBuffer(flushContext, "coverage-aggregated-edges", BufferUsage.Storage | BufferUsage.CopyDst, edgeBufferSize, out edgeBuffer, out error) ||
                !TryGetOrCreateCoverageBuffer(flushContext, "band-offsets", BufferUsage.Storage | BufferUsage.CopyDst, bandOffsetsBufferSize, out bandOffsetsBuffer, out error))
            {
                return false;
            }

            flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, bandOffsetsBuffer, 0, bandOffsetsBufferSize);
            return true;
        }

        // Merge edge arrays. Edges are already pre-split and band-sorted per definition.
        // For single-definition scenes (common case), use the buffer directly.
        int edgeBufferBytes = checked(totalEdgeCount * EdgeStrideBytes);
        edgeBufferSize = (nuint)edgeBufferBytes;

        IMemoryOwner<GpuEdge>? mergedEdgeOwner;
        if (geometries.Length == 1 && geometries[0].EdgeOwner is not null)
        {
            // Single definition: use directly (no per-edge metadata to stamp).
            mergedEdgeOwner = geometries[0].EdgeOwner!;
        }
        else
        {
            // Multiple definitions: concatenate into a new buffer.
            mergedEdgeOwner = flushContext.MemoryAllocator.Allocate<GpuEdge>(totalEdgeCount);
            int mergedEdgeIndex = 0;
            for (int defIndex = 0; defIndex < geometries.Length; defIndex++)
            {
                ref DefinitionGeometry geometry = ref geometries[defIndex];
                if (geometry.EdgeCount == 0 || geometry.EdgeOwner is null)
                {
                    continue;
                }

                ReadOnlySpan<GpuEdge> source = geometry.EdgeOwner.Memory.Span[..geometry.EdgeCount];
                Span<GpuEdge> dest = mergedEdgeOwner.Memory.Span.Slice(mergedEdgeIndex, geometry.EdgeCount);
                source.CopyTo(dest);
                mergedEdgeIndex += geometry.EdgeCount;
            }
        }

        // Reinterpret typed buffer as bytes for GPU upload.
        Span<byte> edgeUpload = MemoryMarshal.AsBytes(mergedEdgeOwner.Memory.Span[..totalEdgeCount]);

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-edges",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)edgeBufferBytes,
                out edgeBuffer,
                out error))
        {
            DisposeGeometries(geometries, mergedEdgeOwner);
            return false;
        }

        if (!this.TryUploadDirtyCoverageRange(
                flushContext,
                edgeBuffer,
                edgeUpload,
                ref this.cachedCoverageLineUpload,
                ref this.cachedCoverageLineLength,
                out error))
        {
            DisposeGeometries(geometries, mergedEdgeOwner);
            return false;
        }

        // Build merged band offsets from pre-computed per-definition data.
        // Band offsets are local to each definition's edge range (0-based).
        int bandOffsetsCount = Math.Max(totalBandOffsetEntries, 1);
        bandOffsetsBufferSize = checked((nuint)(bandOffsetsCount * sizeof(uint)));

        if (!TryGetOrCreateCoverageBuffer(flushContext, "band-offsets", BufferUsage.Storage | BufferUsage.CopyDst, bandOffsetsBufferSize, out bandOffsetsBuffer, out error))
        {
            DisposeGeometries(geometries, mergedEdgeOwner);
            return false;
        }

        if (totalEdgeCount > 0 && totalBandOffsetEntries > 0)
        {
            uint[] mergedOffsets = new uint[totalBandOffsetEntries];
            for (int defIndex = 0; defIndex < geometries.Length; defIndex++)
            {
                ref DefinitionGeometry geometry = ref geometries[defIndex];
                if (geometry.BandOffsets is null)
                {
                    continue;
                }

                EdgePlacement placement = edgePlacements[defIndex];
                int bandStart = (int)placement.CsrOffsetsStart;
                uint[] defOffsets = geometry.BandOffsets;

                // Copy band offsets directly (already 0-based per definition).
                for (int b = 0; b < defOffsets.Length; b++)
                {
                    mergedOffsets[bandStart + b] = defOffsets[b];
                }
            }

            fixed (uint* offsetsPtr = mergedOffsets)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    bandOffsetsBuffer,
                    0,
                    offsetsPtr,
                    (nuint)(totalBandOffsetEntries * sizeof(uint)));
            }
        }
        else
        {
            flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, bandOffsetsBuffer, 0, bandOffsetsBufferSize);
        }

        DisposeGeometries(geometries, mergedEdgeOwner);

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
        int commonAlignedLength = commonLength & ~0x3;
        ReadOnlySpan<uint> sourceWords = MemoryMarshal.Cast<byte, uint>(source[..commonAlignedLength]);
        ReadOnlySpan<uint> cachedWords = MemoryMarshal.Cast<byte, uint>(cached[..commonAlignedLength]);

        int firstDifferentWord = 0;
        while (firstDifferentWord < sourceWords.Length && cachedWords[firstDifferentWord] == sourceWords[firstDifferentWord])
        {
            firstDifferentWord++;
        }

        int firstDifferent = firstDifferentWord * sizeof(uint);
        while (firstDifferent < commonLength && cached[firstDifferent] == source[firstDifferent])
        {
            firstDifferent++;
        }

        if (firstDifferent >= source.Length)
        {
            // Data is identical to cache — skip upload and copy.
            return true;
        }

        int lastDifferent = source.Length - 1;
        if (lastDifferent < commonLength)
        {
            while (lastDifferent >= firstDifferent &&
                   lastDifferent >= commonAlignedLength &&
                   cached[lastDifferent] == source[lastDifferent])
            {
                lastDifferent--;
            }

            int firstWordIndex = firstDifferent / sizeof(uint);
            int lastWordIndex = Math.Min(lastDifferent / sizeof(uint), sourceWords.Length - 1);
            while (lastWordIndex >= firstWordIndex && cachedWords[lastWordIndex] == sourceWords[lastWordIndex])
            {
                lastWordIndex--;
            }

            if (lastWordIndex >= firstWordIndex)
            {
                lastDifferent = Math.Min(lastDifferent, (lastWordIndex * sizeof(uint)) + (sizeof(uint) - 1));
            }
        }

        int uploadOffset = firstDifferent & ~0x3;
        int uploadEnd = firstDifferent + (lastDifferent - firstDifferent) + 1;
        uploadEnd = (uploadEnd + 3) & ~0x3;
        uploadEnd = Math.Min(uploadEnd, source.Length);
        int uploadLength = uploadEnd - uploadOffset;

        fixed (byte* sourcePtr = source)
        {
            flushContext.Api.QueueWriteBuffer(
                flushContext.Queue,
                destinationBuffer,
                (nuint)uploadOffset,
                sourcePtr + uploadOffset,
                (nuint)uploadLength);
        }

        source.CopyTo(cached);
        cachedLength = source.Length;
        return true;
    }

    /// <summary>
    /// Disposes all edge memory owners from geometry entries and the merged owner.
    /// </summary>
    private static void DisposeGeometries(DefinitionGeometry[] geometries, IMemoryOwner<GpuEdge>? mergedEdgeOwner)
    {
        for (int i = 0; i < geometries.Length; i++)
        {
            // For single-definition, EdgeOwner == mergedEdgeOwner; only dispose once.
            if (geometries[i].EdgeOwner is not null && geometries[i].EdgeOwner != mergedEdgeOwner)
            {
                geometries[i].EdgeOwner!.Dispose();
            }

            geometries[i].EdgeOwner = null;
        }

        mergedEdgeOwner?.Dispose();
    }

    private void DisposeCoverageResources()
    {
        this.cachedCoverageLineUpload?.Dispose();
        this.cachedCoverageLineUpload = null;
        this.cachedCoverageLineLength = 0;
    }

    /// <summary>
    /// Flattens a path into fixed-point (24.8) edges placed into per-band regions.
    /// Edges spanning multiple bands are duplicated (not clipped) so each band's
    /// range is contiguous. Band offsets provide direct indexing into the edge buffer.
    /// The shader handles per-tile Y clipping via clip_vertical.
    /// </summary>
    /// <remarks>
    /// Uses a two-pass approach over the flattened path to avoid an intermediate buffer:
    /// pass 1 counts edges per band, pass 2 scatters directly into the final buffer.
    /// </remarks>
    private static bool TryBuildFixedPointEdges(
        MemoryAllocator allocator,
        IPath path,
        in Rectangle interest,
        RasterizerSamplingOrigin samplingOrigin,
        out IMemoryOwner<GpuEdge>? edgeOwner,
        out int edgeCount,
        out uint[]? bandOffsets,
        out string? error)
    {
        error = null;
        edgeOwner = null;
        edgeCount = 0;
        bandOffsets = null;
        bool samplePixelCenter = samplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;
        int height = interest.Height;
        int interestX = interest.X;
        int interestY = interest.Y;
        int bandCount = (int)DivideRoundUp(height, TileHeight);

        // Pass 1: Flatten path and count edges per band.
        int totalSubEdges = 0;
        int[] bandCounts = new int[bandCount];

        foreach (ISimplePath simplePath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = simplePath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            int segmentCount = simplePath.IsClosed ? points.Length : points.Length - 1;
            for (int j = 0; j < segmentCount; j++)
            {
                PointF p0 = points[j];
                PointF p1 = points[j + 1 == points.Length ? 0 : j + 1];

                int y0 = (int)MathF.Round(((p0.Y - interestY) + samplingOffsetY) * FixedOne);
                int y1 = (int)MathF.Round(((p1.Y - interestY) + samplingOffsetY) * FixedOne);
                if (y0 == y1)
                {
                    continue;
                }

                int yMinFixed = Math.Min(y0, y1);
                int yMaxFixed = Math.Max(y0, y1);
                int minRow = Math.Max(0, yMinFixed >> FixedShift);
                int maxRow = Math.Min(height - 1, (yMaxFixed - 1) >> FixedShift);

                if (minRow > maxRow)
                {
                    continue;
                }

                int minBand = minRow / TileHeight;
                int maxBand = maxRow / TileHeight;
                for (int b = minBand; b <= maxBand; b++)
                {
                    bandCounts[b]++;
                }

                totalSubEdges += maxBand - minBand + 1;
            }
        }

        if (totalSubEdges == 0)
        {
            return true;
        }

        // Prefix sum → band offsets.
        uint[] offsets = new uint[bandCount + 1];
        uint running = 0;
        for (int b = 0; b < bandCount; b++)
        {
            offsets[b] = running;
            running += (uint)bandCounts[b];
        }

        offsets[bandCount] = running;

        // Pass 2: Flatten again and scatter edges directly into the final buffer.
        IMemoryOwner<GpuEdge> finalOwner = allocator.Allocate<GpuEdge>(totalSubEdges);
        Span<GpuEdge> finalSpan = finalOwner.Memory.Span;
        uint[] writeCursors = new uint[bandCount];

        foreach (ISimplePath simplePath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = simplePath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            int segmentCount = simplePath.IsClosed ? points.Length : points.Length - 1;
            for (int j = 0; j < segmentCount; j++)
            {
                PointF p0 = points[j];
                PointF p1 = points[j + 1 == points.Length ? 0 : j + 1];
                float fx0 = (p0.X - interestX) + samplingOffsetX;
                float fy0 = (p0.Y - interestY) + samplingOffsetY;
                float fx1 = (p1.X - interestX) + samplingOffsetX;
                float fy1 = (p1.Y - interestY) + samplingOffsetY;

                int x0 = (int)MathF.Round(fx0 * FixedOne);
                int y0 = (int)MathF.Round(fy0 * FixedOne);
                int x1 = (int)MathF.Round(fx1 * FixedOne);
                int y1 = (int)MathF.Round(fy1 * FixedOne);
                if (y0 == y1)
                {
                    continue;
                }

                int yMinFixed = Math.Min(y0, y1);
                int yMaxFixed = Math.Max(y0, y1);
                int minRow = Math.Max(0, yMinFixed >> FixedShift);
                int maxRow = Math.Min(height - 1, (yMaxFixed - 1) >> FixedShift);

                if (minRow > maxRow)
                {
                    continue;
                }

                GpuEdge edge = new() { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };
                int minBand = minRow / TileHeight;
                int maxBand = maxRow / TileHeight;
                for (int band = minBand; band <= maxBand; band++)
                {
                    finalSpan[(int)(offsets[band] + writeCursors[band])] = edge;
                    writeCursors[band]++;
                }
            }
        }

        edgeOwner = finalOwner;
        edgeCount = totalSubEdges;
        bandOffsets = offsets;
        return true;
    }

    /// <summary>
    /// Builds stroke-specific fixed-point edge geometry with cap flags and miter extensions.
    /// Unlike the fill edge builder, this includes horizontal edges and computes
    /// per-vertex miter extensions for Miter/MiterRevert/MiterRound join types.
    /// </summary>
    private static bool TryBuildStrokeEdges(
        MemoryAllocator allocator,
        IPath path,
        in Rectangle interest,
        RasterizerSamplingOrigin samplingOrigin,
        float halfWidth,
        LineJoin lineJoin,
        float miterLimit,
        out IMemoryOwner<GpuEdge>? edgeOwner,
        out int edgeCount,
        out uint[]? bandOffsets,
        out string? error,
        int strokeExpandPixels = 0)
    {
        error = null;
        edgeOwner = null;
        edgeCount = 0;
        bandOffsets = null;
        bool samplePixelCenter = samplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;
        int height = interest.Height;
        int interestX = interest.X;
        int interestY = interest.Y;
        int bandCount = (int)DivideRoundUp(height, TileHeight);
        bool isMiterJoin = lineJoin is LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound;

        // Pre-process: flatten all sub-paths and compute stroke edges with
        // miter extensions and endpoint flags.
        List<GpuEdge> strokeEdges = [];

        foreach (ISimplePath simplePath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = simplePath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            bool isClosed = simplePath.IsClosed;
            int segmentCount = isClosed ? points.Length : points.Length - 1;
            if (segmentCount == 0)
            {
                continue;
            }

            // Pre-compute per-vertex miter extensions.
            // extensions[j] is the amount to extend the outgoing segment backward
            // (and the incoming segment forward) at vertex j.
            float[] extensions = new float[points.Length];
            if (isMiterJoin)
            {
                ComputeMiterExtensions(points, isClosed, halfWidth, miterLimit, lineJoin, extensions);
            }

            for (int j = 0; j < segmentCount; j++)
            {
                int j1 = j + 1 == points.Length ? 0 : j + 1;
                PointF p0 = points[j];
                PointF p1 = points[j1];

                // Apply miter extensions at both endpoints.
                float ext0 = extensions[j];   // extension at start vertex (forward along this segment)
                float ext1 = extensions[j1];  // extension at end vertex (backward along this segment)

                if (ext0 > 0f || ext1 > 0f)
                {
                    float dx = p1.X - p0.X;
                    float dy = p1.Y - p0.Y;
                    float segLen = MathF.Sqrt((dx * dx) + (dy * dy));
                    if (segLen > 1e-6f)
                    {
                        float invLen = 1f / segLen;
                        float dirX = dx * invLen;
                        float dirY = dy * invLen;

                        // Extend start backward (away from p1).
                        if (ext0 > 0f)
                        {
                            p0 = new PointF(p0.X - (dirX * ext0), p0.Y - (dirY * ext0));
                        }

                        // Extend end forward (away from p0).
                        if (ext1 > 0f)
                        {
                            p1 = new PointF(p1.X + (dirX * ext1), p1.Y + (dirY * ext1));
                        }
                    }
                }

                // Compute cap flags.
                int flags = 0;
                if (!isClosed)
                {
                    if (j == 0)
                    {
                        flags |= 1; // open start at (x0, y0)
                    }

                    if (j == segmentCount - 1)
                    {
                        flags |= 2; // open end at (x1, y1)
                    }
                }

                float fx0 = (p0.X - interestX) + samplingOffsetX;
                float fy0 = (p0.Y - interestY) + samplingOffsetY;
                float fx1 = (p1.X - interestX) + samplingOffsetX;
                float fy1 = (p1.Y - interestY) + samplingOffsetY;

                int x0 = (int)MathF.Round(fx0 * FixedOne);
                int y0 = (int)MathF.Round(fy0 * FixedOne);
                int x1 = (int)MathF.Round(fx1 * FixedOne);
                int y1 = (int)MathF.Round(fy1 * FixedOne);

                strokeEdges.Add(new GpuEdge { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Flags = flags });
            }
        }

        if (strokeEdges.Count == 0)
        {
            return true;
        }

        // Count edges per band (including horizontal edges, with stroke expansion).
        int[] bandCounts = new int[bandCount];
        int totalSubEdges = 0;

        for (int i = 0; i < strokeEdges.Count; i++)
        {
            GpuEdge edge = strokeEdges[i];
            ComputeStrokeBandRange(edge, height, strokeExpandPixels, out int minBand, out int maxBand);
            if (minBand > maxBand)
            {
                continue;
            }

            for (int b = minBand; b <= maxBand; b++)
            {
                bandCounts[b]++;
            }

            totalSubEdges += maxBand - minBand + 1;
        }

        if (totalSubEdges == 0)
        {
            return true;
        }

        // Prefix sum → band offsets.
        uint[] offsets = new uint[bandCount + 1];
        uint running = 0;
        for (int b = 0; b < bandCount; b++)
        {
            offsets[b] = running;
            running += (uint)bandCounts[b];
        }

        offsets[bandCount] = running;

        // Scatter edges into band-sorted buffer.
        IMemoryOwner<GpuEdge> finalOwner = allocator.Allocate<GpuEdge>(totalSubEdges);
        Span<GpuEdge> finalSpan = finalOwner.Memory.Span;
        uint[] writeCursors = new uint[bandCount];

        for (int i = 0; i < strokeEdges.Count; i++)
        {
            GpuEdge edge = strokeEdges[i];
            ComputeStrokeBandRange(edge, height, strokeExpandPixels, out int minBand, out int maxBand);
            if (minBand > maxBand)
            {
                continue;
            }

            for (int band = minBand; band <= maxBand; band++)
            {
                finalSpan[(int)(offsets[band] + writeCursors[band])] = edge;
                writeCursors[band]++;
            }
        }

        edgeOwner = finalOwner;
        edgeCount = totalSubEdges;
        bandOffsets = offsets;
        return true;
    }

    /// <summary>
    /// Computes band range for a stroke edge, including horizontal edges and stroke expansion.
    /// </summary>
    private static void ComputeStrokeBandRange(in GpuEdge edge, int height, int strokeExpandPixels, out int minBand, out int maxBand)
    {
        int y0 = edge.Y0;
        int y1 = edge.Y1;

        int yMinFixed, yMaxFixed;
        if (y0 == y1)
        {
            // Horizontal edge: use the row at this Y position.
            yMinFixed = y0;
            yMaxFixed = y0 + 1; // ensure at least one row
        }
        else
        {
            yMinFixed = Math.Min(y0, y1);
            yMaxFixed = Math.Max(y0, y1);
        }

        int minRow = Math.Max(0, yMinFixed >> FixedShift);
        int maxRow = Math.Min(height - 1, (yMaxFixed - 1) >> FixedShift);

        // For horizontal edges the row range can be empty; use the Y row directly.
        if (y0 == y1)
        {
            int row = Math.Clamp(y0 >> FixedShift, 0, height - 1);
            minRow = row;
            maxRow = row;
        }

        if (strokeExpandPixels > 0)
        {
            minRow = Math.Max(0, minRow - strokeExpandPixels);
            maxRow = Math.Min(height - 1, maxRow + strokeExpandPixels);
        }

        if (minRow > maxRow)
        {
            minBand = 1;
            maxBand = 0; // empty range sentinel
            return;
        }

        minBand = minRow / TileHeight;
        maxBand = maxRow / TileHeight;
    }

    /// <summary>
    /// Pre-computes miter extension lengths at each vertex of a sub-path.
    /// At each interior vertex where two segments meet, the extension is
    /// <c>halfWidth / tan(halfAngle)</c>, clamped by the miter limit.
    /// </summary>
    private static void ComputeMiterExtensions(
        ReadOnlySpan<PointF> points,
        bool isClosed,
        float halfWidth,
        float miterLimit,
        LineJoin lineJoin,
        float[] extensions)
    {
        int n = points.Length;

        for (int i = 0; i < n; i++)
        {
            extensions[i] = 0f;
        }

        // For each interior vertex, compute the miter extension.
        int startVertex = isClosed ? 0 : 1;
        int endVertex = isClosed ? n : n - 1;
        float limit = halfWidth * miterLimit;

        for (int i = startVertex; i < endVertex; i++)
        {
            int prev = isClosed ? (i - 1 + n) % n : i - 1;
            int next = isClosed ? (i + 1) % n : i + 1;

            float dx1 = points[i].X - points[prev].X;
            float dy1 = points[i].Y - points[prev].Y;
            float dx2 = points[next].X - points[i].X;
            float dy2 = points[next].Y - points[i].Y;

            float len1 = MathF.Sqrt((dx1 * dx1) + (dy1 * dy1));
            float len2 = MathF.Sqrt((dx2 * dx2) + (dy2 * dy2));
            if (len1 < 1e-6f || len2 < 1e-6f)
            {
                continue;
            }

            // Normalize directions.
            float ux1 = dx1 / len1;
            float uy1 = dy1 / len1;
            float ux2 = dx2 / len2;
            float uy2 = dy2 / len2;

            // Dot product of directions gives cos(angle).
            float dot = (ux1 * ux2) + (uy1 * uy2);
            dot = Math.Clamp(dot, -1f, 1f);

            // Half-angle: cos(θ) = dot → θ = acos(dot) → half = θ/2.
            float angle = MathF.Acos(dot);
            float halfAngle = angle * 0.5f;
            float sinHalf = MathF.Sin(halfAngle);

            if (sinHalf < 1e-6f)
            {
                // Near-parallel segments (straight line), no miter needed.
                continue;
            }

            float cosHalf = MathF.Cos(halfAngle);
            float tanHalf = sinHalf / cosHalf;
            if (tanHalf < 1e-6f)
            {
                continue;
            }

            // Full extension along each segment = halfWidth / tan(halfAngle).
            float ext = halfWidth / tanHalf;

            // Miter distance from vertex = halfWidth / sin(halfAngle).
            float miterDistance = halfWidth / sinHalf;

            if (miterDistance <= limit)
            {
                // Within miter limit: full extension for all join types.
                extensions[i] = ext;
            }
            else
            {
                // Miter limit exceeded: behavior depends on join type.
                switch (lineJoin)
                {
                    case LineJoin.Miter:
                        // Clip the miter at the limit distance.
                        // The clipped extension is the projection of the clipped point onto the segment.
                        extensions[i] = limit * cosHalf;
                        break;

                    case LineJoin.MiterRevert:
                        // Bevel fallback: no extension needed.
                        break;

                    case LineJoin.MiterRound:
                        // Round fallback: natural SDF handles it, no extension needed.
                        break;
                }
            }
        }
    }

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
        ComputePassDescriptor passDescriptor = default;
        ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(flushContext.CommandEncoder, in passDescriptor);
        if (passEncoder is null)
        {
            error = $"Failed to begin compute pass for pipeline '{pipelineKey}'.";
            return false;
        }

        try
        {
            return this.DispatchIntoComputePass(
                flushContext,
                passEncoder,
                pipelineKey,
                shaderCode,
                bindGroupLayoutFactory,
                entryWriter,
                dispatch,
                out error);
        }
        finally
        {
            flushContext.Api.ComputePassEncoderEnd(passEncoder);
            flushContext.Api.ComputePassEncoderRelease(passEncoder);
        }
    }

    private bool DispatchIntoComputePass(
        WebGPUFlushContext flushContext,
        ComputePassEncoder* passEncoder,
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
        flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
        flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
        dispatch(passEncoder);

        error = null;
        return true;
    }

    /// <summary>
    /// Creates the bind-group layout used by the CSR count shader.
    /// </summary>
    private static bool TryCreateCsrCountBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
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
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = sizeof(uint)
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 3,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create CSR count bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates the bind-group layout used by the CSR scatter shader.
    /// </summary>
    private static bool TryCreateCsrScatterBindGroupLayout(
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
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
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
                MinBindingSize = 0
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
                MinBindingSize = 0
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = sizeof(uint)
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
            error = "Failed to create CSR scatter bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// CSR configuration uniform passed to count and scatter shaders.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CsrConfig
    {
        public readonly uint TotalEdgeCount;

        public CsrConfig(uint totalEdgeCount)
            => this.TotalEdgeCount = totalEdgeCount;
    }

    /// <summary>
    /// GPU edge record matching the WGSL storage buffer layout (16 bytes, sequential).
    /// Edges are pre-split at tile-row boundaries so each edge belongs to exactly one band.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GpuEdge
    {
        public int X0;
        public int Y0;
        public int X1;
        public int Y1;

        /// <summary>
        /// Bit flags for stroke edge metadata.
        /// Bit 0: open start — the (X0,Y0) endpoint is an open path start (cap applies).
        /// Bit 1: open end — the (X1,Y1) endpoint is an open path end (cap applies).
        /// </summary>
        public int Flags;
    }

    /// <summary>
    /// Transient per-definition geometry produced during edge buffer construction.
    /// Edges are pre-split at tile-row boundaries and sorted by band.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct DefinitionGeometry
    {
        public IMemoryOwner<GpuEdge>? EdgeOwner;
        public int EdgeCount;
        public int BandCount;
        public uint[]? BandOffsets;

        public DefinitionGeometry(
            IMemoryOwner<GpuEdge>? edgeOwner,
            int edgeCount,
            int bandCount,
            uint[]? bandOffsets)
        {
            this.EdgeOwner = edgeOwner;
            this.EdgeCount = edgeCount;
            this.BandCount = bandCount;
            this.BandOffsets = bandOffsets;
        }
    }
}
