// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Internal logic for integrating linear paths.
/// </summary>
internal class InternalPath
{
    /// <summary>
    /// The epsilon for float comparison
    /// </summary>
    private const float Epsilon = 0.003f;
    private const float Epsilon2 = 0.2f;

    /// <summary>
    /// The maximum vector
    /// </summary>
    private static readonly Vector2 MaxVector = new Vector2(float.MaxValue);

    /// <summary>
    /// The points.
    /// </summary>
    private readonly PointData[] points;

    /// <summary>
    /// The closed path.
    /// </summary>
    private readonly bool closedPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalPath"/> class.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
    /// <param name="removeCloseAndCollinear">Whether to remove close and collinear vertices</param>
    internal InternalPath(IReadOnlyList<ILineSegment> segments, bool isClosedPath, bool removeCloseAndCollinear = true)
        : this(Simplify(segments, isClosedPath, removeCloseAndCollinear), isClosedPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalPath" /> class.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
    internal InternalPath(ILineSegment segment, bool isClosedPath)
        : this(segment?.Flatten() ?? Array.Empty<PointF>(), isClosedPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalPath" /> class.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
    internal InternalPath(ReadOnlyMemory<PointF> points, bool isClosedPath)
        : this(Simplify(points.Span, isClosedPath, true), isClosedPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalPath" /> class.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
    private InternalPath(PointData[] points, bool isClosedPath)
    {
        this.points = points;
        this.closedPath = isClosedPath;

        if (this.points.Length > 0)
        {
            float minX, minY, maxX, maxY, length;
            length = 0;
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;

            foreach (var point in this.points)
            {
                length += point.Length;
                minX = Math.Min(point.Point.X, minX);
                minY = Math.Min(point.Point.Y, minY);
                maxX = Math.Max(point.Point.X, maxX);
                maxY = Math.Max(point.Point.Y, maxY);
            }

            this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            this.Length = length;
        }
        else
        {
            this.Bounds = RectangleF.Empty;
            this.Length = 0;
        }
    }

    /// <summary>
    /// Gets the bounds.
    /// </summary>
    /// <value>
    /// The bounds.
    /// </value>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the length.
    /// </summary>
    /// <value>
    /// The length.
    /// </value>
    public float Length { get; }

    /// <summary>
    /// Gets the length.
    /// </summary>
    public int PointCount => this.points.Length;

    /// <summary>
    /// Gets the points.
    /// </summary>
    /// <returns>The <see cref="IReadOnlyCollection{PointF}"/></returns>
    internal ReadOnlyMemory<PointF> Points() => this.points.Select(x => x.Point).ToArray();

    /// <summary>
    /// Calculates the point a certain distance a path.
    /// </summary>
    /// <param name="distanceAlongPath">The distance along the path to find details of.</param>
    /// <returns>
    /// Returns details about a point along a path.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if no points found.</exception>
    internal SegmentInfo PointAlongPath(float distanceAlongPath)
    {
        int pointCount = this.PointCount;
        if (this.closedPath)
        {
            // Move the distance back to the beginning since this is a closed polygon.
            distanceAlongPath %= this.Length;
            pointCount--;
        }

        for (int i = 0; i < pointCount; i++)
        {
            int next = WrapArrayIndex(i + 1, this.PointCount);
            if (distanceAlongPath < this.points[next].Length)
            {
                float t = distanceAlongPath / this.points[next].Length;
                var point = Vector2.Lerp(this.points[i].Point, this.points[next].Point, t);
                Vector2 diff = this.points[i].Point - this.points[next].Point;

                return new SegmentInfo
                {
                    Point = point,
                    Angle = (float)(Math.Atan2(diff.Y, diff.X) % (Math.PI * 2))
                };
            }

            distanceAlongPath -= this.points[next].Length;
        }

        // Closed paths will never reach this point.
        // For open paths we're going to create a new virtual point that extends past the path.
        // The position and angle for that point are calculated based upon the last two points.
        PointF a = this.points[Math.Max(this.points.Length - 2, 0)].Point;
        PointF b = this.points[this.points.Length - 1].Point;
        Vector2 delta = a - b;
        float angle = (float)(Math.Atan2(delta.Y, delta.X) % (Math.PI * 2));

        Matrix3x2 transform = Matrix3x2.CreateRotation(angle - MathF.PI) * Matrix3x2.CreateTranslation(b.X, b.Y);

        return new SegmentInfo
        {
            Point = Vector2.Transform(new Vector2(distanceAlongPath, 0), transform),
            Angle = angle
        };
    }

    internal IMemoryOwner<PointF> ExtractVertices(MemoryAllocator allocator)
    {
        IMemoryOwner<PointF> buffer = allocator.Allocate<PointF>(this.points.Length + 1);
        Span<PointF> span = buffer.Memory.Span;

        for (int i = 0; i < this.points.Length; i++)
        {
            span[i] = this.points[i].Point;
        }

        return buffer;
    }

    // Modulo is a very slow operation.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WrapArrayIndex(int i, int arrayLength) => i < arrayLength ? i : i - arrayLength;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointOrientation CalculateOrientation(Vector2 p, Vector2 q, Vector2 r)
    {
        // See http://www.geeksforgeeks.org/orientation-3-ordered-points/
        // for details of below formula.
        Vector2 qp = q - p;
        Vector2 rq = r - q;
        float val = (qp.Y * rq.X) - (qp.X * rq.Y);

        if (val is > -Epsilon and < Epsilon)
        {
            return PointOrientation.Collinear;  // colinear
        }

        return (val > 0) ? PointOrientation.Clockwise : PointOrientation.Counterclockwise; // clock or counterclock wise
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointOrientation CalculateOrientation(Vector2 qp, Vector2 rq)
    {
        // See http://www.geeksforgeeks.org/orientation-3-ordered-points/
        // for details of below formula.
        float val = (qp.Y * rq.X) - (qp.X * rq.Y);

        if (val > -Epsilon && val < Epsilon)
        {
            return PointOrientation.Collinear;  // colinear
        }

        return (val > 0) ? PointOrientation.Clockwise : PointOrientation.Counterclockwise; // clock or counterclock wise
    }

    /// <summary>
    /// Simplifies the collection of segments.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="isClosed">Weather the path is closed or open.</param>
    /// <param name="removeCloseAndCollinear">Whether to remove close and collinear vertices</param>
    /// <returns>
    /// The <see cref="T:PointData[]"/>.
    /// </returns>
    private static PointData[] Simplify(IReadOnlyList<ILineSegment> segments, bool isClosed, bool removeCloseAndCollinear)
    {
        List<PointF> simplified = new(segments.Count);

        foreach (ILineSegment seg in segments)
        {
            ReadOnlyMemory<PointF> points = seg.Flatten();
            simplified.AddRange(points.Span);
        }

        return Simplify(CollectionsMarshal.AsSpan(simplified), isClosed, removeCloseAndCollinear);
    }

    private static PointData[] Simplify(ReadOnlySpan<PointF> points, bool isClosed, bool removeCloseAndCollinear)
    {
        int polyCorners = points.Length;
        if (polyCorners == 0)
        {
            return [];
        }

        List<PointData> results = new(polyCorners);
        Vector2 lastPoint = points[0];

        if (!isClosed)
        {
            results.Add(new PointData
            {
                Point = points[0],
                Orientation = PointOrientation.Collinear,
                Length = 0
            });
        }
        else
        {
            int prev = polyCorners;
            do
            {
                prev--;
                if (prev == 0)
                {
                    // All points are common, shouldn't match anything
                    results.Add(
                        new PointData
                        {
                            Point = points[0],
                            Orientation = PointOrientation.Collinear,
                            Length = 0,
                        });

                    return [.. results];
                }
            }
            while (removeCloseAndCollinear && points[0].Equivalent(points[prev], Epsilon2)); // skip points too close together

            polyCorners = prev + 1;
            lastPoint = points[prev];

            results.Add(
                new PointData
                {
                    Point = points[0],
                    Orientation = CalculateOrientation(lastPoint, points[0], points[1]),
                    Length = Vector2.Distance(lastPoint, points[0]),
                });

            lastPoint = points[0];
        }

        for (int i = 1; i < polyCorners; i++)
        {
            int next = WrapArrayIndex(i + 1, polyCorners);
            PointOrientation or = CalculateOrientation(lastPoint, points[i], points[next]);
            if (or == PointOrientation.Collinear && next != 0)
            {
                continue;
            }

            results.Add(
                new PointData
                {
                    Point = points[i],
                    Orientation = or,
                    Length = Vector2.Distance(lastPoint, points[i]),
                });
            lastPoint = points[i];
        }

        if (isClosed && removeCloseAndCollinear)
        {
            // walk back removing collinear points
            while (results.Count > 2 && results[^1].Orientation == PointOrientation.Collinear)
            {
                results.RemoveAt(results.Count - 1);
            }
        }

        return [.. results];
    }

    private struct PointData
    {
        public PointF Point;
        public PointOrientation Orientation;
        public float Length;
    }
}
