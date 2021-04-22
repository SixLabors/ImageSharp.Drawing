// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities
{
    /// <summary>
    /// Allows the comparison of rectangular polygons by value.
    /// </summary>
    internal static class RectangularPolygonValueComparer
    {
        public static bool Equals(RectangularPolygon x, RectangularPolygon y)
            => x.Left == y.Left
            && x.Top == y.Top
            && x.Right == y.Right
            && x.Bottom == y.Bottom;

        public static bool Equals(RectangularPolygon x, object y)
            => y is RectangularPolygon polygon && Equals(x, polygon);
    }
}
