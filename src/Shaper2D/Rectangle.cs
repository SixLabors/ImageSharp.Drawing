// <copyright file="Rectangle.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;

    /// <summary>
    /// A way of optermising drawing rectangles.
    /// </summary>
    /// <seealso cref="Shaper2D.IShape" />
    public class Rectangle : IShape, IPath
    {
        private readonly Vector2 topLeft;
        private readonly Vector2 bottomRight;
        private readonly ImmutableArray<Point> points;
        private readonly IEnumerable<IPath> pathCollection;
        private readonly float halfLength;
        private readonly float length;

        /// <summary>
        /// Initializes a new instance of the <see cref="Rectangle" /> class.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        public Rectangle(float x, float y, float width, float height)
            : this(new Point(x, y), new Size(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Rectangle"/> class.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <param name="size">The size.</param>
        public Rectangle(Point location, Size size)
        {
            this.Location = location;
            this.topLeft = location;
            this.bottomRight = location.Offset(size);
            this.Size = size;

            this.points = ImmutableArray.Create(new Point[4]
            {
                this.topLeft,
                new Vector2(this.bottomRight.X, this.topLeft.Y),
                this.bottomRight,
                new Vector2(this.topLeft.X, this.bottomRight.Y)
            });

            this.halfLength = size.Width + size.Height;
            this.length = this.halfLength * 2;
            this.pathCollection = new[] { this };
        }

        /// <summary>
        /// Gets the location.
        /// </summary>
        /// <value>
        /// The location.
        /// </value>
        public Point Location { get; }

        /// <summary>
        /// Gets the left.
        /// </summary>
        /// <value>
        /// The left.
        /// </value>
        public float Left => this.topLeft.X;

        /// <summary>
        /// Gets the right.
        /// </summary>
        /// <value>
        /// The right.
        /// </value>
        public float Right => this.bottomRight.X;

        /// <summary>
        /// Gets the top.
        /// </summary>
        /// <value>
        /// The top.
        /// </value>
        public float Top => this.topLeft.Y;

        /// <summary>
        /// Gets the bottom.
        /// </summary>
        /// <value>
        /// The bottom.
        /// </value>
        public float Bottom => this.bottomRight.Y;

        /// <summary>
        /// Gets the bounding box of this shape.
        /// </summary>
        /// <value>
        /// The bounds.
        /// </value>
        Rectangle IShape.Bounds => this;

        /// <summary>
        /// Gets the bounding box of this shape.
        /// </summary>
        /// <value>
        /// The bounds.
        /// </value>
        Rectangle IPath.Bounds => this;

        /// <summary>
        /// Gets the paths that make up this shape
        /// </summary>
        /// <value>
        /// The paths.
        /// </value>
        IEnumerable<IPath> IShape.Paths => this.pathCollection;

        /// <summary>
        /// Gets a value indicating whether this instance is closed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is closed; otherwise, <c>false</c>.
        /// </value>
        bool IPath.IsClosed => true;

        /// <summary>
        /// Gets the length of the path
        /// </summary>
        /// <value>
        /// The length.
        /// </value>
        float IPath.Length => this.length;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        /// <value>
        /// The maximum intersections.
        /// </value>
        int IShape.MaxIntersections => 4;

        /// <summary>
        /// Gets the size.
        /// </summary>
        /// <value>
        /// The size.
        /// </value>
        public Size Size { get; private set; }

        /// <summary>
        /// Determines if the specfied point is contained within the rectangular region defined by
        /// this <see cref="Rectangle" />.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        /// The <see cref="bool" />
        /// </returns>
        public bool Contains(Point point)
        {
            var v = point.ToVector2();
            return Vector2.Clamp(v, this.topLeft, this.bottomRight) == v;
        }

        /// <summary>
        /// Calculates the distance along and away from the path for a specified point.
        /// </summary>
        /// <param name="point">The point along the path.</param>
        /// <returns>
        /// Returns details about the point and its distance away from the path.
        /// </returns>
        PointInfo IPath.Distance(Point point)
        {
            bool inside; // dont care about inside/outside for paths just distance
            return this.Distance(point, false, out inside);
        }

        /// <summary>
        /// the distance of the point from the outline of the shape, if the value is negative it is inside the polygon bounds
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        /// Returns the distance from the shape to the point
        /// </returns>
        public float Distance(Point point)
        {
            bool insidePoly;
            PointInfo result = this.Distance(point, true, out insidePoly);

            // invert the distance from path when inside
            return insidePoly ? -result.DistanceFromPath : result.DistanceFromPath;
        }

        /// <summary>
        /// Based on a line described by <paramref name="start" /> and <paramref name="end" />
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>
        /// The locations along the line segment that intersect with the edges of the shape.
        /// </returns>
        public IEnumerable<Point> FindIntersections(Point start, Point end)
        {
            var buffer = new Point[2];
            var c = this.FindIntersections(start, end, buffer, 2, 0);
            if (c == 2)
            {
                return buffer;
            }
            else if (c == 1)
            {
                return new[] { buffer[0] };
            }
            else
            {
                return Enumerable.Empty<Point>();
            }
        }

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the edges of the <see cref="Rectangle"/>
        /// that the line intersects.
        /// </summary>
        /// <param name="start">The start point of the line.</param>
        /// <param name="end">The end point of the line.</param>
        /// <param name="buffer">The buffer that will be populated with intersections.</param>
        /// <param name="count">The count.</param>
        /// <param name="offset">The offset.</param>
        /// <returns>
        /// The number of intersections populated into the buffer.
        /// </returns>
        public int FindIntersections(Point start, Point end, Point[] buffer, int count, int offset)
        {
            int discovered = 0;
            Vector2 startPoint = Vector2.Clamp(start, this.topLeft, this.bottomRight);
            Vector2 endPoint = Vector2.Clamp(end, this.topLeft, this.bottomRight);

            if (startPoint == Vector2.Clamp(startPoint, start, end))
            {
                // if start closest is within line then its a valid point
                discovered++;
                buffer[offset++] = startPoint;
            }

            if (endPoint == Vector2.Clamp(endPoint, start, end))
            {
                // if start closest is within line then its a valid point
                discovered++;
                buffer[offset++] = endPoint;
            }

            return discovered;
        }

        /// <summary>
        /// Converts the <see cref="ILineSegment" /> into a simple linear path..
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="ILineSegment" /> as simple linear path.
        /// </returns>
        ImmutableArray<Point> ILineSegment.AsSimpleLinearPath()
        {
            return this.points;
        }

        private PointInfo Distance(Vector2 point, bool getDistanceAwayOnly, out bool isInside)
        {
            // point in rectangle
            // if after its clamped by the extreams its still the same then it must be inside :)
            Vector2 clamped = Vector2.Clamp(point, this.topLeft, this.bottomRight);
            isInside = clamped == point;

            float distanceFromEdge = float.MaxValue;
            float distanceAlongEdge = 0f;

            if (isInside)
            {
                // get the absolute distances from the extreams
                Vector2 topLeftDist = Vector2.Abs(point - this.topLeft);
                Vector2 bottomRightDist = Vector2.Abs(point - this.bottomRight);

                // get the min components
                Vector2 minDists = Vector2.Min(topLeftDist, bottomRightDist);

                // and then the single smallest (dont have to worry about direction)
                distanceFromEdge = Math.Min(minDists.X, minDists.Y);

                if (!getDistanceAwayOnly)
                {
                    // we need to make clamped the closest point
                    if (this.topLeft.X + distanceFromEdge == point.X)
                    {
                        // closer to lhf
                        clamped.X = this.topLeft.X; // y is already the same

                        // distance along edge is length minus the amout down we are from the top of the rect
                        distanceAlongEdge = this.length - (clamped.Y - this.topLeft.Y);
                    }
                    else if (this.topLeft.Y + distanceFromEdge == point.Y)
                    {
                        // closer to top
                        clamped.Y = this.topLeft.Y; // x is already the same

                        distanceAlongEdge = clamped.X - this.topLeft.X;
                    }
                    else if (this.bottomRight.Y - distanceFromEdge == point.Y)
                    {
                        // closer to bottom
                        clamped.Y = this.bottomRight.Y; // x is already the same

                        distanceAlongEdge = (this.bottomRight.X - clamped.X) + this.halfLength;
                    }
                    else if (this.bottomRight.X - distanceFromEdge == point.X)
                    {
                        // closer to rhs
                        clamped.X = this.bottomRight.X; // x is already the same

                        distanceAlongEdge = (this.bottomRight.Y - clamped.Y) + this.Size.Width;
                    }
                }
            }
            else
            {
                // clamped is the point on the path thats closest no matter what
                distanceFromEdge = (clamped - point).Length();

                if (!getDistanceAwayOnly)
                {
                    // we need to figure out whats the cloests edge now and thus what distance/poitn is closest
                    if (this.topLeft.X == clamped.X)
                    {
                        // distance along edge is length minus the amout down we are from the top of the rect
                        distanceAlongEdge = this.length - (clamped.Y - this.topLeft.Y);
                    }
                    else if (this.topLeft.Y == clamped.Y)
                    {
                        distanceAlongEdge = clamped.X - this.topLeft.X;
                    }
                    else if (this.bottomRight.Y == clamped.Y)
                    {
                        distanceAlongEdge = (this.bottomRight.X - clamped.X) + this.halfLength;
                    }
                    else if (this.bottomRight.X == clamped.X)
                    {
                        distanceAlongEdge = (this.bottomRight.Y - clamped.Y) + this.Size.Width;
                    }
                }
            }

            return new PointInfo
            {
                SearchPoint = point,
                DistanceFromPath = distanceFromEdge,
                ClosestPointOnPath = clamped,
                DistanceAlongPath = distanceAlongEdge
            };
        }
    }
}