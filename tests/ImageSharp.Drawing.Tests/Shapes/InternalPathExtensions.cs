// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

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
            PointF[] buffer = ArrayPool<PointF>.Shared.Rent(path.PointCount);
            try
            {
                int hits = path.FindIntersections(start, end, buffer, intersectionRule);
                for (int i = 0; i < hits; i++)
                {
                    results.Add(buffer[i]);
                }
            }
            finally
            {
                ArrayPool<PointF>.Shared.Return(buffer);
            }

            return results;
        }
    }
}
