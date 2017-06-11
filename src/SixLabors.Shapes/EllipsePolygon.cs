// <copyright file="Ellipse.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// A shape made up of a single path made up of one of more <see cref="ILineSegment"/>s
    /// </summary>
    public class EllipsePolygon : IPath, ISimplePath
    {
        private readonly InternalPath innerPath;
        private readonly CubicBezierLineSegment segment;

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipsePolygon" /> class.
        /// </summary>
        /// <param name="location">The location the center of the ellipse will be placed.</param>
        /// <param name="size">The width/hight of the final ellipse.</param>
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
        /// <param name="x">The X coordinate of the center of the ellipse.</param>
        /// <param name="y">The Y coordinate of the center of the ellipse.</param>
        /// <param name="width">The width the ellipse should have.</param>
        /// <param name="height">The height the ellipse should have.</param>
        public EllipsePolygon(float x, float y, float width, float height)
            : this(new PointF(x, y), new SizeF(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipsePolygon" /> class.
        /// </summary>
        /// <param name="x">The X coordinate of the center of the circle.</param>
        /// <param name="y">The Y coordinate of the center of the circle.</param>
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

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        bool ISimplePath.IsClosed => true;

        /// <summary>
        /// Gets the points that make up this simple linear path.
        /// </summary>
        IReadOnlyList<PointF> ISimplePath.Points => this.innerPath.Points();

        /// <inheritdoc />
        public RectangleF Bounds => this.innerPath.Bounds;

        /// <summary>
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
        /// </summary>
        PathTypes IPath.PathType => PathTypes.Closed;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        int IPath.MaxIntersections => this.innerPath.PointCount;

        /// <inheritdoc />
        public float Length => this.innerPath.Length;

        /// <inheritdoc />
        public PointInfo Distance(PointF point)
        {
            PointInfo dist = this.innerPath.DistanceFromPath(point);
            bool isInside = this.innerPath.PointInPolygon(point);
            if (isInside)
            {
                dist.DistanceFromPath *= -1;
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
        public EllipsePolygon Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            return new EllipsePolygon(this.segment.Transform(matrix));
        }

        /// <summary>
        /// Transforms the path using the specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new path with the matrix applied to it.
        /// </returns>
        IPath IPath.Transform(Matrix3x2 matrix) => this.Transform(matrix);

        /// <summary>
        /// Returns this polygon as a path
        /// </summary>
        /// <returns>This polygon as a path</returns>
        IPath IPath.AsClosedPath()
        {
            return this;
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
        int IPath.FindIntersections(PointF start, PointF end, Span<PointF> buffer)
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

            PointF[] points = new PointF[]
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

        /// <inheritdoc /> 
        public SegmentInfo PointAlongPath(float distanceAlongPath)
        {
            // TODO switch this out to a calculated algorithum
            return this.innerPath.PointAlongPath(distanceAlongPath);
        }
    }
}
