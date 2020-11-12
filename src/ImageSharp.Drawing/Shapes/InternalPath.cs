// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Utilities;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing
{
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
        internal InternalPath(IReadOnlyList<ILineSegment> segments, bool isClosedPath)
            : this(Simplify(segments, isClosedPath, true), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(IReadOnlyList<ILineSegment> segments, bool isClosedPath, bool removeCloseAndCollinear)
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
            : this(Simplify(points, isClosedPath, true), isClosedPath)
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
                float minX = this.points.Min(x => x.Point.X);
                float maxX = this.points.Max(x => x.Point.X);
                float minY = this.points.Min(x => x.Point.Y);
                float maxY = this.points.Max(x => x.Point.Y);

                this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
                this.Length = this.points.Sum(x => x.Length);
            }
            else
            {
                this.Bounds = RectangleF.Empty;
                this.Length = 0;
            }
        }

        /// <summary>
        /// the orrientateion of an point form a line
        /// </summary>
        internal enum PointOrientation
        {
            /// <summary>
            /// Point is colienear
            /// </summary>
            Colinear = 0,

            /// <summary>
            /// Its clockwise
            /// </summary>
            Clockwise = 1,

            /// <summary>
            /// Its counter clockwise
            /// </summary>
            Counterclockwise = 2
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
        /// Calculates the distance from the path.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>Returns the distance from the path</returns>
        public PointInfo DistanceFromPath(PointF point)
        {
            PointInfoInternal internalInfo = default;
            internalInfo.DistanceSquared = float.MaxValue; // Set it to max so that CalculateShorterDistance can reduce it back down

            int polyCorners = this.points.Length;

            if (!this.closedPath)
            {
                polyCorners -= 1;
            }

            int closestPoint = 0;
            for (int i = 0; i < polyCorners; i++)
            {
                int next = i + 1;
                if (this.closedPath && next == polyCorners)
                {
                    next = 0;
                }

                if (this.CalculateShorterDistance(this.points[i].Point, this.points[next].Point, point, ref internalInfo))
                {
                    closestPoint = i;
                }
            }

            return new PointInfo
            {
                DistanceAlongPath = this.points[closestPoint].TotalLength + Vector2.Distance(this.points[closestPoint].Point, internalInfo.PointOnLine),
                DistanceFromPath = MathF.Sqrt(internalInfo.DistanceSquared),
                SearchPoint = point,
                ClosestPointOnPath = internalInfo.PointOnLine
            };
        }

        /// <summary>
        /// Based on a line described by <paramref name="start" /> and <paramref name="end" />
        /// populates a buffer for all points on the path that the line intersects.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="buffer">The buffer.</param>
        /// <returns>number of intersections hit</returns>
        public int FindIntersections(PointF start, PointF end, Span<PointF> buffer)
            => this.FindIntersections(start, end, buffer, IntersectionRule.OddEven);

        /// <summary>
        /// Based on a line described by <paramref name="start" /> and <paramref name="end" />
        /// populates a buffer for all points on the path that the line intersects.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="buffer">The buffer.</param>
        /// <param name="intersectionRule">Intersection rule types</param>
        /// <returns>number of intersections hit</returns>
        public int FindIntersections(PointF start, PointF end, Span<PointF> buffer, IntersectionRule intersectionRule)
        {
            PointOrientation[] orientations = ArrayPool<PointOrientation>.Shared.Rent(buffer.Length);
            try
            {
                Span<PointOrientation> orientationsSpan = orientations.AsSpan(0, buffer.Length);
                var position = this.FindIntersectionsWithOrientation(start, end, buffer, orientationsSpan);

                var activeBuffer = buffer.Slice(0, position);
                var activeOrientationsSpan = orientationsSpan.Slice(0, position);

                // intersection rules only really apply to closed paths
                if (intersectionRule == IntersectionRule.Nonzero && this.closedPath)
                {
                    position = ApplyNonZeroIntersectionRules(activeBuffer, activeOrientationsSpan);
                }

                return position;
            }
            finally
            {
                ArrayPool<PointOrientation>.Shared.Return(orientations);
            }
        }

        /// <summary>
        /// Based on a line described by <paramref name="start" /> and <paramref name="end" />
        /// populates a buffer for all points on the path that the line intersects.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="buffer">The buffer.</param>
        /// <param name="orientationsSpan">The buffer for storeing the orientation of each intersection.</param>
        /// <returns>number of intersections hit</returns>
        public int FindIntersectionsWithOrientation(PointF start, PointF end, Span<PointF> buffer, Span<PointOrientation> orientationsSpan)
        {
            if (this.points.Length < 2)
            {
                return 0;
            }

            int count = buffer.Length;

            this.ClampPoints(ref start, ref end);

            var target = new Segment(start, end);

            int polyCorners = this.points.Length;

            if (!this.closedPath)
            {
                polyCorners -= 1;
            }

            int position = 0;
            Vector2 lastPoint = MaxVector;

            PassPointData[] precaclulate = ArrayPool<PassPointData>.Shared.Rent(this.points.Length);

            Span<PassPointData> precaclulateSpan = precaclulate.AsSpan(0, this.points.Length);

            try
            {
                // pre calculate relative orientations X places ahead and behind
                Vector2 startToEnd = end - start;
                PointOrientation prevOrientation = CalulateOrientation(startToEnd, this.points[polyCorners - 1].Point - end);
                PointOrientation nextOrientation = CalulateOrientation(startToEnd, this.points[0].Point - end);
                PointOrientation nextPlus1Orientation = CalulateOrientation(startToEnd, this.points[1].Point - end);

                // iterate over all points and precalculate data about each, pre cacluating it relative orientation
                for (int i = 0; i < polyCorners && count > 0; i++)
                {
                    ref Segment edge = ref this.points[i].Segment;

                    // shift all orientations along but one place and fill in the last one
                    PointOrientation pointOrientation = nextOrientation;
                    nextOrientation = nextPlus1Orientation;
                    nextPlus1Orientation = CalulateOrientation(startToEnd, this.points[WrapArrayIndex(i + 2, this.points.Length)].Point - end);

                    // should this point cause the last matched point to be excluded
                    bool removeLastIntersection = nextOrientation == PointOrientation.Colinear &&
                                                  pointOrientation == PointOrientation.Colinear &&
                                                  nextPlus1Orientation != prevOrientation &&
                                                  (this.closedPath || i > 0) &&
                                                  (IsOnSegment(target, edge.Start) || IsOnSegment(target, edge.End));

                    // is there any chance the segments will intersection (do their bounding boxes touch)
                    bool doIntersect = false;
                    if (pointOrientation == PointOrientation.Colinear || pointOrientation != nextOrientation)
                    {
                        doIntersect = (edge.Min.X - Epsilon) <= target.Max.X &&
                                      (edge.Max.X + Epsilon) >= target.Min.X &&
                                      (edge.Min.Y - Epsilon) <= target.Max.Y &&
                                      (edge.Max.Y + Epsilon) >= target.Min.Y;
                    }

                    precaclulateSpan[i] = new PassPointData
                    {
                        RemoveLastIntersectionAndSkip = removeLastIntersection,
                        RelativeOrientation = pointOrientation,
                        DoIntersect = doIntersect
                    };

                    prevOrientation = pointOrientation;
                }

                // seed the last point for deduping at begining of closed line
                if (this.closedPath)
                {
                    int prev = polyCorners - 1;

                    if (precaclulateSpan[prev].DoIntersect)
                    {
                        lastPoint = FindIntersection(this.points[prev].Segment, target);
                    }
                }

                for (int i = 0; i < polyCorners && count > 0; i++)
                {
                    int next = WrapArrayIndex(i + 1, this.points.Length);

                    if (precaclulateSpan[i].RemoveLastIntersectionAndSkip)
                    {
                        if (position > 0)
                        {
                            position--;
                            count++;
                        }

                        continue;
                    }

                    if (precaclulateSpan[i].DoIntersect)
                    {
                        Vector2 point = FindIntersection(this.points[i].Segment, target);
                        if (point != MaxVector)
                        {
                            if (lastPoint.Equivalent(point, Epsilon2))
                            {
                                lastPoint = MaxVector;

                                int last = WrapArrayIndex(i - 1 + polyCorners, polyCorners);

                                // hit the same point a second time do we need to remove the old one if just clipping
                                if (this.points[next].Point.Equivalent(point, Epsilon))
                                {
                                    next = i;
                                }

                                if (this.points[last].Point.Equivalent(point, Epsilon))
                                {
                                    last = i;
                                }

                                PointOrientation side = precaclulateSpan[next].RelativeOrientation;
                                PointOrientation side2 = precaclulateSpan[last].RelativeOrientation;

                                if (side != side2)
                                {
                                    // differnet side we skip adding as we are passing through it
                                    continue;
                                }
                            }

                            // only need to track this during odd non zero rulings
                            orientationsSpan[position] = precaclulateSpan[i].RelativeOrientation;
                            buffer[position] = point;
                            position++;
                            count--;
                        }

                        lastPoint = point;
                    }
                    else
                    {
                        lastPoint = MaxVector;
                    }
                }

                Vector2 startVector = start;
                Span<float> distances = stackalloc float[position];
                for (int i = 0; i < position; i++)
                {
                    distances[i] = Vector2.DistanceSquared(startVector, buffer[i]);
                }

                var activeBuffer = buffer.Slice(0, position);
                var activeOrientationsSpan = orientationsSpan.Slice(0, position);
                SortUtility.Sort(distances, activeBuffer, activeOrientationsSpan);

                return position;
            }
            finally
            {
                ArrayPool<PassPointData>.Shared.Return(precaclulate);
            }
        }

        internal static int ApplyNonZeroIntersectionRules(Span<PointF> buffer, Span<PointOrientation> orientationsSpan)
        {
            int newpositions = 0;
            int tracker = 0;
            int diff = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                bool include = tracker == 0;
                switch (orientationsSpan[i])
                {
                    case PointOrientation.Counterclockwise:
                        diff = 1;
                        break;
                    case PointOrientation.Clockwise:
                        diff = -1;
                        break;
                    case PointOrientation.Colinear:
                    default:
                        diff *= -1;
                        break;
                }

                tracker += diff;

                if (include || tracker == 0)
                {
                    buffer[newpositions] = buffer[i];
                    newpositions++;
                }
            }

            return newpositions;
        }

        /// <summary>
        /// Determines if the specified point is inside or outside the path.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>Returns true if the point is inside the closed path.</returns>
        public bool PointInPolygon(PointF point)
        {
            // You can only be inside a path if its "closed"
            if (!this.closedPath)
            {
                return false;
            }

            if (!this.Bounds.Contains(point))
            {
                return false;
            }

            // if it hit any points then class it as inside
            PointF[] buffer = ArrayPool<PointF>.Shared.Rent(this.points.Length);
            try
            {
                int intersection = this.FindIntersections(point, new Vector2(this.Bounds.Left - 1, this.Bounds.Top - 1), buffer);
                if ((intersection & 1) == 1)
                {
                    return true;
                }

                // check if the point is on an intersection is it is then inside
                for (int i = 0; i < intersection; i++)
                {
                    if (buffer[i].Equivalent(point, Epsilon))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                ArrayPool<PointF>.Shared.Return(buffer);
            }

            return false;
        }

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
        internal SegmentInfo PointAlongPath(float distanceAlongPath)
        {
            distanceAlongPath = distanceAlongPath % this.Length;
            int pointCount = this.PointCount;
            if (this.closedPath)
            {
                pointCount--;
            }

            for (int i = 0; i < pointCount; i++)
            {
                int next = WrapArrayIndex(i + 1, this.PointCount);
                if (distanceAlongPath < this.points[next].Length)
                {
                    float t = distanceAlongPath / this.points[next].Length;
                    Vector2 point = (this.points[i].Point * (1 - t)) + (this.points[next].Point * t);

                    Vector2 diff = this.points[i].Point - this.points[next].Point;

                    return new SegmentInfo
                    {
                        Point = point,
                        Angle = (float)(Math.Atan2(diff.Y, diff.X) % (Math.PI * 2))
                    };
                }
                else
                {
                    distanceAlongPath -= this.points[next].Length;
                }
            }

            throw new InvalidOperationException("should alwys reach a point along the path");
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOnSegment(Vector2 p, Vector2 q, Vector2 r)
        {
            var min = Vector2.Min(p, r);
            var max = Vector2.Max(p, r);

            return (q.X - Epsilon2) <= max.X &&
                    (q.X + Epsilon2) >= min.X &&
                    (q.Y - Epsilon2) <= max.Y &&
                    (q.Y + Epsilon2) >= min.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOnSegment(in Segment seg, Vector2 q)
        {
            return (q.X - Epsilon2) <= seg.Max.X &&
                    (q.X + Epsilon2) >= seg.Min.X &&
                    (q.Y - Epsilon2) <= seg.Max.Y &&
                    (q.Y + Epsilon2) >= seg.Min.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOnSegments(in Segment seg1, in Segment seg2, Vector2 q)
        {
            float t = q.X - Epsilon2;
            if (t > seg1.Max.X || t > seg2.Max.X)
            {
                return false;
            }

            t = q.X + Epsilon2;
            if (t < seg1.Min.X || t < seg2.Min.X)
            {
                return false;
            }

            t = q.Y - Epsilon2;
            if (t > seg1.Max.Y || t > seg2.Max.Y)
            {
                return false;
            }

            t = q.Y + Epsilon2;
            if (t < seg1.Min.Y || t < seg2.Min.Y)
            {
                return false;
            }

            return true;
        }

        // Modulo is a very slow operation.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WrapArrayIndex(int i, int arrayLength)
        {
            return i < arrayLength ? i : i - arrayLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PointOrientation CalulateOrientation(Vector2 p, Vector2 q, Vector2 r)
        {
            // See http://www.geeksforgeeks.org/orientation-3-ordered-points/
            // for details of below formula.
            Vector2 qp = q - p;
            Vector2 rq = r - q;
            float val = (qp.Y * rq.X) - (qp.X * rq.Y);

            if (val > -Epsilon && val < Epsilon)
            {
                return PointOrientation.Colinear;  // colinear
            }

            return (val > 0) ? PointOrientation.Clockwise : PointOrientation.Counterclockwise; // clock or counterclock wise
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PointOrientation CalulateOrientation(Vector2 qp, Vector2 rq)
        {
            // See http://www.geeksforgeeks.org/orientation-3-ordered-points/
            // for details of below formula.
            float val = (qp.Y * rq.X) - (qp.X * rq.Y);

            if (val > -Epsilon && val < Epsilon)
            {
                return PointOrientation.Colinear;  // colinear
            }

            return (val > 0) ? PointOrientation.Clockwise : PointOrientation.Counterclockwise; // clock or counterclock wise
        }

        /// <summary>
        /// Finds the point on line described by <paramref name="source" />
        /// that intersects with line described by <paramref name="target" />.
        /// </summary>
        /// <param name="source">The line1 start.</param>
        /// <param name="target">The target line.</param>
        /// <returns>
        /// The point on the line that it hit
        /// </returns>
        private static Vector2 FindIntersection(in Segment source, in Segment target)
        {
            Vector2 line1Start = source.Start;
            Vector2 line1End = source.End;
            Vector2 line2Start = target.Start;
            Vector2 line2End = target.End;

            // Use double precision for the intermediate calculations, because single precision calculations
            // easily gets over the Epsilon2 threshold for bitmap sizes larger than about 1500.
            // This is still symptom fighting though, and probably the intersection finding algorithm
            // should be looked over in the future (making the segments fat using epsilons doesn't truely fix the
            // robustness problem).
            // Future potential improvement: the precision problem will be reduced if the center of the bitmap is used as origin (0, 0),
            // this will keep coordinates smaller and relatively precision will be larger.
            double x1, y1, x2, y2, x3, y3, x4, y4;
            x1 = line1Start.X;
            y1 = line1Start.Y;
            x2 = line1End.X;
            y2 = line1End.Y;

            x3 = line2Start.X;
            y3 = line2Start.Y;
            x4 = line2End.X;
            y4 = line2End.Y;

            double x12 = x1 - x2;
            double y12 = y1 - y2;
            double x34 = x3 - x4;
            double y34 = y3 - y4;
            double inter = (x12 * y34) - (y12 * x34);

            if (inter > -Epsilon && inter < Epsilon)
            {
                return MaxVector;
            }

            double u = (x1 * y2) - (x2 * y1);
            double v = (x3 * y4) - (x4 * y3);
            double x = ((x34 * u) - (x12 * v)) / inter;
            double y = ((y34 * u) - (y12 * v)) / inter;

            var point = new Vector2((float)x, (float)y);

            if (IsOnSegments(source, target, point))
            {
                return point;
            }

            return MaxVector;
        }

        /// <summary>
        /// Simplifies the collection of segments.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <param name="isClosed">Weather the path is closed or open.</param>
        /// <param name="removeCloseAndCollinear">Whether to remove close and collinear vertices</param>
        /// <returns>
        /// The <see cref="T:Vector2[]"/>.
        /// </returns>
        private static PointData[] Simplify(IReadOnlyList<ILineSegment> segments, bool isClosed, bool removeCloseAndCollinear)
        {
            var simplified = new List<PointF>();

            foreach (ILineSegment seg in segments)
            {
                ReadOnlyMemory<PointF> points = seg.Flatten();
                simplified.AddRange(points.ToArray());
            }

            return Simplify(simplified.ToArray(), isClosed, removeCloseAndCollinear);
        }

        private static PointData[] Simplify(ReadOnlyMemory<PointF> vectors, bool isClosed, bool removeCloseAndCollinear)
        {
            ReadOnlySpan<PointF> points = vectors.Span;

            int polyCorners = points.Length;
            if (polyCorners == 0)
            {
                return Array.Empty<PointData>();
            }

            var results = new List<PointData>();
            Vector2 lastPoint = points[0];

            if (!isClosed)
            {
                results.Add(new PointData
                {
                    Point = points[0],
                    Orientation = PointOrientation.Colinear,
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
                        int next = points.Length == 1 ? 0 : 1;
                        results.Add(
                            new PointData
                            {
                                Point = points[0],
                                Orientation = PointOrientation.Colinear,
                                Segment = new Segment(points[0], points[next]),
                                Length = 0,
                                TotalLength = 0
                            });

                        return results.ToArray();
                    }
                }
                while (removeCloseAndCollinear && points[0].Equivalent(points[prev], Epsilon2)); // skip points too close together

                polyCorners = prev + 1;
                lastPoint = points[prev];

                results.Add(
                    new PointData
                    {
                        Point = points[0],
                        Orientation = CalulateOrientation(lastPoint, points[0], points[1]),
                        Length = Vector2.Distance(lastPoint, points[0]),
                        TotalLength = 0
                    });

                lastPoint = points[0];
            }

            float totalDist = 0;
            for (int i = 1; i < polyCorners; i++)
            {
                int next = WrapArrayIndex(i + 1, polyCorners);
                PointOrientation or = CalulateOrientation(lastPoint, points[i], points[next]);
                if (or == PointOrientation.Colinear && next != 0)
                {
                    continue;
                }

                float dist = Vector2.Distance(lastPoint, points[i]);
                totalDist += dist;
                results.Add(
                    new PointData
                    {
                        Point = points[i],
                        Orientation = or,
                        Length = dist,
                        TotalLength = totalDist
                    });
                lastPoint = points[i];
            }

            if (isClosed && removeCloseAndCollinear)
            {
                // walk back removing collinear points
                while (results.Count > 2 && results.Last().Orientation == PointOrientation.Colinear)
                {
                    results.RemoveAt(results.Count - 1);
                }
            }

            PointData[] data = results.ToArray();
            for (int i = 0; i < data.Length; i++)
            {
                int next = WrapArrayIndex(i + 1, data.Length);
                data[i].Segment = new Segment(data[i].Point, data[next].Point);
            }

            return data;
        }

        private void ClampPoints(ref PointF start, ref PointF end)
        {
            // clean up start and end points
            if (start.X == float.MaxValue)
            {
                start.X = this.Bounds.Right + 1;
            }

            if (start.X == float.MinValue)
            {
                start.X = this.Bounds.Left - 1;
            }

            if (end.X == float.MaxValue)
            {
                end.X = this.Bounds.Right + 1;
            }

            if (end.X == float.MinValue)
            {
                end.X = this.Bounds.Left - 1;
            }

            if (start.Y == float.MaxValue)
            {
                start.Y = this.Bounds.Bottom + 1;
            }

            if (start.Y == float.MinValue)
            {
                start.Y = this.Bounds.Top - 1;
            }

            if (end.Y == float.MaxValue)
            {
                end.Y = this.Bounds.Bottom + 1;
            }

            if (end.Y == float.MinValue)
            {
                end.Y = this.Bounds.Top - 1;
            }
        }

        /// <summary>
        /// Calculate any shorter distances along the path.
        /// </summary>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <param name="point">The current point.</param>
        /// <param name="info">The info.</param>
        /// <returns>
        /// The <see cref="bool"/>.
        /// </returns>
        private bool CalculateShorterDistance(Vector2 start, Vector2 end, Vector2 point, ref PointInfoInternal info)
        {
            Vector2 diffEnds = end - start;

            float lengthSquared = diffEnds.LengthSquared();
            Vector2 diff = point - start;

            Vector2 multiplied = diff * diffEnds;
            float u = (multiplied.X + multiplied.Y) / lengthSquared;

            if (u > 1)
            {
                u = 1;
            }
            else if (u < 0)
            {
                u = 0;
            }

            Vector2 multipliedByU = diffEnds * u;

            Vector2 pointOnLine = start + multipliedByU;

            Vector2 d = pointOnLine - point;

            float dist = d.LengthSquared();

            if (info.DistanceSquared > dist)
            {
                info.DistanceSquared = dist;
                info.PointOnLine = pointOnLine;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Contains information about the current point.
        /// </summary>
        private struct PointInfoInternal
        {
            /// <summary>
            /// The distance squared.
            /// </summary>
            public float DistanceSquared;

            /// <summary>
            /// The point on the current line.
            /// </summary>
            public PointF PointOnLine;
        }

        private struct PointData
        {
            public PointF Point;
            public PointOrientation Orientation;

            public float Length;
            public float TotalLength;
            public Segment Segment;
        }

        private struct PassPointData
        {
            public bool RemoveLastIntersectionAndSkip;
            public PointOrientation RelativeOrientation;
            public bool DoIntersect;
        }

        private readonly struct Segment
        {
            public readonly PointF Start;
            public readonly PointF End;
            public readonly PointF Min;
            public readonly PointF Max;

            public Segment(PointF start, PointF end)
            {
                this.Start = start;
                this.End = end;

                this.Min = Vector2.Min(start, end);
                this.Max = Vector2.Max(start, end);
            }
        }
    }
}
