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
    private const int EdgeStrideBytes = 28;
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
    /// Computes the maximum number of outline edges a single centerline edge can produce
    /// for the given stroke parameters. The worst case is a round join or cap where
    /// the arc step count scales with stroke width: max(4, ceil(π * halfWidth * 0.5)).
    /// </summary>
    private static int ComputeOutlineEdgesPerCenterline(float halfWidth, LineJoin lineJoin, LineCap lineCap)
    {
        // Side edges always produce exactly 2.
        // Join: 1 inner bevel + outer edges (miter variants: up to 3, bevel: 1, round: arc steps).
        // Cap: butt=1, square=3, round=arc steps.
        int roundArcSteps = Math.Max(4, (int)MathF.Ceiling(MathF.PI * halfWidth * 0.5f));
        int maxJoin = lineJoin is LineJoin.Round or LineJoin.MiterRound
            ? 1 + roundArcSteps
            : 4; // miter clamp worst case: 1 inner + 3 outer
        int maxCap = lineCap == LineCap.Round
            ? roundArcSteps
            : 3; // square cap
        return Math.Max(Math.Max(maxJoin, maxCap), 2);
    }

    private bool TryCreateEdgeBuffer<TPixel>(
        WebGPUFlushContext flushContext,
        List<CompositionCoverageDefinition> definitions,
        Configuration configuration,
        out WgpuBuffer* edgeBuffer,
        out nuint edgeBufferSize,
        out IMemoryOwner<EdgePlacement> edgePlacements,
        out int totalEdgeCount,
        out int totalBandOffsetEntries,
        out WgpuBuffer* bandOffsetsBuffer,
        out nuint bandOffsetsBufferSize,
        out StrokeExpandInfo strokeExpandInfo,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        edgeBuffer = null;
        edgeBufferSize = 0;
        edgePlacements = null!;
        totalEdgeCount = 0;
        totalBandOffsetEntries = 0;
        bandOffsetsBuffer = null;
        bandOffsetsBufferSize = 0;
        strokeExpandInfo = default;
        error = null;
        if (definitions.Count == 0)
        {
            return true;
        }

        edgePlacements = flushContext.MemoryAllocator.Allocate<EdgePlacement>(definitions.Count);
        Span<EdgePlacement> edgePlacementsSpan = edgePlacements.Memory.Span;
        int runningEdgeStart = 0;
        int runningBandOffset = 0;

        // Build pre-split geometry for each definition and compute edge placements.
        DefinitionGeometry[] geometries = new DefinitionGeometry[definitions.Count];

        // Track stroke definitions for the expand dispatch.
        List<StrokeExpandCommand>? strokeCommands = null;
        int totalStrokeCenterlineEdges = 0;
        int totalOutlineSlots = 0;

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

            IMemoryOwner<GpuEdge>? defEdgeOwner;
            int edgeCount;
            IMemoryOwner<uint>? defBandOffsets;
            bool edgeSuccess;
            if (definition.IsStroke)
            {
                edgeSuccess = TryBuildStrokeEdges(
                    flushContext.MemoryAllocator,
                    definition.Path,
                    in interest,
                    definition.RasterizerOptions.SamplingOrigin,
                    definition.StrokeWidth,
                    (float)(definition.StrokeOptions?.MiterLimit ?? 4.0),
                    bandCount,
                    out defEdgeOwner,
                    out edgeCount,
                    out defBandOffsets,
                    out error);
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
                for (int j = 0; j < i; j++)
                {
                    geometries[j].EdgeOwner?.Dispose();
                }

                return false;
            }

            geometries[i] = new DefinitionGeometry(defEdgeOwner, edgeCount, bandCount, defBandOffsets);

            if (definition.IsStroke && edgeCount > 0)
            {
                // Centerline edges are band-sorted. Create one StrokeExpandCommand per band
                // so the GPU expand shader writes outline edges into per-band output slots.
                // This produces band-sorted outline edges compatible with the fill rasterizer.
                Span<uint> clBandOffsets = defBandOffsets!.Memory.Span;
                LineCap defLineCap = definition.StrokeOptions?.LineCap ?? LineCap.Butt;
                LineJoin defLineJoin = definition.StrokeOptions?.LineJoin ?? LineJoin.Bevel;
                int outlineEdgesPerCenterline = ComputeOutlineEdgesPerCenterline(
                    definition.StrokeWidth * 0.5f, defLineJoin, defLineCap);
                strokeCommands ??= [];
                for (int b = 0; b < bandCount; b++)
                {
                    uint bandStart = clBandOffsets[b];
                    uint bandEnd = b + 1 < clBandOffsets.Length ? clBandOffsets[b + 1] : (uint)edgeCount;
                    uint bandEdgeCount = bandEnd - bandStart;
                    if (bandEdgeCount == 0)
                    {
                        continue;
                    }

                    int bandOutlineMax = (int)bandEdgeCount * outlineEdgesPerCenterline;
                    strokeCommands.Add(new StrokeExpandCommand(
                        i,
                        (uint)runningEdgeStart + bandStart,
                        bandEdgeCount,
                        definition.StrokeWidth * 0.5f,
                        (uint)defLineCap,
                        (uint)defLineJoin,
                        (float)(definition.StrokeOptions?.MiterLimit ?? 4.0),
                        bandOutlineMax,
                        Band: b));

                    totalOutlineSlots += bandOutlineMax;
                }

                totalStrokeCenterlineEdges += edgeCount;

                // Placeholder EdgePlacement — will be updated after outline space is allocated.
                int bandOffsetEntriesForDef = bandCount + 1;
                edgePlacementsSpan[i] = new EdgePlacement(
                    (uint)runningEdgeStart,
                    (uint)edgeCount,
                    fillRule,
                    (uint)runningBandOffset,
                    (uint)bandCount);

                runningEdgeStart += edgeCount;
                runningBandOffset += bandOffsetEntriesForDef;
            }
            else
            {
                int bandOffsetEntriesForDef = bandCount + 1;

                edgePlacementsSpan[i] = new EdgePlacement(
                    (uint)runningEdgeStart,
                    (uint)edgeCount,
                    fillRule,
                    (uint)runningBandOffset,
                    (uint)bandCount);

                runningEdgeStart += edgeCount;
                runningBandOffset += bandOffsetEntriesForDef;
            }
        }

        totalEdgeCount = runningEdgeStart;
        totalBandOffsetEntries = runningBandOffset;

        // Reserve outline edge space for stroke definitions.
        // Outline edges are placed after all centerline/fill edges in the buffer.
        // Each band within a stroke definition gets its own output slot so the
        // resulting outline edges are band-sorted — compatible with the fill rasterizer.
        // outlineBandOffsetsPerDef[defIndex] stores the outline band offsets array for
        // stroke definitions (null for fills). Used in the merged band offsets upload.
        IMemoryOwner<uint>?[] outlineBandOffsetsPerDef = new IMemoryOwner<uint>?[definitions.Count];
        int outlineRegionStart = totalEdgeCount;
        if (strokeCommands is not null)
        {
            // Assign per-command output offsets and build per-definition outline band offsets.
            int runningOutlineOffset = outlineRegionStart;
            for (int sc = 0; sc < strokeCommands.Count; sc++)
            {
                StrokeExpandCommand cmd = strokeCommands[sc];
                strokeCommands[sc] = cmd with
                {
                    OutputStart = (uint)runningOutlineOffset,
                    OutputMax = (uint)(runningOutlineOffset + cmd.OutlineMax)
                };
                runningOutlineOffset += cmd.OutlineMax;
            }

            totalEdgeCount = runningOutlineOffset;

            // Build outline band offsets for each stroke definition.
            // Commands are ordered by definition, then by band within each definition.
            int cmdCursor = 0;
            while (cmdCursor < strokeCommands.Count)
            {
                int defIndex = strokeCommands[cmdCursor].DefinitionIndex;
                int defBandCount = geometries[defIndex].BandCount;
                int defOutlineStart = (int)strokeCommands[cmdCursor].OutputStart;

                // Build full band offsets: offsets[b] = local offset within this def's outline region.
                IMemoryOwner<uint> outOffsetsOwner = flushContext.MemoryAllocator.Allocate<uint>(defBandCount + 1);
                Span<uint> outOffsets = outOffsetsOwner.Memory.Span;
                uint localOffset = 0;
                for (int b = 0; b < defBandCount; b++)
                {
                    outOffsets[b] = localOffset;
                    if (cmdCursor < strokeCommands.Count
                        && strokeCommands[cmdCursor].DefinitionIndex == defIndex
                        && strokeCommands[cmdCursor].Band == b)
                    {
                        localOffset += (uint)strokeCommands[cmdCursor].OutlineMax;
                        cmdCursor++;
                    }
                }

                int defOutlineTotal = (int)localOffset;
                outOffsets[defBandCount] = (uint)defOutlineTotal;
                outlineBandOffsetsPerDef[defIndex] = outOffsetsOwner;

                // Update EdgePlacement to point to the outline region.
                EdgePlacement oldPlacement = edgePlacementsSpan[defIndex];
                edgePlacementsSpan[defIndex] = new EdgePlacement(
                    (uint)defOutlineStart,
                    (uint)defOutlineTotal,
                    oldPlacement.FillRule,
                    (uint)runningBandOffset,
                    (uint)defBandCount);

                runningBandOffset += defBandCount + 1;
                totalBandOffsetEntries = runningBandOffset;
            }
        }

        if (totalEdgeCount == 0)
        {
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

        // Merge edge arrays. Includes space for outline edges at the end.
        int edgeBufferBytes = checked(totalEdgeCount * EdgeStrideBytes);
        edgeBufferSize = (nuint)edgeBufferBytes;

        // Upload only the centerline/fill edges; outline region will be written by GPU.
        int uploadEdgeCount = outlineRegionStart;

        IMemoryOwner<GpuEdge>? mergedEdgeOwner;
        if (geometries.Length == 1 && geometries[0].EdgeOwner is not null && strokeCommands is null)
        {
            mergedEdgeOwner = geometries[0].EdgeOwner!;
        }
        else
        {
            mergedEdgeOwner = flushContext.MemoryAllocator.Allocate<GpuEdge>(uploadEdgeCount > 0 ? uploadEdgeCount : 1);
            int mergedEdgeIndex = 0;
            for (int defIndex = 0; defIndex < geometries.Length; defIndex++)
            {
                ref DefinitionGeometry geometry = ref geometries[defIndex];
                if (geometry.EdgeCount == 0 || geometry.EdgeOwner is null)
                {
                    continue;
                }

                ReadOnlySpan<GpuEdge> source = geometry.EdgeOwner.Memory.Span;
                Span<GpuEdge> dest = mergedEdgeOwner.Memory.Span.Slice(mergedEdgeIndex, geometry.EdgeCount);
                source.CopyTo(dest);
                mergedEdgeIndex += geometry.EdgeCount;
            }
        }

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-edges",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)edgeBufferBytes,
                out edgeBuffer,
                out error))
        {
            DisposeGeometries(geometries, mergedEdgeOwner, outlineBandOffsetsPerDef);
            return false;
        }

        // Upload centerline/fill edges to the beginning of the buffer.
        if (uploadEdgeCount > 0)
        {
            Span<byte> edgeUpload = MemoryMarshal.AsBytes(mergedEdgeOwner.Memory.Span);
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
        }

        // Clear the outline region so unused slots have y0 == y1 == 0 (no winding contribution).
        if (totalOutlineSlots > 0)
        {
            nuint outlineByteOffset = (nuint)(outlineRegionStart * EdgeStrideBytes);
            nuint outlineByteSize = (nuint)(totalOutlineSlots * EdgeStrideBytes);
            flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, edgeBuffer, outlineByteOffset, outlineByteSize);
        }

        // Build merged band offsets from pre-computed per-definition data.
        int bandOffsetsCount = Math.Max(totalBandOffsetEntries, 1);
        bandOffsetsBufferSize = checked((nuint)(bandOffsetsCount * sizeof(uint)));

        if (!TryGetOrCreateCoverageBuffer(flushContext, "band-offsets", BufferUsage.Storage | BufferUsage.CopyDst, bandOffsetsBufferSize, out bandOffsetsBuffer, out error))
        {
            DisposeGeometries(geometries, mergedEdgeOwner, outlineBandOffsetsPerDef);
            return false;
        }

        if (totalEdgeCount > 0 && totalBandOffsetEntries > 0)
        {
            using IMemoryOwner<uint> mergedOffsetsOwner = flushContext.MemoryAllocator.Allocate<uint>(totalBandOffsetEntries, AllocationOptions.Clean);
            Span<uint> mergedOffsets = mergedOffsetsOwner.Memory.Span;
            for (int defIndex = 0; defIndex < geometries.Length; defIndex++)
            {
                ref DefinitionGeometry geometry = ref geometries[defIndex];
                EdgePlacement placement = edgePlacementsSpan[defIndex];
                int bandStart = (int)placement.CsrOffsetsStart;

                // Use outline band offsets for stroke definitions, fill band offsets otherwise.
                IMemoryOwner<uint>? defOffsetsOwner = outlineBandOffsetsPerDef[defIndex] ?? geometry.BandOffsets;
                if (defOffsetsOwner is not null)
                {
                    ReadOnlySpan<uint> defOffsets = defOffsetsOwner.Memory.Span;
                    for (int b = 0; b < defOffsets.Length; b++)
                    {
                        mergedOffsets[bandStart + b] = defOffsets[b];
                    }
                }
            }

            fixed (uint* offsetsPtr = &MemoryMarshal.GetReference(mergedOffsets))
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

        // Build stroke expand info for the GPU dispatch.
        if (strokeCommands is not null && strokeCommands.Count > 0)
        {
            strokeExpandInfo = new StrokeExpandInfo(strokeCommands, totalStrokeCenterlineEdges);
        }

        DisposeGeometries(geometries, mergedEdgeOwner, outlineBandOffsetsPerDef);

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
    private static void DisposeGeometries(
        DefinitionGeometry[] geometries,
        IMemoryOwner<GpuEdge>? mergedEdgeOwner,
        IMemoryOwner<uint>?[]? outlineBandOffsets = null)
    {
        for (int i = 0; i < geometries.Length; i++)
        {
            // For single-definition, EdgeOwner == mergedEdgeOwner; only dispose once.
            if (geometries[i].EdgeOwner is not null && geometries[i].EdgeOwner != mergedEdgeOwner)
            {
                geometries[i].EdgeOwner!.Dispose();
            }

            geometries[i].EdgeOwner = null;
            geometries[i].BandOffsets?.Dispose();
            geometries[i].BandOffsets = null;
        }

        mergedEdgeOwner?.Dispose();

        if (outlineBandOffsets is not null)
        {
            for (int i = 0; i < outlineBandOffsets.Length; i++)
            {
                outlineBandOffsets[i]?.Dispose();
                outlineBandOffsets[i] = null;
            }
        }
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
        out IMemoryOwner<uint>? bandOffsets,
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
        using IMemoryOwner<int> bandCountsOwner = allocator.Allocate<int>(bandCount, AllocationOptions.Clean);
        Span<int> bandCounts = bandCountsOwner.Memory.Span;

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
        IMemoryOwner<uint> offsetsOwner = allocator.Allocate<uint>(bandCount + 1);
        Span<uint> offsets = offsetsOwner.Memory.Span;
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
        using IMemoryOwner<uint> writeCursorsOwner = allocator.Allocate<uint>(bandCount, AllocationOptions.Clean);
        Span<uint> writeCursors = writeCursorsOwner.Memory.Span;

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
        bandOffsets = offsetsOwner;
        return true;
    }

    /// <summary>
    /// Builds stroke centerline edges with join and cap descriptors for GPU-side outline generation.
    /// The GPU shader expands centerline edges into outline polygon edges and rasterizes them
    /// using the same fill rasterizer. Edges are band-sorted with expanded Y ranges to account
    /// for stroke offset so each tile only processes edges relevant to its vertical range.
    /// </summary>
    private static bool TryBuildStrokeEdges(
        MemoryAllocator allocator,
        IPath path,
        in Rectangle interest,
        RasterizerSamplingOrigin samplingOrigin,
        float strokeWidth,
        float miterLimit,
        int bandCount,
        out IMemoryOwner<GpuEdge>? edgeOwner,
        out int edgeCount,
        out IMemoryOwner<uint>? bandOffsets,
        out string? error)
    {
        error = null;
        edgeOwner = null;
        edgeCount = 0;
        bandOffsets = null;
        bool samplePixelCenter = samplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;
        int interestX = interest.X;
        int interestY = interest.Y;
        int height = interest.Height;

        // Maximum Y expansion in pixels: miter joins can extend up to miterLimit * halfWidth.
        float halfWidth = strokeWidth * 0.5f;
        int yExpansionFixed = (int)MathF.Ceiling(Math.Max(miterLimit, 1f) * halfWidth * FixedOne);

        // Pass 1: Collect all stroke edges and count per band.
        List<GpuEdge> strokeEdges = [];
        List<(int YMinFixed, int YMaxFixed)> edgeYRanges = [];

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

            // Emit centerline edges.
            for (int j = 0; j < segmentCount; j++)
            {
                int j1 = j + 1 == points.Length ? 0 : j + 1;
                PointF p0 = points[j];
                PointF p1 = points[j1];

                float fy0 = (p0.Y - interestY) + samplingOffsetY;
                float fy1 = (p1.Y - interestY) + samplingOffsetY;
                int iy0 = (int)MathF.Round(fy0 * FixedOne);
                int iy1 = (int)MathF.Round(fy1 * FixedOne);

                strokeEdges.Add(new GpuEdge
                {
                    X0 = (int)MathF.Round(((p0.X - interestX) + samplingOffsetX) * FixedOne),
                    Y0 = iy0,
                    X1 = (int)MathF.Round(((p1.X - interestX) + samplingOffsetX) * FixedOne),
                    Y1 = iy1,
                });

                int eMin = Math.Min(iy0, iy1) - yExpansionFixed;
                int eMax = Math.Max(iy0, iy1) + yExpansionFixed;
                edgeYRanges.Add((eMin, eMax));
            }

            // Emit join descriptors at interior vertices.
            int startVertex = isClosed ? 0 : 1;
            int endVertex = isClosed ? points.Length : points.Length - 1;

            for (int i = startVertex; i < endVertex; i++)
            {
                int prev = isClosed ? ((i - 1 + points.Length) % points.Length) : i - 1;
                int next = isClosed ? ((i + 1) % points.Length) : i + 1;

                PointF v = points[i];
                PointF pv = points[prev];
                PointF nv = points[next];

                int vy = (int)MathF.Round(((v.Y - interestY) + samplingOffsetY) * FixedOne);

                strokeEdges.Add(new GpuEdge
                {
                    X0 = (int)MathF.Round(((v.X - interestX) + samplingOffsetX) * FixedOne),
                    Y0 = vy,
                    X1 = (int)MathF.Round(((pv.X - interestX) + samplingOffsetX) * FixedOne),
                    Y1 = (int)MathF.Round(((pv.Y - interestY) + samplingOffsetY) * FixedOne),
                    Flags = StrokeEdgeFlags.Join,
                    AdjX = (int)MathF.Round(((nv.X - interestX) + samplingOffsetX) * FixedOne),
                    AdjY = (int)MathF.Round(((nv.Y - interestY) + samplingOffsetY) * FixedOne),
                });

                edgeYRanges.Add((vy - yExpansionFixed, vy + yExpansionFixed));
            }

            // Emit cap descriptors at open endpoints.
            if (!isClosed)
            {
                PointF capStart = points[0];
                PointF adjStart = points[1];
                int csy = (int)MathF.Round(((capStart.Y - interestY) + samplingOffsetY) * FixedOne);

                strokeEdges.Add(new GpuEdge
                {
                    X0 = (int)MathF.Round(((capStart.X - interestX) + samplingOffsetX) * FixedOne),
                    Y0 = csy,
                    X1 = (int)MathF.Round(((adjStart.X - interestX) + samplingOffsetX) * FixedOne),
                    Y1 = (int)MathF.Round(((adjStart.Y - interestY) + samplingOffsetY) * FixedOne),
                    Flags = StrokeEdgeFlags.CapStart,
                });

                edgeYRanges.Add((csy - yExpansionFixed, csy + yExpansionFixed));

                PointF capEnd = points[^1];
                PointF adjEnd = points[^2];
                int cey = (int)MathF.Round(((capEnd.Y - interestY) + samplingOffsetY) * FixedOne);

                strokeEdges.Add(new GpuEdge
                {
                    X0 = (int)MathF.Round(((capEnd.X - interestX) + samplingOffsetX) * FixedOne),
                    Y0 = cey,
                    X1 = (int)MathF.Round(((adjEnd.X - interestX) + samplingOffsetX) * FixedOne),
                    Y1 = (int)MathF.Round(((adjEnd.Y - interestY) + samplingOffsetY) * FixedOne),
                    Flags = StrokeEdgeFlags.CapEnd,
                });

                edgeYRanges.Add((cey - yExpansionFixed, cey + yExpansionFixed));
            }
        }

        if (strokeEdges.Count == 0)
        {
            return true;
        }

        // Band-sort centerline edges using expanded Y ranges so each band contains
        // all centerline edges whose outline could affect that band's vertical range.
        // This mirrors TryBuildFixedPointEdges but uses pre-computed Y expansion.
        using IMemoryOwner<int> bandCountsOwner = allocator.Allocate<int>(bandCount, AllocationOptions.Clean);
        Span<int> bandCounts = bandCountsOwner.Memory.Span;
        int totalBandEdges = 0;

        for (int i = 0; i < strokeEdges.Count; i++)
        {
            (int yMin, int yMax) = edgeYRanges[i];
            int minRow = Math.Max(0, yMin >> FixedShift);
            int maxRow = Math.Min(height - 1, Math.Max(0, (yMax - 1) >> FixedShift));
            int minBand = Math.Min(minRow / TileHeight, bandCount - 1);
            int maxBand = Math.Min(maxRow / TileHeight, bandCount - 1);
            for (int b = minBand; b <= maxBand; b++)
            {
                bandCounts[b]++;
            }

            totalBandEdges += maxBand - minBand + 1;
        }

        if (totalBandEdges == 0)
        {
            return true;
        }

        // Prefix sum → band offsets.
        IMemoryOwner<uint> offsetsOwner = allocator.Allocate<uint>(bandCount + 1);
        Span<uint> offsets = offsetsOwner.Memory.Span;
        uint running = 0;
        for (int b = 0; b < bandCount; b++)
        {
            offsets[b] = running;
            running += (uint)bandCounts[b];
        }

        offsets[bandCount] = running;

        // Scatter centerline edges into band-sorted buffer.
        IMemoryOwner<GpuEdge> finalOwner = allocator.Allocate<GpuEdge>(totalBandEdges);
        Span<GpuEdge> finalSpan = finalOwner.Memory.Span;
        using IMemoryOwner<uint> writeCursorsOwner = allocator.Allocate<uint>(bandCount, AllocationOptions.Clean);
        Span<uint> writeCursors = writeCursorsOwner.Memory.Span;

        for (int i = 0; i < strokeEdges.Count; i++)
        {
            GpuEdge edge = strokeEdges[i];
            (int yMin, int yMax) = edgeYRanges[i];
            int minRow = Math.Max(0, yMin >> FixedShift);
            int maxRow = Math.Min(height - 1, Math.Max(0, (yMax - 1) >> FixedShift));
            int minBand = Math.Min(minRow / TileHeight, bandCount - 1);
            int maxBand = Math.Min(maxRow / TileHeight, bandCount - 1);
            for (int band = minBand; band <= maxBand; band++)
            {
                finalSpan[(int)(offsets[band] + writeCursors[band])] = edge;
                writeCursors[band]++;
            }
        }

        edgeOwner = finalOwner;
        edgeCount = totalBandEdges;
        bandOffsets = offsetsOwner;
        return true;
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
        /// Stroke edge type flags matching the WGSL shader constants.
        /// </summary>
        public StrokeEdgeFlags Flags;

        /// <summary>
        /// Auxiliary coordinates (fixed-point). For bevel fill edges, stores the
        /// join vertex V so the shader can compute the bevel triangle SDF.
        /// </summary>
        public int AdjX;
        public int AdjY;
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
        public IMemoryOwner<uint>? BandOffsets;

        public DefinitionGeometry(
            IMemoryOwner<GpuEdge>? edgeOwner,
            int edgeCount,
            int bandCount,
            IMemoryOwner<uint>? bandOffsets)
        {
            this.EdgeOwner = edgeOwner;
            this.EdgeCount = edgeCount;
            this.BandCount = bandCount;
            this.BandOffsets = bandOffsets;
        }
    }

    /// <summary>
    /// Describes a stroke expand command for the GPU shader.
    /// Each command expands one coverage definition's centerline edges into outline edges.
    /// </summary>
    private record struct StrokeExpandCommand(
        int DefinitionIndex,
        uint InputStart,
        uint InputCount,
        float HalfWidth,
        uint LineCap,
        uint LineJoin,
        float MiterLimit,
        int OutlineMax,
        uint OutputStart = 0,
        uint OutputMax = 0,
        int Band = 0);

    /// <summary>
    /// GPU-side stroke expand command matching the WGSL StrokeExpandCommand struct layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GpuStrokeExpandCommand
    {
        public readonly uint InputStart;
        public readonly uint InputCount;
        public readonly uint OutputStart;
        public readonly uint OutputMax;
        public readonly uint HalfWidth; // f32 as bits
        public readonly uint LineCap;
        public readonly uint LineJoin;
        public readonly uint MiterLimit; // f32 as bits

        public GpuStrokeExpandCommand(StrokeExpandCommand cmd)
        {
            this.InputStart = cmd.InputStart;
            this.InputCount = cmd.InputCount;
            this.OutputStart = cmd.OutputStart;
            this.OutputMax = cmd.OutputMax;
            this.HalfWidth = FloatToUInt32Bits(cmd.HalfWidth);
            this.LineCap = cmd.LineCap;
            this.LineJoin = cmd.LineJoin;
            this.MiterLimit = FloatToUInt32Bits(cmd.MiterLimit);
        }
    }

    /// <summary>
    /// GPU-side stroke expand config matching the WGSL StrokeExpandConfig struct layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GpuStrokeExpandConfig
    {
        public readonly uint TotalInputEdges;
        public readonly uint CommandCount;

        public GpuStrokeExpandConfig(uint totalInputEdges, uint commandCount)
        {
            this.TotalInputEdges = totalInputEdges;
            this.CommandCount = commandCount;
        }
    }

    /// <summary>
    /// Contains stroke expansion data needed for the GPU dispatch.
    /// </summary>
    private readonly struct StrokeExpandInfo
    {
        public readonly List<StrokeExpandCommand>? Commands;
        public readonly int TotalCenterlineEdges;

        public StrokeExpandInfo(List<StrokeExpandCommand>? commands, int totalCenterlineEdges)
        {
            this.Commands = commands;
            this.TotalCenterlineEdges = totalCenterlineEdges;
        }

        public bool HasCommands => this.Commands is not null && this.Commands.Count > 0;
    }
}
