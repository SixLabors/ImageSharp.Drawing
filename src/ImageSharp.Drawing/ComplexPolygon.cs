// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a complex polygon made up of one or more shapes overlayed on each other,
/// where overlaps cause holes.
/// </summary>
/// <seealso cref="IPath" />
public sealed class ComplexPolygon : IPath, IInternalPathOwner
{
    /// <summary>
    /// The child paths that make up the shape.
    /// </summary>
    private readonly IPath[] paths;

    /// <summary>
    /// The lazily initialized internal representation of each flattened ring.
    /// </summary>
    private List<InternalPath>? internalPaths;

    /// <summary>
    /// The lazily computed union of the child path bounds.
    /// </summary>
    private RectangleF? bounds;

    /// <summary>
    /// The cached result of <see cref="AsClosedPath"/>.
    /// </summary>
    private IPath? closedPath;

    /// <summary>
    /// The per-scale cache of retained linear geometry.
    /// </summary>
    private LinearGeometryCache geometryCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplexPolygon"/> class.
    /// </summary>
    /// <param name="contour">The points defining the outer contour.</param>
    /// <param name="hole">The points defining the hole.</param>
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
    public LinearGeometry ToLinearGeometry(Vector2 scale)
        => this.geometryCache.TryGet(scale, out LinearGeometry? hit)
            ? hit
            : this.geometryCache.Store(scale, this.BuildLinearGeometry(scale));

    /// <inheritdoc/>
    public float ComputeLength(Vector2 scale)
        => this.ToLinearGeometry(scale).ComputeLength();

    /// <inheritdoc/>
    public float ComputeArea(Vector2 scale)
        => this.ToLinearGeometry(scale).ComputeArea();

    /// <inheritdoc/>
    public bool Contains(PointF point, IntersectionRule intersectionRule, Vector2 scale)
    {
        PointF scaledPoint = new(point.X * scale.X, point.Y * scale.Y);

        return this.ToLinearGeometry(scale).Contains(scaledPoint, intersectionRule);
    }

    /// <summary>
    /// Concatenates the child path geometries into a single retained geometry, rebasing each
    /// child's point, contour and segment indices onto the combined arrays.
    /// </summary>
    /// <param name="scale">The X/Y scale at which curves are flattened.</param>
    /// <returns>The combined linear geometry.</returns>
    private LinearGeometry BuildLinearGeometry(Vector2 scale)
    {
        int pointCount = 0;
        int contourCount = 0;
        int segmentCount = 0;

        bool hasBounds = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (IPath path in this.paths)
        {
            LinearGeometry geometry = path.ToLinearGeometry(scale);

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
        }

        PointF[] points = new PointF[pointCount];
        LinearContour[] contours = new LinearContour[contourCount];
        int pointStart = 0;
        int contourStart = 0;
        int segmentStart = 0;

        foreach (IPath path in this.paths)
        {
            LinearGeometry geometry = path.ToLinearGeometry(scale);
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
                    Bounds = contour.Bounds,
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
                SegmentCount = segmentCount
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
    public bool TryGetPathPointAtDistance(float distance, Vector2 scale, out PathPoint pathPoint)
        => this.ToLinearGeometry(scale).TryGetPathPointAtDistance(distance, out pathPoint);

    /// <inheritdoc/>
    public bool TryGetPathPointAtDistanceUnbounded(float distance, Vector2 scale, out PathPoint pathPoint)
        => this.ToLinearGeometry(scale).TryGetPathPointAtDistanceUnbounded(distance, out pathPoint);

    /// <inheritdoc/>
    public bool TryGetSegment(float startDistance, float stopDistance, bool startOnBeginFigure, Vector2 scale, out IPath path)
        => this.ToLinearGeometry(scale).TryGetSegment(startDistance, stopDistance, startOnBeginFigure, out path);

    /// <inheritdoc/>
    IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath()
    {
        this.EnsureInternalPaths();
        return this.internalPaths;
    }

    /// <summary>
    /// Ensures <see cref="internalPaths"/> is initialized.
    /// </summary>
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
    /// Initializes <see cref="internalPaths"/>.
    /// </summary>
    [MemberNotNull(nameof(internalPaths))]
    private void InitInternalPaths()
    {
        this.internalPaths = new List<InternalPath>(this.paths.Length);

        foreach (IPath p in this.paths)
        {
            foreach (ISimplePath s in p.Flatten())
            {
                InternalPath ip = new(s.Points, s.IsClosed);
                this.internalPaths.Add(ip);
            }
        }
    }

    /// <summary>
    /// Computes the union of the child path bounds.
    /// </summary>
    /// <returns>The axis-aligned bounds enclosing all child paths.</returns>
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
}
