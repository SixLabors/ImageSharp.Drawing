// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGeometry"/> class.
    /// </summary>
    /// <param name="info">The geometry metadata.</param>
    /// <param name="contours">The contour metadata.</param>
    /// <param name="points">The point storage.</param>
    public LinearGeometry(LinearGeometryInfo info, IReadOnlyList<LinearContour> contours, IReadOnlyList<PointF> points)
    {
        this.Info = info;
        this.Contours = contours ?? throw new ArgumentNullException(nameof(contours));
        this.Points = points ?? throw new ArgumentNullException(nameof(points));
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
    /// Creates retained geometry for one open two-point polyline.
    /// </summary>
    /// <param name="start">The first point.</param>
    /// <param name="end">The second point.</param>
    /// <returns>The retained open polyline geometry.</returns>
    public static LinearGeometry CreateOpenPolyline(PointF start, PointF end)
        => CreateOpenPolyline([start, end]);

    /// <summary>
    /// Creates retained geometry for one open polyline.
    /// </summary>
    /// <param name="points">The polyline points.</param>
    /// <returns>The retained open polyline geometry.</returns>
    public static LinearGeometry CreateOpenPolyline(PointF[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Open polylines require at least two points.");
        }

        RectangleF bounds = GetPointBounds(points);
        int segmentCount = points.Length - 1;
        int nonHorizontalBoundary = 0;
        int nonHorizontalCenter = 0;
        for (int i = 0; i < segmentCount; i++)
        {
            PointF start = points[i];
            PointF end = points[i + 1];
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
                PointCount = points.Length,
                SegmentCount = segmentCount,
                NonHorizontalSegmentCountPixelBoundary = nonHorizontalBoundary,
                NonHorizontalSegmentCountPixelCenter = nonHorizontalCenter
            },
            [new LinearContour
            {
                PointStart = 0,
                PointCount = points.Length,
                SegmentStart = 0,
                SegmentCount = segmentCount,
                IsClosed = false
            }
            ],
            points);
    }

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
