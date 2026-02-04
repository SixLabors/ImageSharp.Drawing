// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

/// <summary>
/// By far the most widely used filling rules for polygons are EvenOdd
/// and NonZero, sometimes called Alternate and Winding respectively.
/// <see href="https://en.wikipedia.org/wiki/Nonzero-rule"/>
/// </summary>
/// <remarks>
/// TODO: This overlaps with the <see cref="IntersectionRule"/> enum.
/// We should see if we can enhance the <see cref="PolygonScanner"/> to support all these rules.
/// </remarks>
internal enum ClipperFillRule
{
    EvenOdd,
    NonZero,
    Positive,
    Negative
}
