// <copyright file="RectangularePolygon.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;

    /// <summary>
    /// A way of optimizing drawing rectangles.
    /// </summary>
    /// <seealso cref="SixLabors.Shapes.IPath" />
    public class RectangularePolygon : IPath, ISimplePath
    {
        private readonly Vector2 topLeft;
        private readonly Vector2 bottomRight;
        private readonly PointF[] points;
        private readonly float halfLength;
        private readonly float length;
        private readonly RectangleF bounds;

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangularePolygon" /> class.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        public RectangularePolygon(float x, float y, float width, float height)
            : this(new PointF(x, y), new SizeF(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangularePolygon" /> class.
        /// </summary>
        /// <param name="topLeft">The top left.</param>
        /// <param name="bottomRight">The bottom right.</param>
        public RectangularePolygon(PointF topLeft, PointF bottomRight)
        {
            this.Location = topLeft;
            this.topLeft = topLeft;
            this.bottomRight = bottomRight;
            this.Size = new SizeF(bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);

            this.points = new PointF[4]
            {
                this.topLeft,
                new Vector2(this.bottomRight.X, this.topLeft.Y),
                this.bottomRight,
                new Vector2(this.topLeft.X, this.bottomRight.Y)
            };

            this.halfLength = this.Size.Width + this.Size.Height;
            this.length = this.halfLength * 2;
            this.bounds = new RectangleF(this.Location, this.Size);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangularePolygon"/> class.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <param name="size">The size.</param>
        public RectangularePolygon(PointF location, SizeF size)
            : this(location, location + size)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangularePolygon"/> class.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        public RectangularePolygon(RectangleF rectangle)
            : this(rectangle.Location, rectangle.Location + rectangle.Size)
        {
        }

        /// <summary>
        /// Gets the location.
        /// </summary>
        /// <value>
        /// The location.
        /// </value>
        public PointF Location { get; }

        /// <summary>
        /// Gets the left.
        /// </summary>
        /// <value>
        /// The left.
        /// </value>
        public float Left => this.topLeft.X;

        /// <summary>
        /// Gets the X.
        /// </summary>
        /// <value>
        /// The X.
        /// </value>
        public float X => this.topLeft.X;

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
        /// Gets the Y.
        /// </summary>
        /// <value>
        /// The Y.
        /// </value>
        public float Y => this.topLeft.Y;

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
        RectangleF IPath.Bounds => this.bounds;

        /// <inheritdoc />
        float IPath.Length => this.length;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        /// <value>
        /// The maximum intersections.
        /// </value>
        int IPath.MaxIntersections => 4;

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        bool ISimplePath.IsClosed => true;

        /// <summary>
        /// Gets the points that make this up as a simple linear path.
        /// </summary>
        IReadOnlyList<PointF> ISimplePath.Points => this.points;

        /// <summary>
        /// Gets the size.
        /// </summary>
        /// <value>
        /// The size.
        /// </value>
        public SizeF Size { get; private set; }

        /// <summary>
        /// Gets the size.
        /// </summary>
        /// <value>
        /// The size.
        /// </value>
        public float Width => this.Size.Width;

        /// <summary>
        /// Gets the height.
        /// </summary>
        /// <value>
        /// The height.
        /// </value>
        public float Height => this.Size.Height;

        /// <summary>
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
        /// </summary>
        PathTypes IPath.PathType => PathTypes.Closed;

        /// <summary>
        /// Gets the center.
        /// </summary>
        /// <value>
        /// The center.
        /// </value>
        public PointF Center => (this.topLeft + this.bottomRight) / 2;

        /// <summary>
        /// Determines if the specified point is contained within the rectangular region defined by
        /// this <see cref="RectangularePolygon" />.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        /// The <see cref="bool" />
        /// </returns>
        public bool Contains(PointF point)
        {
            return Vector2.Clamp(point, this.topLeft, this.bottomRight) == (Vector2)point;
        }

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the edges of the <see cref="RectangularePolygon"/>
        /// that the line intersects.
        /// </summary>
        /// <param name="start">The start point of the line.</param>
        /// <param name="end">The end point of the line.</param>
        /// <param name="buffer">The buffer that will be populated with intersections.</param>
        /// <returns>
        /// The number of intersections populated into the buffer.
        /// </returns>
        public int FindIntersections(PointF start, PointF end, Span<PointF> buffer)
        {
            int offset = 0;
            int discovered = 0;
            Vector2 startPoint = Vector2.Clamp(start, this.topLeft, this.bottomRight);
            Vector2 endPoint = Vector2.Clamp(end, this.topLeft, this.bottomRight);

            // start doesn't change when its inside the shape thus not crossing
            if (startPoint != (Vector2)start)
            {
                if (startPoint == Vector2.Clamp(startPoint, start, end))
                {
                    // if start closest is within line then its a valid point
                    discovered++;
                    buffer[offset++] = startPoint;
                }
            }

            // end didn't change it must not intercept with an edge
            if (endPoint != (Vector2)end)
            {
                if (endPoint == Vector2.Clamp(endPoint, start, end))
                {
                    // if start closest is within line then its a valid point
                    discovered++;
                    buffer[offset] = endPoint;
                }
            }

            return discovered;
        }

        /// <summary>
        /// Transforms the rectangle using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new shape with the matrix applied to it.
        /// </returns>
        public IPath Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            // rectangles may be rotated and skewed which means they will then nedd representing by a polygon
            return new Polygon(new LinearLineSegment(this.points).Transform(matrix));
        }


        /// <inheritdoc /> 
        public SegmentInfo PointAlongPath(float distanceAlongPath)
        {
            distanceAlongPath = distanceAlongPath % this.length;

            if (distanceAlongPath < this.Width)
            {
                // we are on the top stretch
                return new SegmentInfo
                {
                    Point = new Vector2(this.Left + distanceAlongPath, this.Top),
                    Angle = (float)Math.PI
                };
            }
            else
            {
                distanceAlongPath -= this.Width;
                if (distanceAlongPath < this.Height)
                {
                    // down on right
                    return new SegmentInfo
                    {
                        Point = new Vector2(this.Right, this.Top + distanceAlongPath),
                        Angle = -(float)Math.PI / 2
                    };
                }
                else
                {
                    distanceAlongPath -= this.Height;
                    if (distanceAlongPath < this.Width)
                    {
                        // botom right to left
                        return new SegmentInfo
                        {
                            Point = new Vector2(this.Right - distanceAlongPath, this.Bottom),
                            Angle = 0
                        };
                    }
                    else
                    {

                        distanceAlongPath -= this.Width;
                        return new SegmentInfo
                        {
                            Point = new Vector2(this.Left, this.Bottom - distanceAlongPath),
                            Angle = (float)(Math.PI / 2)
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Calculates the distance along and away from the path for a specified point.
        /// </summary>
        /// <param name="point">The point along the path.</param>
        /// <returns>
        /// Returns details about the point and its distance away from the path.
        /// </returns>
        public PointInfo Distance(PointF point)
        {
            Vector2 vectorPoint = point;
            // point in rectangle
            // if after its clamped by the extreams its still the same then it must be inside :)
            Vector2 clamped = Vector2.Clamp(point, this.topLeft, this.bottomRight);
            bool isInside = clamped == vectorPoint;

            float distanceFromEdge = float.MaxValue;
            float distanceAlongEdge = 0f;

            if (isInside)
            {
                // get the absolute distances from the extreams
                Vector2 topLeftDist = Vector2.Abs(vectorPoint - this.topLeft);
                Vector2 bottomRightDist = Vector2.Abs(vectorPoint - this.bottomRight);

                // get the min components
                Vector2 minDists = Vector2.Min(topLeftDist, bottomRightDist);

                // and then the single smallest (dont have to worry about direction)
                distanceFromEdge = Math.Min(minDists.X, minDists.Y);

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
                    distanceAlongEdge = (clamped.Y - this.topLeft.Y) + this.Size.Width;
                }
            }
            else
            {
                // clamped is the point on the path thats closest no matter what
                distanceFromEdge = (clamped - vectorPoint).Length();

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
                    distanceAlongEdge = (clamped.Y - this.topLeft.Y) + this.Size.Width;
                }
            }

            if (distanceAlongEdge == this.length)
            {
                distanceAlongEdge = 0;
            }

            distanceFromEdge = isInside ? -distanceFromEdge : distanceFromEdge;

            return new PointInfo
            {
                SearchPoint = point,
                DistanceFromPath = distanceFromEdge,
                ClosestPointOnPath = clamped,
                DistanceAlongPath = distanceAlongEdge
            };
        }

        /// <summary>
        /// Converts the <see cref="IPath" /> into a simple linear path..
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="IPath" /> as simple linear path.
        /// </returns>
        public IEnumerable<ISimplePath> Flatten()
        {
            yield return this;
        }

        /// <summary>
        /// Converts a path to a closed path.
        /// </summary>
        /// <returns>
        /// Returns the path as a closed path.
        /// </returns>
        IPath IPath.AsClosedPath()
        {
            return this;
        }
    }
}