// <copyright file="LinearLineSegment.cs" company="Scott Williams">
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
    /// Represents a series of control points that will be joined by straight lines
    /// </summary>
    /// <seealso cref="ILineSegment" />
    public class LinearLineSegment : ILineSegment
    {
        /// <summary>
        /// The collection of points.
        /// </summary>
        private readonly PointF[] points;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearLineSegment"/> class.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        public LinearLineSegment(PointF start, PointF end)
            : this(new[] { start, end })
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearLineSegment" /> class.
        /// </summary>
        /// <param name="point1">The point1.</param>
        /// <param name="point2">The point2.</param>
        /// <param name="additionalPoints">Additional points</param>
        public LinearLineSegment(PointF point1, PointF point2, params PointF[] additionalPoints)
            : this(new[] { point1, point2 }.Concat(additionalPoints))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearLineSegment"/> class.
        /// </summary>
        /// <param name="points">The points.</param>
        public LinearLineSegment(IEnumerable<PointF> points)
        {
            Guard.NotNull(points, nameof(points));

            this.points = points.ToArray();

            Guard.MustBeGreaterThanOrEqualTo(this.points.Length, 2, nameof(points));

            this.EndPoint = this.points[this.points.Length - 1];
        }

        /// <summary>
        /// Gets the end point.
        /// </summary>
        /// <value>
        /// The end point.
        /// </value>
        public PointF EndPoint { get; private set; }

        /// <summary>
        /// Converts the <see cref="ILineSegment" /> into a simple linear path..
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="ILineSegment" /> as simple linear path.
        /// </returns>
        public IReadOnlyList<PointF> Flatten()
        {
            return this.points;
        }

        /// <summary>
        /// Transforms the current LineSegment using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A line segment with the matrix applied to it.
        /// </returns>
        public LinearLineSegment Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                // no transform to apply skip it
                return this;
            }

            PointF[] points = new PointF[this.points.Length];
            int i = 0;
            foreach (PointF p in this.points)
            {
                points[i++] = PointF.Transform(p, matrix);
            }

            return new LinearLineSegment(points);
        }

        /// <summary>
        /// Transforms the current LineSegment using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A line segment with the matrix applied to it.</returns>
        ILineSegment ILineSegment.Transform(Matrix3x2 matrix) => this.Transform(matrix);
    }
}