// <copyright file="IPath.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System.Numerics;

    /// <summary>
    /// Represents a logic path that can be drawn
    /// </summary>
    public interface IPath : ILineSegment
    {
        /// <summary>
        /// Gets the bounds enclosing the path
        /// </summary>
        /// <value>
        /// The bounds.
        /// </value>
        Rectangle Bounds { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is closed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is closed; otherwise, <c>false</c>.
        /// </value>
        bool IsClosed { get; }

        /// <summary>
        /// Gets the length of the path
        /// </summary>
        /// <value>
        /// The length.
        /// </value>
        float Length { get; }

        /// <summary>
        /// Calculates the distance along and away from the path for a specified point.
        /// </summary>
        /// <param name="point">The point along the path.</param>
        /// <returns>
        /// Returns details about the point and its distance away from the path.
        /// </returns>
        PointInfo Distance(Point point);
    }
}
