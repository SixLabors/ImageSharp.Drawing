// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities
{
    /// <summary>
    /// Allows the comparison of rectangular polygons by value.
    /// </summary>
    internal static class RectangularPolygonValueComparer
    {
        public const float DefaultTolerance = ClipperUtils.FloatingPointTolerance;

        public static bool Equals(RectangularPolygon x, RectangularPolygon y, float epsilon = DefaultTolerance)
            => Math.Abs(x.Left - y.Left) < epsilon
            && Math.Abs(x.Top - y.Top) < epsilon
            && Math.Abs(x.Right - y.Right) < epsilon
            && Math.Abs(x.Bottom - y.Bottom) < epsilon;

        public static bool Equals(RectangularPolygon x, object y, float epsilon = DefaultTolerance)
            => y is RectangularPolygon polygon && Equals(x, polygon, epsilon);
    }
}
