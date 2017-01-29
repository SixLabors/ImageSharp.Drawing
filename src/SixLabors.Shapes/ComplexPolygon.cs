// <copyright file="ComplexPolygon.cs" company="Scott Williams">
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

    using PolygonClipper;

    /// <summary>
    /// Represents a complex polygon made up of one or more shapes overlayed on each other, where overlaps causes holes.
    /// </summary>
    /// <seealso cref="SixLabors.Shapes.IShape" />
    public sealed class ComplexPolygon : IShape
    {
        private const float ClipperScaleFactor = 100f;
        private ImmutableArray<IShape> shapes;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public ComplexPolygon(params IShape[] shapes)
            : this(ImmutableArray.Create(shapes))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon"/> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public ComplexPolygon(ImmutableArray<IShape> shapes)
            : this(shapes, ImmutableArray<IPath>.Empty)
        {
            Guard.MustBeGreaterThanOrEqualTo(shapes.Length, 1, nameof(shapes));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public ComplexPolygon(params IPath[] paths)
            : this(ImmutableArray.Create(paths))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public ComplexPolygon(ImmutableArray<IPath> paths)
            : this(ImmutableArray<IShape>.Empty, paths)
        {
            Guard.MustBeGreaterThanOrEqualTo(paths.Length, 1, nameof(paths));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        /// <param name="paths">The paths.</param>
        private ComplexPolygon(ImmutableArray<IShape> shapes, ImmutableArray<IPath> paths)
        {
            var pathCount = shapes.Sum(x => x.Paths.Length);

            var allShapes = new List<IShape>(shapes.Length + paths.Length);
            var allPaths = new List<IPath>(pathCount + paths.Length);

            foreach (var p in paths)
            {
                allPaths.Add(p);
                allShapes.Add(p.AsShape());
            }

            foreach (var s in shapes)
            {
                allPaths.AddRange(s.Paths);
                allShapes.Add(s);
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            foreach (var s in allShapes)
            {
                if (s.Bounds.Left < minX)
                {
                    minX = s.Bounds.Left;
                }

                if (s.Bounds.Right > maxX)
                {
                    maxX = s.Bounds.Right;
                }

                if (s.Bounds.Top < minY)
                {
                    minY = s.Bounds.Top;
                }

                if (s.Bounds.Bottom > maxY)
                {
                    maxY = s.Bounds.Bottom;
                }

                this.MaxIntersections += s.MaxIntersections;
            }

            this.Paths = ImmutableArray.Create(allPaths.ToArray());
            this.shapes = ImmutableArray.Create(allShapes.ToArray());

            this.Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Gets the paths that make up this shape
        /// </summary>
        /// <value>
        /// The paths.
        /// </value>
        public ImmutableArray<IPath> Paths { get; }

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
        /// therefore for a point to be in more that one we must be in a hole of another, theoretically this could
        /// then flip again to be in a outline inside a hole inside an outline :)
        /// </remarks>
        float IShape.Distance(Vector2 point)
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
        public int FindIntersections(Vector2 start, Vector2 end, Vector2[] buffer, int count, int offset)
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
        public bool Contains(Vector2 point)
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
        public IEnumerable<Vector2> FindIntersections(Vector2 start, Vector2 end)
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

        /// <summary>
        /// Transforms the shape using the specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new shape with the matrix applied to it.
        /// </returns>
        public IShape Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                // no transform to apply skip it
                return this;
            }

            var shapes = new IShape[this.shapes.Length];
            var i = 0;
            foreach (var s in this.shapes)
            {
                shapes[i++] = s.Transform(matrix);
            }

            return new ComplexPolygon(shapes);
        }
    }
}