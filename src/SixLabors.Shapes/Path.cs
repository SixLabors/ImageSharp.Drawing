// <copyright file="Path.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Collections.Immutable;
    using System.Numerics;

    /// <summary>
    /// A aggregate of <see cref="ILineSegment"/>s making a single logical path
    /// </summary>
    /// <seealso cref="IPath" />
    public class Path : IPath, ISimplePath
    {
        /// <summary>
        /// The inner path.
        /// </summary>
        private readonly InternalPath innerPath;
        private readonly ImmutableArray<ISimplePath> flatPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public Path(params ILineSegment[] segment)
            : this(ImmutableArray.Create(segment))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Path(ImmutableArray<ILineSegment> segments)
        {
            this.innerPath = new InternalPath(segments, IsClosed);
            this.LineSegments = segments;
            this.flatPath = ImmutableArray.Create<ISimplePath>(this);
        }

        public virtual bool IsClosed => false;

        /// <summary>
        /// Gets the line segments.
        /// </summary>
        /// <value>
        /// The line segments.
        /// </value>
        public ImmutableArray<ILineSegment> LineSegments { get; }

        /// <inheritdoc />
        public Rectangle Bounds => this.innerPath.Bounds;

        /// <inheritdoc />
        public PointInfo Distance(Vector2 point)
        {
            var dist = this.innerPath.DistanceFromPath(point);

            if (this.IsClosed)
            {
                bool isInside = this.innerPath.PointInPolygon(point);
                if (isInside)
                {
                    dist.DistanceFromPath *= -1;
                }
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
        public virtual IPath Transform(Matrix3x2 matrix)
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

            return new Path(segments);
        }

        /// <summary>
        /// Returns this polygon as a path
        /// </summary>
        /// <returns>This polygon as a path</returns>
        public IPath AsClosedPath()
        {
            if (this.IsClosed)
            {
                return this;
            }
            else
            {
                return new Polygon(this.LineSegments);
            }
        }

        public PathTypes PathType => (this.IsClosed ? PathTypes.Open : PathTypes.Closed);

        public int MaxIntersections => innerPath.Points.Length;

        public ImmutableArray<ISimplePath> Flatten()
        {
            return flatPath;
        }

        public int FindIntersections(Vector2 start, Vector2 end, Vector2[] buffer, int count, int offset)
        {
            return this.innerPath.FindIntersections(start, end, buffer, count, offset);
        }

        public bool Contains(Vector2 point)
        {
            return this.innerPath.PointInPolygon(point);
        }

        public ImmutableArray<Vector2> Points => this.innerPath.Points;
    }
}