// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

/// <summary>
/// Allows the comparison of rectangle polygons by value.
/// </summary>
internal static class RectanglePolygonValueComparer
{
    public const float DefaultTolerance = 1e-05F;

    public static bool Equals(RectanglePolygon x, RectanglePolygon y, float epsilon = DefaultTolerance)
        => Math.Abs(x.Left - y.Left) < epsilon
        && Math.Abs(x.Top - y.Top) < epsilon
        && Math.Abs(x.Right - y.Right) < epsilon
        && Math.Abs(x.Bottom - y.Bottom) < epsilon;

    public static bool Equals(RectanglePolygon x, object y, float epsilon = DefaultTolerance)
        => y is RectanglePolygon polygon && Equals(x, polygon, epsilon);
}
