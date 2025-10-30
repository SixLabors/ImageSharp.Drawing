// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a line segment that contains a lists of control points that will be rendered as a cubic bezier curve
/// </summary>
/// <seealso cref="ILineSegment" />
public sealed class CubicBezierLineSegment : ILineSegment
{
    // code for this taken from <see href="http://devmag.org.za/2011/04/05/bzier-curves-a-tutorial/"/>
    private const float MinimumSqrDistance = 1.75f;
    private const float DivisionThreshold = -.9995f;

    /// <summary>
    /// The line points.
    /// </summary>
    private PointF[]? linePoints;

    private readonly PointF[] controlPoints;

    /// <summary>
    /// Initializes a new instance of the <see cref="CubicBezierLineSegment"/> class.
    /// </summary>
    /// <param name="points">The points.</param>
    public CubicBezierLineSegment(PointF[] points)
    {
        this.controlPoints = points ?? throw new ArgumentNullException(nameof(points));

        Guard.MustBeGreaterThanOrEqualTo(this.controlPoints.Length, 4, nameof(points));

        int correctPointCount = (this.controlPoints.Length - 1) % 3;
        if (correctPointCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "points must be a multiple of 3 plus 1 long.");
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CubicBezierLineSegment"/> class.
    /// </summary>
    /// <param name="start">The start.</param>
    /// <param name="controlPoint1">The control point1.</param>
    /// <param name="controlPoint2">The control point2.</param>
    /// <param name="end">The end.</param>
    /// <param name="additionalPoints">The additional points.</param>
    public CubicBezierLineSegment(PointF start, PointF controlPoint1, PointF controlPoint2, PointF end, params PointF[] additionalPoints)
        : this(new[] { start, controlPoint1, controlPoint2, end }.Merge(additionalPoints))
    {
    }

    /// <inheritdoc cref="CubicBezierLineSegment(PointF, PointF, PointF, PointF, PointF[])" />
    public CubicBezierLineSegment(PointF start, PointF controlPoint1, PointF controlPoint2, PointF end)
        : this(new[] { start, controlPoint1, controlPoint2, end })
    {
    }

    /// <summary>
    /// Gets the control points.
    /// </summary>
    public IReadOnlyList<PointF> ControlPoints => this.controlPoints;

    /// <inheritdoc/>
    public PointF EndPoint => this.controlPoints[^1];

    /// <inheritdoc/>
    public ReadOnlyMemory<PointF> Flatten() => this.linePoints ??= GetDrawingPoints(this.controlPoints);

    /// <summary>
    /// Gets the control points of this curve.
    /// </summary>
    /// <returns>The control points of this curve.</returns>
    public ReadOnlyMemory<PointF> GetControlPoints() => this.controlPoints;

    /// <summary>
    /// Transforms this line segment using the specified matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>A line segment with the matrix applied to it.</returns>
    public CubicBezierLineSegment Transform(Matrix3x2 matrix)
    {
        if (matrix.IsIdentity)
        {
            // no transform to apply skip it
            return this;
        }

        PointF[] transformedPoints = new PointF[this.controlPoints.Length];

        for (int i = 0; i < this.controlPoints.Length; i++)
        {
            transformedPoints[i] = PointF.Transform(this.controlPoints[i], matrix);
        }

        return new CubicBezierLineSegment(transformedPoints);
    }

    /// <inheritdoc/>
    ILineSegment ILineSegment.Transform(Matrix3x2 matrix) => this.Transform(matrix);

    private static PointF[] GetDrawingPoints(PointF[] controlPoints)
    {
        List<PointF> drawingPoints = [];
        int curveCount = (controlPoints.Length - 1) / 3;

        for (int curveIndex = 0; curveIndex < curveCount; curveIndex++)
        {
            List<PointF> bezierCurveDrawingPoints = FindDrawingPoints(curveIndex, controlPoints);

            if (curveIndex != 0)
            {
                // remove the fist point, as it coincides with the last point of the previous Bezier curve.
                bezierCurveDrawingPoints.RemoveAt(0);
            }

            drawingPoints.AddRange(bezierCurveDrawingPoints);
        }

        return drawingPoints.ToArray();
    }

    private static List<PointF> FindDrawingPoints(int curveIndex, PointF[] controlPoints)
    {
        List<PointF> pointList = [];

        Vector2 left = CalculateBezierPoint(curveIndex, 0, controlPoints);
        Vector2 right = CalculateBezierPoint(curveIndex, 1, controlPoints);

        pointList.Add(left);
        pointList.Add(right);

        FindDrawingPoints(curveIndex, 0, 1, pointList, 1, controlPoints, 0);

        return pointList;
    }

    private static int FindDrawingPoints(
        int curveIndex,
        float t0,
        float t1,
        List<PointF> pointList,
        int insertionIndex,
        PointF[] controlPoints,
        int depth)
    {
        // max recursive depth for control points, means this is approx the max number of points discoverable
        if (depth > 999)
        {
            return 0;
        }

        Vector2 left = CalculateBezierPoint(curveIndex, t0, controlPoints);
        Vector2 right = CalculateBezierPoint(curveIndex, t1, controlPoints);

        if ((left - right).LengthSquared() < MinimumSqrDistance)
        {
            return 0;
        }

        float midT = (t0 + t1) / 2;
        Vector2 mid = CalculateBezierPoint(curveIndex, midT, controlPoints);

        Vector2 leftDirection = Vector2.Normalize(left - mid);
        Vector2 rightDirection = Vector2.Normalize(right - mid);

        if (Vector2.Dot(leftDirection, rightDirection) > DivisionThreshold || Math.Abs(midT - 0.5f) < 0.0001f)
        {
            int pointsAddedCount = 0;

            pointsAddedCount += FindDrawingPoints(curveIndex, t0, midT, pointList, insertionIndex, controlPoints, depth + 1);
            pointList.Insert(insertionIndex + pointsAddedCount, mid);
            pointsAddedCount++;
            pointsAddedCount += FindDrawingPoints(curveIndex, midT, t1, pointList, insertionIndex + pointsAddedCount, controlPoints, depth + 1);

            return pointsAddedCount;
        }

        return 0;
    }

    private static PointF CalculateBezierPoint(int curveIndex, float t, PointF[] controlPoints)
    {
        int nodeIndex = curveIndex * 3;

        Vector2 p0 = controlPoints[nodeIndex];
        Vector2 p1 = controlPoints[nodeIndex + 1];
        Vector2 p2 = controlPoints[nodeIndex + 2];
        Vector2 p3 = controlPoints[nodeIndex + 3];

        return CalculateBezierPoint(t, p0, p1, p2, p3);
    }

    /// <summary>
    /// Calculates the bezier point along the line.
    /// </summary>
    /// <param name="t">The position within the line.</param>
    /// <param name="p0">The p 0.</param>
    /// <param name="p1">The p 1.</param>
    /// <param name="p2">The p 2.</param>
    /// <param name="p3">The p 3.</param>
    /// <returns>
    /// The <see cref="Vector2"/>.
    /// </returns>
    private static Vector2 CalculateBezierPoint(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector2 p = uuu * p0; // first term

        p += 3 * uu * t * p1; // second term
        p += 3 * u * tt * p2; // third term
        p += ttt * p3; // fourth term

        return p;
    }
}
