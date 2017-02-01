// <copyright file="Helpers.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Some helpers for vector data
    /// </summary>
    internal static class Helpers
    {
        /// <summary>
        /// Swaps the values in val1 and val2.
        /// </summary>
        /// <param name="val1">The val1.</param>
        /// <param name="val2">The val2.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Swap(ref float val1, ref float val2)
        {
            float tmp = val1;
            val1 = val2;
            val2 = tmp;
        }

        /// <summary>
        /// Rounds the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>the value rounded</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(double value)
        {
            return value < 0 ? (float)(value - 0.5) : (float)(value + 0.5);
        }

        /// <summary>
        /// Slopeses the equal.
        /// </summary>
        /// <param name="pt1">The PT1.</param>
        /// <param name="pt2">The PT2.</param>
        /// <param name="pt3">The PT3.</param>
        /// <returns>Returns true if the slopes are equal</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SlopesEqual(Vector2 pt1, Vector2 pt2, Vector2 pt3)
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
        public static bool SlopesEqual(Vector2 pt1, Vector2 pt2, Vector2 pt3, Vector2 pt4)
        {
            var dif12 = pt1 - pt2;
            var dif34 = pt3 - pt4;

            return (dif12.Y * dif34.X) - (dif12.X * dif34.Y) == 0;
        }

        /// <summary>
        /// Dxes the specified PT2.
        /// </summary>
        /// <param name="pt1">The PT1.</param>
        /// <param name="pt2">The PT2.</param>
        /// <returns></returns>
        public static double Dx(this Vector2 pt1, Vector2 pt2)
        {
            if (pt1.Y == pt2.Y)
            {
                return Constants.HorizontalDeltaLimit;
            }
            else
            {
                return (double)(pt2.X - pt1.X) / (pt2.Y - pt1.Y);
            }
        }

        public static bool GetOverlap(float a1, float a2, float b1, float b2, out float left, out float right)
        {
            if (a1 < a2)
            {
                if (b1 < b2)
                {
                    left = Math.Max(a1, b1);
                    right = Math.Min(a2, b2);
                }
                else
                {
                    left = Math.Max(a1, b2);
                    right = Math.Min(a2, b1);
                }
            }
            else
            {
                if (b1 < b2)
                {
                    left = Math.Max(a2, b1);
                    right = Math.Min(a1, b2);
                }
                else
                {
                    left = Math.Max(a2, b2);
                    right = Math.Min(a1, b1);
                }
            }

            return left < right;
        }
    }
}
