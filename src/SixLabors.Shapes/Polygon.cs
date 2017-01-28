// <copyright file="Polygon.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Numerics;

    /// <summary>
    /// A shape made up of a single path made up of one of more <see cref="ILineSegment"/>s
    /// </summary>
    public sealed class Polygon : IShape, IPath
    {
        private readonly InternalPath innerPath;
        private readonly ImmutableArray<IPath> pathCollection;

        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Polygon(params ILineSegment[] segments)
            : this(ImmutableArray.Create(segments))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Polygon(ImmutableArray<ILineSegment> segments)
        {
            this.LineSegments = segments;
            this.innerPath = new InternalPath(segments, true);
            this.pathCollection = ImmutableArray.Create<IPath>(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon" /> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public Polygon(ILineSegment segment)
        {
            this.LineSegments = ImmutableArray.Create(segment);
            this.innerPath = new InternalPath(segment, true);
            this.pathCollection = ImmutableArray.Create<IPath>(this);
        }

        /// <summary>
        /// Gets the line segments.
        /// </summary>
        /// <value>
        /// The line segments.
        /// </value>
        public ImmutableArray<ILineSegment> LineSegments { get; }

        /// <summary>
        /// Gets the bounding box of this shape.
        /// </summary>
        /// <value>
        /// The bounds.
        /// </value>
        public Rectangle Bounds => this.innerPath.Bounds;

        /// <summary>
        /// Gets the length of the path
        /// </summary>
        /// <value>
        /// The length.
        /// </value>
        public float Length => this.innerPath.Length;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        /// <value>
        /// The maximum intersections.
        /// </value>
        public int MaxIntersections => this.innerPath.Points.Length;

        /// <summary>
        /// Gets the paths that make up this shape
        /// </summary>
        /// <value>
        /// The paths.
        /// </value>
        public ImmutableArray<IPath> Paths => this.pathCollection;

        /// <summary>
        /// the distance of the point from the outline of the shape, if the value is negative it is inside the polygon bounds
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        /// The distance of the point away from the shape
        /// </returns>
        public float Distance(Vector2 point)
        {
            bool isInside = this.innerPath.PointInPolygon(point);
            if (isInside)
            {
                return 0;
            }

            return this.innerPath.DistanceFromPath(point).DistanceFromPath;
        }

        /// <summary>
        /// Determines whether the <see cref="IShape" /> contains the specified point
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        ///   <c>true</c> if the <see cref="IShape" /> contains the specified point; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(Vector2 point)
        {
            return this.innerPath.PointInPolygon(point);
        }

        /// <summary>
        /// Calcualtes the distance along and away from the path for a specified point.
        /// </summary>
        /// <param name="point">The point along the path.</param>
        /// <returns>
        /// distance metadata about the point.
        /// </returns>
        PointInfo IPath.Distance(Vector2 point)
        {
            return this.innerPath.DistanceFromPath(point);
        }

        /// <summary>
        /// Returns the current shape as a simple linear path.
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="ILineSegment" /> as simple linear path.
        /// </returns>
        public ImmutableArray<Vector2> Flatten()
        {
            return this.innerPath.Points;
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
        public int FindIntersections(Vector2 start, Vector2 end, Vector2[] buffer, int count, int offset)
        {
            return this.innerPath.FindIntersections(start, end, buffer, count, offset);
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
        public IEnumerable<Vector2> FindIntersections(Vector2 start, Vector2 end)
        {
            return this.innerPath.FindIntersections(start, end);
        }

        /// <summary>
        /// Transforms the rectangle using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new shape with the matrix applied to it.
        /// </returns>
        public IShape Transform(Matrix3x2 matrix)
        {
            var segments = new ILineSegment[this.LineSegments.Length];
            var i = 0;
            foreach (var s in this.LineSegments)
            {
                segments[i++] = s.Transform(matrix);
            }

            return new Polygon(segments);
        }

        /// <summary>
        /// Transforms the path using the specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new path with the matrix applied to it.
        /// </returns>
        IPath IPath.Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            var segments = new ILineSegment[this.LineSegments.Length];
            var i = 0;
            foreach (var s in this.LineSegments)
            {
                segments[i++] = s.Transform(matrix);
            }

            return new Polygon(segments);
        }

        /// <summary>
        /// Converts a path to a closed shape.
        /// </summary>
        /// <returns>
        /// Returns the path as a closed shape.
        /// </returns>
        IShape IPath.AsShape()
        {
            return this;
        }
    }
}
