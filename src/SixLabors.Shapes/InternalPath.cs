// <copyright file="InternalPath.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>
namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;

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
        /// The locker.
        /// </summary>
        private static readonly object Locker = new object();

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
        internal InternalPath(IEnumerable<ILineSegment> segments, bool isClosedPath)
            : this(Simplify(segments, isClosedPath), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(ILineSegment segment, bool isClosedPath)
            : this(segment?.Flatten() ?? Enumerable.Empty<PointF>(), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(IEnumerable<PointF> points, bool isClosedPath)
            : this(Simplify(points, isClosedPath), isClosedPath)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        private InternalPath(PointData[] points, bool isClosedPath)
        {
            this.points = points;
            this.closedPath = isClosedPath;

            float minX = this.points.Min(x => x.Point.X);
            float maxX = this.points.Max(x => x.Point.X);
            float minY = this.points.Min(x => x.Point.Y);
            float maxY = this.points.Max(x => x.Point.Y);

            this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            this.Length = this.points.Sum(x => x.Length);
        }

        /// <summary>
        /// the orrientateion of an point form a line
        /// </summary>
        private enum Orientation
        {
            /// <summary>
            /// POint is colienier
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
        public RectangleF Bounds
        {
            get;
        }

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
        /// <value>
        /// The length.
        /// </value>
        public int PointCount => this.points.Length;

        /// <summary>
        /// Gets the points.
        /// </summary>
        /// <value>
        /// The points.
        /// </value>
        internal IReadOnlyList<PointF> Points() => this.points.Select(X => (PointF)X.Point).ToArray();

        /// <summary>
        /// Calculates the distance from the path.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>Returns the distance from the path</returns>
        public PointInfo DistanceFromPath(PointF point)
        {
            PointInfoInternal internalInfo = default(PointInfoInternal);
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
                DistanceFromPath = (float)Math.Sqrt(internalInfo.DistanceSquared),
                SearchPoint = point,
                ClosestPointOnPath = internalInfo.PointOnLine
            };
        }

        internal SegmentInfo PointAlongPath(float distanceAlongPath)
        {
            distanceAlongPath = distanceAlongPath % this.Length;
            int pointCount = this.PointCount;
            if (this.closedPath)
            {
                pointCount--;
            }

            for(int i = 0; i < pointCount; i++)
            {
                int next = (i + 1) % this.PointCount;
                if(distanceAlongPath < this.points[next].Length)
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

        /// <summary>
        /// Based on a line described by <paramref name="start" /> and <paramref name="end" />
        /// populates a buffer for all points on the path that the line intersects.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="buffer">The buffer.</param>
        /// <param name="count">The count.</param>
        /// <param name="offset">The offset.</param>
        /// <returns>number of intersections hit</returns>
        public int FindIntersections(Vector2 start, Vector2 end, Span<PointF> buffer)
        {
            // TODO remove the need for these 2 vars if possible
            int offset = 0;
            int count = buffer.Length;

            if (this.points.Length < 2)
            {
                return 0;
            }

            ClampPoints(ref start, ref end);

            Segment target = new Segment(start, end);

            int polyCorners = this.points.Length;

            if (!this.closedPath)
            {
                polyCorners -= 1;
            }

            int position = 0;
            Vector2 lastPoint = MaxVector;

            PassPointData[] precaclulate = ArrayPool<PassPointData>.Shared.Rent(this.points.Length);

            try
            {
                // pre calculate relative orientations X places ahead and behind
                Orientation prevOrientation = CalulateOrientation(start, end, this.points[polyCorners - 1].Point);
                Orientation nextOrientation = CalulateOrientation(start, end, this.points[0].Point);
                Orientation nextPlus1Orientation = CalulateOrientation(start, end, this.points[1].Point);

                // iterate over all points and precalculate data about each, pre cacluating it relative orientation
                for (int i = 0; i < polyCorners && count > 0; i++)
                {
                    Segment edge = this.points[i].Segment;

                    // shift all orientations along but one place and fill in the last one
                    Orientation pointOrientation = nextOrientation;
                    nextOrientation = nextPlus1Orientation;
                    nextPlus1Orientation = CalulateOrientation(start, end, this.points[(i + 2) % this.points.Length].Point);

                    // should this point cause the last matched point to be excluded
                    bool removeLastIntersection = nextOrientation == Orientation.Colinear &&
                                                  pointOrientation == Orientation.Colinear &&
                                                  nextPlus1Orientation != prevOrientation &&
                                                  (this.closedPath || i > 0) &&
                                                  (IsOnSegment(target, edge.Start) || IsOnSegment(target, edge.End));

                    // is there any chance the segments will intersection (do their bounding boxes touch)
                    bool doIntersect = false;
                    if (pointOrientation == Orientation.Colinear || pointOrientation != nextOrientation)
                    {
                        doIntersect = (edge.Min.X - Epsilon) <= target.Max.X &&
                                      (edge.Max.X + Epsilon) >= target.Min.X &&
                                      (edge.Min.Y - Epsilon) <= target.Max.Y &&
                                      (edge.Max.Y + Epsilon) >= target.Min.Y;
                    }

                    precaclulate[i] = new PassPointData
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

                    if (precaclulate[prev].DoIntersect)
                    {
                        lastPoint = FindIntersection(this.points[prev].Segment, target);
                    }
                }

                for (int i = 0; i < polyCorners && count > 0; i++)
                {
                    int next = (i + 1) % this.points.Length;

                    if (precaclulate[i].RemoveLastIntersectionAndSkip)
                    {
                        if (position > 0)
                        {
                            position--;
                            count++;
                        }
                        continue;
                    }
                    if (precaclulate[i].DoIntersect)
                    {
                        Vector2 point = FindIntersection(this.points[i].Segment, target);
                        if (point != MaxVector)
                        {
                            if (lastPoint.Equivelent(point, Epsilon2))
                            {
                                lastPoint = MaxVector;

                                int last = (i - 1 + polyCorners) % polyCorners;

                                // hit the same point a second time do we need to remove the old one if just clipping
                                if (this.points[next].Point.Equivelent(point, Epsilon))
                                {
                                    next = i;
                                }

                                if (this.points[last].Point.Equivelent(point, Epsilon))
                                {
                                    last = i;
                                }

                                Orientation side = precaclulate[next].RelativeOrientation;
                                Orientation side2 = precaclulate[last].RelativeOrientation;

                                if (side != side2)
                                {
                                    // differnet side we skip adding as we are passing through it
                                    continue;
                                }
                            }

                            // we are not double crossing so just add it once
                            buffer[position + offset] = point;
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

                return position;
            }
            finally
            {
                ArrayPool<PassPointData>.Shared.Return(precaclulate);
            }
        }

        private void ClampPoints(ref Vector2 start, ref Vector2 end)
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
                var bufferSpan = new Span<PointF>(buffer);
                int intersection = this.FindIntersections(point, new Vector2(this.Bounds.Left - 1, this.Bounds.Top - 1), bufferSpan);
                if (intersection % 2 == 1)
                {
                    return true;
                }

                // check if the point is on an intersection is it is then inside
                for (int i = 0; i < intersection; i++)
                {
                    if (buffer[i].Equivelent(point, Epsilon))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOnSegment(Vector2 p, Vector2 q, Vector2 r)
        {
            Vector2 min = Vector2.Min(p, r);
            Vector2 max = Vector2.Max(p, r);

            return (q.X - Epsilon2) <= max.X &&
                    (q.X + Epsilon2) >= min.X &&
                    (q.Y - Epsilon2) <= max.Y &&
                    (q.Y + Epsilon2) >= min.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOnSegment(Segment seg, Vector2 q)
        {
            return (q.X - Epsilon2) <= seg.Max.X &&
                    (q.X + Epsilon2) >= seg.Min.X &&
                    (q.Y - Epsilon2) <= seg.Max.Y &&
                    (q.Y + Epsilon2) >= seg.Min.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Orientation CalulateOrientation(Vector2 p, Vector2 q, Vector2 r)
        {
            // See http://www.geeksforgeeks.org/orientation-3-ordered-points/
            // for details of below formula.
            Vector2 qp = q - p;
            Vector2 rq = r - q;

            float val = (qp.Y * rq.X) - (qp.X * rq.Y);

            if (val > -Epsilon && val < Epsilon)
            {
                return Orientation.Colinear;  // colinear
            }

            return (val > 0) ? Orientation.Clockwise : Orientation.Counterclockwise; // clock or counterclock wise
        }

        /// <summary>
        /// Finds the point on line described by <paramref name="source" /> 
        /// that intersects with line described by <paramref name="target" /> 
        /// </summary>
        /// <param name="source">The line1 start.</param>
        /// <param name="target">The target line.</param>
        /// <returns>
        /// The point on the line that it hit
        /// </returns>
        private static Vector2 FindIntersection(Segment source, Segment target)
        {
            Vector2 line1Start = source.Start;
            Vector2 line1End = source.End;
            Vector2 line2Start = target.Start;
            Vector2 line2End = target.End;

            float x1, y1, x2, y2, x3, y3, x4, y4;
            x1 = line1Start.X;
            y1 = line1Start.Y;
            x2 = line1End.X;
            y2 = line1End.Y;

            x3 = line2Start.X;
            y3 = line2Start.Y;
            x4 = line2End.X;
            y4 = line2End.Y;

            float inter = ((x1 - x2) * (y3 - y4)) - ((y1 - y2) * (x3 - x4));

            if (inter > -Epsilon && inter < Epsilon)
            {
                return MaxVector;
            }

            float x = (((x2 - x1) * ((x3 * y4) - (x4 * y3))) - ((x4 - x3) * ((x1 * y2) - (x2 * y1)))) / inter;
            float y = (((y3 - y4) * ((x1 * y2) - (x2 * y1))) - ((y1 - y2) * ((x3 * y4) - (x4 * y3)))) / inter;

            Vector2 point = new Vector2(x, y);

            if (IsOnSegment(source, point) && IsOnSegment(target, point))
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
        /// <returns>
        /// The <see cref="T:Vector2[]"/>.
        /// </returns>
        private static PointData[] Simplify(IEnumerable<ILineSegment> segments, bool isClosed)
        {
            List<PointF> simplified = new List<PointF>();
            foreach (ILineSegment seg in segments)
            {
                simplified.AddRange(seg.Flatten());
            }

            return Simplify(simplified, isClosed);
        }

        private static PointData[] Simplify(IEnumerable<PointF> vectors, bool isClosed)
        {
            PointF[] points = vectors.ToArray();
            List<PointData> results = new List<PointData>();

            int polyCorners = points.Length;

            Vector2 lastPoint = points[0];

            if (!isClosed)
            {
                results.Add(new PointData
                {
                    Point = points[0],
                    Orientation = Orientation.Colinear,
                    Length = 0
                });
            }
            else
            {
                if (isClosed)
                {
                    int prev = polyCorners;
                    do
                    {
                        prev--;
                        if (prev == 0)
                        {
                            // all points are common, shouldn't match anything
                            results.Add(new PointData
                            {
                                Point = points[0],
                                Orientation = Orientation.Colinear,
                                Segment = new Segment(points[0], points[1]),
                                Length = 0,
                                TotalLength = 0
                            });
                            return results.ToArray();
                        }
                    }
                    while (points[0].Equivelent(points[prev], Epsilon2)); // skip points too close together
                    polyCorners = prev + 1;
                    lastPoint = points[prev];
                }

                results.Add(new PointData
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
                int next = (i + 1) % polyCorners;
                Orientation or = CalulateOrientation(lastPoint, points[i], points[next]);
                if (or == Orientation.Colinear && next != 0)
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

            if (isClosed)
            {
                // walk back removing collinear points
                while (results.Count > 2 && results.Last().Orientation == Orientation.Colinear)
                {
                    results.RemoveAt(results.Count - 1);
                }
            }

            PointData[] data = results.ToArray();
            for (int i = 0; i< data.Length; i++)
            {
                int next = (i + 1) % data.Length;
                data[i].Segment = new Segment(data[i].Point, data[next].Point);
            }

            return data;
        }

        /// <summary>
        /// Returns the length of the path.
        /// </summary>
        /// <returns>
        /// The <see cref="float"/>.
        /// </returns>
        private float CalculateLength()
        {
            float length = 0;
            int polyCorners = this.points.Length;

            if (!this.closedPath)
            {
                polyCorners -= 1;
            }

            for (int i = 0; i < polyCorners; i++)
            {
                int next = i + 1;
                if (this.closedPath && next == polyCorners)
                {
                    next = 0;
                }

                length += this.points[i].Length;
            }

            return length;
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
            public Vector2 PointOnLine;
        }

        private struct PointData
        {
            public Vector2 Point;
            public Orientation Orientation;

            public float Length;
            public float TotalLength;
            public Segment Segment;
        }

        private struct PassPointData
        {
            public bool RemoveLastIntersectionAndSkip;
            public Orientation RelativeOrientation;
            public bool DoIntersect;
        }

        private struct Segment
        {
            public Vector2 Start;
            public Vector2 End;
            public Vector2 Min;
            public Vector2 Max;

            public Segment(Vector2 start, Vector2 end)
            {
                this.Start = start;
                this.End = end;

                this.Min = Vector2.Min(start, end);
                this.Max = Vector2.Max(start, end);
            }
        }
    }
}
