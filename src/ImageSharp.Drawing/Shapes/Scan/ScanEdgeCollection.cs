// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    internal class ScanEdgeCollection : IDisposable
    {
        private IMemoryOwner<ScanEdge> buffer;

        private Memory<ScanEdge> memory;

        private ScanEdgeCollection(IMemoryOwner<ScanEdge> buffer, int count)
        {
            this.buffer = buffer;
            this.memory = buffer.Memory.Slice(0, count);
        }

        public Span<ScanEdge> Edges => this.memory.Span;

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

        public static ScanEdgeCollection Create(IPath path, MemoryAllocator allocator, in TolerantComparer comparer)
        {
            TessellatedMultipolygon multipolygon = TessellatedMultipolygon.Create(path, allocator, comparer);
            return Create(multipolygon, allocator, comparer);
        }

        public static ScanEdgeCollection Create(TessellatedMultipolygon multipolygon,  MemoryAllocator allocator, in TolerantComparer comparer)
        {
            throw new NotImplementedException();
        }
    }
}