// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Drawing.Shapes.Helpers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes
{
    /// <summary>
    /// Compact representation of a multipolygon.
    /// Applies some rules which are optimal to implement geometric algorithms:
    /// - Outer contour is oriented "Positive" (CCW in world coords, CW on screen)
    /// - Holes are oriented "Negative" (CW in world, CCW on screen)
    /// - First vertex is always repeated at the end of the span in each ring
    /// </summary>
    internal class TessellatedMultipolygon : IDisposable, IReadOnlyList<TessellatedMultipolygon.Ring>
    {
        internal enum RingType
        {
            Contour,
            Hole
        }

        internal class Ring : IDisposable
        {
            private IMemoryOwner<PointF> buffer;
            private Memory<PointF> memory;

            public RingType RingType { get; }

            public ReadOnlySpan<PointF> Vertices => this.memory.Span;

            public int VertexCount => this.memory.Length - 1; // Last vertex is repeated

            internal Ring(IMemoryOwner<PointF> buffer, RingType ringType)
            {
                this.RingType = ringType;
                this.buffer = buffer;
                this.memory = buffer.Memory;
            }

            public void Dispose()
            {
                this.buffer?.Dispose();
                this.buffer = null;
                this.memory = default;
            }
        }

        private Ring[] rings;

        private TessellatedMultipolygon(Ring[] rings)
        {
            this.rings = rings;
            this.TotalVertexCount = rings.Sum(r => r.VertexCount);
        }

        public int TotalVertexCount { get; }

        public static TessellatedMultipolygon Create(IPath path, MemoryAllocator memoryAllocator, in TolerantComparer comparer)
        {
            // For now let's go with the assumption that first loop is always an external contour,
            // and the rests are loops.
            if (path is IInternalPathOwner ipo)
            {
                IReadOnlyList<InternalPath> internalPaths = ipo.GetRingsAsInternalPath();
                Ring[] rings = new Ring[internalPaths.Count];
                IMemoryOwner<PointF> pointBuffer = internalPaths[0].ExtractVertices(memoryAllocator);
                RepeateFirstVertexAndEnsureOrientation(pointBuffer.Memory.Span, RingType.Contour, comparer);
                rings[0] = new Ring(pointBuffer, RingType.Contour);
                for (int i = 1; i < internalPaths.Count; i++)
                {
                    pointBuffer = internalPaths[i].ExtractVertices(memoryAllocator);
                    RepeateFirstVertexAndEnsureOrientation(pointBuffer.Memory.Span, RingType.Hole, comparer);
                    rings[i] = new Ring(pointBuffer, RingType.Hole);
                }

                return new TessellatedMultipolygon(rings);
            }
            else
            {
                ReadOnlyMemory<PointF>[] points = path.Flatten().Select(sp => sp.Points).ToArray();
                Ring[] rings = new Ring[points.Length];
                rings[0] = MakeRing(points[0], RingType.Contour, memoryAllocator, comparer);
                for (int i = 1; i < points.Length; i++)
                {
                    rings[i] = MakeRing(points[i], RingType.Hole, memoryAllocator, comparer);
                }

                return new TessellatedMultipolygon(rings);
            }

            static Ring MakeRing(ReadOnlyMemory<PointF> points, RingType ringType, MemoryAllocator allocator, in TolerantComparer comparer)
            {
                IMemoryOwner<PointF> buffer = allocator.Allocate<PointF>(points.Length + 1);
                Span<PointF> span = buffer.Memory.Span;
                points.Span.CopyTo(span);
                RepeateFirstVertexAndEnsureOrientation(span, ringType, comparer);
                return new Ring(buffer, ringType);
            }

            static void RepeateFirstVertexAndEnsureOrientation(Span<PointF> span, RingType ringType, in TolerantComparer comparer)
            {
                // Repeat first vertex for perf:
                span[span.Length - 1] = span[0];

                int orientation = ringType == RingType.Contour ? 1 : -1;
                TopologyUtilities.EnsureOrientation(span, orientation, comparer);
            }
        }

        public void Dispose()
        {
            if (this.rings == null)
            {
                return;
            }

            foreach (Ring ring in this.rings)
            {
                ring.Dispose();
            }

            this.rings = null;
        }

        public IEnumerator<Ring> GetEnumerator() => this.rings.AsEnumerable().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public int Count => this.rings.Length;

        public Ring this[int index] => this.rings[index];
    }
}