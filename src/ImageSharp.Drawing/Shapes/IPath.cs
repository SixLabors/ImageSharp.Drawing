// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
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
        /// Calculates the point a certain distance along a path.
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
        /// Converts the <see cref="IPath" /> into a simple linear path.
        /// </summary>
        /// <returns>Returns the current <see cref="IPath" /> as simple linear path.</returns>
        IEnumerable<ISimplePath> Flatten();

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <param name="intersections">The buffer for storing each intersection.</param>
        /// <param name="orientations">
        /// The buffer for storing the orientation of each intersection.
        /// Must be the same length as <paramref name="intersections"/>.
        /// </param>
        /// <returns>
        /// The number of intersections found.
        /// </returns>
        int FindIntersections(PointF start, PointF end, Span<PointF> intersections, Span<PointOrientation> orientations);

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <param name="intersections">The buffer for storing each intersection.</param>
        /// <param name="orientations">
        /// The buffer for storing the orientation of each intersection.
        /// Must be the same length as <paramref name="intersections"/>.
        /// </param>
        /// <param name="intersectionRule">How intersections should be handled.</param>
        /// <returns>
        /// The number of intersections found.
        /// </returns>
        int FindIntersections(
            PointF start,
            PointF end,
            Span<PointF> intersections,
            Span<PointOrientation> orientations,
            IntersectionRule intersectionRule);

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
