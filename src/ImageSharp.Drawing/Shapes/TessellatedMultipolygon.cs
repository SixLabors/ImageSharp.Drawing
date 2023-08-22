// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections;
using SixLabors.ImageSharp.Drawing.Shapes.Helpers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes;

/// <summary>
/// Compact representation of a multipolygon.
/// Applies some rules which are optimal to implement geometric algorithms:
/// - Outer contour is oriented "Positive" (CCW in world coords, CW on screen)
/// - Holes are oriented "Negative" (CW in world, CCW on screen)
/// - First vertex is always repeated at the end of the span in each ring
/// </summary>
internal sealed class TessellatedMultipolygon : IDisposable, IReadOnlyList<TessellatedMultipolygon.Ring>
{
    private readonly Ring[] rings;

    private TessellatedMultipolygon(Ring[] rings)
    {
        this.rings = rings;
        this.TotalVertexCount = rings.Sum(r => r.VertexCount);
    }

    public int TotalVertexCount { get; }

    public int Count => this.rings.Length;

    public Ring this[int index] => this.rings[index];

    public static TessellatedMultipolygon Create(IPath path, MemoryAllocator memoryAllocator)
    {
        if (path is IInternalPathOwner ipo)
        {
            IReadOnlyList<InternalPath> internalPaths = ipo.GetRingsAsInternalPath();

            // If we have only one ring, we can change it's orientation without negative side-effects.
            // Since the algorithm works best with positively-oriented polygons,
            // we enforce the orientation for best output quality.
            bool enforcePositiveOrientationOnFirstRing = internalPaths.Count == 1;

            Ring[] rings = new Ring[internalPaths.Count];
            IMemoryOwner<PointF> pointBuffer = internalPaths[0].ExtractVertices(memoryAllocator);
            RepeatFirstVertexAndEnsureOrientation(pointBuffer.Memory.Span, enforcePositiveOrientationOnFirstRing);
            rings[0] = new Ring(pointBuffer);

            for (int i = 1; i < internalPaths.Count; i++)
            {
                pointBuffer = internalPaths[i].ExtractVertices(memoryAllocator);
                RepeatFirstVertexAndEnsureOrientation(pointBuffer.Memory.Span, false);
                rings[i] = new Ring(pointBuffer);
            }

            return new TessellatedMultipolygon(rings);
        }
        else
        {
            ReadOnlyMemory<PointF>[] points = path.Flatten().Select(sp => sp.Points).ToArray();

            // If we have only one ring, we can change it's orientation without negative side-effects.
            // Since the algorithm works best with positively-oriented polygons,
            // we enforce the orientation for best output quality.
            bool enforcePositiveOrientationOnFirstRing = points.Length == 1;

            Ring[] rings = new Ring[points.Length];
            rings[0] = MakeRing(points[0], enforcePositiveOrientationOnFirstRing, memoryAllocator);
            for (int i = 1; i < points.Length; i++)
            {
                rings[i] = MakeRing(points[i], false, memoryAllocator);
            }

            return new TessellatedMultipolygon(rings);
        }

        static Ring MakeRing(ReadOnlyMemory<PointF> points, bool enforcePositiveOrientation, MemoryAllocator allocator)
        {
            IMemoryOwner<PointF> buffer = allocator.Allocate<PointF>(points.Length + 1);
            Span<PointF> span = buffer.Memory.Span;
            points.Span.CopyTo(span);
            RepeatFirstVertexAndEnsureOrientation(span, enforcePositiveOrientation);
            return new Ring(buffer);
        }

        static void RepeatFirstVertexAndEnsureOrientation(Span<PointF> span, bool enforcePositiveOrientation)
        {
            // Repeat first vertex for perf:
            span[^1] = span[0];

            if (enforcePositiveOrientation)
            {
                TopologyUtilities.EnsureOrientation(span, 1);
            }
        }
    }

    public void Dispose()
    {
        foreach (Ring ring in this.rings)
        {
            ring.Dispose();
        }
    }

    public IEnumerator<Ring> GetEnumerator() => this.rings.AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    internal sealed class Ring : IDisposable
    {
        private readonly IMemoryOwner<PointF> buffer;
        private Memory<PointF> memory;

        internal Ring(IMemoryOwner<PointF> buffer)
        {
            this.buffer = buffer;
            this.memory = buffer.Memory;
        }

        public ReadOnlySpan<PointF> Vertices => this.memory.Span;

        public int VertexCount => this.memory.Length - 1; // Last vertex is repeated

        public void Dispose()
        {
            this.buffer.Dispose();
            this.memory = default;
        }
    }
}
