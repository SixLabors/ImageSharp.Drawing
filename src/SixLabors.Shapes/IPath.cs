// <copyright file="IPath.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System.Collections.Generic;
    using System.Numerics;

    /// <summary>
    /// Represents a logic path that can be drawn
    /// </summary>
    public interface IPath
    {
        /// <summary>
        /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
        /// </summary>
        PathTypes PathType { get; }

        /// <summary>
        /// Gets the bounds enclosing the path
        /// </summary>
        RectangleF Bounds { get; }

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        int MaxIntersections { get; }

        /// <summary>
        /// Gets the length of the path.
        /// </summary>
        float Length { get; }

        /// <summary>
        /// Calculates the the point a certain distance along a path.
        /// </summary>
        /// <param name="distanceAlongPath">The distance along the path to find details of.</param>
        /// <returns>
        /// Returns details about a point along a path.
        /// </returns>
        SegmentInfo PointAlongPath(float distanceAlongPath);
        
        /// <summary>
        /// Calculates the distance along and away from the path for a specified point.
        /// </summary>
        /// <param name="point">The point along the path.</param>
        /// <returns>
        /// Returns details about the point and its distance away from the path.
        /// </returns>
        PointInfo Distance(PointF point);

        /// <summary>
        /// Converts the <see cref="IPath" /> into a simple linear path..
        /// </summary>
        /// <returns>Returns the current <see cref="IPath" /> as simple linear path.</returns>
        IEnumerable<ISimplePath> Flatten();

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="start">The start point of the line.</param>
        /// <param name="end">The end point of the line.</param>
        /// <param name="buffer">The buffer that will be populated with intersections.</param>
        /// <returns>
        /// The number of intersections populated into the buffer.
        /// </returns>
        int FindIntersections(PointF start, PointF end, System.Span<PointF> buffer);

        /// <summary>
        /// Determines whether the <see cref="IPath"/> contains the specified point
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>
        ///   <c>true</c> if the <see cref="IPath"/> contains the specified point; otherwise, <c>false</c>.
        /// </returns>
        bool Contains(PointF point);

        /// <summary>
        /// Transforms the path using the specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A new path with the matrix applied to it.</returns>
        IPath Transform(Matrix3x2 matrix);

        /// <summary>
        /// Returns this path with all figures closed.
        /// </summary>
        /// <returns>Returns the path as a closed path.</returns>
        IPath AsClosedPath();
    }
}