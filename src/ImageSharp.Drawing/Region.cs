// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.ObjectModel;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents an integer region composed from axis-aligned rectangles.
/// </summary>
/// <remarks>
/// The region stores exact integer coverage using a normalized rect-set model: horizontal
/// Y bands contain sorted X intervals. Region operations preserve the covered area as a
/// union of rectangles; <see cref="ToPath()"/> exports that area as boundary geometry.
/// </remarks>
public sealed class Region
{
    // The canonical model is the same shape used by SkRegion: a sorted set of horizontal
    // Y bands, where each band owns sorted, non-overlapping X intervals. This preserves
    // disjoint islands, L shapes, holes, and stair-step edges without collapsing anything
    // to the bounding rectangle.
    private readonly List<RegionBand> bands = [];

    // Rectangles and the boundary path are exported views over the band model. They are
    // cached because callers often ask for bounds/rectangles/path after building a region,
    // but the bands remain the source of truth for region operations.
    private readonly List<Rectangle> rectangles = [];
    private readonly ReadOnlyCollection<Rectangle> rectanglesView;
    private bool rectanglesValid = true;
    private Rectangle bounds;
    private IPath? path;

    /// <summary>
    /// Initializes a new instance of the <see cref="Region"/> class.
    /// </summary>
    public Region()
        => this.rectanglesView = this.rectangles.AsReadOnly();

    /// <summary>
    /// Initializes a new instance of the <see cref="Region"/> class containing the specified rectangle.
    /// </summary>
    /// <param name="rectangle">The rectangle to add to the region.</param>
    public Region(Rectangle rectangle)
        : this() => this.Add(rectangle);

    /// <summary>
    /// Initializes a new instance of the <see cref="Region"/> class containing the same area as the specified region.
    /// </summary>
    /// <param name="region">The region to copy.</param>
    public Region(Region region)
        : this() => this.CopyFrom(region);

    /// <summary>
    /// Initializes a new instance of the <see cref="Region"/> class from normalized or unnormalized rectangles.
    /// </summary>
    /// <param name="rectangles">The rectangles to union into the region.</param>
    internal Region(IReadOnlyList<Rectangle> rectangles)
        : this()
    {
        for (int i = 0; i < rectangles.Count; i++)
        {
            this.Add(rectangles[i]);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the region contains no area.
    /// </summary>
    public bool IsEmpty => this.bands.Count == 0;

    /// <summary>
    /// Gets the bounding rectangle of the region.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="Rectangle.Empty"/> when the region is empty.
    /// </remarks>
    public Rectangle Bounds => this.IsEmpty ? Rectangle.Empty : this.bounds;

    /// <summary>
    /// Gets the non-overlapping rectangles that describe the region.
    /// </summary>
    /// <remarks>
    /// The rectangles are an exported view of the region area. They preserve the region shape
    /// as a rect-set and are not collapsed to <see cref="Bounds"/>.
    /// </remarks>
    public IReadOnlyList<Rectangle> Rectangles
    {
        get
        {
            this.EnsureRectangles();
            return this.rectanglesView;
        }
    }

    /// <summary>
    /// Adds a rectangle to the region.
    /// </summary>
    /// <param name="rectangle">The rectangle to add.</param>
    /// <remarks>
    /// Rectangles with non-positive width or height do not change the region. The rectangle is
    /// unioned with the existing rect-set.
    /// </remarks>
    public void Add(Rectangle rectangle)
    {
        int left = rectangle.Left;
        int top = rectangle.Top;
        int right = rectangle.Right;
        int bottom = rectangle.Bottom;

        if (left >= right || top >= bottom)
        {
            return;
        }

        bool wasEmpty = this.IsEmpty;

        // Split existing bands at the incoming rectangle's top/bottom so the union can
        // operate by merging only X intervals inside bands that exactly match the new
        // rectangle's vertical span.
        this.SplitAt(top);
        this.SplitAt(bottom);

        int y = top;
        int index = 0;
        while (index < this.bands.Count && this.bands[index].Bottom <= y)
        {
            index++;
        }

        while (y < bottom)
        {
            if (index < this.bands.Count && this.bands[index].Top < bottom)
            {
                RegionBand band = this.bands[index];
                if (y < band.Top)
                {
                    int gapBottom = Math.Min(bottom, band.Top);
                    this.bands.Insert(index, new RegionBand(y, gapBottom, left, right));
                    y = gapBottom;
                    index++;
                    continue;
                }

                AddInterval(band.Intervals, left, right);
                y = band.Bottom;
                index++;
                continue;
            }

            this.bands.Insert(index, new RegionBand(y, bottom, left, right));
            y = bottom;
            index++;
        }

        this.MergeAdjacentBands();
        this.rectanglesValid = false;
        this.path = null;

        this.bounds = wasEmpty
            ? Rectangle.FromLTRB(left, top, right, bottom)
            : Rectangle.FromLTRB(
                Math.Min(this.bounds.Left, left),
                Math.Min(this.bounds.Top, top),
                Math.Max(this.bounds.Right, right),
                Math.Max(this.bounds.Bottom, bottom));
    }

    /// <summary>
    /// Removes all rectangles from the region.
    /// </summary>
    public void Clear()
    {
        this.bands.Clear();
        this.rectangles.Clear();
        this.rectanglesValid = true;
        this.bounds = Rectangle.Empty;
        this.path = null;
    }

    /// <summary>
    /// Returns a value indicating whether the region contains the specified point.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> if the region contains the point; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Point point) => this.Contains(point.X, point.Y);

    /// <summary>
    /// Returns a value indicating whether the region contains the specified point.
    /// </summary>
    /// <param name="x">The x-coordinate to test.</param>
    /// <param name="y">The y-coordinate to test.</param>
    /// <returns><see langword="true"/> if the region contains the point; otherwise, <see langword="false"/>.</returns>
    public bool Contains(int x, int y)
    {
        for (int i = 0; i < this.bands.Count; i++)
        {
            RegionBand band = this.bands[i];
            if (y < band.Top)
            {
                return false;
            }

            if (y >= band.Bottom)
            {
                continue;
            }

            List<Interval> intervals = band.Intervals;
            for (int j = 0; j < intervals.Count; j++)
            {
                Interval interval = intervals[j];
                if (x < interval.Left)
                {
                    return false;
                }

                if (x < interval.Right)
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Returns a value indicating whether the region intersects the specified rectangle.
    /// </summary>
    /// <param name="rectangle">The rectangle to test.</param>
    /// <returns><see langword="true"/> if the region intersects the rectangle; otherwise, <see langword="false"/>.</returns>
    public bool Intersects(Rectangle rectangle)
    {
        int left = rectangle.Left;
        int top = rectangle.Top;
        int right = rectangle.Right;
        int bottom = rectangle.Bottom;

        if (left >= right || top >= bottom)
        {
            return false;
        }

        for (int i = 0; i < this.bands.Count; i++)
        {
            RegionBand band = this.bands[i];
            if (band.Bottom <= top)
            {
                continue;
            }

            if (band.Top >= bottom)
            {
                return false;
            }

            List<Interval> intervals = band.Intervals;
            for (int j = 0; j < intervals.Count; j++)
            {
                Interval interval = intervals[j];
                if (interval.Right <= left)
                {
                    continue;
                }

                if (interval.Left >= right)
                {
                    break;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Intersects this region with the specified rectangle.
    /// </summary>
    /// <param name="rectangle">The rectangle to intersect with this region.</param>
    /// <returns><see langword="true"/> when the resulting region is not empty; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// This operation clips the existing rect-set to the rectangle. It does not replace the
    /// result with one bounding rectangle.
    /// </remarks>
    public bool Intersect(Rectangle rectangle)
    {
        int left = rectangle.Left;
        int top = rectangle.Top;
        int right = rectangle.Right;
        int bottom = rectangle.Bottom;
        if (left >= right || top >= bottom || this.IsEmpty)
        {
            this.Clear();
            return false;
        }

        for (int i = 0; i < this.bands.Count;)
        {
            RegionBand band = this.bands[i];
            if (band.Bottom <= top || band.Top >= bottom)
            {
                this.bands.RemoveAt(i);
                continue;
            }

            band.Top = Math.Max(band.Top, top);
            band.Bottom = Math.Min(band.Bottom, bottom);

            for (int j = 0; j < band.Intervals.Count;)
            {
                Interval interval = band.Intervals[j];
                int intervalLeft = Math.Max(interval.Left, left);
                int intervalRight = Math.Min(interval.Right, right);
                if (intervalLeft >= intervalRight)
                {
                    band.Intervals.RemoveAt(j);
                    continue;
                }

                band.Intervals[j] = new Interval(intervalLeft, intervalRight);
                j++;
            }

            if (band.Intervals.Count == 0)
            {
                this.bands.RemoveAt(i);
                continue;
            }

            i++;
        }

        this.MergeAdjacentBands();
        this.UpdateBoundsFromBands();
        return !this.IsEmpty;
    }

    /// <summary>
    /// Intersects this region with the specified region.
    /// </summary>
    /// <param name="region">The region to intersect with this region.</param>
    /// <returns><see langword="true"/> when the resulting region is not empty; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// The operation intersects every overlapping X interval pair in every overlapping Y band
    /// and unions the results. This is the rect-set form of region intersection.
    /// </remarks>
    public bool Intersect(Region region)
    {
        if (this.IsEmpty || region.IsEmpty)
        {
            this.Clear();
            return false;
        }

        Region result = new();
        int firstIndex = 0;
        int secondIndex = 0;
        while (firstIndex < this.bands.Count && secondIndex < region.bands.Count)
        {
            RegionBand first = this.bands[firstIndex];
            RegionBand second = region.bands[secondIndex];
            if (first.Bottom <= second.Top)
            {
                firstIndex++;
                continue;
            }

            if (second.Bottom <= first.Top)
            {
                secondIndex++;
                continue;
            }

            int top = Math.Max(first.Top, second.Top);
            int bottom = Math.Min(first.Bottom, second.Bottom);
            AddBandIntersection(result.bands, top, bottom, first.Intervals, second.Intervals);

            if (first.Bottom == bottom)
            {
                firstIndex++;
            }

            if (second.Bottom == bottom)
            {
                secondIndex++;
            }
        }

        result.MergeAdjacentBands();
        result.UpdateBoundsFromBands();
        this.CopyFrom(result);
        return !this.IsEmpty;
    }

    /// <summary>
    /// Creates a path representing the region.
    /// </summary>
    /// <returns>The path representing the region.</returns>
    /// <remarks>
    /// The returned path describes the exact boundary of the region. Complex regions may
    /// produce multiple closed figures. Returns <see cref="EmptyPath.ClosedPath"/> when the region is empty.
    /// </remarks>
    public IPath ToPath()
    {
        if (this.path is not null)
        {
            return this.path;
        }

        this.EnsureRectangles();
        if (this.rectangles.Count == 0)
        {
            this.path = EmptyPath.ClosedPath;
            return this.path;
        }

        if (this.rectangles.Count == 1)
        {
            Rectangle rectangle = this.rectangles[0];
            this.path = new RegionPath([rectangle], ToPath(rectangle));
            return this.path;
        }

        this.path = new RegionPath([.. this.rectangles], this.BuildBoundaryPath());
        return this.path;
    }

    /// <summary>
    /// Creates a closed path around the outside boundary of the region.
    /// </summary>
    /// <returns>The path describing the region boundary.</returns>
    private IPath BuildBoundaryPath()
    {
        // Match SkRegion's boundary export shape: rectangles are first represented as
        // opposing vertical edges, then linked into closed contours around the region
        // boundary. Shared internal edges cancel because the rectangle list is already
        // normalized into non-overlapping bands/intervals.
        List<BoundaryEdge> edges = new(this.rectangles.Count * 2);
        for (int i = 0; i < this.rectangles.Count; i++)
        {
            Rectangle rectangle = this.rectangles[i];
            edges.Add(new BoundaryEdge(rectangle.Left, rectangle.Bottom, rectangle.Top));
            edges.Add(new BoundaryEdge(rectangle.Right, rectangle.Top, rectangle.Bottom));
        }

        edges.Sort(static (a, b) =>
        {
            int x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Top.CompareTo(b.Top);
        });

        for (int i = 0; i < edges.Count; i++)
        {
            LinkBoundaryEdge(edges, i);
        }

        PathBuilder builder = new();
        int remaining = edges.Count;
        while (remaining > 0)
        {
            remaining -= ExtractBoundaryFigure(edges, builder);
        }

        return builder.Build();
    }

    /// <summary>
    /// Links one vertical edge to the two neighbouring vertical edges that share its end points.
    /// </summary>
    /// <param name="edges">The sorted boundary edges.</param>
    /// <param name="index">The edge index to link.</param>
    private static void LinkBoundaryEdge(List<BoundaryEdge> edges, int index)
    {
        BoundaryEdge edge = edges[index];
        if (edge.Flags == BoundaryEdge.Complete)
        {
            return;
        }

        if ((edge.Flags & BoundaryEdge.Y0Linked) == 0)
        {
            int i = index + 1;
            while ((edges[i].Flags & BoundaryEdge.Y1Linked) != 0 || edge.Y0 != edges[i].Y1)
            {
                i++;
            }

            BoundaryEdge linked = edges[i];
            linked.Next = edge;
            linked.Flags |= BoundaryEdge.Y1Linked;
        }

        if ((edge.Flags & BoundaryEdge.Y1Linked) == 0)
        {
            int i = index + 1;
            while ((edges[i].Flags & BoundaryEdge.Y0Linked) != 0 || edge.Y1 != edges[i].Y0)
            {
                i++;
            }

            BoundaryEdge linked = edges[i];
            edge.Next = linked;
            linked.Flags |= BoundaryEdge.Y0Linked;
        }

        edge.Flags = BoundaryEdge.Complete;
    }

    /// <summary>
    /// Extracts one closed boundary figure from the linked edge graph.
    /// </summary>
    /// <param name="edges">The boundary edges.</param>
    /// <param name="builder">The path builder to append to.</param>
    /// <returns>The number of consumed edges.</returns>
    private static int ExtractBoundaryFigure(List<BoundaryEdge> edges, PathBuilder builder)
    {
        int index = 0;
        while (edges[index].Flags == 0)
        {
            index++;
        }

        BoundaryEdge first = edges[index];
        BoundaryEdge previous = first;
        BoundaryEdge edge = first.Next!;

        _ = builder.MoveTo(previous.X, previous.Y0);

        previous.Flags = 0;
        int count = 1;
        while (!ReferenceEquals(edge, first))
        {
            // Emit the vertical remainder of the previous edge and the horizontal
            // connector to the next edge. Collinear continuations (same X, contiguous Y)
            // need no intermediate points.
            if (previous.X != edge.X || previous.Y1 != edge.Y0)
            {
                _ = builder.LineTo(previous.X, previous.Y1);
                _ = builder.LineTo(edge.X, edge.Y0);
            }

            previous = edge;
            edge = edge.Next!;
            previous.Flags = 0;
            count++;
        }

        _ = builder.LineTo(previous.X, previous.Y1);
        _ = builder.CloseFigure();

        return count;
    }

    /// <summary>
    /// Splits the band containing the specified Y coordinate.
    /// </summary>
    /// <param name="y">The Y coordinate where a band boundary is required.</param>
    private void SplitAt(int y)
    {
        // Region operations work on whole bands. Splitting at a Y boundary creates the
        // exact band ranges needed for the next union/intersection step without changing
        // the represented area.
        for (int i = 0; i < this.bands.Count; i++)
        {
            RegionBand band = this.bands[i];
            if (y <= band.Top)
            {
                return;
            }

            if (y >= band.Bottom)
            {
                continue;
            }

            RegionBand lower = band.DeepClone(y, band.Bottom);
            band.Bottom = y;
            this.bands.Insert(i + 1, lower);
            return;
        }
    }

    /// <summary>
    /// Merges neighbouring bands that have identical X coverage.
    /// </summary>
    private void MergeAdjacentBands()
    {
        // Adjacent bands with identical X coverage represent one taller rectangle strip.
        // Merging keeps the canonical representation compact and keeps exported rectangles
        // stable without changing the region area.
        for (int i = 1; i < this.bands.Count;)
        {
            RegionBand previous = this.bands[i - 1];
            RegionBand current = this.bands[i];

            if (previous.Bottom == current.Top && IntervalsEqual(previous.Intervals, current.Intervals))
            {
                previous.Bottom = current.Bottom;
                this.bands.RemoveAt(i);
                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// Replaces this region with a copy of another region's canonical band data.
    /// </summary>
    /// <param name="region">The region to copy from.</param>
    private void CopyFrom(Region region)
    {
        this.bands.Clear();
        for (int i = 0; i < region.bands.Count; i++)
        {
            this.bands.Add(region.bands[i].DeepClone());
        }

        this.bounds = region.bounds;
        this.rectangles.Clear();
        this.rectanglesValid = false;
        this.path = null;

        if (region.IsEmpty)
        {
            this.rectanglesValid = true;
        }
    }

    /// <summary>
    /// Recomputes exported state after a destructive band operation.
    /// </summary>
    private void UpdateBoundsFromBands()
    {
        // Bounds are a view over the represented area. Recompute from intervals after
        // destructive operations so complex shapes keep their actual extents instead of
        // inheriting stale operand bounds.
        this.bounds = Rectangle.Empty;
        this.rectanglesValid = false;
        this.path = null;

        if (this.bands.Count == 0)
        {
            this.rectangles.Clear();
            this.rectanglesValid = true;
            return;
        }

        int left = int.MaxValue;
        int top = this.bands[0].Top;
        int right = int.MinValue;
        int bottom = this.bands[^1].Bottom;
        for (int i = 0; i < this.bands.Count; i++)
        {
            List<Interval> intervals = this.bands[i].Intervals;
            for (int j = 0; j < intervals.Count; j++)
            {
                Interval interval = intervals[j];
                left = Math.Min(left, interval.Left);
                right = Math.Max(right, interval.Right);
            }
        }

        this.bounds = Rectangle.FromLTRB(left, top, right, bottom);
    }

    /// <summary>
    /// Materializes the exported rectangle view from the canonical band data.
    /// </summary>
    private void EnsureRectangles()
    {
        if (this.rectanglesValid)
        {
            return;
        }

        this.rectangles.Clear();
        for (int i = 0; i < this.bands.Count; i++)
        {
            RegionBand band = this.bands[i];
            List<Interval> intervals = band.Intervals;

            for (int j = 0; j < intervals.Count; j++)
            {
                Interval interval = intervals[j];
                this.rectangles.Add(Rectangle.FromLTRB(interval.Left, band.Top, interval.Right, band.Bottom));
            }
        }

        this.rectanglesValid = true;
    }

    /// <summary>
    /// Unions one X interval into a sorted interval list.
    /// </summary>
    /// <param name="intervals">The interval list to update.</param>
    /// <param name="left">The left edge of the interval.</param>
    /// <param name="right">The right edge of the interval.</param>
    private static void AddInterval(List<Interval> intervals, int left, int right)
    {
        // Intervals inside one band are sorted and non-overlapping. Adding one interval
        // is therefore a local union operation over X coverage for that Y span.
        int index = 0;
        while (index < intervals.Count && intervals[index].Right < left)
        {
            index++;
        }

        int mergedLeft = left;
        int mergedRight = right;

        // Touching intervals are merged because integer regions have no gap between [a,b) and [b,c).
        while (index < intervals.Count && intervals[index].Left <= mergedRight)
        {
            Interval interval = intervals[index];
            mergedLeft = Math.Min(mergedLeft, interval.Left);
            mergedRight = Math.Max(mergedRight, interval.Right);
            intervals.RemoveAt(index);
        }

        intervals.Insert(index, new Interval(mergedLeft, mergedRight));
    }

    /// <summary>
    /// Adds the X interval intersections for one overlapping Y band.
    /// </summary>
    /// <param name="bands">The result bands receiving the intersection band.</param>
    /// <param name="top">The top of the overlapping Y band.</param>
    /// <param name="bottom">The bottom of the overlapping Y band.</param>
    /// <param name="first">The first sorted X interval list.</param>
    /// <param name="second">The second sorted X interval list.</param>
    private static void AddBandIntersection(
        List<RegionBand> bands,
        int top,
        int bottom,
        List<Interval> first,
        List<Interval> second)
    {
        // Both interval lists are sorted. Sweep them to produce the X-overlap intervals
        // for the already-overlapped Y band. The caller unions every produced band into
        // the result region, preserving L shapes and disjoint islands as a rect-set.
        RegionBand? band = null;
        int firstIndex = 0;
        int secondIndex = 0;
        while (firstIndex < first.Count && secondIndex < second.Count)
        {
            Interval a = first[firstIndex];
            Interval b = second[secondIndex];
            if (a.Right <= b.Left)
            {
                firstIndex++;
                continue;
            }

            if (b.Right <= a.Left)
            {
                secondIndex++;
                continue;
            }

            int left = Math.Max(a.Left, b.Left);
            int right = Math.Min(a.Right, b.Right);
            if (left < right)
            {
                band ??= new RegionBand(top, bottom);
                band.Intervals.Add(new Interval(left, right));
            }

            if (a.Right == right)
            {
                firstIndex++;
            }

            if (b.Right == right)
            {
                secondIndex++;
            }
        }

        if (band is not null)
        {
            bands.Add(band);
        }
    }

    /// <summary>
    /// Compares two X interval lists for identical coverage.
    /// </summary>
    /// <param name="first">The first interval list.</param>
    /// <param name="second">The second interval list.</param>
    /// <returns><see langword="true"/> when both interval lists describe the same X coverage.</returns>
    private static bool IntervalsEqual(List<Interval> first, List<Interval> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (int i = 0; i < first.Count; i++)
        {
            if (first[i].Left != second[i].Left || first[i].Right != second[i].Right)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts one rectangle to its boundary path.
    /// </summary>
    /// <param name="rectangle">The rectangle to convert.</param>
    /// <returns>The rectangle path.</returns>
    private static RectanglePolygon ToPath(Rectangle rectangle)
        => new(rectangle);

    /// <summary>
    /// Represents one filled X interval inside a region band.
    /// </summary>
    private readonly struct Interval
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Interval"/> struct.
        /// </summary>
        /// <param name="left">The inclusive left edge.</param>
        /// <param name="right">The exclusive right edge.</param>
        public Interval(int left, int right)
        {
            this.Left = left;
            this.Right = right;
        }

        /// <summary>
        /// Gets the inclusive left edge.
        /// </summary>
        public int Left { get; }

        /// <summary>
        /// Gets the exclusive right edge.
        /// </summary>
        public int Right { get; }
    }

    /// <summary>
    /// Represents one Y band with common X interval coverage.
    /// </summary>
    private sealed class RegionBand
    {
        // A band covers [Top, Bottom) and owns all X intervals that are filled for every
        // scanline in that vertical span.

        /// <summary>
        /// Initializes a new instance of the <see cref="RegionBand"/> class.
        /// </summary>
        /// <param name="top">The inclusive top edge.</param>
        /// <param name="bottom">The exclusive bottom edge.</param>
        public RegionBand(int top, int bottom)
        {
            this.Top = top;
            this.Bottom = bottom;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RegionBand"/> class.
        /// </summary>
        /// <param name="top">The inclusive top edge.</param>
        /// <param name="bottom">The exclusive bottom edge.</param>
        /// <param name="left">The inclusive left edge of the initial interval.</param>
        /// <param name="right">The exclusive right edge of the initial interval.</param>
        public RegionBand(int top, int bottom, int left, int right)
        {
            this.Top = top;
            this.Bottom = bottom;
            this.Intervals.Add(new Interval(left, right));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RegionBand"/> class.
        /// </summary>
        /// <param name="top">The inclusive top edge.</param>
        /// <param name="bottom">The exclusive bottom edge.</param>
        /// <param name="intervals">The intervals to copy.</param>
        private RegionBand(int top, int bottom, List<Interval> intervals)
        {
            this.Top = top;
            this.Bottom = bottom;
            this.Intervals.AddRange(intervals);
        }

        /// <summary>
        /// Gets or sets the inclusive top edge.
        /// </summary>
        public int Top { get; set; }

        /// <summary>
        /// Gets or sets the exclusive bottom edge.
        /// </summary>
        public int Bottom { get; set; }

        /// <summary>
        /// Gets the sorted, non-overlapping X intervals for this band.
        /// </summary>
        public List<Interval> Intervals { get; } = [];

        /// <summary>
        /// Creates a copy of this band.
        /// </summary>
        /// <returns>The copied band.</returns>
        public RegionBand DeepClone() => new(this.Top, this.Bottom, this.Intervals);

        /// <summary>
        /// Creates a copy of this band's X coverage over a different Y range.
        /// </summary>
        /// <param name="top">The inclusive top edge.</param>
        /// <param name="bottom">The exclusive bottom edge.</param>
        /// <returns>The copied band.</returns>
        public RegionBand DeepClone(int top, int bottom) => new(top, bottom, this.Intervals);
    }

    /// <summary>
    /// Represents one vertical edge used when exporting the region boundary path.
    /// </summary>
    private sealed class BoundaryEdge
    {
        // Boundary export works by linking vertical edges into closed contours. Y0/Y1
        // preserve edge direction so the resulting path follows the outside boundary
        // rather than emitting independent rectangle outlines.

        /// <summary>
        /// Flag set when another edge has been linked into this edge's <see cref="Y0"/> endpoint.
        /// </summary>
        public const byte Y0Linked = 0x01;

        /// <summary>
        /// Flag set when this edge's <see cref="Y1"/> endpoint has been linked to another edge.
        /// </summary>
        public const byte Y1Linked = 0x02;

        /// <summary>
        /// Flag value indicating both endpoints are linked and the edge needs no further processing.
        /// </summary>
        public const byte Complete = Y0Linked | Y1Linked;

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundaryEdge"/> class.
        /// </summary>
        /// <param name="x">The X coordinate of the vertical edge.</param>
        /// <param name="y0">The first Y endpoint.</param>
        /// <param name="y1">The second Y endpoint.</param>
        public BoundaryEdge(int x, int y0, int y1)
        {
            this.X = x;
            this.Y0 = y0;
            this.Y1 = y1;
            this.Top = Math.Min(y0, y1);
        }

        /// <summary>
        /// Gets the X coordinate of the vertical edge.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// Gets the first Y endpoint.
        /// </summary>
        public int Y0 { get; }

        /// <summary>
        /// Gets the second Y endpoint.
        /// </summary>
        public int Y1 { get; }

        /// <summary>
        /// Gets the topmost Y endpoint.
        /// </summary>
        public int Top { get; }

        /// <summary>
        /// Gets or sets the edge linkage flags.
        /// </summary>
        public byte Flags { get; set; }

        /// <summary>
        /// Gets or sets the next edge in the exported boundary contour.
        /// </summary>
        public BoundaryEdge? Next { get; set; }
    }

    /// <summary>
    /// Wraps a region boundary path with the rect-set metadata that produced it.
    /// </summary>
    private sealed class RegionPath : IRegionPath
    {
        private readonly IPath path;

        /// <summary>
        /// Initializes a new instance of the <see cref="RegionPath"/> class.
        /// </summary>
        /// <param name="rectangles">The normalized rectangles that describe the same region as the boundary path.</param>
        /// <param name="path">The exported boundary path.</param>
        public RegionPath(Rectangle[] rectangles, IPath path)
        {
            this.Rectangles = rectangles;
            this.path = path;
        }

        /// <inheritdoc />
        public IReadOnlyList<Rectangle> Rectangles { get; }

        /// <inheritdoc />
        public PathTypes PathType => this.path.PathType;

        /// <inheritdoc />
        public RectangleF Bounds => this.path.Bounds;

        /// <inheritdoc />
        public IPath AsClosedPath() => this;

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten() => this.path.Flatten();

        /// <inheritdoc />
        public bool Contains(PointF point, IntersectionRule intersectionRule, Vector2 scale)
            => this.path.Contains(point, intersectionRule, scale);

        /// <inheritdoc />
        public bool TryGetPathPointAtDistance(float distance, Vector2 scale, out PathPoint pathPoint)
            => this.path.TryGetPathPointAtDistance(distance, scale, out pathPoint);

        /// <inheritdoc />
        public bool TryGetPathPointAtDistanceUnbounded(float distance, Vector2 scale, out PathPoint pathPoint)
            => this.path.TryGetPathPointAtDistanceUnbounded(distance, scale, out pathPoint);

        /// <inheritdoc />
        public bool TryGetSegment(float startDistance, float stopDistance, bool startOnBeginFigure, Vector2 scale, out IPath segment)
            => this.path.TryGetSegment(startDistance, stopDistance, startOnBeginFigure, scale, out segment);

        /// <inheritdoc />
        public LinearGeometry ToLinearGeometry(Vector2 scale) => this.path.ToLinearGeometry(scale);

        /// <inheritdoc />
        public float ComputeLength(Vector2 scale) => this.path.ComputeLength(scale);

        /// <inheritdoc />
        public float ComputeArea(Vector2 scale) => this.path.ComputeArea(scale);

        /// <inheritdoc />
        public IPath Transform(Matrix4x4 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            if (MatrixUtilities.PreservesAxisAlignedRectangles(matrix))
            {
                // RegionPath metadata is an integer rect-set. It can survive translation,
                // scale, reflection, and axis swaps only while every transformed edge is
                // still integer; fractional edges need the exact path geometry fallback.
                Rectangle[] transformedRectangles = new Rectangle[this.Rectangles.Count];
                for (int i = 0; i < transformedRectangles.Length; i++)
                {
                    Rectangle rectangle = this.Rectangles[i];

                    // Transform every corner so negative scales and axis swaps cannot leave
                    // left/right or top/bottom inverted.
                    Vector2 p0 = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Top), matrix);
                    Vector2 p1 = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Top), matrix);
                    Vector2 p2 = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Bottom), matrix);
                    Vector2 p3 = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Bottom), matrix);

                    float left = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
                    float top = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
                    float right = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
                    float bottom = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

                    int integerLeft = (int)left;
                    int integerTop = (int)top;
                    int integerRight = (int)right;
                    int integerBottom = (int)bottom;
                    if (left != integerLeft || top != integerTop || right != integerRight || bottom != integerBottom)
                    {
                        // Keep exact clipping semantics rather than widening a fractional
                        // transformed region into conservative integer rectangles.
                        return this.path.Transform(matrix);
                    }

                    transformedRectangles[i] = Rectangle.FromLTRB(integerLeft, integerTop, integerRight, integerBottom);
                }

                // Rebuild through Region so scaled/reflected rectangles are normalized before
                // the metadata is exposed again.
                return new Region(transformedRectangles).ToPath();
            }

            // Skew and free rotation turn the rect-set into ordinary path geometry.
            return this.path.Transform(matrix);
        }
    }
}
