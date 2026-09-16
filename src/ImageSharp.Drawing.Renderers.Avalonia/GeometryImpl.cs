// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Base Avalonia geometry implementation backed by ImageSharp paths.
/// </summary>
internal abstract class GeometryImpl : IGeometryImpl
{
    /// <summary>
    /// Initializes a new geometry from ImageSharp stroke and fill paths.
    /// </summary>
    /// <param name="strokePath">The path used for stroke operations.</param>
    /// <param name="fillPath">The path used for fill operations.</param>
    /// <param name="fillRule">The fill rule used for the fill path.</param>
    protected GeometryImpl(IPath strokePath, IPath? fillPath, IntersectionRule fillRule)
        => this.SetPaths(strokePath, fillPath, fillRule);

    /// <summary>
    /// Gets the stroke path used as the primary geometry path.
    /// </summary>
    public IPath Path => this.StrokePath;

    /// <summary>
    /// Gets the path used for stroke operations.
    /// </summary>
    public IPath StrokePath { get; private set; } = EmptyPath.OpenPath;

    /// <summary>
    /// Gets the path used for fill operations.
    /// </summary>
    public IPath? FillPath { get; private set; } = EmptyPath.OpenPath;

    /// <summary>
    /// Gets the fill rule used for the fill path.
    /// </summary>
    public IntersectionRule FillRule { get; private set; }

    /// <inheritdoc />
    public Rect Bounds { get; private set; }

    /// <inheritdoc />
    public double ContourLength { get; private set; }

    private LinearGeometry strokeGeometry = EmptyPath.OpenPath.ToLinearGeometry(Vector2.One);

    private LinearGeometry? fillGeometry = EmptyPath.OpenPath.ToLinearGeometry(Vector2.One);

    private StrokeCache strokeCache;

    /// <summary>
    /// Replaces the stroke and fill path with the same path.
    /// </summary>
    /// <param name="path">The replacement path.</param>
    protected void SetPath(IPath path)
        => this.SetPaths(path, path, this.FillRule);

    /// <summary>
    /// Replaces the geometry paths and cached path data.
    /// </summary>
    /// <param name="strokePath">The path used for stroke operations.</param>
    /// <param name="fillPath">The path used for fill operations.</param>
    /// <param name="fillRule">The fill rule used for <paramref name="fillPath"/>.</param>
    protected void SetPaths(IPath strokePath, IPath? fillPath, IntersectionRule fillRule)
    {
        this.StrokePath = strokePath;
        this.FillPath = fillPath;
        this.FillRule = fillRule;
        this.strokeGeometry = strokePath.ToLinearGeometry(Vector2.One);
        this.fillGeometry = fillPath is null
            ? null
            : ReferenceEquals(fillPath, strokePath) ? this.strokeGeometry : fillPath.ToLinearGeometry(Vector2.One);
        this.Bounds = this.strokeGeometry.Info.Bounds.ToAvaloniaRect();
        this.ContourLength = this.strokeGeometry.ComputeLength();
        this.strokeCache = default;
    }

    /// <inheritdoc />
    public Rect GetRenderBounds(IPen? pen)
    {
        Rect bounds = this.Bounds;
        if (this.fillGeometry is not null)
        {
            bounds = bounds.Union(this.fillGeometry.Info.Bounds.ToAvaloniaRect());
        }

        if (pen is not null)
        {
            LinearGeometry geometry = this.GetStrokeGeometry(pen);
            bounds = bounds.Union(geometry.Info.Bounds.ToAvaloniaRect());
        }

        return bounds;
    }

    /// <inheritdoc />
    public IGeometryImpl GetWidenedGeometry(IPen pen)
    {
        IPath path = this.StrokePath.CreateStrokePath(pen);

        return new StreamGeometryImpl(path, path, this.FillRule);
    }

    /// <inheritdoc />
    public bool FillContains(AvaloniaPoint point)
        => this.fillGeometry is not null && this.fillGeometry.Contains(point.ToPointF(), this.FillRule);

    /// <inheritdoc />
    public IntersectionResult GetFillIntersectionResult(IGeometryImpl geometry)
    {
        if (geometry is not GeometryImpl other || this.FillPath is null || other.FillPath is null)
        {
            return IntersectionResult.Empty;
        }

        Region region = new(this.FillPath, this.FillRule);
        Region otherRegion = new(other.FillPath, other.FillRule);

        // Containment is tested before intersection so equal regions produce FullyInside
        // rather than FullyContains.
        if (region.Contains(otherRegion))
        {
            return IntersectionResult.FullyInside;
        }

        if (otherRegion.Contains(region))
        {
            return IntersectionResult.FullyContains;
        }

        if (region.Intersects(otherRegion))
        {
            return IntersectionResult.Intersects;
        }

        return IntersectionResult.Empty;
    }

    /// <inheritdoc />
    public IGeometryImpl? Intersect(IGeometryImpl geometry)
        => CombinedGeometryImpl.Create(GeometryCombineMode.Intersect, this, (GeometryImpl)geometry);

    /// <inheritdoc />
    public bool StrokeContains(IPen? pen, AvaloniaPoint point)
        => pen is not null && this.GetStrokeGeometry(pen).Contains(point.ToPointF(), IntersectionRule.NonZero);

    /// <inheritdoc />
    public ITransformedGeometryImpl WithTransform(Matrix transform)
        => new TransformedGeometryImpl(this, transform);

    /// <inheritdoc />
    public bool TryGetPointAtDistance(double distance, out AvaloniaPoint point)
    {
        if (!this.TryGetPointAndTangentAtDistance(distance, out point, out _))
        {
            point = default;
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryGetPointAndTangentAtDistance(double distance, out AvaloniaPoint point, out AvaloniaPoint tangent)
    {
        if (this.ContourLength <= 0)
        {
            point = default;
            tangent = default;
            return false;
        }

        if (!this.StrokePath.TryGetPathPointAtDistance((float)distance, Vector2.One, out PathPoint segment))
        {
            point = default;
            tangent = default;
            return false;
        }

        point = new AvaloniaPoint(segment.Point.X, segment.Point.Y);
        tangent = new AvaloniaPoint(segment.Tangent.X, segment.Tangent.Y);
        return true;
    }

    /// <inheritdoc />
    public bool TryGetSegment(double startDistance, double stopDistance, bool startOnBeginFigure, [NotNullWhen(true)] out IGeometryImpl? segmentGeometry)
    {
        if (stopDistance <= startDistance || this.ContourLength <= 0)
        {
            segmentGeometry = null;
            return false;
        }

        if (!this.strokeGeometry.TryGetSegment((float)startDistance, (float)stopDistance, startOnBeginFigure, out IPath path))
        {
            segmentGeometry = null;
            return false;
        }

        segmentGeometry = new StreamGeometryImpl(path, null, this.FillRule);
        return true;
    }

    private LinearGeometry GetStrokeGeometry(IPen pen)
    {
        LinearGeometry? cachedGeometry = this.strokeCache.Geometry;
        if (cachedGeometry is not null && this.strokeCache.Matches(pen))
        {
            return cachedGeometry;
        }

        LinearGeometry geometry = this.StrokePath.CreateStrokePath(pen).ToLinearGeometry(Vector2.One);
        this.strokeCache = new StrokeCache(pen, geometry);

        return geometry;
    }

    private struct StrokeCache
    {
        private double thickness;
        private PenLineCap lineCap;
        private PenLineJoin lineJoin;
        private double miterLimit;
        private double dashOffset;
        private double[]? dashes;

        /// <summary>
        /// Initializes cached widened-stroke geometry for a pen.
        /// </summary>
        /// <param name="pen">The pen used to create the widened stroke.</param>
        /// <param name="geometry">The widened stroke geometry.</param>
        public StrokeCache(IPen pen, LinearGeometry geometry)
        {
            this.thickness = pen.Thickness;
            this.lineCap = pen.LineCap;
            this.lineJoin = pen.LineJoin;
            this.miterLimit = pen.MiterLimit;
            this.dashOffset = pen.DashStyle?.Offset ?? 0;
            this.dashes = CopyDashes(pen.DashStyle?.Dashes);
            this.Geometry = geometry;
        }

        /// <summary>
        /// Gets the cached widened stroke geometry.
        /// </summary>
        public LinearGeometry? Geometry { get; }

        /// <summary>
        /// Returns whether the cached geometry was created for the supplied pen.
        /// </summary>
        /// <param name="pen">The pen to compare.</param>
        /// <returns><see langword="true"/> when the pen matches the cache key.</returns>
        public bool Matches(IPen pen)
            => this.thickness == pen.Thickness &&
               this.lineCap == pen.LineCap &&
               this.lineJoin == pen.LineJoin &&
               this.miterLimit == pen.MiterLimit &&
               this.dashOffset == (pen.DashStyle?.Offset ?? 0) &&
               DashesEqual(this.dashes, pen.DashStyle?.Dashes);

        private static double[]? CopyDashes(IReadOnlyList<double>? source)
        {
            if (source is null || source.Count == 0)
            {
                return null;
            }

            double[] result = new double[source.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        private static bool DashesEqual(double[]? left, IReadOnlyList<double>? right)
        {
            if (left is null || left.Length == 0)
            {
                return right is null || right.Count == 0;
            }

            if (right is null || left.Length != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

}
