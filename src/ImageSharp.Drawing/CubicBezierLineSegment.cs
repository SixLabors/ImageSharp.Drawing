// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a line segment that contains a list of control points that will be rendered as a cubic bezier curve.
/// </summary>
/// <seealso cref="ILineSegment" />
public sealed class CubicBezierLineSegment : ILineSegment
{
    // Code for this taken from <see href="http://devmag.org.za/2011/04/05/bzier-curves-a-tutorial/"/>

    /// <summary>
    /// The squared distance between span endpoints below which subdivision stops.
    /// </summary>
    private const float MinimumSqrDistance = 1.75f;

    /// <summary>
    /// The dot-product threshold used to decide whether a span is flat enough. Directions from the
    /// midpoint to each endpoint are nearly opposite (dot close to -1) when the span is straight;
    /// values above this threshold indicate curvature that requires further subdivision.
    /// </summary>
    private const float DivisionThreshold = -.9995f;

    /// <summary>
    /// The bezier control points; the length is a multiple of 3 plus 1.
    /// </summary>
    private readonly PointF[] controlPoints;

    /// <summary>
    /// The most recently flattened point run, keyed by the scale it was baked at.
    /// </summary>
    private FlattenedCache? flattenedCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="CubicBezierLineSegment"/> class.
    /// </summary>
    /// <param name="points">The control points. The length must be a multiple of 3 plus 1 (4, 7, 10...).</param>
    public CubicBezierLineSegment(PointF[] points)
    {
        Guard.NotNull(points, nameof(points));
        Guard.MustBeGreaterThanOrEqualTo(points.Length, 4, nameof(points));
        Guard.IsTrue((points.Length - 1) % 3 == 0, nameof(points), "points must be a multiple of 3 plus 1 long.");
        this.controlPoints = points;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CubicBezierLineSegment"/> class.
    /// </summary>
    /// <param name="start">The start point of the curve.</param>
    /// <param name="controlPoint1">The first control point.</param>
    /// <param name="controlPoint2">The second control point.</param>
    /// <param name="end">The end point of the curve.</param>
    /// <param name="additionalPoints">
    /// Additional points appended after <paramref name="end"/>; each group of three
    /// (two control points and an end point) defines a further cubic span.
    /// </param>
    public CubicBezierLineSegment(PointF start, PointF controlPoint1, PointF controlPoint2, PointF end, params PointF[] additionalPoints)
        : this(new[] { start, controlPoint1, controlPoint2, end }.Concat(additionalPoints))
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
    public RectangleF Bounds => CalculateBounds(this.GetFlattenedPoints(Vector2.One));

    /// <inheritdoc />
    public int LinearVertexCount(Vector2 scale) => this.GetFlattenedPoints(scale).Length;

    /// <inheritdoc />
    public void CopyTo(Span<PointF> destination, bool skipFirstPoint, Vector2 scale)
    {
        PointF[] flattened = this.GetFlattenedPoints(scale);
        int startIndex = skipFirstPoint ? 1 : 0;
        flattened.AsSpan(startIndex).CopyTo(destination);
    }

    /// <summary>
    /// Returns the flattened point run for this curve under <paramref name="scale"/>, computing it on first
    /// request and reusing the cached result for subsequent calls at the same scale.
    /// </summary>
    /// <remarks>
    /// Publication uses <see cref="Volatile.Write{T}(ref T, T)"/> so a concurrent reader either observes
    /// <see langword="null"/> or a fully-constructed entry.
    /// </remarks>
    /// <param name="scale">The X/Y scale at which the curve is flattened.</param>
    /// <returns>The flattened points at the requested scale.</returns>
    private PointF[] GetFlattenedPoints(Vector2 scale)
    {
        FlattenedCache? hit = Volatile.Read(ref this.flattenedCache);
        if (hit is not null && hit.Scale == scale)
        {
            return hit.Points;
        }

        PointF[] baked = FlattenCurve(this.controlPoints, scale);
        Volatile.Write(ref this.flattenedCache, new FlattenedCache(scale, baked));
        return baked;
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

    /// <summary>
    /// Flattens every cubic in <paramref name="controlPoints"/> under the supplied device-space
    /// <paramref name="scale"/> into a single contiguous point run. Subdivision density is evaluated
    /// against the scaled control points so the polyline adapts to rendering scale.
    /// </summary>
    /// <param name="controlPoints">The bezier control points; the length is a multiple of 3 plus 1.</param>
    /// <param name="scale">The X/Y scale at which the curve is flattened.</param>
    /// <returns>The flattened points.</returns>
    private static PointF[] FlattenCurve(PointF[] controlPoints, Vector2 scale)
    {
        int curveCount = (controlPoints.Length - 1) / 3;

        // Flattened points are cached as a retained array, so use the builder to avoid
        // the intermediate collection and copy a list would generate.
        FlattenedPointBuilder output = new(curveCount * 4);

        for (int curveIndex = 0; curveIndex < curveCount; curveIndex++)
        {
            int nodeIndex = curveIndex * 3;
            Vector2 p0 = new(controlPoints[nodeIndex].X * scale.X, controlPoints[nodeIndex].Y * scale.Y);
            Vector2 p1 = new(controlPoints[nodeIndex + 1].X * scale.X, controlPoints[nodeIndex + 1].Y * scale.Y);
            Vector2 p2 = new(controlPoints[nodeIndex + 2].X * scale.X, controlPoints[nodeIndex + 2].Y * scale.Y);
            Vector2 p3 = new(controlPoints[nodeIndex + 3].X * scale.X, controlPoints[nodeIndex + 3].Y * scale.Y);

            if (curveIndex == 0)
            {
                output.Add((PointF)p0);
            }

            SubdivideAndAppend(0F, 1F, p0, p1, p2, p3, ref output, 0);
            output.Add((PointF)p3);
        }

        return output.Detach();
    }

    /// <summary>
    /// Recursively subdivides the scaled cubic segment, appending midpoints in left-to-right order.
    /// </summary>
    /// <param name="t0">The curve parameter at the start of the span.</param>
    /// <param name="t1">The curve parameter at the end of the span.</param>
    /// <param name="p0">The start point of the cubic.</param>
    /// <param name="p1">The first control point of the cubic.</param>
    /// <param name="p2">The second control point of the cubic.</param>
    /// <param name="p3">The end point of the cubic.</param>
    /// <param name="output">The builder receiving the appended points.</param>
    /// <param name="depth">The current recursion depth; used to bound the subdivision.</param>
    private static void SubdivideAndAppend(
        float t0,
        float t1,
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 p3,
        ref FlattenedPointBuilder output,
        int depth)
    {
        if (depth > 999)
        {
            return;
        }

        Vector2 left = CalculateBezierPoint(t0, p0, p1, p2, p3);
        Vector2 right = CalculateBezierPoint(t1, p0, p1, p2, p3);

        if ((left - right).LengthSquared() < MinimumSqrDistance)
        {
            return;
        }

        float midT = (t0 + t1) / 2;
        Vector2 mid = CalculateBezierPoint(midT, p0, p1, p2, p3);

        Vector2 leftDirection = Vector2.Normalize(left - mid);
        Vector2 rightDirection = Vector2.Normalize(right - mid);

        if (Vector2.Dot(leftDirection, rightDirection) > DivisionThreshold || Math.Abs(midT - 0.5f) < 0.0001f)
        {
            SubdivideAndAppend(t0, midT, p0, p1, p2, p3, ref output, depth + 1);
            output.Add((PointF)mid);
            SubdivideAndAppend(midT, t1, p0, p1, p2, p3, ref output, depth + 1);
        }
    }

    /// <summary>
    /// Calculates the bezier point along the line.
    /// </summary>
    /// <param name="t">The position within the line.</param>
    /// <param name="p0">The start point of the cubic.</param>
    /// <param name="p1">The first control point of the cubic.</param>
    /// <param name="p2">The second control point of the cubic.</param>
    /// <param name="p3">The end point of the cubic.</param>
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
    /// <param name="points">The linearized bezier points.</param>
    /// <returns>The axis-aligned bounds enclosing the points.</returns>
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

    /// <summary>
    /// An immutable pairing of a flatten scale with the point run baked at that scale.
    /// </summary>
    /// <param name="scale">The scale the points were baked at.</param>
    /// <param name="points">The baked points.</param>
    private sealed class FlattenedCache(Vector2 scale, PointF[] points)
    {
        /// <summary>
        /// Gets the scale the points were baked at.
        /// </summary>
        public Vector2 Scale { get; } = scale;

        /// <summary>
        /// Gets the baked points.
        /// </summary>
        public PointF[] Points { get; } = points;
    }
}
