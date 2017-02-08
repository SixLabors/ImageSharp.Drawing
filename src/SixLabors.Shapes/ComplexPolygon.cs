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
    public sealed class ComplexPolygon : IPath
    {

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
        {
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            
            foreach (var s in paths)
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
            if (paths.Length == 1)
            {
                this.PathType = paths[0].PathType;
                
            }
            else
            {
                this.PathType = PathTypes.Mixed;
            }

            this.Paths = paths;

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
        public PointInfo Distance(Vector2 point)
        {
            float dist = float.MaxValue;
            var pointInfo = default(PointInfo);
            bool inside = false;
            foreach (IPath shape in this.Paths)
            {
                var d = shape.Distance(point);

                if (d.DistanceFromPath <= 0)
                {
                    // we are inside a poly
                    d.DistanceFromPath = -d.DistanceFromPath;  // flip the sign
                    inside ^= true; // flip the inside flag
                }

                if (d.DistanceFromPath < dist)
                {
                    dist = d.DistanceFromPath;
                    pointInfo = d;
                }
            }

            if (inside)
            {
                pointInfo.DistanceFromPath = -pointInfo.DistanceFromPath;
            }

            return pointInfo;
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
            for (int i = 0; i < this.Paths.Length; i++)
            {
                int added = this.Paths[i].FindIntersections(start, end, buffer, count, offset);
                count -= added;
                offset += added;
                totalAdded += added;
            }

            // TODO we should sort by distance from start
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
            foreach (var shape in this.Paths)
            {
                if (shape.Contains(point))
                {
                    inside ^= true; // flip the inside flag
                }
            }

            return inside;
        }

        /// <summary>
        /// Transforms the shape using the specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new shape with the matrix applied to it.
        /// </returns>
        public IPath Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                // no transform to apply skip it
                return this;
            }

            var shapes = new IPath[this.Paths.Length];
            var i = 0;
            foreach (var s in this.Paths)
            {
                shapes[i++] = s.Transform(matrix);
            }

            return new ComplexPolygon(shapes);
        }

        public PathTypes PathType { get; }

        public ImmutableArray<ISimplePath> Flatten()
        {
            var paths = new List<ISimplePath>();
            foreach (var path in this.Paths)
            {
                paths.AddRange(path.Flatten());
            }

            return ImmutableArray.Create(paths.ToArray());
        }

        public IPath AsClosedPath()
        {
            if (this.PathType == PathTypes.Closed)
            {
                return this;
            }
            else
            {
                IPath[] paths = new IPath[this.Paths.Length];
                for(var i = 0; i< this.Paths.Length; i++)
                {
                    paths[i] = this.Paths[i].AsClosedPath();
                }

                return new ComplexPolygon(paths);
            }
        }
    }
}