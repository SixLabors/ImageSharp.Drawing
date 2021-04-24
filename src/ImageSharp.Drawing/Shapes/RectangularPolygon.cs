// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// A polygon tha allows the optimized drawing of rectangles.
    /// </summary>
    /// <seealso cref="IPath" />
    public class RectangularPolygon : IPath, ISimplePath, IPathInternals
    {
        private readonly Vector2 topLeft;
        private readonly Vector2 bottomRight;
        private readonly PointF[] points;
        private readonly float halfLength;
        private readonly float length;
        private readonly RectangleF bounds;

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangularPolygon" /> class.
        /// </summary>
        /// <param name="x">The horizontal position of the rectangle.</param>
        /// <param name="y">The vertical position of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        public RectangularPolygon(float x, float y, float width, float height)
            : this(new PointF(x, y), new SizeF(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangularPolygon" /> class.
        /// </summary>
        /// <param name="topLeft">
        /// The <see cref="PointF"/> which specifies the rectangles top/left point in a two-dimensional plane.
        /// </param>
        /// <param name="bottomRight">
        /// The <see cref="PointF"/> which specifies the rectangles bottom/right point in a two-dimensional plane.
        /// </param>
        public RectangularPolygon(PointF topLeft, PointF bottomRight)
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
        /// Initializes a new instance of the <see cref="RectangularPolygon"/> class.
        /// </summary>
        /// <param name="point">
        /// The <see cref="PointF"/> which specifies the rectangles point in a two-dimensional plane.
        /// </param>
        /// <param name="size">
        /// The <see cref="SizeF"/> which specifies the rectangles height and width.
        /// </param>
        public RectangularPolygon(PointF point, SizeF size)
            : this(point, point + size)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangularPolygon"/> class.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        public RectangularPolygon(RectangleF rectangle)
            : this(rectangle.Location, rectangle.Location + rectangle.Size)
        {
        }

        /// <summary>
        /// Gets the location.
        /// </summary>
        public PointF Location { get; }

        /// <summary>
        /// Gets the x-coordinate of the left edge.
        /// </summary>
        public float Left => this.X;

        /// <summary>
        /// Gets the x-coordinate.
        /// </summary>
        public float X => this.topLeft.X;

        /// <summary>
        /// Gets the x-coordinate of the right edge.
        /// </summary>
        public float Right => this.bottomRight.X;

        /// <summary>
        /// Gets the y-coordinate of the top edge.
        /// </summary>
        public float Top => this.Y;

        /// <summary>
        /// Gets the y-coordinate.
        /// </summary>
        public float Y => this.topLeft.Y;

        /// <summary>
        /// Gets the y-coordinate of the bottom edge.
        /// </summary>
        public float Bottom => this.bottomRight.Y;

        /// <summary>
        /// Gets the bounding box of this shape.
        /// </summary>
        RectangleF IPath.Bounds => this.bounds;

        /// <inheritdoc/>
        int IPathInternals.MaxIntersections => 4;

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        bool ISimplePath.IsClosed => true;

        /// <summary>
        /// Gets the points that make this up as a simple linear path.
        /// </summary>
        ReadOnlyMemory<PointF> ISimplePath.Points => this.points;

        /// <summary>
        /// Gets the size.
        /// </summary>
        /// <value>
        /// The size.
        /// </value>
        public SizeF Size { get; }

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
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture
        /// of open and closed figures.
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
        /// Converts the polygon to a rectangular polygon from its bounds.
        /// </summary>
        /// <param name="polygon">The polygon to convert.</param>
        public static explicit operator RectangularPolygon(Polygon polygon)
            => new RectangularPolygon(polygon.Bounds.X, polygon.Bounds.Y, polygon.Bounds.Width, polygon.Bounds.Height);

        /// <summary>
        /// Determines if the specified point is contained within the rectangular region defined by
        /// this <see cref="RectangularPolygon" />.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        /// The <see cref="bool" />
        /// </returns>
        public bool Contains(PointF point)
            => Vector2.Clamp(point, this.topLeft, this.bottomRight) == (Vector2)point;

        /// <inheritdoc />
        int IPathInternals.FindIntersections(PointF start, PointF end, Span<PointF> intersections, Span<PointOrientation> orientations)
            => ((IPathInternals)this).FindIntersections(start, end, intersections, orientations, IntersectionRule.OddEven);

        /// <inheritdoc />
        int IPathInternals.FindIntersections(
            PointF start,
            PointF end,
            Span<PointF> intersections,
            Span<PointOrientation> orientations,
            IntersectionRule intersectionRule)
        {
            int offset = 0;
            int discovered = 0;
            var startPoint = Vector2.Clamp(start, this.topLeft, this.bottomRight);
            var endPoint = Vector2.Clamp(end, this.topLeft, this.bottomRight);

            // Start doesn't change when its inside the shape thus not crossing
            if (startPoint != (Vector2)start)
            {
                if (startPoint == Vector2.Clamp(startPoint, start, end))
                {
                    // If start closest is within line then its a valid point
                    discovered++;
                    intersections[offset++] = startPoint;
                }
            }

            // End didn't change it must not intercept with an edge
            if (endPoint != (Vector2)end)
            {
                if (endPoint == Vector2.Clamp(endPoint, start, end))
                {
                    // If start closest is within line then its a valid point
                    discovered++;
                    intersections[offset] = endPoint;
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
        SegmentInfo IPathInternals.PointAlongPath(float distance)
        {
            distance %= this.length;

            if (distance < this.Width)
            {
                // we are on the top stretch
                return new SegmentInfo
                {
                    Point = new Vector2(this.Left + distance, this.Top),
                    Angle = MathF.PI
                };
            }
            else
            {
                distance -= this.Width;
                if (distance < this.Height)
                {
                    // down on right
                    return new SegmentInfo
                    {
                        Point = new Vector2(this.Right, this.Top + distance),
                        Angle = -MathF.PI / 2
                    };
                }
                else
                {
                    distance -= this.Height;
                    if (distance < this.Width)
                    {
                        // botom right to left
                        return new SegmentInfo
                        {
                            Point = new Vector2(this.Right - distance, this.Bottom),
                            Angle = 0
                        };
                    }
                    else
                    {
                        distance -= this.Width;
                        return new SegmentInfo
                        {
                            Point = new Vector2(this.Left, this.Bottom - distance),
                            Angle = (float)(Math.PI / 2)
                        };
                    }
                }
            }
        }

        /// <inheritdoc/>
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
        IPath IPath.AsClosedPath() => this;
    }
}
