// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.ObjectModel;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents an integer rectangle region.
/// </summary>
public sealed class Region
{
    // Store the area as horizontal bands with sorted, merged X intervals. That gives the same
    // non-overlapping rectangle model as Skia regions without invoking polygon clipping to union rectangles.
    private readonly List<RegionBand> bands = [];
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
    /// Rectangles with non-positive width or height do not change the region.
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
    /// Creates a path representing the region.
    /// </summary>
    /// <returns>The path representing the region.</returns>
    /// <remarks>
    /// Returns <see cref="EmptyPath.ClosedPath"/> when the region is empty.
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

    private void SplitAt(int y)
    {
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

            RegionBand lower = band.Clone(y, band.Bottom);
            band.Bottom = y;
            this.bands.Insert(i + 1, lower);
            return;
        }
    }

    private void MergeAdjacentBands()
    {
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

    private static void AddInterval(List<Interval> intervals, int left, int right)
    {
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

    private static RectanglePolygon ToPath(Rectangle rectangle)
        => new(rectangle);

    private readonly struct Interval
    {
        public Interval(int left, int right)
        {
            this.Left = left;
            this.Right = right;
        }

        public int Left { get; }

        public int Right { get; }
    }

    private sealed class RegionBand
    {
        public RegionBand(int top, int bottom, int left, int right)
        {
            this.Top = top;
            this.Bottom = bottom;
            this.Intervals.Add(new Interval(left, right));
        }

        private RegionBand(int top, int bottom, List<Interval> intervals)
        {
            this.Top = top;
            this.Bottom = bottom;
            this.Intervals.AddRange(intervals);
        }

        public int Top { get; set; }

        public int Bottom { get; set; }

        public List<Interval> Intervals { get; } = [];

        public RegionBand Clone(int top, int bottom) => new(top, bottom, this.Intervals);
    }

    private sealed class BoundaryEdge
    {
        public const byte Y0Linked = 0x01;
        public const byte Y1Linked = 0x02;
        public const byte Complete = Y0Linked | Y1Linked;

        public BoundaryEdge(int x, int y0, int y1)
        {
            this.X = x;
            this.Y0 = y0;
            this.Y1 = y1;
            this.Top = Math.Min(y0, y1);
        }

        public int X { get; }

        public int Y0 { get; }

        public int Y1 { get; }

        public int Top { get; }

        public byte Flags { get; set; }

        public BoundaryEdge? Next { get; set; }
    }

    private sealed class RegionPath : IRegionPath
    {
        private readonly IPath path;

        public RegionPath(Rectangle[] rectangles, IPath path)
        {
            this.Rectangles = rectangles;
            this.path = path;
        }

        public IReadOnlyList<Rectangle> Rectangles { get; }

        public PathTypes PathType => this.path.PathType;

        public RectangleF Bounds => this.path.Bounds;

        public IPath AsClosedPath() => this;

        public IEnumerable<ISimplePath> Flatten() => this.path.Flatten();

        public PathPoint GetPathPointAtDistance(float distance) => this.path.GetPathPointAtDistance(distance);

        public LinearGeometry ToLinearGeometry(Vector2 scale) => this.path.ToLinearGeometry(scale);

        public IPath Transform(Matrix4x4 matrix)
            => matrix.IsIdentity ? this : this.path.Transform(matrix);
    }
}
