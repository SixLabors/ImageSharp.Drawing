// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The dark elements of one barcode symbol as a single closed path: the bars or dark module runs, each an axis aligned rectangle of four points, the dark
/// hexagons of a hexagonal symbol, each a pointed hexagon of six points around its centre, and the dark dots of a dotted symbol, each a circle of one radius
/// around its centre. The canvas retains the path until it flushes, so the path owns its arrays.
/// </summary>
internal sealed class BarcodePath : IPath
{
    /// <summary>
    /// The number of points of the closed contour of a rectangle, one for each corner.
    /// </summary>
    private const int RectanglePointCount = 4;

    /// <summary>
    /// The number of points of the closed contour of a hexagon, one for each vertex.
    /// </summary>
    private const int HexagonPointCount = 6;

    /// <summary>
    /// The rectangles, in pixels. No two rectangles overlap.
    /// </summary>
    private readonly RectangleF[] rectangles;

    /// <summary>
    /// The centres of the hexagons, in pixels. No two hexagons overlap.
    /// </summary>
    private readonly PointF[] hexagonCentres;

    /// <summary>
    /// Half the width and half the height of every hexagon, in pixels. A hexagon has its points at the top and the bottom and its flat sides on the left and
    /// the right.
    /// </summary>
    private readonly SizeF hexagonHalfSize;

    /// <summary>
    /// The centres of the circles, in pixels. No two circles overlap.
    /// </summary>
    private readonly PointF[] circleCentres;

    /// <summary>
    /// The radius of every circle, in pixels.
    /// </summary>
    private readonly float circleRadius;

    /// <summary>
    /// The geometry baked at the last scale the pipeline asked for.
    /// </summary>
    private LinearGeometryCache geometryCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodePath"/> class with rectangles.
    /// </summary>
    /// <param name="rectangles">The rectangles, in pixels. The path takes ownership of the array.</param>
    /// <param name="bounds">The union of the rectangles.</param>
    public BarcodePath(RectangleF[] rectangles, RectangleF bounds)
        : this(rectangles, [], default, [], 0, bounds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodePath"/> class with hexagons of one size.
    /// </summary>
    /// <param name="hexagonCentres">The centres of the hexagons, in pixels. The path takes ownership of the array.</param>
    /// <param name="hexagonHalfSize">Half the width and half the height of every hexagon, in pixels.</param>
    /// <param name="bounds">The union of the hexagons.</param>
    public BarcodePath(PointF[] hexagonCentres, SizeF hexagonHalfSize, RectangleF bounds)
        : this([], hexagonCentres, hexagonHalfSize, [], 0, bounds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodePath"/> class with circles of one radius.
    /// </summary>
    /// <param name="circleCentres">The centres of the circles, in pixels. The path takes ownership of the array.</param>
    /// <param name="circleRadius">The radius of every circle, in pixels.</param>
    /// <param name="bounds">The union of the circles.</param>
    public BarcodePath(PointF[] circleCentres, float circleRadius, RectangleF bounds)
        : this([], [], default, circleCentres, circleRadius, bounds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodePath"/> class.
    /// </summary>
    /// <param name="rectangles">The rectangles, in pixels.</param>
    /// <param name="hexagonCentres">The centres of the hexagons, in pixels.</param>
    /// <param name="hexagonHalfSize">Half the width and half the height of every hexagon, in pixels.</param>
    /// <param name="circleCentres">The centres of the circles, in pixels.</param>
    /// <param name="circleRadius">The radius of every circle, in pixels.</param>
    /// <param name="bounds">The union of the rectangles, the hexagons and the circles.</param>
    private BarcodePath(RectangleF[] rectangles, PointF[] hexagonCentres, SizeF hexagonHalfSize, PointF[] circleCentres, float circleRadius, RectangleF bounds)
    {
        this.rectangles = rectangles;
        this.hexagonCentres = hexagonCentres;
        this.hexagonHalfSize = hexagonHalfSize;
        this.circleCentres = circleCentres;
        this.circleRadius = circleRadius;
        this.Bounds = bounds;
    }

    /// <inheritdoc/>
    public PathTypes PathType => PathTypes.Closed;

    /// <inheritdoc/>
    public RectangleF Bounds { get; }

    /// <inheritdoc/>
    public IEnumerable<ISimplePath> Flatten()
    {
        foreach (RectangleF rectangle in this.rectangles)
        {
            yield return new RectanglePolygon(rectangle);
        }

        foreach (PointF centre in this.hexagonCentres)
        {
            PointF[] points = new PointF[HexagonPointCount];
            WriteHexagon(centre, this.hexagonHalfSize, Vector2.One, points);
            yield return new Polygon(points);
        }

        foreach (PointF centre in this.circleCentres)
        {
            yield return new EllipsePolygon(centre, this.circleRadius);
        }
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
    public IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        RectangleF[] rectangles = this.rectangles;
        PointF[] centres = this.hexagonCentres;
        PointF[] circles = this.circleCentres;

        // A translation or a scale keeps every rectangle axis aligned and every hexagon pointed at the top and the bottom, so the path keeps its form. An axis
        // swap keeps the rectangles but turns the hexagons on their side, so it keeps the form only when there are no hexagons. A circle stays a circle only
        // when both axes scale by the same magnitude; otherwise it becomes an ellipse.
        bool scaleOnly = matrix.M12 == 0 && matrix.M21 == 0;
        bool keepsCircles = circles.Length == 0 || MathF.Abs(matrix.M11) == MathF.Abs(matrix.M22);
        if (keepsCircles && (scaleOnly || (centres.Length == 0 && circles.Length == 0 && MatrixUtilities.PreservesAxisAlignedRectangles(matrix))))
        {
            // Each rectangle transforms by two opposite corners; a negative scale or a swap can exchange the edges, so the edges are sorted again. A hexagon is
            // symmetric about both of its axes, so a negative scale moves only its centre, and its half size takes the scale magnitudes.
            RectangleF[] transformed = new RectangleF[rectangles.Length];
            Vector2 boundsMin = Vector2.Transform(new Vector2(this.Bounds.Left, this.Bounds.Top), matrix);
            Vector2 boundsMax = Vector2.Transform(new Vector2(this.Bounds.Right, this.Bounds.Bottom), matrix);
            for (int i = 0; i < rectangles.Length; i++)
            {
                RectangleF rectangle = rectangles[i];
                Vector2 a = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Top), matrix);
                Vector2 b = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Bottom), matrix);
                transformed[i] = RectangleF.FromLTRB(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
            }

            PointF[] transformedCentres = new PointF[centres.Length];
            for (int i = 0; i < centres.Length; i++)
            {
                transformedCentres[i] = Vector2.Transform(centres[i], matrix);
            }

            // A circle is symmetric about its centre, so only the centre moves, and the radius takes the scale magnitude, which is the same on both axes here.
            PointF[] transformedCircles = new PointF[circles.Length];
            for (int i = 0; i < circles.Length; i++)
            {
                transformedCircles[i] = Vector2.Transform(circles[i], matrix);
            }

            return new BarcodePath(
                transformed,
                transformedCentres,
                new SizeF(this.hexagonHalfSize.Width * MathF.Abs(matrix.M11), this.hexagonHalfSize.Height * MathF.Abs(matrix.M22)),
                transformedCircles,
                this.circleRadius * MathF.Abs(matrix.M11),
                RectangleF.FromLTRB(
                    MathF.Min(boundsMin.X, boundsMax.X),
                    MathF.Min(boundsMin.Y, boundsMax.Y),
                    MathF.Max(boundsMin.X, boundsMax.X),
                    MathF.Max(boundsMin.Y, boundsMax.Y)));
        }

        // A rotation or a skew turns the rectangles into general quadrilaterals, the hexagons into general hexagons and the circles into ellipses.
        IPath[] paths = new IPath[rectangles.Length + centres.Length + circles.Length];
        int index = 0;
        foreach (ISimplePath polygon in this.Flatten())
        {
            paths[index++] = ((IPath)polygon).Transform(matrix);
        }

        return new ComplexPolygon(paths);
    }

    /// <inheritdoc/>
    public IPath AsClosedPath() => this;

    /// <summary>
    /// Writes the six points of a hexagon clockwise from its top point, scaled by <paramref name="scale"/>. The flat sides stand at half the width from the
    /// centre, and they run from half the half height above the centre to half the half height below it.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="halfSize">Half the width and half the height.</param>
    /// <param name="scale">The X/Y scale applied to the points.</param>
    /// <param name="points">The buffer of six points.</param>
    private static void WriteHexagon(PointF centre, SizeF halfSize, Vector2 scale, Span<PointF> points)
    {
        float x = centre.X;
        float y = centre.Y;
        float w = halfSize.Width;
        float h = halfSize.Height;
        points[0] = new PointF(x * scale.X, (y - h) * scale.Y);
        points[1] = new PointF((x + w) * scale.X, (y - (h / 2)) * scale.Y);
        points[2] = new PointF((x + w) * scale.X, (y + (h / 2)) * scale.Y);
        points[3] = new PointF(x * scale.X, (y + h) * scale.Y);
        points[4] = new PointF((x - w) * scale.X, (y + (h / 2)) * scale.Y);
        points[5] = new PointF((x - w) * scale.X, (y - (h / 2)) * scale.Y);
    }

    /// <summary>
    /// Builds the geometry: one closed contour of four points for each rectangle, clockwise from the top left corner, then one closed contour of six points for
    /// each hexagon, clockwise from its top point, then one closed contour for each circle, each point scaled by <paramref name="scale"/>.
    /// </summary>
    /// <param name="scale">The X/Y scale applied to the points.</param>
    /// <returns>The retained linear geometry.</returns>
    private LinearGeometry BuildLinearGeometry(Vector2 scale)
    {
        RectangleF[] rectangles = this.rectangles;
        PointF[] centres = this.hexagonCentres;
        PointF[] circles = this.circleCentres;

        // Every circle has the same radius, so one circle about the origin is flattened at this scale by the ellipse's own curve flattening, and each circle
        // copies its points offset by its scaled centre. The template is one closed contour that does not repeat its first point.
        IReadOnlyList<PointF> circlePoints = [];
        RectangleF circleBounds = default;
        if (circles.Length != 0)
        {
            LinearGeometry template = new EllipsePolygon(PointF.Empty, this.circleRadius).ToLinearGeometry(scale);
            circlePoints = template.Points;
            circleBounds = template.Info.Bounds;
        }

        int circlePointCount = circlePoints.Count;
        int rectanglePoints = rectangles.Length * RectanglePointCount;
        int hexagonPoints = centres.Length * HexagonPointCount;
        PointF[] points = new PointF[rectanglePoints + hexagonPoints + (circles.Length * circlePointCount)];
        LinearContour[] contours = new LinearContour[rectangles.Length + centres.Length + circles.Length];
        for (int i = 0; i < rectangles.Length; i++)
        {
            RectangleF rectangle = rectangles[i];
            float left = rectangle.Left * scale.X;
            float top = rectangle.Top * scale.Y;
            float right = rectangle.Right * scale.X;
            float bottom = rectangle.Bottom * scale.Y;

            // A negative scale mirrors the rectangle, so the contour bounds sort the edges.
            RectangleF bounds = RectangleF.FromLTRB(MathF.Min(left, right), MathF.Min(top, bottom), MathF.Max(left, right), MathF.Max(top, bottom));
            int start = i * RectanglePointCount;
            points[start] = new PointF(left, top);
            points[start + 1] = new PointF(right, top);
            points[start + 2] = new PointF(right, bottom);
            points[start + 3] = new PointF(left, bottom);
            contours[i] = new LinearContour
            {
                PointStart = start,
                PointCount = RectanglePointCount,
                Bounds = bounds,
                SegmentStart = start,
                SegmentCount = RectanglePointCount,
                IsClosed = true
            };
        }

        SizeF halfSize = this.hexagonHalfSize;
        float halfWidth = MathF.Abs(halfSize.Width * scale.X);
        float halfHeight = MathF.Abs(halfSize.Height * scale.Y);
        for (int i = 0; i < centres.Length; i++)
        {
            int start = rectanglePoints + (i * HexagonPointCount);
            WriteHexagon(centres[i], halfSize, scale, points.AsSpan(start, HexagonPointCount));
            float x = centres[i].X * scale.X;
            float y = centres[i].Y * scale.Y;
            contours[rectangles.Length + i] = new LinearContour
            {
                PointStart = start,
                PointCount = HexagonPointCount,
                Bounds = RectangleF.FromLTRB(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight),
                SegmentStart = start,
                SegmentCount = HexagonPointCount,
                IsClosed = true
            };
        }

        int circleContour = rectangles.Length + centres.Length;
        for (int i = 0; i < circles.Length; i++)
        {
            int start = rectanglePoints + hexagonPoints + (i * circlePointCount);
            float x = circles[i].X * scale.X;
            float y = circles[i].Y * scale.Y;
            for (int p = 0; p < circlePointCount; p++)
            {
                PointF point = circlePoints[p];
                points[start + p] = new PointF(point.X + x, point.Y + y);
            }

            contours[circleContour + i] = new LinearContour
            {
                PointStart = start,
                PointCount = circlePointCount,
                Bounds = new RectangleF(circleBounds.X + x, circleBounds.Y + y, circleBounds.Width, circleBounds.Height),
                SegmentStart = start,
                SegmentCount = circlePointCount,
                IsClosed = true
            };
        }

        float boundsLeft = this.Bounds.Left * scale.X;
        float boundsTop = this.Bounds.Top * scale.Y;
        float boundsRight = this.Bounds.Right * scale.X;
        float boundsBottom = this.Bounds.Bottom * scale.Y;
        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = RectangleF.FromLTRB(
                    MathF.Min(boundsLeft, boundsRight),
                    MathF.Min(boundsTop, boundsBottom),
                    MathF.Max(boundsLeft, boundsRight),
                    MathF.Max(boundsTop, boundsBottom)),
                ContourCount = contours.Length,
                PointCount = points.Length,
                SegmentCount = points.Length
            },
            contours,
            points);
    }
}
