// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// The legacy path simplification engine. Reduces a segment or point sequence to a simplified vertex list
/// (removing coincident and collinear vertices while preserving user-intended direction reversals) and
/// computes the bounds and total length of the result.
/// </summary>
internal class InternalPath
{
    /// <summary>
    /// The epsilon used for orientation (collinearity) and zero-length vector tests.
    /// </summary>
    private const float Epsilon = 0.003f;

    /// <summary>
    /// The per-axis epsilon used to merge near-coincident vertices during simplification.
    /// </summary>
    private const float Epsilon2 = 0.2f;

    /// <summary>
    /// The simplified vertices together with their orientation and incoming edge length.
    /// </summary>
    private readonly PointData[] points;

    /// <summary>
    /// Materialized points projected from <see cref="points"/>.
    /// </summary>
    private PointF[]? materializedPoints;

    /// <summary>
    /// Whether the path is closed.
    /// </summary>
    private readonly bool closedPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalPath"/> class.
    /// </summary>
    /// <param name="segments">The segments to flatten and simplify.</param>
    /// <param name="isClosedPath">Whether the path is closed.</param>
    /// <param name="removeCloseAndCollinear">Whether to remove close and collinear vertices.</param>
    public InternalPath(IReadOnlyList<ILineSegment> segments, bool isClosedPath, bool removeCloseAndCollinear = true)
        : this(Simplify(segments, isClosedPath, removeCloseAndCollinear), isClosedPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalPath" /> class.
    /// </summary>
    /// <param name="points">The points to simplify.</param>
    /// <param name="isClosedPath">Whether the path is closed.</param>
    public InternalPath(ReadOnlyMemory<PointF> points, bool isClosedPath)
        : this(Simplify(points.Span, isClosedPath, true), isClosedPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalPath" /> class.
    /// </summary>
    /// <param name="points">The simplified vertex data.</param>
    /// <param name="isClosedPath">Whether the path is closed.</param>
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

            foreach (PointData point in this.points)
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
    /// Gets the axis-aligned bounds of the simplified vertices.
    /// </summary>
    /// <value>
    /// The bounds.
    /// </value>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the total length of the simplified path.
    /// </summary>
    /// <value>
    /// The length.
    /// </value>
    public float Length { get; }

    /// <summary>
    /// Gets the number of simplified vertices.
    /// </summary>
    public int PointCount => this.points.Length;

    /// <summary>
    /// Gets the simplified points, materializing and caching them on first use.
    /// </summary>
    /// <returns>The <see cref="ReadOnlyMemory{PointF}"/> of simplified points.</returns>
    public ReadOnlyMemory<PointF> Points() => this.materializedPoints ??= this.CreatePoints();

    /// <summary>
    /// Wraps an index that is at most one length beyond the end back into array range.
    /// </summary>
    /// <param name="i">The candidate index. Must be less than twice <paramref name="arrayLength"/>.</param>
    /// <param name="arrayLength">The array length.</param>
    /// <returns>The wrapped index.</returns>
    // Modulo is a very slow operation.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WrapArrayIndex(int i, int arrayLength) => i < arrayLength ? i : i - arrayLength;

    /// <summary>
    /// Projects the simplified vertex data to a plain point array.
    /// </summary>
    /// <returns>The projected points.</returns>
    private PointF[] CreatePoints()
    {
        PointF[] result = new PointF[this.points.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = this.points[i].Point;
        }

        return result;
    }

    /// <summary>
    /// Calculates the orientation of the ordered point triplet (p, q, r).
    /// </summary>
    /// <param name="p">The first point.</param>
    /// <param name="q">The second (middle) point.</param>
    /// <param name="r">The third point.</param>
    /// <returns>The <see cref="PointOrientation"/> of the triplet.</returns>
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
            return PointOrientation.Collinear;  // collinear
        }

        return (val > 0) ? PointOrientation.Clockwise : PointOrientation.Counterclockwise; // clock or counterclock wise
    }

    /// <summary>
    /// Flattens the collection of segments and simplifies the resulting points.
    /// </summary>
    /// <param name="segments">The segments to flatten.</param>
    /// <param name="isClosed">Whether the path is closed or open.</param>
    /// <param name="removeCloseAndCollinear">Whether to remove close and collinear vertices.</param>
    /// <returns>
    /// The <see cref="T:PointData[]"/>.
    /// </returns>
    private static PointData[] Simplify(IReadOnlyList<ILineSegment> segments, bool isClosed, bool removeCloseAndCollinear)
    {
        // Pre-compute capacity from identity-transform vertex counts to avoid List resizing.
        int totalPoints = 0;
        for (int s = 0; s < segments.Count; s++)
        {
            totalPoints += segments[s].LinearVertexCount(Vector2.One);
        }

        List<PointF> simplified = new(totalPoints);

        // Track indices where collinear direction reversals represent user-intended
        // geometry: interior points of multi-point linear segments, and junction
        // points between two linear segments (e.g. PathBuilder LineTo -> LineTo).
        // Reversals at all other indices (flattened curves, curve junctions) are
        // artifacts and should be removed normally.
        HashSet<int>? linearReversalIndices = null;
        ILineSegment? prevSeg = null;

        foreach (ILineSegment seg in segments)
        {
            int start = simplified.Count;
            int segmentCount = seg.LinearVertexCount(Vector2.One);
            CollectionsMarshal.SetCount(simplified, start + segmentCount);
            Span<PointF> destination = CollectionsMarshal.AsSpan(simplified).Slice(start, segmentCount);
            seg.CopyTo(destination, skipFirstPoint: false, Vector2.One);

            if (seg is LinearLineSegment)
            {
                // Interior points of a multi-point linear segment (e.g. DrawLine with 3+ points).
                if (segmentCount > 2)
                {
                    linearReversalIndices ??= [];
                    for (int i = start + 1; i < start + segmentCount - 1; i++)
                    {
                        _ = linearReversalIndices.Add(i);
                    }
                }

                // Junction between two linear segments (e.g. PathBuilder LineTo -> LineTo).
                if (prevSeg is LinearLineSegment && start > 0)
                {
                    linearReversalIndices ??= [];
                    _ = linearReversalIndices.Add(start);
                }
            }

            prevSeg = seg;
        }

        return Simplify(CollectionsMarshal.AsSpan(simplified), isClosed, removeCloseAndCollinear, linearReversalIndices);
    }

    /// <summary>
    /// Simplifies a point sequence into vertex data annotated with orientation and incoming edge length.
    /// </summary>
    /// <param name="points">The points to simplify.</param>
    /// <param name="isClosed">Whether the path is closed or open.</param>
    /// <param name="removeCloseAndCollinear">Whether to remove close and collinear vertices.</param>
    /// <param name="linearReversalIndices">
    /// Indices whose collinear direction reversals are user-intended and must be preserved.
    /// When <see langword="null"/>, reversals are preserved at every index.
    /// </param>
    /// <returns>
    /// The <see cref="T:PointData[]"/>.
    /// </returns>
    private static PointData[] Simplify(ReadOnlySpan<PointF> points, bool isClosed, bool removeCloseAndCollinear, HashSet<int>? linearReversalIndices = null)
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
            while (removeCloseAndCollinear && Equivalent(points[0], points[prev], Epsilon2)); // skip points too close together

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
            if (removeCloseAndCollinear && or == PointOrientation.Collinear && next != 0)
            {
                // Preserve collinear points that represent a direction reversal (U-turn)
                // within a single segment. E.g. (10,10) -> (90,10) -> (20,10): the middle point
                // is collinear but the stroker needs to see the reversal.
                // Don't preserve reversals at segment boundaries; these arise from joining
                // different path segments (e.g. arc-to-arc) and are not user-intended.
                bool preserve = false;
                if (linearReversalIndices == null || linearReversalIndices.Contains(i))
                {
                    Vector2 incoming = (Vector2)points[i] - lastPoint;
                    Vector2 outgoing = (Vector2)points[next] - (Vector2)points[i];
                    float inLen = incoming.Length();
                    float outLen = outgoing.Length();
                    preserve = inLen > Epsilon && outLen > Epsilon && Vector2.Dot(incoming, outgoing) < 0;
                }

                if (!preserve)
                {
                    continue;
                }
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

    /// <summary>
    /// Determines whether two points are within the specified coordinate threshold of one another.
    /// </summary>
    /// <param name="source1">The first point.</param>
    /// <param name="source2">The second point.</param>
    /// <param name="threshold">The per-axis distance threshold.</param>
    /// <returns>
    /// <see langword="true"/> when both coordinates are within <paramref name="threshold"/>; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Equivalent(PointF source1, PointF source2, float threshold)
    {
        Vector2 abs = Vector2.Abs(source1 - source2);
        return abs.X < threshold && abs.Y < threshold;
    }

    /// <summary>
    /// A simplified vertex together with its derived metadata.
    /// </summary>
    private struct PointData
    {
        /// <summary>
        /// The vertex position.
        /// </summary>
        public PointF Point;

        /// <summary>
        /// The orientation of the triplet formed by the previous vertex, this vertex, and the next vertex.
        /// </summary>
        public PointOrientation Orientation;

        /// <summary>
        /// The length of the edge from the previous vertex to this vertex. Zero for the first vertex of an open path.
        /// </summary>
        public float Length;
    }
}
