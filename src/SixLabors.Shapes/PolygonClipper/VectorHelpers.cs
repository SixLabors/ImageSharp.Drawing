// <copyright file="VectorHelpers.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Some helpers for vector data
    /// </summary>
    internal static class VectorHelpers
    {
        /// <summary>
        /// Slopeses the equal.
        /// </summary>
        /// <param name="pt1">The PT1.</param>
        /// <param name="pt2">The PT2.</param>
        /// <param name="pt3">The PT3.</param>
        /// <returns>Returns true if the slopes are equal</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool SlopesEqual(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        {
            return SlopesEqual(pt1, pt2, pt2, pt3);
        }

        /// <summary>
        /// Slopeses the equal.
        /// </summary>
        /// <param name="pt1">The PT1.</param>
        /// <param name="pt2">The PT2.</param>
        /// <param name="pt3">The PT3.</param>
        /// <param name="pt4">The PT4.</param>
        /// <returns>Returns true if the slopes are equal</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool SlopesEqual(Vector2 pt1, Vector2 pt2, Vector2 pt3, Vector2 pt4)
        {
            var dif12 = pt1 - pt2;
            var dif34 = pt3 - pt4;

            return (dif12.Y * dif34.X) - (dif12.X * dif34.Y) == 0;
        }
    }
}
