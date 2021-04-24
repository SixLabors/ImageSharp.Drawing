// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Utilities;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Represents a complex polygon made up of one or more shapes overlayed on each other,
    /// where overlaps causes holes.
    /// </summary>
    /// <seealso cref="IPath" />
    public sealed class ComplexPolygon : IPath, IPathInternals, IInternalPathOwner
    {
        private readonly IPath[] paths;
        private readonly List<InternalPath> internalPaths;
        private readonly int maxIntersections;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public ComplexPolygon(IEnumerable<IPath> paths)
            : this(paths?.ToArray())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public ComplexPolygon(params IPath[] paths)
        {
            Guard.NotNull(paths, nameof(paths));

            this.paths = paths;
            this.internalPaths = new List<InternalPath>(this.paths.Length);

            if (paths.Length > 0)
            {
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float minY = float.MaxValue;
                float maxY = float.MinValue;
                float length = 0;
                int intersections = 0;

                foreach (IPath p in this.paths)
                {
                    length += p.Length;
                    if (p.Bounds.Left < minX)
                    {
                        minX = p.Bounds.Left;
                    }

                    if (p.Bounds.Right > maxX)
                    {
                        maxX = p.Bounds.Right;
                    }

                    if (p.Bounds.Top < minY)
                    {
                        minY = p.Bounds.Top;
                    }

                    if (p.Bounds.Bottom > maxY)
                    {
                        maxY = p.Bounds.Bottom;
                    }

                    foreach (ISimplePath s in p.Flatten())
                    {
                        var ip = new InternalPath(s.Points, s.IsClosed);
                        intersections += ip.PointCount;

                        this.internalPaths.Add(ip);
                    }
                }

                this.maxIntersections = intersections;
                this.Length = length;
                this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
            else
            {
                this.maxIntersections = 0;
                this.Length = 0;
                this.Bounds = RectangleF.Empty;
            }

            this.PathType = PathTypes.Mixed;
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
        public IEnumerable<IPath> Paths => this.paths;

        /// <summary>
        /// Gets the bounding box of this shape.
        /// </summary>
        /// <value>
        /// The bounds.
        /// </value>
        public RectangleF Bounds { get; }

        /// <inheritdoc/>
        int IPathInternals.MaxIntersections => this.maxIntersections;

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
            int totalAdded = 0;
            foreach (InternalPath ip in this.internalPaths)
            {
                Span<PointF> subBuffer = intersections.Slice(totalAdded);
                Span<PointOrientation> subOrientationsSpan = orientations.Slice(totalAdded);

                int position = ip.FindIntersectionsWithOrientation(start, end, subBuffer, subOrientationsSpan);
                totalAdded += position;
            }

            // Avoid pool overhead for short runs.
            // This method can be called in high volume.
            const int MaxStackSize = 1024 / sizeof(float);
            float[] rentedFromPool = null;
            Span<float> buffer =
                totalAdded > MaxStackSize
                ? (rentedFromPool = ArrayPool<float>.Shared.Rent(totalAdded))
                : stackalloc float[MaxStackSize];

            Span<float> distances = buffer.Slice(0, totalAdded);

            for (int i = 0; i < totalAdded; i++)
            {
                distances[i] = Vector2.DistanceSquared(start, intersections[i]);
            }

            Span<PointF> activeIntersections = intersections.Slice(0, totalAdded);
            Span<PointOrientation> activeOrientations = orientations.Slice(0, totalAdded);
            SortUtility.Sort(distances, activeIntersections, activeOrientations);

            if (intersectionRule == IntersectionRule.Nonzero)
            {
                totalAdded = InternalPath.ApplyNonZeroIntersectionRules(activeIntersections, activeOrientations);
            }

            if (rentedFromPool != null)
            {
                ArrayPool<float>.Shared.Return(rentedFromPool);
            }

            return totalAdded;
        }

        /// <summary>
        /// Determines whether the <see cref="IPath" /> contains the specified point
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        ///   <c>true</c> if the <see cref="IPath" /> contains the specified point; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(PointF point)
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

            var shapes = new IPath[this.paths.Length];
            int i = 0;
            foreach (IPath s in this.Paths)
            {
                shapes[i++] = s.Transform(matrix);
            }

            return new ComplexPolygon(shapes);
        }

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten()
        {
            var paths = new List<ISimplePath>();
            foreach (IPath path in this.Paths)
            {
                paths.AddRange(path.Flatten());
            }

            return paths.ToArray();
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
                var paths = new IPath[this.paths.Length];
                for (int i = 0; i < this.paths.Length; i++)
                {
                    paths[i] = this.paths[i].AsClosedPath();
                }

                return new ComplexPolygon(paths);
            }
        }

        /// <summary>
        /// Calculates the point a certain distance a path.
        /// </summary>
        /// <param name="distance">The distance along the path to find details of.</param>
        /// <returns>
        /// Returns details about a point along a path.
        /// </returns>
        SegmentInfo IPathInternals.PointAlongPath(float distance)
        {
            distance %= this.Length;
            foreach (InternalPath p in this.internalPaths)
            {
                if (p.Length >= distance)
                {
                    return p.PointAlongPath(distance);
                }

                // Reduce it before trying the next path
                distance -= p.Length;
            }

            // TODO: Perf. Throwhelper
            throw new InvalidOperationException("Should not be possible to reach this line");
        }

        /// <inheritdoc/>
        IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath()
            => this.internalPaths;
    }
}
