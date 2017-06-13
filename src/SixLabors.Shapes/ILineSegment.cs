// <copyright file="ILineSegment.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System.Collections.Generic;
    using System.Numerics;

    /// <summary>
    /// Represents a simple path segment
    /// </summary>
    public interface ILineSegment
    {
        /// <summary>
        /// Gets the end point.
        /// </summary>
        /// <value>
        /// The end point.
        /// </value>
        PointF EndPoint { get; }

        /// <summary>
        /// Converts the <see cref="ILineSegment" /> into a simple linear path..
        /// </summary>
        /// <returns>Returns the current <see cref="ILineSegment" /> as simple linear path.</returns>
        IReadOnlyList<PointF> Flatten();

        /// <summary>
        /// Transforms the current LineSegment using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A line segment with the matrix applied to it.</returns>
        ILineSegment Transform(Matrix3x2 matrix);
    }
}
