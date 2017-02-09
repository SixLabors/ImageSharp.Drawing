// <copyright file="Ellipse.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// A shape made up of a single path made up of one of more <see cref="ILineSegment"/>s
    /// </summary>
    public class Ellipse : IPath, ISimplePath
    {
        private readonly InternalPath innerPath;
        private readonly ImmutableArray<ISimplePath> flatPath;
        private readonly BezierLineSegment segment;

        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="location">The location the center of the ellipse will be placed.</param>
        /// <param name="size">The width/hight of the final ellipse.</param>
        public Ellipse(Vector2 location, Size size)
            : this(CreateSegment(location, size))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="location">The location the center of the circle will be placed.</param>
        /// <param name="radius">The radius final circle.</param>
        public Ellipse(Vector2 location, float radius)
            : this(location, new Size(radius * 2))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="x">The X coordinate of the center of the ellipse.</param>
        /// <param name="y">The Y coordinate of the center of the ellipse.</param>
        /// <param name="width">The width the ellipse should have.</param>
        /// <param name="height">The height the ellipse should have.</param>
        public Ellipse(float x, float y, float width, float height)
            : this(new Vector2(x, y), new Size(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="x">The X coordinate of the center of the circle.</param>
        /// <param name="y">The Y coordinate of the center of the circle.</param>
        /// <param name="radius">The radius final circle.</param>
        public Ellipse(float x, float y, float radius)
            : this(new Vector2(x, y), new Size(radius * 2))
        {
        }

        private Ellipse(BezierLineSegment segment)
        {
            this.segment = segment;
            this.innerPath = new InternalPath(segment, true);
            this.flatPath = ImmutableArray.Create<ISimplePath>(this);
        }

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        bool ISimplePath.IsClosed => true;

        /// <summary>
        /// Gets the points that make up this simple linear path.
        /// </summary>
        ImmutableArray<Vector2> ISimplePath.Points => this.innerPath.Points;

        /// <inheritdoc />
        public Rectangle Bounds => this.innerPath.Bounds;

        /// <summary>
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
        /// </summary>
        PathTypes IPath.PathType => PathTypes.Closed;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        int IPath.MaxIntersections => this.innerPath.Points.Length;

        /// <inheritdoc />
        public PointInfo Distance(Vector2 point)
        {
            var dist = this.innerPath.DistanceFromPath(point);
            bool isInside = this.innerPath.PointInPolygon(point);
            if (isInside)
            {
                dist.DistanceFromPath *= -1;
            }

            return dist;
        }

        /// <summary>
        /// Transforms the rectangle using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new path with the matrix applied to it.
        /// </returns>
        public Ellipse Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            return new Ellipse(this.segment.Transform(matrix));
        }

        /// <summary>
        /// Transforms the path using the specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new path with the matrix applied to it.
        /// </returns>
        IPath IPath.Transform(Matrix3x2 matrix) => this.Transform(matrix);

        /// <summary>
        /// Returns this polygon as a path
        /// </summary>
        /// <returns>This polygon as a path</returns>
        IPath IPath.AsClosedPath()
        {
            return this;
        }

        /// <summary>
        /// Converts the <see cref="IPath" /> into a simple linear path..
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="IPath" /> as simple linear path.
        /// </returns>
        public ImmutableArray<ISimplePath> Flatten()
        {
            return this.flatPath;
        }

        /// <summary>
        /// Based on a line described by <paramref name="start" /> and <paramref name="end" />
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="start">The start point of the line.</param>
        /// <param name="end">The end point of the line.</param>
        /// <param name="buffer">The buffer that will be populated with intersections.</param>
        /// <param name="count">The count.</param>
        /// <param name="offset">The offset.</param>
        /// <returns>
        /// The number of intersections populated into the buffer.
        /// </returns>
        int IPath.FindIntersections(Vector2 start, Vector2 end, Vector2[] buffer, int count, int offset)
        {
            return this.innerPath.FindIntersections(start, end, buffer, count, offset);
        }

        /// <summary>
        /// Determines whether the <see cref="IPath" /> contains the specified point
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        ///   <c>true</c> if the <see cref="IPath" /> contains the specified point; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(Vector2 point)
        {
            return this.innerPath.PointInPolygon(point);
        }

        private static BezierLineSegment CreateSegment(Vector2 location, Size size)
        {
            Guard.MustBeGreaterThan(size.Width, 0, "width");
            Guard.MustBeGreaterThan(size.Height, 0, "height");

            var halfWidth = size.Width / 2;
            var twoThirdsWidth = size.Width * 2 / 3;
            var halfHeight = size.Height / 2;

            var halfHeightVector = new Vector2(0, halfHeight);
            var twoThirdsWidthVector = new Vector2(twoThirdsWidth, 0);
            var points = new Vector2[7]
            {
                location - halfHeightVector,
                location + twoThirdsWidthVector - halfHeightVector,
                location + twoThirdsWidthVector + halfHeightVector,
                location + halfHeightVector,
                location - twoThirdsWidthVector + halfHeightVector,
                location - twoThirdsWidthVector - halfHeightVector,
                location - halfHeightVector,
            };
            return new BezierLineSegment(points);
        }
    }
}
