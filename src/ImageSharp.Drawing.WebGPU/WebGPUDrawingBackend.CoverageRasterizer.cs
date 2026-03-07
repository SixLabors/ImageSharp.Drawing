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
    private const int EdgeStrideBytes = 16;
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

            if (!TryBuildFixedPointEdges(
                    flushContext.MemoryAllocator,
                    definition.Path,
                    in interest,
                    definition.RasterizerOptions.SamplingOrigin,
                    out IMemoryOwner<GpuEdge>? defEdgeOwner,
                    out int edgeCount,
                    out uint[]? defBandOffsets,
                    out error))
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
            edgePlacements[i] = new EdgePlacement(
                (uint)runningEdgeStart,
                (uint)edgeCount,
                fillRule,
                (uint)runningBandOffset,
                (uint)bandCount);

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
