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
        /// Initializes a new instance of the <see cref="Path" /> class.
        /// </summary>
        /// <param name="path">The path.</param>
        public Path(Path path)
            : this(path.LineSegments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Path(ImmutableArray<ILineSegment> segments)
        {
            this.innerPath = new InternalPath(segments, this.IsClosed);
            this.LineSegments = segments;
            this.flatPath = ImmutableArray.Create<ISimplePath>(this);
        }

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        bool ISimplePath.IsClosed => this.IsClosed;

        /// <summary>
        /// Gets the points that make up this simple linear path.
        /// </summary>
        public ImmutableArray<Vector2> Points => this.innerPath.Points;

        /// <inheritdoc />
        public Rectangle Bounds => this.innerPath.Bounds;

        /// <summary>
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
        /// </summary>
        public PathTypes PathType => this.IsClosed ? PathTypes.Open : PathTypes.Closed;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        public int MaxIntersections => this.innerPath.Points.Length;

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        protected virtual bool IsClosed => false;

        /// <summary>
        /// Gets the line segments
        /// </summary>
        protected ImmutableArray<ILineSegment> LineSegments { get; }

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
        public int FindIntersections(Vector2 start, Vector2 end, Vector2[] buffer, int count, int offset)
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
    }
}