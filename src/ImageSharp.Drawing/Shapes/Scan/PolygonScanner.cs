// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
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
        private IMemoryOwner<float> intersectionsBuffer;
        private Span<float> intersections;
        private IMemoryOwner<int> sorted0Buffer;
        private Span<int> sorted0;
        private IMemoryOwner<int> sorted1Buffer;
        private Span<int> sorted1;

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
            this.intersectionsBuffer = allocator.Allocate<float>(edges.Length);

            this.intersections = default;
            this.sorted0Buffer = default;
            this.sorted0 = default;
            this.sorted1Buffer = default;
            this.sorted1 = default;
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
            int edgeCount = this.edges.Length;
            using IMemoryOwner<float> keys0Buffer = this.allocator.Allocate<float>(edgeCount);
            Span<float> keys0 = keys0Buffer.Memory.Span;
            using IMemoryOwner<float> keys1Buffer = this.allocator.Allocate<float>(edgeCount);
            Span<float> keys1 = keys1Buffer.Memory.Span;

            this.sorted0Buffer = this.allocator.Allocate<int>(edgeCount);
            this.sorted0 = this.sorted0Buffer.Memory.Span;
            this.sorted1Buffer = this.allocator.Allocate<int>(edgeCount);
            this.sorted1 = this.sorted1Buffer.Memory.Span;

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
            this.edgeCollection?.Dispose();
            this.edgeCollection = null;
            this.edges = default;

            this.intersectionsBuffer?.Dispose();
            this.intersectionsBuffer = null;
            this.intersections = default;

            this.sorted0Buffer?.Dispose();
            this.sorted0Buffer = null;
            this.sorted0 = default;

            this.sorted1Buffer?.Dispose();
            this.sorted1Buffer = null;
            this.sorted1 = default;
        }
    }
}