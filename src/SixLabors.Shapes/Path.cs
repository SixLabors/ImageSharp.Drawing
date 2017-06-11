// <copyright file="Path.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// A aggregate of <see cref="ILineSegment"/>s making a single logical path
    /// </summary>
    /// <seealso cref="IPath" />
    public class Path : IPath, ISimplePath
    {
        private InternalPath _innerPath;
        private InternalPath innerPath => _innerPath ?? (_innerPath = new InternalPath(lineSegments, IsClosed));

        private readonly ILineSegment[] lineSegments;

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public Path(params ILineSegment[] segment)
            : this((IEnumerable<ILineSegment>)segment)
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
        public Path(IEnumerable<ILineSegment> segments)
        {
            this.lineSegments = segments.ToArray();
        }

        /// <summary>
        /// Gets the length of the path.
        /// </summary>
        public float Length => this.innerPath.Length;

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        bool ISimplePath.IsClosed => this.IsClosed;

        /// <summary>
        /// Gets the points that make up this simple linear path.
        /// </summary>
        IReadOnlyList<PointF> ISimplePath.Points => this.innerPath.Points();

        /// <inheritdoc />
        public RectangleF Bounds => this.innerPath.Bounds;

        /// <summary>
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
        /// </summary>
        public PathTypes PathType => this.IsClosed ? PathTypes.Open : PathTypes.Closed;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        public int MaxIntersections => this.innerPath.PointCount;

        /// <summary>
        /// Gets the line segments
        /// </summary>
        public IReadOnlyList<ILineSegment> LineSegments => this.lineSegments;

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        protected virtual bool IsClosed => false;

        /// <inheritdoc />
        public PointInfo Distance(PointF point)
        {
            PointInfo dist = this.innerPath.DistanceFromPath(point);

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

            ILineSegment[] segments = new ILineSegment[this.lineSegments.Length];
            int i = 0;
            foreach (ILineSegment s in this.LineSegments)
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
        public IEnumerable<ISimplePath> Flatten()
        {
            yield return this;
        }

        /// <summary>
        /// Based on a line described by <paramref name="start" /> and <paramref name="end" />
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="start">The start point of the line.</param>
        /// <param name="end">The end point of the line.</param>
        /// <param name="buffer">The buffer that will be populated with intersections.</param>
        /// <returns>
        /// The number of intersections populated into the buffer.
        /// </returns>
        public int FindIntersections(PointF start, PointF end, Span<PointF> buffer)
        {
            return this.innerPath.FindIntersections(start, end, buffer);
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
            return this.innerPath.PointInPolygon(point);
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
            return this.innerPath.PointAlongPath(distanceAlongPath);
        }
    }
}