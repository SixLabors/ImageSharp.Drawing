// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    internal ref struct PolygonScanner
    {
        private readonly float min;
        private readonly float max;
        private readonly float step;
        private ScanEdgeCollection edgeCollection;
        private Span<ScanEdge> edges;

        private PolygonScanner(ScanEdgeCollection edgeCollection, float min, float max, float step, MemoryAllocator allocator)
        {
            this.min = min;
            this.max = max;
            this.step = step;
            this.edgeCollection = edgeCollection;
            this.edges = edgeCollection.Edges;
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
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            this.edgeCollection?.Dispose();
            this.edgeCollection = null;
            this.edges = default;
        }
    }
}