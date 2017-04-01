// <copyright file="InternalPath.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>
namespace SixLabors.Shapes
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Numerics;

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
            : this(segment?.Flatten() ?? Enumerable.Empty<Vector2>(), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(IEnumerable<Vector2> points, bool isClosedPath)
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

            this.Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
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
        public Rectangle Bounds
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
        internal ImmutableArray<Vector2> Points() => this.points.Select(X=>X.Point).ToImmutableArray();

        /// <summary>
        /// Calculates the distance from the path.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>Returns the distance from the path</returns>
        public PointInfo DistanceFromPath(Vector2 point)
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
        public int FindIntersections(Vector2 start, Vector2 end, Vector2[] buffer, int count, int offset)
        {
            int polyCorners = this.points.Length;

            if (!this.closedPath)
            {
                polyCorners -= 1;
            }
            
            int position = 0;
            Vector2 lastPoint = MaxVector;
            if (this.closedPath)
            {
                int prev = polyCorners-1;

                lastPoint = FindIntersection(this.points[prev].Point, this.points[0].Point, start, end);
            }

            int inc = 0;
            int lastCorner = polyCorners-1;
            for (int i = 0; i < polyCorners && count > 0; i ++)
            {
                var next = (i + 1) % this.points.Length;

                if (closedPath && AreColliner(this.points[i].Point, this.points[next].Point, start, end))
                {
                    // lines are colinear and intersect
                    // if this is the case we need to tell if this is an inflection or not
                    var nextSide = Orientation.Colinear;
                    // keep going next untill we are no longer on the line
                    while (nextSide == Orientation.Colinear)
                    {
                        var nextPlus1 = FindNextPoint(polyCorners, next);
                        nextSide = CalulateOrientation (this.points[nextPlus1].Point, this.points[i].Point, this.points[next].Point);
                        if (nextSide == Orientation.Colinear)
                        {
                            //skip a point
                            next = nextPlus1;
                            if (nextPlus1 > next)
                            {
                                inc += nextPlus1 - next;
                            }
                            else
                            {
                                inc++;
                            }
                        }
                    }

                    var prevSide = CalulateOrientation(this.points[lastCorner].Point, this.points[i].Point, this.points[next].Point);
                    if (prevSide != nextSide)
                    {
                        position--;
                        count++;
                        continue;
                    }
                }

                Vector2 point = FindIntersection(this.points[i].Point, this.points[next].Point, start, end);
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

                        var side = CalulateOrientation(this.points[last].Point, start, end);
                        var side2 = CalulateOrientation(this.points[next].Point, start, end);

                        if (side == Orientation.Colinear && side2 == Orientation.Colinear)
                        {
                            position--;
                            count++;
                            continue;
                        }

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
                lastCorner = i;
            }

            return position;
        }

        private int FindNextPoint(int polyCorners, int i)
        {
            int inc1 = 0;
            int nxt = i;

            nxt = i + 1;
            if (this.closedPath && nxt == polyCorners)
            {
                nxt -= polyCorners;
            }

            return nxt;
        }

        /// <summary>
        /// Ares the colliner.
        /// </summary>
        /// <param name="vector21">The vector21.</param>
        /// <param name="vector22">The vector22.</param>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns></returns>
        private bool AreColliner(Vector2 vector21, Vector2 vector22, Vector2 start, Vector2 end)
        {
            return CalulateOrientation(vector21, start, end) == Orientation.Colinear &&
                CalulateOrientation(vector22, start, end) == Orientation.Colinear && 
                DoIntersect(vector21, vector22, start, end);
        }

        /// <summary>
        /// Determines if the specified point is inside or outside the path.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>Returns true if the point is inside the closed path.</returns>
        public bool PointInPolygon(Vector2 point)
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
            var buffer = ArrayPool<Vector2>.Shared.Rent(this.points.Length);
            try
            {
                var intersection = this.FindIntersections(point, new Vector2(this.Bounds.Left - 1, this.Bounds.Top - 1), buffer, this.points.Length, 0);
                if (intersection % 2 == 1)
                {
                    return true;
                }

                // check if the point is on an intersection is it is then inside
                for (var i = 0; i < intersection; i++)
                {
                    if (buffer[i].Equivelent(point, Epsilon))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(buffer);
            }

            return false;
        }

        private static bool DoIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            // Find the four orientations needed for general and
            // special cases
            var o1 = CalulateOrientation(p1, q1, p2);
            var o2 = CalulateOrientation(p1, q1, q2);
            var o3 = CalulateOrientation(p2, q2, p1);
            var o4 = CalulateOrientation(p2, q2, q1);

            // General case
            if (o1 != o2 && o3 != o4)
            {
                return true;
            }

            // Special Cases
            // p1, q1 and p2 are colinear and p2 lies on segment p1q1
            if (o1 == Orientation.Colinear && IsOnSegment(p1, p2, q1))
            {
                return true;
            }

            // p1, q1 and p2 are colinear and q2 lies on segment p1q1
            if (o2 == Orientation.Colinear && IsOnSegment(p1, q2, q1))
            {
                return true;
            }

            // p2, q2 and p1 are colinear and p1 lies on segment p2q2
            if (o3 == Orientation.Colinear && IsOnSegment(p2, p1, q2))
            {
                return true;
            }

            // p2, q2 and q1 are colinear and q1 lies on segment p2q2
            if (o4 == Orientation.Colinear && IsOnSegment(p2, q1, q2))
            {
                return true;
            }

            return false; // Doesn't fall in any of the above cases
        }

        private static bool IsOnSegment(Vector2 p, Vector2 q, Vector2 r)
        {
            return (q.X-Epsilon2) <= Math.Max(p.X, r.X) && (q.X + Epsilon2) >= Math.Min(p.X, r.X) &&
                (q.Y - Epsilon2) <= Math.Max(p.Y, r.Y) && (q.Y + Epsilon2) >= Math.Min(p.Y, r.Y);
        }

        private static Orientation CalulateOrientation(Vector2 p, Vector2 q, Vector2 r)
        {
            // See http://www.geeksforgeeks.org/orientation-3-ordered-points/
            // for details of below formula.
            float val = ((q.Y - p.Y) * (r.X - q.X)) -
                      ((q.X - p.X) * (r.Y - q.Y));

            if (val > -Epsilon && val < Epsilon)
            {
                return Orientation.Colinear;  // colinear
            }

            return (val > 0) ? Orientation.Clockwise : Orientation.Counterclockwise; // clock or counterclock wise
        }

        /// <summary>
        /// Determines if the bounding box for 2 lines
        /// described by <paramref name="line1Start" /> and <paramref name="line1End" />
        /// and  <paramref name="line2Start" /> and <paramref name="line2End" /> overlap.
        /// </summary>
        /// <param name="line1Start">The line1 start.</param>
        /// <param name="line1End">The line1 end.</param>
        /// <param name="line2Start">The line2 start.</param>
        /// <param name="line2End">The line2 end.</param>
        /// <returns>Returns true it the bounding box of the 2 lines intersect</returns>
        private static bool BoundingBoxesIntersect(Vector2 line1Start, Vector2 line1End, Vector2 line2Start, Vector2 line2End)
        {
            Vector2 topLeft1 = Vector2.Min(line1Start, line1End);
            Vector2 bottomRight1 = Vector2.Max(line1Start, line1End);

            Vector2 topLeft2 = Vector2.Min(line2Start, line2End);
            Vector2 bottomRight2 = Vector2.Max(line2Start, line2End);

            float left1 = topLeft1.X - Epsilon;
            float right1 = bottomRight1.X + Epsilon;
            float top1 = topLeft1.Y - Epsilon;
            float bottom1 = bottomRight1.Y + Epsilon;

            float left2 = topLeft2.X - Epsilon;
            float right2 = bottomRight2.X + Epsilon;
            float top2 = topLeft2.Y - Epsilon;
            float bottom2 = bottomRight2.Y + Epsilon;

            return left1 <= right2 && right1 >= left2
                &&
                top1 <= bottom2 && bottom1 >= top2;
        }

        /// <summary>
        /// Finds the point on line described by <paramref name="line1Start" /> and <paramref name="line1End" />
        /// that intersects with line described by <paramref name="line2Start" /> and <paramref name="line2End" />
        /// </summary>
        /// <param name="line1Start">The line1 start.</param>
        /// <param name="line1End">The line1 end.</param>
        /// <param name="line2Start">The line2 start.</param>
        /// <param name="line2End">The line2 end.</param>
        /// <param name="buffer">The buffer.</param>
        /// <returns>
        /// the number of points on the line that it hit
        /// </returns>
        private static Vector2 FindIntersection(Vector2 line1Start, Vector2 line1End, Vector2 line2Start, Vector2 line2End)
        {
            // do bounding boxes overlap, if not then the lines can't and return fast.
            if (!DoIntersect(line1Start, line1End, line2Start, line2End))
            {
                return MaxVector;
            }

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

            return new Vector2(x, y);
        }

        /// <summary>
        /// Simplifies the collection of segments.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <returns>
        /// The <see cref="T:Vector2[]"/>.
        /// </returns>
        private static PointData[] Simplify(IEnumerable<ILineSegment> segments, bool isClosed)
        {
            List<Vector2> simplified = new List<Vector2>();
            foreach (ILineSegment seg in segments)
            {
                simplified.AddRange(seg.Flatten());
            }

            return Simplify(simplified, isClosed);
        }

        private static PointData[] Simplify(IEnumerable<Vector2> vectors, bool isClosed)
        {
            Vector2[] points = vectors.ToArray();
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
            for (var i = 1; i < polyCorners; i++)
            {
                var next = (i + 1) % polyCorners;
                var or = CalulateOrientation(lastPoint, points[i], points[next]);
                if(or == Orientation.Colinear && next != 0)
                {
                    continue;
                }

                var dist = Vector2.Distance(lastPoint, points[i]);
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
            // walk back removing collinear points
            while (results.Count > 2 && results.Last().Orientation == Orientation.Colinear)
            {
                results.RemoveAt(results.Count - 1);
            }
            return results.ToArray();
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
        }
    }
}
