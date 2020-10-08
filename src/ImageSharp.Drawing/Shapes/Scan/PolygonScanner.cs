// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Utilities;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    internal ref struct PolygonScanner
    {
        private readonly float min;
        private readonly float max;
        private readonly float step;
        private readonly MemoryAllocator allocator;
        private int counter;
        private readonly int counterMax;
        private ScanEdgeCollection edgeCollection;
        private Span<ScanEdge> edges;

        // Common contiguous buffer for sorted0, sorted1, intersections, activeEdges
        private IMemoryOwner<int> dataBuffer;

        // | <- edgeCnt -> | <- edgeCnt -> | <- edgeCnt -> | <- edgeCnt -> |
        // |---------------|---------------|---------------|---------------|
        // | sorted0       | sorted1       | intersections | activeEdges   |
        // |---------------|---------------|---------------|---------------|
        private Span<int> sorted0;
        private Span<int> sorted1;
        private Span<float> intersections;
        private ActiveEdgeList activeEdges;

        private PolygonScanner(ScanEdgeCollection edgeCollection, float min, float max, float step, MemoryAllocator allocator)
        {
            this.min = min;
            this.max = max;
            this.step = step;
            this.allocator = allocator;
            this.edgeCollection = edgeCollection;
            this.edges = edgeCollection.Edges;
            this.counter = -1;
            float range = max - min;
            this.counterMax = (int)MathF.Ceiling(range / step);
            int edgeCount = this.edges.Length;
            this.dataBuffer = allocator.Allocate<int>(edgeCount * 4);
            Span<int> dataBufferInt32Span = this.dataBuffer.Memory.Span;
            Span<float> dataBufferFloatSpan = MemoryMarshal.Cast<int, float>(dataBufferInt32Span);
            this.sorted0 = dataBufferInt32Span.Slice(0, edgeCount);
            this.sorted1 = dataBufferInt32Span.Slice(edgeCount, edgeCount);

            this.intersections = dataBufferFloatSpan.Slice(edgeCount * 2, edgeCount);

            // this.activeEdges = new ActiveEdgeList(dataBufferInt32Span.Slice(edgeCount * 3, edgeCount));
            this.activeEdges = default;
        }

        public static PolygonScanner Create(IPath polygon, float min, float max, float step, MemoryAllocator allocator)
        {
            var comparer = new TolerantComparer(step / 2f);
            ScanEdgeCollection edges = ScanEdgeCollection.Create(polygon, allocator, comparer);
            PolygonScanner scanner = new PolygonScanner(edges, min, max, step, allocator);
            scanner.Init();
            return scanner;
        }

        private void Init()
        {
            // Reuse memory buffers of 'intersections' and 'activeEdges' for key-value sorting,
            // since that region is unused at initialization time.
            Span<float> keys0 = this.intersections;
            Span<float> keys1 = MemoryMarshal.Cast<int, float>(this.activeEdges.Buffer);

            for (int i = 0; i < this.edges.Length; i++)
            {
                ref ScanEdge edge = ref this.edges[i];
                keys0[i] = edge.Y0;
                keys1[i] = edge.Y1;
                this.sorted0[i] = i;
                this.sorted1[i] = i;
            }

            SortUtility<int>.Sort(keys0, this.sorted0);
            SortUtility<int>.Sort(keys1, this.sorted1);
        }

        public bool MoveToNextScanline() => ++this.counter < this.counterMax;

        public ReadOnlySpan<float> ScanCurrentLine()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            this.edgeCollection.Dispose();
            this.dataBuffer.Dispose();
        }
    }
}