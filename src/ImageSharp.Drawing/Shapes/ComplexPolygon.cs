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
        private readonly float length;

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
                        length += ip.Length;
                        this.internalPaths.Add(ip);
                    }
                }

                this.maxIntersections = intersections;
                this.length = length;
                this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
            else
            {
                this.maxIntersections = 0;
                this.length = 0;
                this.Bounds = RectangleF.Empty;
            }

            this.PathType = PathTypes.Mixed;
        }

        /// <inheritdoc/>
        public PathTypes PathType { get; }

        /// <summary>
        /// Gets the collection of paths that make up this shape.
        /// </summary>
        public IEnumerable<IPath> Paths => this.paths;

        /// <inheritdoc/>
        public RectangleF Bounds { get; }

        /// <inheritdoc/>
        int IPathInternals.MaxIntersections => this.maxIntersections;

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public IPath Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                // No transform to apply skip it
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

        /// <inheritdoc/>
        public IPath AsClosedPath()
        {
            if (this.PathType == PathTypes.Closed)
            {
                return this;
            }

            var paths = new IPath[this.paths.Length];
            for (int i = 0; i < this.paths.Length; i++)
            {
                paths[i] = this.paths[i].AsClosedPath();
            }

            return new ComplexPolygon(paths);
        }

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
            const int maxStackSize = 1024 / sizeof(float);
            float[] rentedFromPool = null;
            Span<float> buffer =
                totalAdded > maxStackSize
                ? (rentedFromPool = ArrayPool<float>.Shared.Rent(totalAdded))
                : stackalloc float[maxStackSize];

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

        /// <inheritdoc/>
        SegmentInfo IPathInternals.PointAlongPath(float distance)
        {
            distance %= this.length;
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
