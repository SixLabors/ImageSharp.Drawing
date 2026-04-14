// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a complex polygon made up of one or more shapes overlayed on each other,
/// where overlaps causes holes.
/// </summary>
/// <seealso cref="IPath" />
public sealed class ComplexPolygon : IPath, IPathInternals, IInternalPathOwner
{
    private readonly IPath[] paths;
    private List<InternalPath>? internalPaths;
    private float length;
    private RectangleF? bounds;
    private IPath? closedPath;
    private LinearGeometry? linearGeometry;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplexPolygon"/> class.
    /// </summary>
    /// <param name="contour">The contour path.</param>
    /// <param name="hole">The hole path.</param>
    public ComplexPolygon(PointF[] contour, PointF[] hole)
        : this(new Path(new LinearLineSegment(contour)), new Path(new LinearLineSegment(hole)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
    /// </summary>
    /// <param name="paths">The paths.</param>
    public ComplexPolygon(IEnumerable<IPath> paths)
        : this([.. paths])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
    /// </summary>
    /// <param name="paths">The paths.</param>
    public ComplexPolygon(params IPath[] paths)
    {
        Guard.NotNull(paths, nameof(paths));

        this.paths = paths;

        if (paths.Length == 0)
        {
            this.bounds = RectangleF.Empty;
        }

        this.PathType = PathTypes.Mixed;
    }

    /// <inheritdoc/>
    public PathTypes PathType { get; }

    /// <summary>
    /// Gets the collection of paths that make up this shape.
    /// </summary>
    public IEnumerable<IPath> Paths => this.paths;

    /// <inheritdoc/>
    public RectangleF Bounds => this.bounds ??= this.CalcBounds();

    /// <inheritdoc/>
    public IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            // No transform to apply skip it
            return this;
        }

        IPath[] shapes = new IPath[this.paths.Length];

        for (int i = 0; i < shapes.Length; i++)
        {
            shapes[i] = this.paths[i].Transform(matrix);
        }

        return new ComplexPolygon(shapes);
    }

    /// <inheritdoc />
    public IEnumerable<ISimplePath> Flatten()
    {
        List<ISimplePath> paths = new(this.paths.Length);
        foreach (IPath path in this.Paths)
        {
            paths.AddRange(path.Flatten());
        }

        return paths;
    }

    /// <inheritdoc/>
    public LinearGeometry ToLinearGeometry(Matrix4x4 transform)
        => transform.IsIdentity ? this.GetLinearGeometryCore() : this.CreateTransformedLinearGeometryCore(transform);

    /// <summary>
    /// Returns the retained identity geometry, publishing it once for concurrent readers.
    /// </summary>
    /// <returns>The retained identity geometry.</returns>
    private LinearGeometry GetLinearGeometryCore()
    {
        LinearGeometry? cached = Volatile.Read(ref this.linearGeometry);
        if (cached is not null)
        {
            return cached;
        }

        LinearGeometry geometry = this.CreateLinearGeometryCore();
        LinearGeometry? published = Interlocked.CompareExchange(ref this.linearGeometry, geometry, null);
        return published ?? geometry;
    }

    /// <summary>
    /// Materializes the retained identity geometry for the composed contours.
    /// </summary>
    /// <returns>The retained identity geometry.</returns>
    private LinearGeometry CreateLinearGeometryCore()
    {
        int pointCount = 0;
        int contourCount = 0;
        int segmentCount = 0;
        int nonHorizontalSegmentCountPixelBoundary = 0;
        int nonHorizontalSegmentCountPixelCenter = 0;

        bool hasBounds = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (IPath path in this.paths)
        {
            LinearGeometry geometry = path.ToLinearGeometry(Matrix4x4.Identity);

            if (geometry.Info.PointCount == 0)
            {
                continue;
            }

            RectangleF childBounds = geometry.Info.Bounds;
            minX = MathF.Min(minX, childBounds.Left);
            minY = MathF.Min(minY, childBounds.Top);
            maxX = MathF.Max(maxX, childBounds.Right);
            maxY = MathF.Max(maxY, childBounds.Bottom);
            hasBounds = true;

            pointCount += geometry.Info.PointCount;
            contourCount += geometry.Info.ContourCount;
            segmentCount += geometry.Info.SegmentCount;
            nonHorizontalSegmentCountPixelBoundary += geometry.Info.NonHorizontalSegmentCountPixelBoundary;
            nonHorizontalSegmentCountPixelCenter += geometry.Info.NonHorizontalSegmentCountPixelCenter;
        }

        PointF[] points = new PointF[pointCount];
        LinearContour[] contours = new LinearContour[contourCount];
        int pointStart = 0;
        int contourStart = 0;
        int segmentStart = 0;

        foreach (IPath path in this.paths)
        {
            LinearGeometry geometry = path.ToLinearGeometry(Matrix4x4.Identity);
            if (geometry.Info.PointCount == 0)
            {
                continue;
            }

            for (int i = 0; i < geometry.Points.Count; i++)
            {
                points[pointStart + i] = geometry.Points[i];
            }

            for (int i = 0; i < geometry.Contours.Count; i++)
            {
                LinearContour contour = geometry.Contours[i];
                contours[contourStart + i] = new LinearContour
                {
                    PointStart = pointStart + contour.PointStart,
                    PointCount = contour.PointCount,
                    SegmentStart = segmentStart + contour.SegmentStart,
                    SegmentCount = contour.SegmentCount,
                    IsClosed = contour.IsClosed
                };
            }

            pointStart += geometry.Info.PointCount;
            contourStart += geometry.Info.ContourCount;
            segmentStart += geometry.Info.SegmentCount;
        }

        RectangleF bounds = hasBounds ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : RectangleF.Empty;

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = bounds,
                ContourCount = contours.Length,
                PointCount = points.Length,
                SegmentCount = segmentCount,
                NonHorizontalSegmentCountPixelBoundary = nonHorizontalSegmentCountPixelBoundary,
                NonHorizontalSegmentCountPixelCenter = nonHorizontalSegmentCountPixelCenter
            },
            contours,
            points);
    }

    /// <summary>
    /// Materializes transformed linear geometry without caching the transformed result.
    /// </summary>
    /// <param name="transform">The transform to apply to each emitted point.</param>
    /// <returns>The transformed retained geometry.</returns>
    private LinearGeometry CreateTransformedLinearGeometryCore(Matrix4x4 transform)
    {
        int pointCount = 0;
        int contourCount = 0;
        int segmentCount = 0;
        int nonHorizontalSegmentCountPixelBoundary = 0;
        int nonHorizontalSegmentCountPixelCenter = 0;

        bool hasBounds = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (IPath path in this.paths)
        {
            LinearGeometry geometry = path.ToLinearGeometry(transform);

            if (geometry.Info.PointCount == 0)
            {
                continue;
            }

            RectangleF childBounds = geometry.Info.Bounds;
            minX = MathF.Min(minX, childBounds.Left);
            minY = MathF.Min(minY, childBounds.Top);
            maxX = MathF.Max(maxX, childBounds.Right);
            maxY = MathF.Max(maxY, childBounds.Bottom);
            hasBounds = true;

            pointCount += geometry.Info.PointCount;
            contourCount += geometry.Info.ContourCount;
            segmentCount += geometry.Info.SegmentCount;
            nonHorizontalSegmentCountPixelBoundary += geometry.Info.NonHorizontalSegmentCountPixelBoundary;
            nonHorizontalSegmentCountPixelCenter += geometry.Info.NonHorizontalSegmentCountPixelCenter;
        }

        PointF[] points = new PointF[pointCount];
        LinearContour[] contours = new LinearContour[contourCount];
        int pointStart = 0;
        int contourStart = 0;
        int segmentStart = 0;

        foreach (IPath path in this.paths)
        {
            LinearGeometry geometry = path.ToLinearGeometry(transform);
            if (geometry.Info.PointCount == 0)
            {
                continue;
            }

            for (int i = 0; i < geometry.Points.Count; i++)
            {
                points[pointStart + i] = geometry.Points[i];
            }

            for (int i = 0; i < geometry.Contours.Count; i++)
            {
                LinearContour contour = geometry.Contours[i];
                contours[contourStart + i] = new LinearContour
                {
                    PointStart = pointStart + contour.PointStart,
                    PointCount = contour.PointCount,
                    SegmentStart = segmentStart + contour.SegmentStart,
                    SegmentCount = contour.SegmentCount,
                    IsClosed = contour.IsClosed
                };
            }

            pointStart += geometry.Info.PointCount;
            contourStart += geometry.Info.ContourCount;
            segmentStart += geometry.Info.SegmentCount;
        }

        RectangleF bounds = hasBounds ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : RectangleF.Empty;

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = bounds,
                ContourCount = contours.Length,
                PointCount = points.Length,
                SegmentCount = segmentCount,
                NonHorizontalSegmentCountPixelBoundary = nonHorizontalSegmentCountPixelBoundary,
                NonHorizontalSegmentCountPixelCenter = nonHorizontalSegmentCountPixelCenter
            },
            contours,
            points);
    }

    /// <inheritdoc/>
    public IPath AsClosedPath()
    {
        if (this.PathType == PathTypes.Closed)
        {
            return this;
        }

        if (this.closedPath is not null)
        {
            return this.closedPath;
        }

        IPath[] paths = new IPath[this.paths.Length];
        for (int i = 0; i < this.paths.Length; i++)
        {
            paths[i] = this.paths[i].AsClosedPath();
        }

        this.closedPath = new ComplexPolygon(paths);
        return this.closedPath;
    }

    /// <inheritdoc/>
    SegmentInfo IPathInternals.PointAlongPath(float distance)
    {
        this.EnsureInternalPaths();

        distance %= this.length;
        foreach (InternalPath p in this.internalPaths)
        {
            if (p.Length >= distance)
            {
                return p.PointAlongPath(distance);
            }

            // Reduce it before trying the next path
            distance -= p.Length;
        }

        ThrowOutOfRange();
        return default;
    }

    /// <inheritdoc/>
    IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath()
    {
        this.EnsureInternalPaths();
        return this.internalPaths;
    }

    [MemberNotNull(nameof(internalPaths))]
    private void EnsureInternalPaths()
    {
        if (this.internalPaths is not null)
        {
            return;
        }

        this.InitInternalPaths();
    }

    /// <summary>
    /// Initializes <see cref="internalPaths"/> and <see cref="length"/>.
    /// </summary>
    [MemberNotNull(nameof(internalPaths))]
    private void InitInternalPaths()
    {
        this.internalPaths = new List<InternalPath>(this.paths.Length);
        this.length = 0;

        foreach (IPath p in this.paths)
        {
            foreach (ISimplePath s in p.Flatten())
            {
                InternalPath ip = new(s.Points, s.IsClosed);
                this.length += ip.Length;
                this.internalPaths.Add(ip);
            }
        }
    }

    private RectangleF CalcBounds()
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (IPath p in this.paths)
        {
            RectangleF pBounds = p.Bounds;

            minX = MathF.Min(minX, pBounds.Left);
            maxX = MathF.Max(maxX, pBounds.Right);
            minY = MathF.Min(minY, pBounds.Top);
            maxY = MathF.Max(maxY, pBounds.Bottom);
        }

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    private static InvalidOperationException ThrowOutOfRange() => new("Should not be possible to reach this line");
}
