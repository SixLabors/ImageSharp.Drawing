// <copyright file="LinearLineSegment.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System.Collections.Immutable;
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
        private readonly ImmutableArray<Point> points;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearLineSegment"/> class.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        public LinearLineSegment(Point start, Point end)
            : this(new[] { start, end })
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearLineSegment"/> class.
        /// </summary>
        /// <param name="points">The points.</param>
        public LinearLineSegment(params Point[] points)
        {
            Guard.NotNull(points, nameof(points));
            Guard.MustBeGreaterThanOrEqualTo(points.Count(), 2, nameof(points));

            this.points = ImmutableArray.Create(points);
        }

        /// <summary>
        /// Converts the <see cref="ILineSegment" /> into a simple linear path..
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="ILineSegment" /> as simple linear path.
        /// </returns>
        public ImmutableArray<Point> AsSimpleLinearPath()
        {
            return this.points;
        }
    }
}