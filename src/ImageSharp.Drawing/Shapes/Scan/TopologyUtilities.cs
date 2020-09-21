// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    /// <summary>
    /// Implements some basic algorithms on raw data structures.
    /// Polygons are represented with a span of points,
    /// where first point should be repeated at the end.
    /// </summary>
    /// <remarks>
    /// Positive orientation means Clockwise in world coordinates (positive direction goes UP on paper).
    /// Since the Drawing library deals mostly with Screen coordinates where this is opposite,
    /// we use different terminology here to avoid confusion.
    /// </remarks>
    internal static class TopologyUtilities
    {
        /// <summary>
        /// Zero: area is 0
        /// Positive: CCW in world coords (CW on screen)
        /// Negative: CW in world coords (CCW on screen)
        /// </summary>
        public static int GetPolygonOrientation(ReadOnlySpan<PointF> polygon, in TolerantComparer comparer)
        {
            float sum = 0f;
            for (var i = 0; i < polygon.Length - 1; ++i)
            {
                PointF curr = polygon[i];
                PointF next = polygon[i + 1];
                sum += (curr.X * next.Y) - (next.X * curr.Y);
            }

            return comparer.Sign(sum);
        }

        /// <summary>
        /// Positive: CCW in world coords (CW on screen)
        /// Negative: CW in woorld coords (CCW on screen)
        /// </summary>
        public static void EnsureOrientation(Span<PointF> polygon, int expectedOrientation, in TolerantComparer comparer)
        {
            if (GetPolygonOrientation(polygon, in comparer) * expectedOrientation < 0)
            {
                polygon.Reverse();
            }
        }
    }
}