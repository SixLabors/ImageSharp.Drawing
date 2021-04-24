// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// A aggregate of <see cref="ILineSegment"/>s making a single logical path.
    /// </summary>
    /// <seealso cref="IPath" />
    public class Path : IPath, ISimplePath, IPathInternals, IInternalPathOwner
    {
        private readonly ILineSegment[] lineSegments;
        private InternalPath innerPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Path(IEnumerable<ILineSegment> segments)
            : this(segments?.ToArray())
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
        public Path(params ILineSegment[] segments)
            => this.lineSegments = segments ?? throw new ArgumentNullException(nameof(segments));

        /// <inheritdoc/>
        bool ISimplePath.IsClosed => this.IsClosed;

        /// <inheritdoc cref="ISimplePath.IsClosed"/>
        public virtual bool IsClosed => false;

        /// <inheritdoc/>
        public ReadOnlyMemory<PointF> Points => this.InnerPath.Points();

        /// <inheritdoc />
        public RectangleF Bounds => this.InnerPath.Bounds;

        /// <inheritdoc />
        public PathTypes PathType => this.IsClosed ? PathTypes.Open : PathTypes.Closed;

        /// <inheritdoc />
        public int MaxIntersections => this.InnerPath.PointCount;

        /// <summary>
        /// Gets readonly collection of line segments.
        /// </summary>
        public IReadOnlyList<ILineSegment> LineSegments => this.lineSegments;

        /// <summary>
        /// Gets or sets a value indicating whether close or collinear vertices should be removed. TEST ONLY!
        /// </summary>
        internal bool RemoveCloseAndCollinearPoints { get; set; } = true;

        private InternalPath InnerPath =>
            this.innerPath ??= new InternalPath(this.lineSegments, this.IsClosed, this.RemoveCloseAndCollinearPoints);

        /// <inheritdoc />
        public virtual IPath Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            var segments = new ILineSegment[this.lineSegments.Length];

            for (int i = 0; i < this.LineSegments.Count; i++)
            {
                segments[i] = this.lineSegments[i].Transform(matrix);
            }

            return new Path(segments);
        }

        /// <inheritdoc />
        public IPath AsClosedPath()
        {
            if (this.IsClosed)
            {
                return this;
            }

            return new Polygon(this.LineSegments);
        }

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten()
        {
            yield return this;
        }

        /// <inheritdoc />
        public bool Contains(PointF point) => this.InnerPath.PointInPolygon(point);

        /// <inheritdoc />
        int IPathInternals.FindIntersections(PointF start, PointF end, Span<PointF> intersections, Span<PointOrientation> orientations)
            => this.InnerPath.FindIntersections(start, end, intersections, orientations);

        /// <inheritdoc />
        int IPathInternals.FindIntersections(
            PointF start,
            PointF end,
            Span<PointF> intersections,
            Span<PointOrientation> orientations,
            IntersectionRule intersectionRule)
            => this.InnerPath.FindIntersections(start, end, intersections, orientations, intersectionRule);

        /// <inheritdoc/>
        SegmentInfo IPathInternals.PointAlongPath(float distance)
           => this.InnerPath.PointAlongPath(distance);

        /// <inheritdoc/>
        IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath() => new[] { this.InnerPath };
    }
}
