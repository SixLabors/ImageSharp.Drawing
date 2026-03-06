// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal sealed unsafe partial class WebGPUDrawingBackend
{
    private const int TileHeight = 16;
    private const int EdgeStrideBytes = 32;
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
        out int totalCsrEntries,
        out int totalCsrIndices,
        out WgpuBuffer* csrOffsetsBuffer,
        out nuint csrOffsetsBufferSize,
        out WgpuBuffer* csrIndicesBuffer,
        out nuint csrIndicesBufferSize,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        edgeBuffer = null;
        edgeBufferSize = 0;
        edgePlacements = [];
        totalEdgeCount = 0;
        totalCsrEntries = 0;
        totalCsrIndices = 0;
        csrOffsetsBuffer = null;
        csrOffsetsBufferSize = 0;
        csrIndicesBuffer = null;
        csrIndicesBufferSize = 0;
        error = null;
        if (definitions.Count == 0)
        {
            return true;
        }

        edgePlacements = new EdgePlacement[definitions.Count];
        int runningEdgeStart = 0;
        int runningCsrOffset = 0;

        // Build flattened geometry for each definition and compute edge placements.
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

            if (!TryBuildFixedPointEdges(
                    definition.Path,
                    in interest,
                    definition.RasterizerOptions.SamplingOrigin,
                    out GpuEdge[]? defEdges,
                    out int edgeCount,
                    out int bandOverlaps,
                    out uint[]? defCsrOffsets,
                    out uint[]? defCsrIndices,
                    out error))
            {
                // Return any already-built geometry arrays on failure.
                for (int j = 0; j < i; j++)
                {
                    ReturnEdgeArray(geometries[j].Edges);
                }

                return false;
            }

            geometries[i] = new DefinitionGeometry(defEdges, edgeCount, bandCount, bandOverlaps, defCsrOffsets, defCsrIndices);

            int csrEntriesForDef = bandCount + 1;
            edgePlacements[i] = new EdgePlacement(
                (uint)runningEdgeStart,
                (uint)edgeCount,
                fillRule,
                (uint)runningCsrOffset,
                (uint)bandCount);

            runningEdgeStart += edgeCount;
            runningCsrOffset += csrEntriesForDef;
            totalCsrIndices += bandOverlaps;
        }

        totalEdgeCount = runningEdgeStart;
        totalCsrEntries = runningCsrOffset;

        if (totalEdgeCount == 0)
        {
            // Provide properly sized buffers even when there are no edges.
            // totalCsrEntries may be > 0 (definitions exist with band counts)
            // and the shader reads csr_offsets[csrOffsetsStart + band], so the
            // buffer must be large enough and zeroed.
            edgeBufferSize = EdgeStrideBytes;
            int emptyOffsetsCount = Math.Max(totalCsrEntries, 1);
            int emptyIndicesCount = Math.Max(totalCsrIndices, 1);
            csrOffsetsBufferSize = checked((nuint)(emptyOffsetsCount * sizeof(uint)));
            csrIndicesBufferSize = checked((nuint)(emptyIndicesCount * sizeof(uint)));
            if (!TryGetOrCreateCoverageBuffer(flushContext, "coverage-aggregated-edges", BufferUsage.Storage | BufferUsage.CopyDst, edgeBufferSize, out edgeBuffer, out error) ||
                !TryGetOrCreateCoverageBuffer(flushContext, "csr-offsets", BufferUsage.Storage | BufferUsage.CopyDst, csrOffsetsBufferSize, out csrOffsetsBuffer, out error) ||
                !TryGetOrCreateCoverageBuffer(flushContext, "csr-indices", BufferUsage.Storage | BufferUsage.CopyDst, csrIndicesBufferSize, out csrIndicesBuffer, out error))
            {
                return false;
            }

            // Zero the CSR buffers so the shader reads 0 edges per band.
            flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrOffsetsBuffer, 0, csrOffsetsBufferSize);
            flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrIndicesBuffer, 0, csrIndicesBufferSize);
            return true;
        }

        // Merge edge arrays and stamp per-definition metadata (CsrBandOffset, DefinitionEdgeStart).
        // For single-definition scenes (common case), stamp in-place and upload directly.
        int edgeBufferBytes = checked(totalEdgeCount * EdgeStrideBytes);
        edgeBufferSize = (nuint)edgeBufferBytes;

        GpuEdge[]? mergedEdges;
        bool mergedFromPool;
        if (geometries.Length == 1 && geometries[0].Edges is not null)
        {
            // Single definition: stamp metadata directly into source array.
            mergedEdges = geometries[0].Edges;
            mergedFromPool = true;
            EdgePlacement placement = edgePlacements[0];
            Span<GpuEdge> span = mergedEdges.AsSpan(0, totalEdgeCount);
            for (int i = 0; i < span.Length; i++)
            {
                span[i].CsrBandOffset = placement.CsrOffsetsStart;
                span[i].DefinitionEdgeStart = placement.EdgeStart;
            }
        }
        else
        {
            // Multiple definitions: merge into a new array.
            mergedEdges = ArrayPool<GpuEdge>.Shared.Rent(totalEdgeCount);
            mergedFromPool = true;
            int mergedEdgeIndex = 0;
            for (int defIndex = 0; defIndex < geometries.Length; defIndex++)
            {
                ref DefinitionGeometry geometry = ref geometries[defIndex];
                if (geometry.EdgeCount == 0 || geometry.Edges is null)
                {
                    continue;
                }

                EdgePlacement placement = edgePlacements[defIndex];
                ReadOnlySpan<GpuEdge> source = geometry.Edges.AsSpan(0, geometry.EdgeCount);
                Span<GpuEdge> dest = mergedEdges.AsSpan(mergedEdgeIndex, geometry.EdgeCount);
                source.CopyTo(dest);

                for (int i = 0; i < dest.Length; i++)
                {
                    dest[i].CsrBandOffset = placement.CsrOffsetsStart;
                    dest[i].DefinitionEdgeStart = placement.EdgeStart;
                }

                mergedEdgeIndex += geometry.EdgeCount;
            }
        }

        // Reinterpret typed array as bytes for GPU upload.
        Span<byte> edgeUpload = MemoryMarshal.AsBytes(mergedEdges.AsSpan(0, totalEdgeCount));

        if (!TryGetOrCreateCoverageBuffer(
                flushContext,
                "coverage-aggregated-edges",
                BufferUsage.Storage | BufferUsage.CopyDst,
                (nuint)edgeBufferBytes,
                out edgeBuffer,
                out error))
        {
            ReturnGeometries(geometries, mergedEdges, mergedFromPool);
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
            ReturnGeometries(geometries, mergedEdges, mergedFromPool);
            return false;
        }

        // Build merged CSR offsets and indices from pre-computed per-definition data.
        int csrOffsetsCount = Math.Max(totalCsrEntries, 1);
        int csrIndicesCount = Math.Max(totalCsrIndices, 1);
        csrOffsetsBufferSize = checked((nuint)(csrOffsetsCount * sizeof(uint)));
        csrIndicesBufferSize = checked((nuint)(csrIndicesCount * sizeof(uint)));

        if (!TryGetOrCreateCoverageBuffer(flushContext, "csr-offsets", BufferUsage.Storage | BufferUsage.CopyDst, csrOffsetsBufferSize, out csrOffsetsBuffer, out error) ||
            !TryGetOrCreateCoverageBuffer(flushContext, "csr-indices", BufferUsage.Storage | BufferUsage.CopyDst, csrIndicesBufferSize, out csrIndicesBuffer, out error))
        {
            ReturnGeometries(geometries, mergedEdges, mergedFromPool);
            return false;
        }

        if (totalEdgeCount > 0 && totalCsrEntries > 0)
        {
            // Write merged CSR offsets and indices. Each definition's per-band offsets
            // are shifted by a running base so indices from different definitions
            // don't overlap in the global csr_indices array.
            uint[] mergedOffsets = new uint[totalCsrEntries];
            uint[] mergedIndices = new uint[totalCsrIndices];
            uint runningIndicesBase = 0;
            for (int defIndex = 0; defIndex < geometries.Length; defIndex++)
            {
                ref DefinitionGeometry geometry = ref geometries[defIndex];
                if (geometry.CsrOffsets is null || geometry.CsrIndices is null)
                {
                    continue;
                }

                EdgePlacement placement = edgePlacements[defIndex];
                int csrStart = (int)placement.CsrOffsetsStart;
                uint[] defOffsets = geometry.CsrOffsets;
                uint[] defIndices = geometry.CsrIndices;

                // Copy offsets shifted by running base (bandCount + 1 entries).
                for (int b = 0; b < defOffsets.Length; b++)
                {
                    mergedOffsets[csrStart + b] = defOffsets[b] + runningIndicesBase;
                }

                // Copy indices.
                defIndices.AsSpan().CopyTo(mergedIndices.AsSpan((int)runningIndicesBase));
                runningIndicesBase += (uint)defIndices.Length;
            }

            fixed (uint* offsetsPtr = mergedOffsets)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    csrOffsetsBuffer,
                    0,
                    offsetsPtr,
                    (nuint)(totalCsrEntries * sizeof(uint)));
            }

            fixed (uint* indicesPtr = mergedIndices)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    csrIndicesBuffer,
                    0,
                    indicesPtr,
                    (nuint)(totalCsrIndices * sizeof(uint)));
            }
        }
        else
        {
            flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrOffsetsBuffer, 0, csrOffsetsBufferSize);
            flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrIndicesBuffer, 0, csrIndicesBufferSize);
        }

        ReturnGeometries(geometries, mergedEdges, mergedFromPool);

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

        if (firstDifferent < source.Length)
        {
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
        }

        source.CopyTo(cached);
        cachedLength = source.Length;
        return true;
    }

    /// <summary>
    /// Returns all pooled edge arrays from geometry entries.
    /// </summary>
    private static void ReturnGeometries(DefinitionGeometry[] geometries, GpuEdge[]? mergedEdges, bool mergedFromPool)
    {
        for (int i = 0; i < geometries.Length; i++)
        {
            // For single-definition, Edges == mergedEdges; only return once.
            if (geometries[i].Edges is not null && geometries[i].Edges != mergedEdges)
            {
                ArrayPool<GpuEdge>.Shared.Return(geometries[i].Edges!);
            }

            geometries[i].Edges = null;
        }

        if (mergedFromPool && mergedEdges is not null)
        {
            ArrayPool<GpuEdge>.Shared.Return(mergedEdges);
        }
    }

    /// <summary>
    /// Returns a single edge array to the pool.
    /// </summary>
    private static void ReturnEdgeArray(GpuEdge[]? edges)
    {
        if (edges is not null)
        {
            ArrayPool<GpuEdge>.Shared.Return(edges);
        }
    }

    private void DisposeCoverageResources()
    {
        this.cachedCoverageLineUpload?.Dispose();
        this.cachedCoverageLineUpload = null;
        this.cachedCoverageLineLength = 0;
    }

    /// <summary>
    /// Flattens a path into fixed-point (24.8) edge format for GPU rasterization.
    /// Each edge record is 32 bytes: x0, y0, x1, y1, min_row, max_row, 0, 0.
    /// </summary>
    private static bool TryBuildFixedPointEdges(
        IPath path,
        in Rectangle interest,
        RasterizerSamplingOrigin samplingOrigin,
        out GpuEdge[]? edges,
        out int edgeCount,
        out int totalBandOverlaps,
        out uint[]? csrOffsets,
        out uint[]? csrIndices,
        out string? error)
    {
        error = null;
        edges = null;
        edgeCount = 0;
        totalBandOverlaps = 0;
        csrOffsets = null;
        csrIndices = null;
        bool samplePixelCenter = samplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;
        int height = interest.Height;

        // Single-pass: flatten path and write edges directly as typed structs.
        // Use a pooled array that grows as needed.
        GpuEdge[] edgeArray = ArrayPool<GpuEdge>.Shared.Rent(1024);
        int validEdgeCount = 0;
        int bandOverlaps = 0;

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
                int nextIndex = j + 1;
                if (nextIndex == points.Length)
                {
                    nextIndex = 0;
                }

                PointF p1 = points[nextIndex];
                float fx0 = (p0.X - interest.X) + samplingOffsetX;
                float fy0 = (p0.Y - interest.Y) + samplingOffsetY;
                float fx1 = (p1.X - interest.X) + samplingOffsetX;
                float fy1 = (p1.Y - interest.Y) + samplingOffsetY;

                // Convert to 24.8 fixed-point.
                int x0 = (int)MathF.Round(fx0 * FixedOne);
                int y0 = (int)MathF.Round(fy0 * FixedOne);
                int x1 = (int)MathF.Round(fx1 * FixedOne);
                int y1 = (int)MathF.Round(fy1 * FixedOne);

                // Skip horizontal edges (no coverage contribution).
                if (y0 == y1)
                {
                    continue;
                }

                // Compute min/max row (pixel coordinates), clamped to interest.
                int yMinFixed = Math.Min(y0, y1);
                int yMaxFixed = Math.Max(y0, y1);
                int minRow = Math.Max(0, yMinFixed >> FixedShift);
                int maxRow = Math.Min(height - 1, (yMaxFixed - 1) >> FixedShift);

                if (minRow > maxRow)
                {
                    continue;
                }

                // Grow array if needed.
                if (validEdgeCount == edgeArray.Length)
                {
                    GpuEdge[] newArray = ArrayPool<GpuEdge>.Shared.Rent(edgeArray.Length * 2);
                    edgeArray.AsSpan(0, validEdgeCount).CopyTo(newArray);
                    ArrayPool<GpuEdge>.Shared.Return(edgeArray);
                    edgeArray = newArray;
                }

                edgeArray[validEdgeCount] = new GpuEdge
                {
                    X0 = x0,
                    Y0 = y0,
                    X1 = x1,
                    Y1 = y1,
                    MinRow = minRow,
                    MaxRow = maxRow,
                };
                bandOverlaps += (maxRow / TileHeight) - (minRow / TileHeight) + 1;
                validEdgeCount++;
            }
        }

        edgeCount = validEdgeCount;
        totalBandOverlaps = bandOverlaps;

        if (validEdgeCount == 0)
        {
            ArrayPool<GpuEdge>.Shared.Return(edgeArray);
            return true;
        }

        // Build CSR offsets and indices directly from struct fields.
        int bandCount = (int)DivideRoundUp(height, TileHeight);
        uint[] offsets = new uint[bandCount + 1];

        // Count edges per band.
        for (int edgeIdx = 0; edgeIdx < validEdgeCount; edgeIdx++)
        {
            ref GpuEdge edge = ref edgeArray[edgeIdx];
            int minBand = edge.MinRow / TileHeight;
            int maxBand = edge.MaxRow / TileHeight;
            for (int b = minBand; b <= maxBand; b++)
            {
                offsets[b]++;
            }
        }

        // Exclusive prefix sum → offsets[i] = start index for band i.
        uint running = 0;
        for (int b = 0; b <= bandCount; b++)
        {
            uint count = offsets[b];
            offsets[b] = running;
            running += count;
        }

        // Scatter: write local edge indices into CSR index array.
        uint[] indices = new uint[bandOverlaps];
        uint[] writeCursors = new uint[bandCount];
        for (int edgeIdx = 0; edgeIdx < validEdgeCount; edgeIdx++)
        {
            ref GpuEdge edge = ref edgeArray[edgeIdx];
            int minBand = edge.MinRow / TileHeight;
            int maxBand = edge.MaxRow / TileHeight;
            for (int b = minBand; b <= maxBand; b++)
            {
                uint slot = offsets[b] + writeCursors[b];
                indices[slot] = (uint)edgeIdx;
                writeCursors[b]++;
            }
        }

        csrOffsets = offsets;
        csrIndices = indices;
        edges = edgeArray;
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
    /// GPU edge record matching the WGSL storage buffer layout (32 bytes, sequential).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GpuEdge
    {
        public int X0;
        public int Y0;
        public int X1;
        public int Y1;
        public int MinRow;
        public int MaxRow;
        public uint CsrBandOffset;
        public uint DefinitionEdgeStart;
    }

    /// <summary>
    /// Transient per-definition geometry produced during edge buffer construction.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct DefinitionGeometry
    {
        public GpuEdge[]? Edges;
        public int EdgeCount;
        public int BandCount;
        public int TotalBandOverlaps;
        public uint[]? CsrOffsets;
        public uint[]? CsrIndices;

        public DefinitionGeometry(
            GpuEdge[]? edges,
            int edgeCount,
            int bandCount,
            int totalBandOverlaps,
            uint[]? csrOffsets,
            uint[]? csrIndices)
        {
            this.Edges = edges;
            this.EdgeCount = edgeCount;
            this.BandCount = bandCount;
            this.TotalBandOverlaps = totalBandOverlaps;
            this.CsrOffsets = csrOffsets;
            this.CsrIndices = csrIndices;
        }
    }
}
