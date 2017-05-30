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
    /// <seealso cref="SixLabors.Shapes.IPath" />
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
           

            if (paths.Length == 1)
            {
                this.Length = paths[0].Length;
                this.Bounds = paths[0].Bounds;
                this.PathType = paths[0].PathType;
                this.MaxIntersections = paths[0].MaxIntersections;
            }
            else
            {
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float minY = float.MaxValue;
                float maxY = float.MinValue;
                float length = 0;
                int intersections = 0;

                foreach (IPath s in paths)
                {
                    length += s.Length;
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

                    intersections += s.MaxIntersections;
                }

                this.MaxIntersections = intersections;
                this.Length = length;
                this.Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
                this.PathType = PathTypes.Mixed;
            }

            this.Paths = paths;
        }

        /// <summary>
        /// Gets the length of the path.
        /// </summary>
        public float Length { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
        /// </summary>
        public PathTypes PathType { get; }

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
            PointInfo pointInfo = default(PointInfo);
            bool inside = false;
            foreach (IPath shape in this.Paths)
            {
                PointInfo d = shape.Distance(point);

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
        /// Determines whether the <see cref="IPath" /> contains the specified point
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        ///   <c>true</c> if the <see cref="IPath" /> contains the specified point; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(Vector2 point)
        {
            bool inside = false;
            foreach (IPath shape in this.Paths)
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

            IPath[] shapes = new IPath[this.Paths.Length];
            int i = 0;
            foreach (IPath s in this.Paths)
            {
                shapes[i++] = s.Transform(matrix);
            }

            return new ComplexPolygon(shapes);
        }

        /// <summary>
        /// Converts the <see cref="IPath" /> into a simple linear path..
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="IPath" /> as simple linear path.
        /// </returns>
        public ImmutableArray<ISimplePath> Flatten()
        {
            List<ISimplePath> paths = new List<ISimplePath>();
            foreach (IPath path in this.Paths)
            {
                paths.AddRange(path.Flatten());
            }

            return ImmutableArray.Create(paths.ToArray());
        }

        /// <summary>
        /// Converts a path to a closed path.
        /// </summary>
        /// <returns>
        /// Returns the path as a closed path.
        /// </returns>
        public IPath AsClosedPath()
        {
            if (this.PathType == PathTypes.Closed)
            {
                return this;
            }
            else
            {
                IPath[] paths = new IPath[this.Paths.Length];
                for (int i = 0; i < this.Paths.Length; i++)
                {
                    paths[i] = this.Paths[i].AsClosedPath();
                }

                return new ComplexPolygon(paths);
            }
        }

        /// <summary>
        /// Calculates the the point a certain distance a path.
        /// </summary>
        /// <param name="distanceAlongPath">The distance along the path to find details of.</param>
        /// <returns>
        /// Returns details about a point along a path.
        /// </returns>
        public SegmentInfo PointAlongPath(float distanceAlongPath)
        {
            distanceAlongPath  = distanceAlongPath % this.Length;

            foreach(IPath p in this.Paths)
            {
                if(p.Length >= distanceAlongPath)
                {
                    return p.PointAlongPath(distanceAlongPath);
                }
                else
                {
                    //reduce it before trying the next path
                    distanceAlongPath -= p.Length;
                }
            }

            throw new InvalidOperationException("Should not be possible to reach this line");
        }
    }
}