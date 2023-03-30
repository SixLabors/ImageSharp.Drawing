// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities
{
    /// <summary>
    /// Allows the comparison of rectangular polygons by value.
    /// </summary>
    internal static class RectangularPolygonValueComparer
    {
        public static bool Equals(RectangularPolygon x, RectangularPolygon y, float epsilon = 0F)
            => Math.Abs(x.Left - y.Left) < epsilon
            && Math.Abs(x.Top - y.Top) < epsilon
            && Math.Abs(x.Right - y.Right) < epsilon
            && Math.Abs(x.Bottom - y.Bottom) < epsilon;

        public static bool Equals(RectangularPolygon x, object y, float epsilon = 0F)
            => y is RectangularPolygon polygon && Equals(x, polygon, epsilon);
    }
}
