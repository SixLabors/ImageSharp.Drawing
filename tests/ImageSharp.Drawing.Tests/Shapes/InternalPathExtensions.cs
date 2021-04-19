// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    internal static class InternalPathExtensions
    {
        /// <summary>
        /// Finds the intersections.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>The points along the line the intersect with the boundaries of the polygon.</returns>
        internal static IEnumerable<PointF> FindIntersections(this InternalPath path, Vector2 start, Vector2 end, IntersectionRule intersectionRule = IntersectionRule.OddEven)
        {
            var results = new List<PointF>();
            int max = path.PointCount;
            PointF[] intersections = ArrayPool<PointF>.Shared.Rent(max);
            PointOrientation[] orientations = ArrayPool<PointOrientation>.Shared.Rent(max);
            try
            {
                int hits = path.FindIntersections(
                    start,
                    end,
                    intersections.AsSpan(0, max),
                    orientations.AsSpan(0, max),
                    intersectionRule);
                for (int i = 0; i < hits; i++)
                {
                    results.Add(intersections[i]);
                }
            }
            finally
            {
                ArrayPool<PointF>.Shared.Return(intersections);
                ArrayPool<PointOrientation>.Shared.Return(orientations);
            }

            return results;
        }
    }
}
