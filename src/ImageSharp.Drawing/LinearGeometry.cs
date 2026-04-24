// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

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

    internal ReadOnlySpan<LinearContour> GetContours() => this.contours;

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
        int nonHorizontalBoundary = 0;
        int nonHorizontalCenter = 0;
        for (int i = 0; i < segmentCount; i++)
        {
            PointF start = retained[i];
            PointF end = retained[i + 1];
            if ((int)MathF.Floor(start.Y) != (int)MathF.Floor(end.Y))
            {
                nonHorizontalBoundary++;
            }

            if ((int)MathF.Floor(start.Y + 0.5F) != (int)MathF.Floor(end.Y + 0.5F))
            {
                nonHorizontalCenter++;
            }
        }

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = bounds,
                ContourCount = 1,
                PointCount = retained.Length,
                SegmentCount = segmentCount,
                NonHorizontalSegmentCountPixelBoundary = nonHorizontalBoundary,
                NonHorizontalSegmentCountPixelCenter = nonHorizontalCenter
            },
            [new LinearContour
            {
                PointStart = 0,
                PointCount = retained.Length,
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

    private static RectangleF GetPointBounds(PointF[] points)
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
}
