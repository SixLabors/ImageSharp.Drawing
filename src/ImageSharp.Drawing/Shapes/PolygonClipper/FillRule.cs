// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
    /// <summary>
    /// By far the most widely used filling rules for polygons are EvenOdd
    /// and NonZero, sometimes called Alternate and Winding respectively.
    /// <see href="https://en.wikipedia.org/wiki/Nonzero-rule"/>
    /// </summary>
    /// <remarks>
    /// TODO: This overlaps with the <see cref="IntersectionRule"/> enum.
    /// We should see if we can enhance the <see cref="PolygonScanner"/> to support all these rules.
    /// </remarks>
    internal enum FillRule
    {
        EvenOdd,
        NonZero,
        Positive,
        Negative
    }
}
