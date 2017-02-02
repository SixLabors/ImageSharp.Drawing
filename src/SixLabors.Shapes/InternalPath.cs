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
        private const float Epsilon = 0.00001f;

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
            : this(segment.Flatten(), isClosedPath)
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
        /// Initializes a new instance of the <see cref="InternalPath" /> class.
        /// </summary>
        /// <param name="prototype">The prototype.</param>
        /// <param name="isClosedPath">if set to <c>true</c> [is closed path].</param>
        internal InternalPath(InternalPath prototype, bool isClosedPath)
        {
            this.points = prototype.points;
            this.distance = prototype.distance;
            this.calculated = prototype.calculated;
            this.Bounds = prototype.Bounds;
            this.closedPath = isClosedPath;
            this.totalDistance = new Lazy<float>(this.CalculateLength);
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

            int position = 0;
            bool onCornerFirstPass = true;
            Side lastEdgeSide = Side.Same;
            Vector2 lastPoint = MaxVector;
            int last = -1;
            for (int i = 0; i < polyCorners && count > 0; i++)
            {
                int next = i + 1;
                if (this.closedPath && next == polyCorners)
                {
                    next = 0;
                }

                Vector2 point = FindIntersection(this.points[i], this.points[next], start, end);
                if (point != MaxVector)
                {
                    if (lastPoint.Equivelent(point, Epsilon))
                    {
                        // hit the same point a second time do we need to remove the old one if just clipping
                        var side = SideOfLine(this.points[last], start, end);
                        if (side != Side.Same && side == SideOfLine(this.points[next], start, end))
                        {
                            // same side we don't bohter adding the crossing
                            position--; //move back one and the next hist will replace it
                            count++;
                        }
                    }
                    else
                    {
                        // we are not double crossing so just add it once
                        buffer[position + offset] = point;
                        position++;
                        count--;
                    }

                    // record the last point sin the opposite direction
                    last = i;
                    lastPoint = point;
                }
            }

            return position;
        }

        /// <summary>
        /// Finds the intersections.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>The points along the line the intersect with the boundaries of the polygon.</returns>
        public IEnumerable<Vector2> FindIntersections(Vector2 start, Vector2 end)
        {
            var buffer = ArrayPool<Vector2>.Shared.Rent(this.points.Length);
            try
            {
                var hits = this.FindIntersections(start, end, buffer, this.points.Length, 0);
                for (var i = 0; i < hits; i++)
                {
                    yield return buffer[i];
                }
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(buffer);
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

            var topLeft = new Vector2(this.Bounds.Left - 1, this.Bounds.Top - 1);
            var topRight = new Vector2(this.Bounds.Right + 1, this.Bounds.Top - 1);
            var bottomLeft = new Vector2(this.Bounds.Left - 1, this.Bounds.Bottom + 1);
            var bottomRight = new Vector2(this.Bounds.Right + 1, this.Bounds.Bottom + 1);

            //get the point that cause the most intersections
            var buffer = ArrayPool<Vector2>.Shared.Rent(this.points.Length);
            try
            {
                var c1 = this.FindIntersections(point, topLeft, buffer, this.points.Length, 0) % 2 == 1;
                var c2 = this.FindIntersections(point, topRight, buffer, this.points.Length, 0) % 2 == 1;
                var c3 = this.FindIntersections(point, bottomLeft, buffer, this.points.Length, 0) % 2 == 1;
                var c4 = this.FindIntersections(point, bottomRight, buffer, this.points.Length, 0) % 2 == 1;
                return c1 || c2 || c3 || c4;
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(buffer);
            }
        }

        private static Side SideOfLine(Vector2 test, Vector2 lineStart, Vector2 lineEnd)
        {
            var testDiff = test - lineStart;
            var lineDiff = lineEnd - lineStart;
            var crossProduct = (lineDiff.X * testDiff.Y) - (lineDiff.Y * testDiff.X);

            if (crossProduct == 0)
            {
                return Side.Same;
            }

            if (crossProduct > 0)
            {
                return Side.Left;
            }

            return Side.Right;
        }

        public enum Side
        {
            Left,
            Right,
            Same
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
        /// <returns>
        /// A <see cref="Vector2"/> describing the point that the 2 lines cross or <see cref="MaxVector"/> if they do not.
        /// </returns>
        private static Vector2 FindIntersection(Vector2 line1Start, Vector2 line1End, Vector2 line2Start, Vector2 line2End)
        {
            // do bounding boxes overlap, if not then the lines can't and return fast.
            if (!BoundingBoxesIntersect(line1Start, line1End, line2Start, line2End))
            {
                return MaxVector;
            }

            Vector2 line1Diff = line1End - line1Start;
            Vector2 line2Diff = line2End - line2Start;

            Vector2 point;
            if (Math.Abs(line1Diff.X) < Epsilon)
            {
                float slope = line2Diff.Y / line2Diff.X;
                float yinter = line2Start.Y - (slope * line2Start.X);
                float y = (line1Start.X * slope) + yinter;
                point = new Vector2(line1Start.X, y);

                // horizontal and vertical lines
            }
            else if (Math.Abs(line2Diff.X) < Epsilon)
            {
                float slope = line1Diff.Y / line1Diff.X;
                float interY = line1Start.Y - (slope * line1Start.X);
                float y = (line2Start.X * slope) + interY;
                point = new Vector2(line2Start.X, y);

                // horizontal and vertical lines
            }
            else
            {
                float slope1 = line1Diff.Y / line1Diff.X;
                float slope2 = line2Diff.Y / line2Diff.X;

                float interY1 = line1Start.Y - (slope1 * line1Start.X);
                float interY2 = line2Start.Y - (slope2 * line2Start.X);

                if (Math.Abs(slope1 - slope2) < Epsilon && Math.Abs(interY1 - interY2) > Epsilon)
                {
                    return MaxVector;
                }

                float x = (interY2 - interY1) / (slope1 - slope2);
                float y = (slope1 * x) + interY1;

                point = new Vector2(x, y);
            }

            if (BoundingBoxesIntersect(line1Start, line1End, point, point)
                && BoundingBoxesIntersect(line2Start, line2End, point, point))
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
