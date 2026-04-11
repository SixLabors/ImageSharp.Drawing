// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a line segment that contains a lists of control points that will be rendered as a cubic bezier curve
/// </summary>
/// <seealso cref="ILineSegment" />
public sealed class CubicBezierLineSegment : ILineSegment
{
    // Code for this taken from <see href="http://devmag.org.za/2011/04/05/bzier-curves-a-tutorial/"/>
    private const float MinimumSqrDistance = 1.75f;
    private const float DivisionThreshold = -.9995f;

    private RectangleF? bounds;
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
        : this([start, controlPoint1, controlPoint2, end])
    {
    }

    /// <summary>
    /// Gets the control points.
    /// </summary>
    public IReadOnlyList<PointF> ControlPoints => this.controlPoints;

    /// <inheritdoc/>
    public PointF StartPoint => this.controlPoints[0];

    /// <inheritdoc/>
    public PointF EndPoint => this.controlPoints[^1];

    /// <inheritdoc />
    public RectangleF Bounds => this.bounds ??= CalculateBounds(this.GetLinePoints());

    /// <inheritdoc />
    public int LinearVertexCount => this.GetLinePoints().Length;

    /// <inheritdoc/>
    public ReadOnlyMemory<PointF> Flatten() => this.GetLinePoints();

    /// <inheritdoc />
    public void CopyTo(Span<PointF> destination, bool skipFirstPoint)
    {
        PointF[] linePoints = this.GetLinePoints();
        int startIndex = skipFirstPoint ? 1 : 0;

        linePoints.AsSpan(startIndex).CopyTo(destination);
    }

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
    public CubicBezierLineSegment Transform(Matrix4x4 matrix)
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
    ILineSegment ILineSegment.Transform(Matrix4x4 matrix) => this.Transform(matrix);

    private PointF[] GetLinePoints()
        => this.linePoints ??= GetDrawingPoints(this.controlPoints);

    private static PointF[] GetDrawingPoints(PointF[] controlPoints)
    {
        // Each cubic contributes its end point plus a small number of midpoint inserts in the common case,
        // so 4 points per curve is a cheap baseline that avoids most growth without a sizing prepass.
        int curveCount = (controlPoints.Length - 1) / 3;
        List<PointF> drawingPoints = new(curveCount * 4);

        for (int curveIndex = 0; curveIndex < curveCount; curveIndex++)
        {
            if (curveIndex == 0)
            {
                drawingPoints.Add(CalculateBezierPoint(curveIndex, 0, controlPoints));
            }

            SubdivideAndAppend(curveIndex, 0, 1, controlPoints, drawingPoints, 0);
            drawingPoints.Add(CalculateBezierPoint(curveIndex, 1, controlPoints));
        }

        return [.. drawingPoints];
    }

    /// <summary>
    /// Recursively subdivides a cubic bezier curve segment and appends points in left-to-right order.
    /// Points are appended (not inserted), avoiding O(n) shifts per point.
    /// </summary>
    private static void SubdivideAndAppend(
        int curveIndex,
        float t0,
        float t1,
        PointF[] controlPoints,
        List<PointF> output,
        int depth)
    {
        if (depth > 999)
        {
            return;
        }

        Vector2 left = CalculateBezierPoint(curveIndex, t0, controlPoints);
        Vector2 right = CalculateBezierPoint(curveIndex, t1, controlPoints);

        if ((left - right).LengthSquared() < MinimumSqrDistance)
        {
            return;
        }

        float midT = (t0 + t1) / 2;
        Vector2 mid = CalculateBezierPoint(curveIndex, midT, controlPoints);

        Vector2 leftDirection = Vector2.Normalize(left - mid);
        Vector2 rightDirection = Vector2.Normalize(right - mid);

        if (Vector2.Dot(leftDirection, rightDirection) > DivisionThreshold || Math.Abs(midT - 0.5f) < 0.0001f)
        {
            // Recurse left half, emit midpoint, recurse right half — all in order.
            SubdivideAndAppend(curveIndex, t0, midT, controlPoints, output, depth + 1);
            output.Add(mid);
            SubdivideAndAppend(curveIndex, midT, t1, controlPoints, output, depth + 1);
        }
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

    /// <summary>
    /// Computes the bounds for the cached linearized bezier points.
    /// </summary>
    private static RectangleF CalculateBounds(ReadOnlySpan<PointF> points)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        for (int i = 0; i < points.Length; i++)
        {
            PointF point = points[i];
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }
}
