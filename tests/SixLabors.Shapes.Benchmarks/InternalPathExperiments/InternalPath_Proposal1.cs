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
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Internal logic for integrating linear paths.
    /// </summary>
    internal class InternalPath_Proposal1
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
        internal InternalPath_Proposal1(IEnumerable<ILineSegment> segments, bool isClosedPath)
            : this(Simplify(segments, isClosedPath), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath_Proposal1(ILineSegment segment, bool isClosedPath)
            : this(segment?.Flatten() ?? Enumerable.Empty<Vector2>(), isClosedPath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath_Proposal1(IEnumerable<Vector2> points, bool isClosedPath)
            : this(Simplify(points, isClosedPath), isClosedPath)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        private InternalPath_Proposal1(PointData[] points, bool isClosedPath)
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
        internal ImmutableArray<Vector2> Points() => this.points.Select(X => X.Point).ToImmutableArray();

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
            ClampPoints(ref start, ref end);

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
                float targetMinX = Math.Min(start.X, end.X);
                float targetMaxX = Math.Max(start.X, end.X);
                float targetMinY = Math.Min(start.Y, end.Y);
                float targetMaxY = Math.Max(start.Y, end.Y);

                int lastCorner = polyCorners - 1;
                Orientation prevOrientation = CalulateOrientation(start, end, this.points[lastCorner].Point);
                Orientation nextOrientation = CalulateOrientation(start, end, this.points[0].Point);
                Orientation nextPlus1Orientation = CalulateOrientation(start, end, this.points[1].Point);

                for (int i = 0; i < polyCorners && count > 0; i++)
                {
                    int next = (i + 1) % this.points.Length;
                    int nextPlus1 = (next + 1) % this.points.Length;
                    Vector2 edgeStart = this.points[i].Point;
                    Vector2 edgeEnd = this.points[next].Point;

                    Orientation pointOrientation = nextOrientation;
                    nextOrientation = nextPlus1Orientation;
                    nextPlus1Orientation = CalulateOrientation(start, end, this.points[nextPlus1].Point);

                    bool removeLastIntersection = nextOrientation == Orientation.Colinear &&
                                                  pointOrientation == Orientation.Colinear &&
                                                  nextPlus1Orientation != prevOrientation &&
                                                  (this.closedPath || i > 0) &&
                                                  (IsOnSegment(start, edgeStart, end) || IsOnSegment(start, edgeEnd, end));

                    bool doIntersect = false;
                    if (pointOrientation == Orientation.Colinear || pointOrientation != nextOrientation)
                    {
                        //intersect
                        float edgeMinX = Math.Min(edgeStart.X, edgeEnd.X);
                        float edgeMaxX = Math.Max(edgeStart.X, edgeEnd.X);
                        float edgeMinY = Math.Min(edgeStart.Y, edgeEnd.Y);
                        float edgeMaxY = Math.Max(edgeStart.Y, edgeEnd.Y);

                        doIntersect = edgeMinX - Epsilon <= targetMaxX &&
                                      edgeMaxX + Epsilon >= targetMinX &&
                                      edgeMinY - Epsilon <= targetMaxY &&
                                      edgeMaxY + Epsilon >= targetMinY;
                    }

                    precaclulate[i] = new PassPointData
                    {
                        RemoveLastIntersectionAndSkip = removeLastIntersection,
                        RelativeOrientation = pointOrientation,
                        DoIntersect = doIntersect // DoIntersect(edgeStart, edgeEnd, start, end)
                    };
                    lastCorner = i;
                    prevOrientation = pointOrientation;
                }

                if (this.closedPath)
                {
                    int prev = polyCorners - 1;

                    if (precaclulate[prev].DoIntersect)
                    {
                        lastPoint = FindIntersection(this.points[prev].Point, this.points[0].Point, start, end);
                    }
                }

                for (int i = 0; i < polyCorners && count > 0; i++)
                {
                    int next = (i + 1) % this.points.Length;

                    if (precaclulate[i].RemoveLastIntersectionAndSkip)
                    {
                        position--;
                        count++;
                        continue;
                    }
                    if (precaclulate[i].DoIntersect)
                    {
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

                                Orientation side = precaclulate[next].RelativeOrientation;
                                Orientation side2 = precaclulate[last].RelativeOrientation;

                                //if (side == Orientation.Colinear && side2 == Orientation.Colinear)
                                //{
                                //    position--;
                                //    count++;
                                //    continue;
                                //}

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

                    lastCorner = i;
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
            Vector2[] buffer = ArrayPool<Vector2>.Shared.Rent(this.points.Length);
            try
            {
                int intersection = this.FindIntersections(point, new Vector2(this.Bounds.Left - 1, this.Bounds.Top - 1), buffer, this.points.Length, 0);
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
                ArrayPool<Vector2>.Shared.Return(buffer);
            }

            return false;
        }

        private static bool IsOnSegment(Vector2 p, Vector2 q, Vector2 r)
        {
            return (q.X - Epsilon2) <= Math.Max(p.X, r.X) &&
                    (q.X + Epsilon2) >= Math.Min(p.X, r.X) &&
                    (q.Y - Epsilon2) <= Math.Max(p.Y, r.Y) &&
                    (q.Y + Epsilon2) >= Math.Min(p.Y, r.Y);
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
        /// Finds the point on line described by <paramref name="line1Start" /> and <paramref name="line1End" />
        /// that intersects with line described by <paramref name="line2Start" /> and <paramref name="line2End" />
        /// </summary>
        /// <param name="line1Start">The line1 start.</param>
        /// <param name="line1End">The line1 end.</param>
        /// <param name="line2Start">The line2 start.</param>
        /// <param name="line2End">The line2 end.</param>
        /// <returns>
        /// the number of points on the line that it hit
        /// </returns>
        private static Vector2 FindIntersection(Vector2 line1Start, Vector2 line1End, Vector2 line2Start, Vector2 line2End)
        {
            // do bounding boxes overlap, if not then the lines can't and return fast.
            //if (!DoIntersect(line1Start, line1End, line2Start, line2End))
            // {
            //    return MaxVector;
            // }

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

            if (IsOnSegment(line1Start, point, line1End) && IsOnSegment(line2Start, point, line2End))
            {
                return point;
            }

            return MaxVector;
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

        private struct PassPointData
        {
            public bool RemoveLastIntersectionAndSkip;
            public Orientation RelativeOrientation;
            public bool DoIntersect;
        }
    }
}