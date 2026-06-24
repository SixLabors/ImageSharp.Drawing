// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents retained linearized geometry that can be consumed directly by drawing backends.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="LinearGeometry"/> instance stores contour-local point data plus the metadata required to
/// interpret those points as a sequence of final linear segments.
/// </para>
/// <para>
/// Closed contours do not duplicate their first point at the end of the stored point run. Closure is represented
/// by <see cref="LinearContour.IsClosed"/>, and the closing segment is derived by <see cref="GetSegments()"/>.
/// </para>
/// <para>
/// The retained storage model is:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Points"/> stores the concatenated point data for every contour.</description></item>
/// <item><description><see cref="Contours"/> maps each contour to its point run and derived segment range.</description></item>
/// <item><description><see cref="Info"/> exposes geometry-wide metadata such as bounds and total segment count.</description></item>
/// </list>
/// </remarks>
public sealed class LinearGeometry
{
    private readonly LinearContour[] contours;
    private readonly PointF[] points;

    // Computed measurements are finite; NaN keeps the memoized state in one field without nullable value-type overhead.
    private float length = float.NaN;
    private float area = float.NaN;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGeometry"/> class.
    /// </summary>
    /// <param name="info">The geometry metadata.</param>
    /// <param name="contours">The contour metadata.</param>
    /// <param name="points">The point storage.</param>
    public LinearGeometry(LinearGeometryInfo info, IReadOnlyList<LinearContour> contours, IReadOnlyList<PointF> points)
    {
        Guard.NotNull(contours, nameof(contours));
        Guard.NotNull(points, nameof(points));

        this.Info = info;
        this.contours = contours as LinearContour[] ?? [.. contours];
        this.points = points as PointF[] ?? [.. points];
        this.Contours = this.contours;
        this.Points = this.points;
    }

    private enum PointContainment
    {
        Outside,
        Inside,
        OnBoundary
    }

    /// <summary>
    /// Gets geometry-wide metadata for this retained result.
    /// </summary>
    public LinearGeometryInfo Info { get; }

    /// <summary>
    /// Gets the contour metadata describing how <see cref="Points"/> is partitioned.
    /// </summary>
    /// <remarks>
    /// Each entry defines one contour's point run and the corresponding segment range in the derived segment stream.
    /// </remarks>
    public IReadOnlyList<LinearContour> Contours { get; }

    /// <summary>
    /// Gets the retained point storage for all contours in this geometry.
    /// </summary>
    /// <remarks>
    /// Points are stored per contour in contour order. A closed contour does not repeat its first point at the end
    /// of its stored point run.
    /// </remarks>
    public IReadOnlyList<PointF> Points { get; }

    /// <summary>
    /// Gets the retained contour metadata as a span for backend hot paths.
    /// </summary>
    /// <returns>The retained contour metadata.</returns>
    internal ReadOnlySpan<LinearContour> GetContours() => this.contours;

    /// <summary>
    /// Gets the retained point run for the supplied contour.
    /// </summary>
    /// <param name="contour">The contour whose points are returned.</param>
    /// <returns>The retained points belonging to <paramref name="contour"/>.</returns>
    internal ReadOnlySpan<PointF> GetContourPoints(in LinearContour contour)
        => this.points.AsSpan(contour.PointStart, contour.PointCount);

    /// <summary>
    /// Creates retained geometry for one open polyline, baked under the supplied device-space <paramref name="scale"/>.
    /// </summary>
    /// <param name="points">The polyline points.</param>
    /// <param name="scale">The X/Y scale at which the polyline is baked.</param>
    /// <returns>The retained open polyline geometry.</returns>
    public static LinearGeometry CreateOpenPolyline(PointF[] points, Vector2 scale)
    {
        Guard.NotNull(points, nameof(points));
        Guard.MustBeGreaterThanOrEqualTo(points.Length, 2, nameof(points));

        PointF[] retained;
        if (scale == Vector2.One)
        {
            retained = points;
        }
        else
        {
            retained = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                retained[i] = new PointF(points[i].X * scale.X, points[i].Y * scale.Y);
            }
        }

        RectangleF bounds = GetPointBounds(retained);
        int segmentCount = retained.Length - 1;

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = bounds,
                ContourCount = 1,
                PointCount = retained.Length,
                SegmentCount = segmentCount
            },
            [new LinearContour
            {
                PointStart = 0,
                PointCount = retained.Length,
                Bounds = bounds,
                SegmentStart = 0,
                SegmentCount = segmentCount,
                IsClosed = false
            }
            ],
            retained);
    }

    /// <summary>
    /// Creates retained geometry for one open polyline.
    /// </summary>
    /// <param name="points">The polyline points.</param>
    /// <returns>The retained open polyline geometry.</returns>
    public static LinearGeometry CreateOpenPolyline(PointF[] points)
        => CreateOpenPolyline(points, Vector2.One);

    /// <summary>
    /// Gets an enumerator for the derived linear segments represented by <see cref="Points"/> and <see cref="Contours"/>.
    /// </summary>
    /// <returns>
    /// A zero-allocation enumerator that yields the final linear segments in contour order.
    /// </returns>
    public SegmentEnumerator GetSegments() => new(this);

    /// <summary>
    /// Calculates the total length of all derived linear segments.
    /// </summary>
    /// <returns>The total segment length.</returns>
    public float ComputeLength()
    {
        if (float.IsNaN(this.length))
        {
            float calculatedLength = 0;
            SegmentEnumerator segments = this.GetSegments();

            while (segments.MoveNext())
            {
                LinearSegment segment = segments.Current;

                calculatedLength += Vector2.Distance(segment.Start, segment.End);
            }

            this.length = calculatedLength;
        }

        return this.length;
    }

    /// <summary>
    /// Calculates the total absolute area of all contour point runs.
    /// </summary>
    /// <returns>The total contour area.</returns>
    public float ComputeArea()
    {
        if (float.IsNaN(this.area))
        {
            float calculatedArea = 0;

            for (int i = 0; i < this.contours.Length; i++)
            {
                LinearContour contour = this.contours[i];
                if (contour.PointCount < 3)
                {
                    // Open line contours are valid retained geometry, but cannot enclose area.
                    continue;
                }

                ReadOnlySpan<PointF> points = this.GetContourPoints(contour);
                float signedArea = 0;
                Vector2 previous = points[^1];

                for (int p = 0; p < points.Length; p++)
                {
                    Vector2 current = points[p];
                    signedArea += PolygonUtilities.CrossProduct(previous, current);
                    previous = current;
                }

                calculatedArea += MathF.Abs(signedArea) * .5F;
            }

            this.area = calculatedArea;
        }

        return this.area;
    }

    /// <summary>
    /// Returns whether the supplied point is inside the geometry.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="intersectionRule">The fill rule used for containment.</param>
    /// <returns><see langword="true"/> when the point is inside or on the geometry boundary.</returns>
    public bool Contains(PointF point, IntersectionRule intersectionRule)
    {
        RectangleF bounds = this.Info.Bounds;
        if (point.X < bounds.Left ||
            point.X > bounds.Right ||
            point.Y < bounds.Top ||
            point.Y > bounds.Bottom)
        {
            return false;
        }

        int winding = 0;
        bool inside = false;

        for (int i = 0; i < this.contours.Length; i++)
        {
            LinearContour contour = this.contours[i];
            if (!contour.IsClosed)
            {
                continue;
            }

            RectangleF contourBounds = contour.Bounds;
            if (point.X < contourBounds.Left ||
                point.X > contourBounds.Right ||
                point.Y < contourBounds.Top ||
                point.Y > contourBounds.Bottom)
            {
                continue;
            }

            if (intersectionRule == IntersectionRule.NonZero)
            {
                winding += this.GetWindingContribution(contour, point, out bool onBoundary);
                if (onBoundary)
                {
                    return true;
                }

                continue;
            }

            PointContainment containment = this.ClassifyEvenOdd(contour, point);
            if (containment == PointContainment.OnBoundary)
            {
                return true;
            }

            inside ^= containment == PointContainment.Inside;
        }

        return intersectionRule == IntersectionRule.NonZero ? winding != 0 : inside;
    }

    /// <summary>
    /// Gets path information at the specified distance along the geometry.
    /// </summary>
    /// <param name="distance">The distance along the geometry.</param>
    /// <param name="pathPoint">When this method returns, contains the path information at <paramref name="distance"/> if the distance resolves to a point on the geometry; otherwise, the default value.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="distance"/> resolves to a point on the geometry;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetPathPointAtDistance(float distance, out PathPoint pathPoint)
    {
        pathPoint = default;

        float length = this.ComputeLength();
        if (length <= 0 || !float.IsFinite(distance) || distance < 0)
        {
            return false;
        }

        bool allContoursClosed = true;
        for (int i = 0; i < this.contours.Length; i++)
        {
            allContoursClosed &= this.contours[i].IsClosed;
        }

        if (allContoursClosed)
        {
            distance %= length;
        }
        else if (distance > length)
        {
            return false;
        }

        SegmentEnumerator segments = this.GetSegments();
        LinearSegment lastSegment = default;

        while (segments.MoveNext())
        {
            LinearSegment segment = segments.Current;
            float segmentLength = Vector2.Distance(segment.Start, segment.End);

            if (segmentLength <= 0)
            {
                continue;
            }

            if (distance <= segmentLength)
            {
                Vector2 tangent = Vector2.Normalize((Vector2)segment.End - (Vector2)segment.Start);
                float angle = (float)(Math.Atan2(tangent.Y, tangent.X) % (Math.PI * 2));

                pathPoint = new PathPoint
                {
                    Point = Vector2.Lerp(segment.Start, segment.End, distance / segmentLength),
                    Tangent = tangent,
                    Angle = GeometryUtilities.RadianToDegree(angle)
                };

                return true;
            }

            distance -= segmentLength;
            lastSegment = segment;
        }

        Vector2 endTangent = Vector2.Normalize((Vector2)lastSegment.End - (Vector2)lastSegment.Start);
        float endAngle = (float)(Math.Atan2(endTangent.Y, endTangent.X) % (Math.PI * 2));

        pathPoint = new PathPoint
        {
            Point = lastSegment.End,
            Tangent = endTangent,
            Angle = GeometryUtilities.RadianToDegree(endAngle)
        };

        return true;
    }

    /// <summary>
    /// Creates a path segment between two distances along the geometry.
    /// </summary>
    /// <param name="startDistance">The segment start distance.</param>
    /// <param name="stopDistance">The segment stop distance.</param>
    /// <param name="startOnBeginFigure">Whether the returned segment starts a new figure at the first segment point.</param>
    /// <param name="path">When this method returns, contains the segment path if one was created; otherwise, an empty path.</param>
    /// <returns><see langword="true"/> when a segment path was created.</returns>
    public bool TryGetSegment(float startDistance, float stopDistance, bool startOnBeginFigure, out IPath path)
    {
        float length = this.ComputeLength();
        if (stopDistance <= startDistance || length <= 0)
        {
            path = EmptyPath.OpenPath;
            return false;
        }

        PathBuilder builder = new();
        List<PointF> points = [];
        bool added = false;

        float start = Math.Clamp(startDistance, 0, length);
        float stop = Math.Clamp(stopDistance, 0, length);
        float currentDistance = 0;
        int activeContour = -1;
        SegmentEnumerator segments = this.GetSegments();

        while (segments.MoveNext())
        {
            LinearSegment segment = segments.Current;

            if (activeContour != segment.ContourIndex)
            {
                // The enumerator owns segment derivation, but extracted paths still need
                // figure boundaries when the requested range crosses between contours.
                FlushSegmentPoints(builder, points, ref added, startOnBeginFigure);
                activeContour = segment.ContourIndex;
            }

            float segmentLength = Vector2.Distance(segment.Start, segment.End);
            float segmentEndDistance = currentDistance + segmentLength;

            if (segmentEndDistance >= start && currentDistance <= stop && segmentLength > 0)
            {
                float localStart = Math.Clamp((start - currentDistance) / segmentLength, 0, 1);
                float localStop = Math.Clamp((stop - currentDistance) / segmentLength, 0, 1);

                PointF startPoint = Vector2.Lerp(segment.Start, segment.End, localStart);
                if (points.Count == 0 || points[^1] != startPoint)
                {
                    points.Add(startPoint);
                }

                PointF stopPoint = Vector2.Lerp(segment.Start, segment.End, localStop);
                if (points.Count == 0 || points[^1] != stopPoint)
                {
                    points.Add(stopPoint);
                }
            }

            currentDistance = segmentEndDistance;
        }

        FlushSegmentPoints(builder, points, ref added, startOnBeginFigure);

        path = added ? builder.Build() : EmptyPath.OpenPath;
        return added;
    }

    /// <summary>
    /// Calculates the bounds of a retained point array.
    /// </summary>
    /// <param name="points">The points to bound.</param>
    /// <returns>The bounds enclosing <paramref name="points"/>.</returns>
    private static RectangleF GetPointBounds(ReadOnlySpan<PointF> points)
    {
        float minX = points[0].X;
        float minY = points[0].Y;
        float maxX = minX;
        float maxY = minY;

        for (int i = 1; i < points.Length; i++)
        {
            PointF point = points[i];
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Classifies a point against a closed contour using the even-odd rule.
    /// </summary>
    /// <param name="contour">The closed contour to test.</param>
    /// <param name="point">The point to classify.</param>
    /// <returns>The point classification for <paramref name="contour"/>.</returns>
    private PointContainment ClassifyEvenOdd(in LinearContour contour, PointF point)
    {
        bool inside = false;
        PointF previous = this.points[contour.PointStart + contour.PointCount - 1];

        for (int i = 0; i < contour.PointCount; i++)
        {
            PointF current = this.points[contour.PointStart + i];

            if (IsPointOnSegment(point, previous, current))
            {
                return PointContainment.OnBoundary;
            }

            bool crossesY = (previous.Y > point.Y) != (current.Y > point.Y);
            if (crossesY)
            {
                Vector2 edge = (Vector2)current - (Vector2)previous;
                Vector2 pointOffset = (Vector2)point - (Vector2)previous;
                float yRatio = pointOffset.Y / edge.Y;
                float intersectionX = previous.X + (edge.X * yRatio);

                if (point.X < intersectionX)
                {
                    inside = !inside;
                }
            }

            previous = current;
        }

        return inside ? PointContainment.Inside : PointContainment.Outside;
    }

    /// <summary>
    /// Calculates one closed contour's winding contribution for a point.
    /// </summary>
    /// <param name="contour">The closed contour to test.</param>
    /// <param name="point">The point to classify.</param>
    /// <param name="onBoundary">When this method returns, indicates whether <paramref name="point"/> lies on the contour boundary.</param>
    /// <returns>The contour winding contribution.</returns>
    private int GetWindingContribution(in LinearContour contour, PointF point, out bool onBoundary)
    {
        onBoundary = false;

        int winding = 0;
        PointF previous = this.points[contour.PointStart + contour.PointCount - 1];

        for (int i = 0; i < contour.PointCount; i++)
        {
            PointF current = this.points[contour.PointStart + i];

            if (IsPointOnSegment(point, previous, current))
            {
                onBoundary = true;
                return 0;
            }

            Vector2 edge = (Vector2)current - (Vector2)previous;
            Vector2 pointOffset = (Vector2)point - (Vector2)previous;
            float crossProduct = PolygonUtilities.CrossProduct(edge, pointOffset);

            if (previous.Y <= point.Y)
            {
                if (current.Y > point.Y && crossProduct > 0)
                {
                    winding++;
                }
            }
            else if (current.Y <= point.Y && crossProduct < 0)
            {
                winding--;
            }

            previous = current;
        }

        return winding;
    }

    /// <summary>
    /// Returns whether a point lies on a line segment.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="start">The segment start point.</param>
    /// <param name="end">The segment end point.</param>
    /// <returns><see langword="true"/> when <paramref name="point"/> lies on the segment.</returns>
    private static bool IsPointOnSegment(PointF point, PointF start, PointF end)
    {
        Vector2 startVector = start;
        Vector2 segment = (Vector2)end - startVector;
        Vector2 pointOffset = (Vector2)point - startVector;
        float cross = PolygonUtilities.CrossProduct(segment, pointOffset);

        if (cross != 0)
        {
            return false;
        }

        float dot = Vector2.Dot(pointOffset, segment);
        if (dot < 0)
        {
            return false;
        }

        return dot <= segment.LengthSquared();
    }

    /// <summary>
    /// Flushes the current segment extraction buffer to a path builder.
    /// </summary>
    /// <param name="builder">The path builder receiving the extracted segment.</param>
    /// <param name="points">The segment extraction buffer.</param>
    /// <param name="added">Whether a previous figure has already been added.</param>
    /// <param name="startOnBeginFigure">Whether the first emitted segment should start a new figure.</param>
    private static void FlushSegmentPoints(PathBuilder builder, List<PointF> points, ref bool added, bool startOnBeginFigure)
    {
        // The buffer is reused across contour boundaries. Empty or single-point buffers are valid
        // when the requested range misses a contour or only touches it at one exact boundary.
        if (points.Count < 2)
        {
            points.Clear();
            return;
        }

        if (added || !startOnBeginFigure)
        {
            builder.StartFigure();
        }

        builder.AddLines([.. points]);
        points.Clear();
        added = true;
    }
}
