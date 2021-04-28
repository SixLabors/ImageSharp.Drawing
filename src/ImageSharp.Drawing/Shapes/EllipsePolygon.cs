// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// An elliptical shape made up of a single path made up of one of more <see cref="ILineSegment"/>s.
    /// </summary>
    public sealed class EllipsePolygon : IPath, ISimplePath, IPathInternals, IInternalPathOwner
    {
        private readonly InternalPath innerPath;
        private readonly CubicBezierLineSegment segment;

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipsePolygon" /> class.
        /// </summary>
        /// <param name="location">The location the center of the ellipse will be placed.</param>
        /// <param name="size">The width/height of the final ellipse.</param>
        public EllipsePolygon(PointF location, SizeF size)
            : this(CreateSegment(location, size))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipsePolygon" /> class.
        /// </summary>
        /// <param name="location">The location the center of the circle will be placed.</param>
        /// <param name="radius">The radius final circle.</param>
        public EllipsePolygon(PointF location, float radius)
            : this(location, new SizeF(radius * 2, radius * 2))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipsePolygon" /> class.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the ellipse.</param>
        /// <param name="y">The y-coordinate of the center of the ellipse.</param>
        /// <param name="width">The width the ellipse should have.</param>
        /// <param name="height">The height the ellipse should have.</param>
        public EllipsePolygon(float x, float y, float width, float height)
            : this(new PointF(x, y), new SizeF(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipsePolygon" /> class.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the circle.</param>
        /// <param name="y">The y-coordinate of the center of the circle.</param>
        /// <param name="radius">The radius final circle.</param>
        public EllipsePolygon(float x, float y, float radius)
            : this(new PointF(x, y), new SizeF(radius * 2, radius * 2))
        {
        }

        private EllipsePolygon(CubicBezierLineSegment segment)
        {
            this.segment = segment;
            this.innerPath = new InternalPath(segment, true);
        }

        /// <inheritdoc/>
        public bool IsClosed => true;

        /// <inheritdoc/>
        public ReadOnlyMemory<PointF> Points => this.innerPath.Points();

        /// <inheritdoc />
        public RectangleF Bounds => this.innerPath.Bounds;

        /// <inheritdoc/>
        public PathTypes PathType => PathTypes.Closed;

        /// <inheritdoc />
        int IPathInternals.MaxIntersections => this.innerPath.PointCount;

        /// <inheritdoc/>
        public IPath Transform(Matrix3x2 matrix) => matrix.IsIdentity
                  ? this
                  : new EllipsePolygon(this.segment.Transform(matrix));

        /// <inheritdoc/>
        public IPath AsClosedPath() => this;

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten()
        {
            yield return this;
        }

        /// <inheritdoc/>
        public bool Contains(PointF point) => this.innerPath.PointInPolygon(point);

        /// <inheritdoc />
        // TODO switch this out to a calculated algorithm
        SegmentInfo IPathInternals.PointAlongPath(float distance)
            => this.innerPath.PointAlongPath(distance);

        /// <inheritdoc/>
        IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath()
            => new[] { this.innerPath };

        private static CubicBezierLineSegment CreateSegment(Vector2 location, SizeF size)
        {
            Guard.MustBeGreaterThan(size.Width, 0, "width");
            Guard.MustBeGreaterThan(size.Height, 0, "height");

            const float kappa = 0.5522848f;

            Vector2 sizeVector = size;
            sizeVector /= 2;

            Vector2 rootLocation = location - sizeVector;

            Vector2 pointO = sizeVector * kappa;
            Vector2 pointE = location + sizeVector;
            Vector2 pointM = location;
            Vector2 pointMminusO = pointM - pointO;
            Vector2 pointMplusO = pointM + pointO;

            PointF[] points =
            {
                new Vector2(rootLocation.X, pointM.Y),

                new Vector2(rootLocation.X, pointMminusO.Y),
                new Vector2(pointMminusO.X, rootLocation.Y),
                new Vector2(pointM.X, rootLocation.Y),

                new Vector2(pointMplusO.X, rootLocation.Y),
                new Vector2(pointE.X, pointMminusO.Y),
                new Vector2(pointE.X, pointM.Y),

                new Vector2(pointE.X, pointMplusO.Y),
                new Vector2(pointMplusO.X, pointE.Y),
                new Vector2(pointM.X, pointE.Y),

                new Vector2(pointMminusO.X, pointE.Y),
                new Vector2(rootLocation.X, pointMplusO.Y),
                new Vector2(rootLocation.X, pointM.Y),
            };

            return new CubicBezierLineSegment(points);
        }
    }
}
