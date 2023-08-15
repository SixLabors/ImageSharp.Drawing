// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#nullable disable

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

internal partial class ScanEdgeCollection : IDisposable
{
    private IMemoryOwner<ScanEdge> buffer;

    private Memory<ScanEdge> memory;

    private ScanEdgeCollection(IMemoryOwner<ScanEdge> buffer, int count)
    {
        this.buffer = buffer;
        this.memory = buffer.Memory.Slice(0, count);
    }

    public Span<ScanEdge> Edges => this.memory.Span;

    public int Count => this.Edges.Length;

    public void Dispose()
    {
        if (this.buffer == null)
        {
            return;
        }

        this.buffer.Dispose();
        this.buffer = null;
        this.memory = default;
    }

    public static ScanEdgeCollection Create(
        IPath polygon,
        MemoryAllocator allocator,
        int subsampling)
    {
        using TessellatedMultipolygon multipolygon = TessellatedMultipolygon.Create(polygon, allocator);
        return Create(multipolygon, allocator, subsampling);
    }
}
