// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Lightweight indexed view over flattened path data. References existing <see cref="ISimplePath"/>
/// point buffers without copying vertex data — only small metadata arrays are allocated.
/// </summary>
internal readonly struct MaterializedPath
{
    private readonly SubPathInfo[] subPaths;

    private MaterializedPath(SubPathInfo[] subPaths, int totalSegmentCount)
    {
        this.subPaths = subPaths;
        this.TotalSegmentCount = totalSegmentCount;
    }

    /// <summary>
    /// Gets the total number of line segments across all subpaths.
    /// </summary>
    public int TotalSegmentCount { get; }

    /// <summary>
    /// Creates a materialized path from a prepared <see cref="IPath"/>.
    /// </summary>
    public static MaterializedPath Create(IPath path)
    {
        List<SubPathInfo>? list = null;
        int totalSegments = 0;

        foreach (ISimplePath sp in path.Flatten())
        {
            ReadOnlyMemory<PointF> points = sp.Points;
            if (points.Length < 2)
            {
                continue;
            }

            int segCount = sp.IsClosed ? points.Length : points.Length - 1;
            list ??= [];
            list.Add(new SubPathInfo(points, sp.IsClosed, totalSegments));
            totalSegments += segCount;
        }

        if (list is null)
        {
            return default;
        }

        return new MaterializedPath([.. list], totalSegments);
    }

    /// <summary>
    /// Gets the segment (P0, P1) at the given flat index.
    /// The <paramref name="subPathHint"/> is used to avoid searching from the start;
    /// pass 0 initially and the method updates it to the current subpath index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetSegment(int flatIndex, out PointF p0, out PointF p1, ref int subPathHint)
    {
        SubPathInfo info = this.FindSubPath(flatIndex, ref subPathHint);
        int localIndex = flatIndex - info.SegmentOffset;
        ReadOnlySpan<PointF> points = info.Points.Span;
        p0 = points[localIndex];
        p1 = points[(localIndex + 1) % points.Length];
    }

    /// <summary>
    /// Returns an enumerator that yields (P0, P1, MinY, MaxY) for every segment.
    /// </summary>
    public SegmentEnumerator GetSegmentEnumerator() => new(this.subPaths);

    private SubPathInfo FindSubPath(int flatIndex, ref int hint)
    {
        SubPathInfo[] sp = this.subPaths;

        // Start from the hint and scan forward/backward. Band segment indices
        // are sorted so the next lookup is almost always at the same or next subpath.
        int i = Math.Min(hint, sp.Length - 1);

        // If we overshot, scan backward.
        while (i > 0 && flatIndex < sp[i].SegmentOffset)
        {
            i--;
        }

        // Scan forward to the right subpath.
        while (i + 1 < sp.Length && flatIndex >= sp[i + 1].SegmentOffset)
        {
            i++;
        }

        hint = i;
        return sp[i];
    }

    internal readonly struct SubPathInfo(ReadOnlyMemory<PointF> points, bool isClosed, int segmentOffset)
    {
        public readonly ReadOnlyMemory<PointF> Points = points;
        public readonly bool IsClosed = isClosed;
        public readonly int SegmentOffset = segmentOffset;
    }

    /// <summary>
    /// Enumerates all segments in a materialized path, yielding endpoints and Y extents.
    /// </summary>
    internal ref struct SegmentEnumerator
    {
        private readonly SubPathInfo[] subPaths;
        private int subPathIndex;
        private int localIndex;
        private int segmentCount;
        private ReadOnlySpan<PointF> currentPoints;

        internal SegmentEnumerator(SubPathInfo[] subPaths)
        {
            this.subPaths = subPaths;
            this.subPathIndex = 0;
            this.localIndex = -1;
            this.segmentCount = 0;
            this.currentPoints = default;
            this.CurrentP0 = default;
            this.CurrentP1 = default;
            this.CurrentMinY = 0;
            this.CurrentMaxY = 0;

            if (subPaths is { Length: > 0 })
            {
                SubPathInfo first = subPaths[0];
                this.currentPoints = first.Points.Span;
                this.segmentCount = first.IsClosed ? first.Points.Length : first.Points.Length - 1;
            }
        }

        public PointF CurrentP0 { get; private set; }

        public PointF CurrentP1 { get; private set; }

        public float CurrentMinY { get; private set; }

        public float CurrentMaxY { get; private set; }

        public bool MoveNext()
        {
            while (true)
            {
                this.localIndex++;
                if (this.localIndex < this.segmentCount)
                {
                    this.CurrentP0 = this.currentPoints[this.localIndex];
                    this.CurrentP1 = this.currentPoints[(this.localIndex + 1) % this.currentPoints.Length];
                    this.CurrentMinY = Math.Min(this.CurrentP0.Y, this.CurrentP1.Y);
                    this.CurrentMaxY = Math.Max(this.CurrentP0.Y, this.CurrentP1.Y);
                    return true;
                }

                this.subPathIndex++;
                if (this.subPathIndex >= this.subPaths.Length)
                {
                    return false;
                }

                SubPathInfo info = this.subPaths[this.subPathIndex];
                this.currentPoints = info.Points.Span;
                this.segmentCount = info.IsClosed ? info.Points.Length : info.Points.Length - 1;
                this.localIndex = -1;
            }
        }
    }
}
