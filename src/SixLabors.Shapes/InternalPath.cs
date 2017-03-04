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
        private readonly ImmutableArray<Vector2> points;

        /// <summary>
        /// The closed path.
        /// </summary>
        private readonly bool closedPath;

        /// <summary>
        /// The total distance.
        /// </summary>
        private readonly Lazy<float> totalDistance;

        /// <summary>
        /// The distances
        /// </summary>
        private float[] distance;

        /// <summary>
        /// The calculated.
        /// </summary>
        private bool calculated;

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(ILineSegment[] segments, bool isClosedPath)
            : this(ImmutableArray.Create(segments), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(ImmutableArray<ILineSegment> segments, bool isClosedPath)
            : this(Simplify(segments), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(ILineSegment segment, bool isClosedPath)
            : this(segment?.Flatten() ?? ImmutableArray<Vector2>.Empty, isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(ImmutableArray<Vector2> points, bool isClosedPath)
        {
            this.points = points;
            this.closedPath = isClosedPath;

            float minX = this.points.Min(x => x.X);
            float maxX = this.points.Max(x => x.X);
            float minY = this.points.Min(x => x.Y);
            float maxY = this.points.Max(x => x.Y);

            this.Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
            this.totalDistance = new Lazy<float>(this.CalculateLength);
        }

        /// <summary>
        /// The sides a point can land on
        /// </summary>
        public enum Side
        {
            /// <summary>
            /// Indicates the point falls on the left logical side of the line.
            /// </summary>
            Left,

            /// <summary>
            /// Indicates the point falls on the right logical side of the line.
            /// </summary>
            Right,

            /// <summary>
            /// /// Indicates the point falls exactly on the line.
            /// </summary>
            Same
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
        public float Length => this.totalDistance.Value;

        /// <summary>
        /// Gets the points.
        /// </summary>
        /// <value>
        /// The points.
        /// </value>
        internal ImmutableArray<Vector2> Points => this.points;

        /// <summary>
        /// Calculates the distance from the path.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>Returns the distance from the path</returns>
        public PointInfo DistanceFromPath(Vector2 point)
        {
            this.CalculateConstants();

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

                if (this.CalculateShorterDistance(this.points[i], this.points[next], point, ref internalInfo))
                {
                    closestPoint = i;
                }
            }

            return new PointInfo
            {
                DistanceAlongPath = this.distance[closestPoint] + Vector2.Distance(this.points[closestPoint], internalInfo.PointOnLine),
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

            Vector2[] intersectionBuffer = new Vector2[2];
            int position = 0;
            Vector2 lastPoint = MaxVector;
            if (this.closedPath)
            {
                int prev = polyCorners;
                do
                {
                    prev--;
                    if (prev == 0)
                    {
                        // all points are common, shouldn't match anything
                        return 0;
                    }
                }
                while (this.points[0].Equivelent(this.points[prev], Epsilon2)); // skip points too close together

                int hitCount = FindIntersection(this.points[prev], this.points[0], start, end, intersectionBuffer);
                if (hitCount > 0)
                {
                    lastPoint = intersectionBuffer[hitCount - 1];
                }

                polyCorners = prev + 1;
            }

            int inc = 0;
            int lastCorner = polyCorners-1;
            for (int i = 0; i < polyCorners && count > 0; i += inc)
            {
                int next = FindNextPoint(polyCorners, i);
                if (next > i)
                {
                    inc = next - i;
                }else
                {
                    inc = 1;
                }

                if (closedPath && AreColliner(this.points[i], this.points[next], start, end))
                {
                    // lines are colinear and intersect
                    // if this is the case we need to tell if this is an inflection or not
                    var nextSide = Side.Same;
                    // keep going next untill we are no longer on the line
                    while (nextSide == Side.Same)
                    {
                        var nextPlus1 = FindNextPoint(polyCorners, next);
                        nextSide = SideOfLine(this.points[nextPlus1], this.points[i], this.points[next]);
                        if (nextSide == Side.Same)
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

                    var prevSide = SideOfLine(this.points[lastCorner], this.points[i], this.points[next]);
                    if (prevSide != nextSide)
                    {
                        position--;
                        count++;
                        continue;
                    }
                }

                int hitCount = FindIntersection(this.points[i], this.points[next], start, end, intersectionBuffer);
                if (hitCount > 0)
                {
                    for (var p = 0; p < hitCount; p++)
                    {
                        var point = intersectionBuffer[p];
                        if (point != MaxVector)
                        {
                            if (lastPoint.Equivelent(point, Epsilon2))
                            {
                                lastPoint = MaxVector;

                                int last = (i - 1 + polyCorners) % polyCorners;

                                // hit the same point a second time do we need to remove the old one if just clipping
                                if (this.points[next].Equivelent(point, Epsilon))
                                {
                                    next = i;
                                }

                                if (this.points[last].Equivelent(point, Epsilon))
                                {
                                    last = i;
                                }

                                var side = SideOfLine(this.points[last], start, end);
                                var side2 = SideOfLine(this.points[next], start, end);

                                if (side == Side.Same && side2 == Side.Same)
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
                            lastPoint = point;
                        }
                    }
                }
                else
                {
                    // no hit we reset the last hit as we are not testing connectlines anymore
                    lastPoint = MaxVector;
                }
                lastCorner = i;
            }

            return position;
        }

        private int FindNextPoint(int polyCorners, int i)
        {
            int inc1 = 0;
            int nxt = i;
            do
            {
                inc1++;
                nxt = i + inc1;
                if (this.closedPath && nxt == polyCorners)
                {
                    nxt -= polyCorners;
                }
            }
            while (this.points[i].Equivelent(this.points[nxt], Epsilon2) && inc1 < polyCorners); // skip points too close together
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
            return SideOfLine(vector21, start, end) == Side.Same && SideOfLine(vector22, start, end) == Side.Same && DoIntersect(vector21, vector22, start, end);
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

        private static Side SideOfLine(Vector2 test, Vector2 lineStart, Vector2 lineEnd)
        {
            var testDiff = test - lineStart;
            var lineDiff = lineEnd - lineStart;
            if (float.IsInfinity(lineDiff.X))
            {
                if (lineDiff.X > 0)
                {
                    lineDiff.X = float.MaxValue;
                }
                else
                {
                    lineDiff.X = float.MinValue;
                }
            }

            if (float.IsInfinity(lineDiff.Y))
            {
                if (lineDiff.Y > 0)
                {
                    lineDiff.Y = float.MaxValue;
                }
                else
                {
                    lineDiff.Y = float.MinValue;
                }
            }

            var crossProduct = (lineDiff.X * testDiff.Y) - (lineDiff.Y * testDiff.X);

            if (crossProduct > -Epsilon && crossProduct < Epsilon)
            {
                return Side.Same;
            }

            if (crossProduct > 0)
            {
                return Side.Left;
            }

            return Side.Right;
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
        private static int FindIntersection(Vector2 line1Start, Vector2 line1End, Vector2 line2Start, Vector2 line2End, Vector2[] buffer)
        {
            // do bounding boxes overlap, if not then the lines can't and return fast.
            if (!DoIntersect(line1Start, line1End, line2Start, line2End))
            {
                return 0;
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
                return 0;
            }

            float x = (((x2 - x1) * ((x3 * y4) - (x4 * y3))) - ((x4 - x3) * ((x1 * y2) - (x2 * y1)))) / inter;
            float y = (((y3 - y4) * ((x1 * y2) - (x2 * y1))) - ((y1 - y2) * ((x3 * y4) - (x4 * y3)))) / inter;

            buffer[0] = new Vector2(x, y);
            return 1;
        }

        /// <summary>
        /// Simplifies the collection of segments.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <returns>
        /// The <see cref="T:Vector2[]"/>.
        /// </returns>
        private static ImmutableArray<Vector2> Simplify(ImmutableArray<ILineSegment> segments)
        {
            List<Vector2> simplified = new List<Vector2>();
            foreach (ILineSegment seg in segments)
            {
                simplified.AddRange(seg.Flatten());
            }

            return simplified.ToImmutableArray();
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

                length += Vector2.Distance(this.points[i], this.points[next]);
            }

            return length;
        }

        /// <summary>
        /// Calculate the constants.
        /// </summary>
        private void CalculateConstants()
        {
            // http://alienryderflex.com/polygon/ source for point in polygon logic
            if (this.calculated)
            {
                return;
            }

            lock (Locker)
            {
                if (this.calculated)
                {
                    return;
                }

                ImmutableArray<Vector2> poly = this.points;
                int polyCorners = poly.Length;
                this.distance = new float[polyCorners];

                this.distance[0] = 0;

                for (int i = 1; i < polyCorners; i++)
                {
                    int previousIndex = i - 1;
                    this.distance[i] = this.distance[previousIndex] + Vector2.Distance(poly[i], poly[previousIndex]);
                }

                this.calculated = true;
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
            public Vector2 PointOnLine;
        }
    }
}
