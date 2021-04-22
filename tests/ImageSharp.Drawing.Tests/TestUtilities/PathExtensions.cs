// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    /// <summary>
    /// Extensions to <see cref="IPathInternals"/> to simplify unit tests and ensure internal
    /// methods are called in a consistant manner.
    /// </summary>
    public static class PathExtensions
    {
        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// returns the number of intersections found.
        /// </summary>
        /// <param name="path">The path to find intersection upon.</param>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <returns>The <see cref="int"/> count.</returns>
        public static int CountIntersections(this IPath path, PointF start, PointF end)
          => FindIntersections(path, start, end).Count();

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="path">The path to find intersection upon.</param>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <returns>The <see cref="IEnumerable{PointF}"/>.</returns>
        public static IEnumerable<PointF> FindIntersections(this IPath path, PointF start, PointF end)
        {
            if (path is IPathInternals internals)
            {
                return FindIntersections(internals, start, end);
            }

            var complex = new ComplexPolygon(path);
            return FindIntersections(complex, start, end);
        }

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// returns the number of intersections found.
        /// </summary>
        /// <param name="path">The path to find intersection upon.</param>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <returns>The <see cref="int"/> count.</returns>
        internal static int CountIntersections<T>(this T path, PointF start, PointF end)
          where T : IPathInternals
          => FindIntersections(path, start, end).Count();

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="path">The path to find intersection upon.</param>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <returns>The <see cref="IEnumerable{PointF}"/>.</returns>
        internal static IEnumerable<PointF> FindIntersections<T>(this T path, PointF start, PointF end)
            where T : IPathInternals
        {
            int max = path.MaxIntersections;
            PointF[] intersections = ArrayPool<PointF>.Shared.Rent(max);
            PointOrientation[] orientations = ArrayPool<PointOrientation>.Shared.Rent(max);
            try
            {
                int hits = path.FindIntersections(start, end, intersections.AsSpan(0, max), orientations.AsSpan(0, max));
                var results = new PointF[hits];
                Array.Copy(intersections, results, hits);

                return results;
            }
            finally
            {
                ArrayPool<PointF>.Shared.Return(intersections);
                ArrayPool<PointOrientation>.Shared.Return(orientations);
            }
        }

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// returns the number of intersections found.
        /// </summary>
        /// <param name="path">The path to find intersection upon.</param>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <param name="intersectionRule">How intersections should be handled.</param>
        /// <returns>The <see cref="int"/> count.</returns>
        internal static int CountIntersections<T>(
            this T path,
            PointF start,
            PointF end,
            IntersectionRule intersectionRule)
            where T : IPathInternals
            => FindIntersections(path, start, end, intersectionRule).Length;

        /// <summary>
        /// Based on a line described by <paramref name="start"/> and <paramref name="end"/>
        /// populate a buffer for all points on the polygon that the line intersects.
        /// </summary>
        /// <param name="path">The path to find intersection upon.</param>
        /// <param name="start">The start position.</param>
        /// <param name="end">The end position.</param>
        /// <param name="intersectionRule">How intersections should be handled.</param>
        /// <returns>The <see cref="IEnumerable{PointF}"/>.</returns>
        internal static ReadOnlySpan<PointF> FindIntersections<T>(
            this T path,
            PointF start,
            PointF end,
            IntersectionRule intersectionRule)
            where T : IPathInternals
        {
            int max = path.MaxIntersections;
            PointF[] intersections = ArrayPool<PointF>.Shared.Rent(max);
            PointOrientation[] orientations = ArrayPool<PointOrientation>.Shared.Rent(max);
            try
            {
                int hits = path.FindIntersections(start, end, intersections.AsSpan(0, max), orientations.AsSpan(0, max), intersectionRule);
                var results = new PointF[hits];
                Array.Copy(intersections, results, hits);

                return results;
            }
            finally
            {
                ArrayPool<PointF>.Shared.Return(intersections);
                ArrayPool<PointOrientation>.Shared.Return(orientations);
            }
        }
    }
}
