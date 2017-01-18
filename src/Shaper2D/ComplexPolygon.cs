// <copyright file="ComplexPolygon.cs" company="Scott Williams">
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

    using PolygonClipper;

    /// <summary>
    /// Represents a complex polygon made up of one or more outline
    /// polygons and one or more holes to punch out of them.
    /// </summary>
    /// <seealso cref="Shaper2D.IShape" />
    public sealed class ComplexPolygon : IShape
    {
        private const float ClipperScaleFactor = 100f;
        private IShape[] shapes;
        private ImmutableArray<IPath> paths;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon"/> class.
        /// </summary>
        /// <param name="outline">The outline.</param>
        /// <param name="holes">The holes.</param>
        public ComplexPolygon(IShape outline, params IShape[] holes)
            : this(new[] { outline }, holes)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon"/> class.
        /// </summary>
        /// <param name="outlines">The outlines.</param>
        /// <param name="holes">The holes.</param>
        public ComplexPolygon(IShape[] outlines, IShape[] holes)
        {
            Guard.NotNull(outlines, nameof(outlines));
            Guard.MustBeGreaterThanOrEqualTo(outlines.Length, 1, nameof(outlines));

            this.MaxIntersections = this.FixAndSetShapes(outlines, holes);

            float minX = this.shapes.Min(x => x.Bounds.Left);
            float maxX = this.shapes.Max(x => x.Bounds.Right);
            float minY = this.shapes.Min(x => x.Bounds.Top);
            float maxY = this.shapes.Max(x => x.Bounds.Bottom);

            this.Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Gets the paths that make up this shape
        /// </summary>
        /// <value>
        /// The paths.
        /// </value>
        public ImmutableArray<IPath> Paths => this.paths;

        /// <summary>
        /// Gets the bounding box of this shape.
        /// </summary>
        /// <value>
        /// The bounds.
        /// </value>
        public Rectangle Bounds { get; }

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        /// <value>
        /// The maximum intersections.
        /// </value>
        public int MaxIntersections { get; }

        /// <summary>
        /// the distance of the point from the outline of the shape, if the value is negative it is inside the polygon bounds
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        /// Returns the distance from thr shape to the point
        /// </returns>
        /// <remarks>
        /// Due to the clipping we did during construction we know that out shapes do not overlap at there edges
        /// therefore for apoint to be in more that one we must be in a hole of another, theoretically this could
        /// then flip again to be in a outlin inside a hole inside an outline :)
        /// </remarks>
        float IShape.Distance(Point point)
        {
            float dist = float.MaxValue;
            bool inside = false;
            foreach (IShape shape in this.shapes)
            {
                float d = shape.Distance(point);

                if (d <= 0)
                {
                    // we are inside a poly
                    d = -d;  // flip the sign
                    inside ^= true; // flip the inside flag
                }

                if (d < dist)
                {
                    dist = d;
                }
            }

            if (inside)
            {
                return -dist;
            }

            return dist;
        }

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on all the polygons, that make up this complex shape,
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
            int totalAdded = 0;
            for (int i = 0; i < this.shapes.Length; i++)
            {
                int added = this.shapes[i].FindIntersections(start, end, buffer, count, offset);
                count -= added;
                offset += added;
                totalAdded += added;
            }

            return totalAdded;
        }

        /// <summary>
        /// Determines whether the <see cref="IShape" /> contains the specified point
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        ///   <c>true</c> if the <see cref="IShape" /> contains the specified point; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(Point point)
        {
            bool inside = false;
            foreach (IShape shape in this.shapes)
            {
                if (shape.Contains(point))
                {
                    inside ^= true; // flip the inside flag
                }
            }

            return inside;
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
            for (int i = 0; i < this.shapes.Length; i++)
            {
                var points = this.shapes[i].FindIntersections(start, end);
                foreach (var point in points)
                {
                    yield return point;
                }
            }
        }

        private int FixAndSetShapes(IEnumerable<IShape> outlines, IEnumerable<IShape> holes)
        {
            Clipper clipper = new Clipper();

            // add the outlines and the holes to clipper, scaling up from the float source to the int based system clipper uses
            clipper.AddPaths(outlines, PolyType.Subject);
            clipper.AddPaths(holes, PolyType.Clip);

            this.shapes = clipper.Execute();
            this.paths = ImmutableArray.Create(this.shapes.SelectMany(x => x.Paths).ToArray());

            int intersections = 0;
            foreach (IShape s in this.shapes)
            {
                intersections += s.MaxIntersections;
            }

            return intersections;
        }
    }
}