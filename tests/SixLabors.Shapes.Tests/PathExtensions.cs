// <copyright file="PathExtensions.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;

    internal static class InternalPathExtensions
    {

        /// <summary>
        /// Finds the intersections.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>The points along the line the intersect with the boundaries of the polygon.</returns>
        internal static IEnumerable<Vector2> FindIntersections(this InternalPath path, Vector2 start, Vector2 end)
        {
            List<Vector2> results = new List<Vector2>();
            var buffer = ArrayPool<Vector2>.Shared.Rent(path.PointCount);
            try
            {
                var hits = path.FindIntersections(start, end, buffer, path.PointCount, 0);
                for (var i = 0; i < hits; i++)
                {
                    results.Add(buffer[i]);
                }
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(buffer);
            }

            return results;
        }

    }
}
