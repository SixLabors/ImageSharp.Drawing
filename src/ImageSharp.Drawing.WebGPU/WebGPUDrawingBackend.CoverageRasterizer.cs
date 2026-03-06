// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
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

    private readonly Dictionary<CoverageDefinitionIdentity, CachedCoverageGeometry> coverageGeometryCache = [];
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
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        edgeBuffer = null;
        edgeBufferSize = 0;
        edgePlacements = [];
        totalEdgeCount = 0;
        totalCsrEntries = 0;
        totalCsrIndices = 0;
        error = null;
        if (definitions.Count == 0)
        {
            return true;
        }

        edgePlacements = new EdgePlacement[definitions.Count];
        int runningEdgeStart = 0;
        int runningCsrOffset = 0;

        // First pass: resolve/build cached geometry and compute edge placements.
        CachedCoverageGeometry?[] geometries = new CachedCoverageGeometry?[definitions.Count];
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

            CoverageDefinitionIdentity identity = new(definition);
            if (!this.coverageGeometryCache.TryGetValue(identity, out CachedCoverageGeometry? geometry))
            {
                if (!TryBuildFixedPointEdges(
                        definition.Path,
                        in interest,
                        definition.RasterizerOptions.SamplingOrigin,
                        configuration.MemoryAllocator,
                        out IMemoryOwner<byte>? edgeOwner,
                        out int edgeCount,
                        out int bandOverlaps,
                        out error))
                {
                    return false;
                }

                geometry = new CachedCoverageGeometry(edgeOwner, edgeCount, bandCount, bandOverlaps);
                this.coverageGeometryCache[identity] = geometry;
            }

            if (geometry is null)
            {
                error = "Failed to resolve cached coverage geometry.";
                return false;
            }

            geometries[i] = geometry;

            // bandCount + 1 entries in CSR offsets (the +1 is the sentinel for the last band's end).
            int csrEntriesForDef = bandCount + 1;
            edgePlacements[i] = new EdgePlacement(
                (uint)runningEdgeStart,
                (uint)geometry.EdgeCount,
                fillRule,
                (uint)runningCsrOffset,
                (uint)bandCount);

            runningEdgeStart += geometry.EdgeCount;
            runningCsrOffset += csrEntriesForDef;
            totalCsrIndices += geometry.TotalBandOverlaps;
        }

        totalEdgeCount = runningEdgeStart;
        totalCsrEntries = runningCsrOffset;

        if (totalEdgeCount == 0)
        {
            // Provide a minimal buffer so the bind group is valid.
            edgeBufferSize = EdgeStrideBytes;
            if (!TryGetOrCreateCoverageBuffer(
                    flushContext,
                    "coverage-aggregated-edges",
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    edgeBufferSize,
                    out edgeBuffer,
                    out error))
            {
                return false;
            }

            return true;
        }

        // Build merged edge buffer with CSR metadata.
        int edgeBufferBytes = checked(totalEdgeCount * EdgeStrideBytes);
        edgeBufferSize = (nuint)edgeBufferBytes;
        using IMemoryOwner<byte> edgeUploadOwner = configuration.MemoryAllocator.Allocate<byte>(edgeBufferBytes);
        Span<byte> edgeUpload = edgeUploadOwner.Memory.Span[..edgeBufferBytes];

        int mergedEdgeIndex = 0;
        for (int defIndex = 0; defIndex < geometries.Length; defIndex++)
        {
            CachedCoverageGeometry? geometry = geometries[defIndex];
            if (geometry is null || geometry.EdgeCount == 0 || geometry.EdgeOwner is null)
            {
                continue;
            }

            EdgePlacement placement = edgePlacements[defIndex];
            ReadOnlySpan<byte> sourceEdges = geometry.EdgeOwner.Memory.Span[..(geometry.EdgeCount * EdgeStrideBytes)];

            for (int edgeIndex = 0; edgeIndex < geometry.EdgeCount; edgeIndex++)
            {
                int srcOffset = edgeIndex * EdgeStrideBytes;
                int dstOffset = mergedEdgeIndex * EdgeStrideBytes;

                // Copy x0, y0, x1, y1, min_row, max_row (24 bytes).
                sourceEdges.Slice(srcOffset, 24).CopyTo(edgeUpload.Slice(dstOffset, 24));

                // Set csr_band_offset and definition_edge_start.
                BinaryPrimitives.WriteUInt32LittleEndian(edgeUpload.Slice(dstOffset + 24, 4), placement.CsrOffsetsStart);
                BinaryPrimitives.WriteUInt32LittleEndian(edgeUpload.Slice(dstOffset + 28, 4), placement.EdgeStart);
                mergedEdgeIndex++;
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
    }

    /// <summary>
    /// Flattens a path into fixed-point (24.8) edge format for GPU rasterization.
    /// Each edge record is 32 bytes: x0, y0, x1, y1, min_row, max_row, 0, 0.
    /// </summary>
    private static bool TryBuildFixedPointEdges(
        IPath path,
        in Rectangle interest,
        RasterizerSamplingOrigin samplingOrigin,
        MemoryAllocator allocator,
        out IMemoryOwner<byte>? edgeOwner,
        out int edgeCount,
        out int totalBandOverlaps,
        out string? error)
    {
        error = null;
        edgeOwner = null;
        edgeCount = 0;
        totalBandOverlaps = 0;
        bool samplePixelCenter = samplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;

        // First pass: count valid edges.
        List<ISimplePath> simplePaths = [];
        foreach (ISimplePath simplePath in path.Flatten())
        {
            simplePaths.Add(simplePath);
        }

        int maxEdgeCount = 0;
        for (int i = 0; i < simplePaths.Count; i++)
        {
            ReadOnlySpan<PointF> points = simplePaths[i].Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            int segmentCount = simplePaths[i].IsClosed ? points.Length : points.Length - 1;
            if (segmentCount > 0)
            {
                maxEdgeCount += segmentCount;
            }
        }

        if (maxEdgeCount == 0)
        {
            return true;
        }

        int height = interest.Height;
        int bufferBytes = checked(maxEdgeCount * EdgeStrideBytes);
        IMemoryOwner<byte> tempOwner = allocator.Allocate<byte>(bufferBytes);
        Span<byte> edgeBytes = tempOwner.Memory.Span[..bufferBytes];
        edgeBytes.Clear();

        int validEdgeCount = 0;
        int bandOverlaps = 0;
        for (int i = 0; i < simplePaths.Count; i++)
        {
            ReadOnlySpan<PointF> points = simplePaths[i].Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            bool contourClosed = simplePaths[i].IsClosed;
            int segmentCount = contourClosed ? points.Length : points.Length - 1;
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

                // Write edge record (32 bytes).
                int offset = validEdgeCount * EdgeStrideBytes;
                BinaryPrimitives.WriteInt32LittleEndian(edgeBytes.Slice(offset, 4), x0);
                BinaryPrimitives.WriteInt32LittleEndian(edgeBytes.Slice(offset + 4, 4), y0);
                BinaryPrimitives.WriteInt32LittleEndian(edgeBytes.Slice(offset + 8, 4), x1);
                BinaryPrimitives.WriteInt32LittleEndian(edgeBytes.Slice(offset + 12, 4), y1);
                BinaryPrimitives.WriteInt32LittleEndian(edgeBytes.Slice(offset + 16, 4), minRow);
                BinaryPrimitives.WriteInt32LittleEndian(edgeBytes.Slice(offset + 20, 4), maxRow);
                int minBand = minRow / TileHeight;
                int maxBand = maxRow / TileHeight;
                bandOverlaps += maxBand - minBand + 1;
                validEdgeCount++;
            }
        }

        edgeCount = validEdgeCount;
        totalBandOverlaps = bandOverlaps;

        if (validEdgeCount == 0)
        {
            tempOwner.Dispose();
            return true;
        }

        edgeOwner = tempOwner;
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
    /// Cached CPU-side geometry payload reused across coverage flushes.
    /// </summary>
    private sealed class CachedCoverageGeometry : IDisposable
    {
        public CachedCoverageGeometry(
            IMemoryOwner<byte>? edgeOwner,
            int edgeCount,
            int bandCount,
            int totalBandOverlaps)
        {
            this.EdgeOwner = edgeOwner;
            this.EdgeCount = edgeCount;
            this.BandCount = bandCount;
            this.TotalBandOverlaps = totalBandOverlaps;
        }

        /// <summary>
        /// Gets the owned fixed-point edge buffer.
        /// </summary>
        public IMemoryOwner<byte>? EdgeOwner { get; }

        /// <summary>
        /// Gets the number of edges stored in <see cref="EdgeOwner"/>.
        /// </summary>
        public int EdgeCount { get; }

        /// <summary>
        /// Gets the number of 16-row CSR bands for this geometry.
        /// </summary>
        public int BandCount { get; }

        /// <summary>
        /// Gets the total number of edge-band overlaps (for CSR indices sizing).
        /// </summary>
        public int TotalBandOverlaps { get; }

        /// <inheritdoc/>
        public void Dispose()
            => this.EdgeOwner?.Dispose();
    }
}
