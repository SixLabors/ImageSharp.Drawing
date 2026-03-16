// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Backend-neutral prepared geometry emitted by command preparation.
/// </summary>
/// <remarks>
/// This is intentionally line-centric rather than path-centric: transforms, clipping,
/// stroke expansion, flattening, and contour orientation have already been resolved.
/// Backends can now build their own raster-specific edge structures without touching
/// <see cref="IPath"/> again.
/// </remarks>
public sealed class PreparedGeometry
{
    private readonly PreparedLineSegment[] segments;

    private PreparedGeometry(PreparedLineSegment[] segments, RectangleF bounds)
    {
        this.segments = segments;
        this.Bounds = bounds;
    }

    /// <summary>
    /// Gets the shared empty prepared geometry instance.
    /// </summary>
    public static PreparedGeometry Empty { get; } = new([], RectangleF.Empty);

    /// <summary>
    /// Gets the prepared line segments.
    /// </summary>
    public ReadOnlySpan<PreparedLineSegment> Segments => this.segments;

    /// <summary>
    /// Gets the total prepared line segment count.
    /// </summary>
    public int SegmentCount => this.segments.Length;

    /// <summary>
    /// Gets the world-space bounds of the prepared geometry.
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Creates prepared geometry from an arbitrary path by flattening all contours into line segments.
    /// </summary>
    /// <param name="path">The source path in world-space coordinates.</param>
    /// <param name="enforceFillOrientation">
    /// When <see langword="true"/>, closed contours are normalized to match fill-orientation expectations.
    /// </param>
    /// <returns>The prepared geometry.</returns>
    public static PreparedGeometry Create(IPath path, bool enforceFillOrientation = true)
    {
        List<(PointF[] Points, bool IsClosed)>? subPaths = null;
        bool allClosed = true;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        int totalSegments = 0;

        foreach (ISimplePath sp in path.Flatten())
        {
            ReadOnlySpan<PointF> srcPoints = sp.Points.Span;
            if (srcPoints.Length < 2)
            {
                continue;
            }

            PointF[] dstPoints = new PointF[srcPoints.Length];
            srcPoints.CopyTo(dstPoints);

            float spMinX = float.MaxValue;
            float spMinY = float.MaxValue;
            float spMaxX = float.MinValue;
            float spMaxY = float.MinValue;

            for (int i = 0; i < dstPoints.Length; i++)
            {
                PointF p = dstPoints[i];
                if (p.X < spMinX)
                {
                    spMinX = p.X;
                }

                if (p.Y < spMinY)
                {
                    spMinY = p.Y;
                }

                if (p.X > spMaxX)
                {
                    spMaxX = p.X;
                }

                if (p.Y > spMaxY)
                {
                    spMaxY = p.Y;
                }
            }

            subPaths ??= [];
            subPaths.Add((dstPoints, sp.IsClosed));
            allClosed &= sp.IsClosed;
            totalSegments += sp.IsClosed ? dstPoints.Length : dstPoints.Length - 1;

            if (spMinX < minX)
            {
                minX = spMinX;
            }

            if (spMinY < minY)
            {
                minY = spMinY;
            }

            if (spMaxX > maxX)
            {
                maxX = spMaxX;
            }

            if (spMaxY > maxY)
            {
                maxY = spMaxY;
            }
        }

        if (subPaths is null || totalSegments == 0)
        {
            return Empty;
        }

        if (allClosed && enforceFillOrientation)
        {
            for (int i = 0; i < subPaths.Count; i++)
            {
                PolygonUtilities.EnsureOrientation(subPaths[i].Points, i == 0 ? 1 : -1);
            }
        }

        PreparedLineSegment[] segments = new PreparedLineSegment[totalSegments];
        int writeIndex = 0;
        for (int i = 0; i < subPaths.Count; i++)
        {
            (PointF[] points, bool isClosed) = subPaths[i];
            int segmentCount = isClosed ? points.Length : points.Length - 1;
            for (int j = 0; j < segmentCount; j++)
            {
                PointF p0 = points[j];
                PointF p1 = points[j + 1 == points.Length ? 0 : j + 1];
                segments[writeIndex++] = new PreparedLineSegment(p0, p1);
            }
        }

        return new PreparedGeometry(
            segments,
            new RectangleF(minX, minY, maxX - minX, maxY - minY));
    }
}
