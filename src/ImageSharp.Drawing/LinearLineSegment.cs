// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a series of control points that will be joined by straight lines
/// </summary>
/// <seealso cref="ILineSegment" />
public sealed class LinearLineSegment : ILineSegment
{
    /// <summary>
    /// The collection of points.
    /// </summary>
    private readonly PointF[] points;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearLineSegment"/> class.
    /// </summary>
    /// <param name="start">The start.</param>
    /// <param name="end">The end.</param>
    public LinearLineSegment(PointF start, PointF end)
        : this([start, end])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearLineSegment" /> class.
    /// </summary>
    /// <param name="point1">The point1.</param>
    /// <param name="point2">The point2.</param>
    /// <param name="additionalPoints">Additional points</param>
    public LinearLineSegment(PointF point1, PointF point2, params PointF[] additionalPoints)
        : this(new[] { point1, point2 }.Concat(additionalPoints))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearLineSegment"/> class.
    /// </summary>
    /// <param name="points">The points.</param>
    public LinearLineSegment(PointF[] points)
    {
        Guard.NotNull(points, nameof(points));
        Guard.MustBeGreaterThanOrEqualTo(points.Length, 2, nameof(points));
        this.points = points;
        this.Bounds = CalculateBounds(points);
    }

    /// <summary>
    /// Gets the start point.
    /// </summary>
    public PointF StartPoint => this.points[0];

    /// <summary>
    /// Gets the end point.
    /// </summary>
    /// <value>
    /// The end point.
    /// </value>
    public PointF EndPoint => this.points[^1];

    /// <inheritdoc />
    public RectangleF Bounds { get; }

    /// <inheritdoc />
    public int LinearVertexCount(Vector2 scale) => this.points.Length;

    /// <inheritdoc />
    public void CopyTo(Span<PointF> destination, bool skipFirstPoint, Vector2 scale)
    {
        int startIndex = skipFirstPoint ? 1 : 0;
        ReadOnlySpan<PointF> source = this.points.AsSpan(startIndex);

        if (scale == Vector2.One)
        {
            source.CopyTo(destination);
            return;
        }

        for (int i = 0; i < source.Length; i++)
        {
            destination[i] = new PointF(source[i].X * scale.X, source[i].Y * scale.Y);
        }
    }

    /// <summary>
    /// Transforms the current LineSegment using specified matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>
    /// A line segment with the matrix applied to it.
    /// </returns>
    public LinearLineSegment Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            // no transform to apply skip it
            return this;
        }

        PointF[] transformedPoints = new PointF[this.points.Length];

        for (int i = 0; i < this.points.Length; i++)
        {
            transformedPoints[i] = PointF.Transform(this.points[i], matrix);
        }

        return new LinearLineSegment(transformedPoints);
    }

    /// <summary>
    /// Transforms the current LineSegment using specified matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>A line segment with the matrix applied to it.</returns>
    ILineSegment ILineSegment.Transform(Matrix4x4 matrix) => this.Transform(matrix);

    /// <summary>
    /// Computes the bounds for the retained linear point run.
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
