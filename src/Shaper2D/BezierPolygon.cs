// <copyright file="BezierPolygon.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Numerics;

    /// <summary>
    /// Represents a polygon made up exclusivly of a single close cubic Bezier curve.
    /// </summary>
    public sealed class BezierPolygon : IShape
    {
        private Polygon innerPolygon;

        /// <summary>
        /// Initializes a new instance of the <see cref="BezierPolygon"/> class.
        /// </summary>
        /// <param name="points">The points.</param>
        public BezierPolygon(params Point[] points)
        {
            this.innerPolygon = new Polygon(new BezierLineSegment(points));
        }

        /// <summary>
        /// Gets the bounding box of this shape.
        /// </summary>
        /// <value>
        /// The bounds.
        /// </value>
        public Rectangle Bounds => this.innerPolygon.Bounds;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        /// <value>
        /// The maximum intersections.
        /// </value>
        public int MaxIntersections => this.innerPolygon.MaxIntersections;

        /// <summary>
        /// Gets the paths that make up this shape
        /// </summary>
        /// <value>
        /// The paths.
        /// </value>
        public IEnumerable<IPath> Paths => this.innerPolygon.Paths;

        /// <summary>
        /// Determines whether the <see cref="IShape" /> contains the specified point
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        ///   <c>true</c> if the <see cref="IShape" /> contains the specified point; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(Point point) => this.innerPolygon.Contains(point);

        /// <summary>
        /// the distance of the point from the outline of the shape, if the value is negative it is inside the polygon bounds
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        /// The distance from the shape.
        /// </returns>
        public float Distance(Point point) => this.innerPolygon.Distance(point);

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
            => this.innerPolygon.FindIntersections(start, end);

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
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
        public int FindIntersections(Point start, Point end, Point[] buffer, int count, int offset)
            => this.innerPolygon.FindIntersections(start, end, buffer, count, offset);
    }
}
