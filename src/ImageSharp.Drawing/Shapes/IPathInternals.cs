// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// An interface for internal operations we don't want to expose on <see cref="IPath"/>.
    /// </summary>
    internal interface IPathInternals : IPath
    {
        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        int MaxIntersections { get; }

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
    }
}
