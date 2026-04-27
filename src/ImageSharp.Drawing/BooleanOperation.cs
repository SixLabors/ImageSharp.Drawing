// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <inheritdoc cref="PolygonClipper.BooleanOperation" />
public enum BooleanOperation
{
    /// <inheritdoc cref="PolygonClipper.BooleanOperation.Intersection" />
    Intersection = 0,

    /// <inheritdoc cref="PolygonClipper.BooleanOperation.Union" />
    Union = 1,

    /// <inheritdoc cref="PolygonClipper.BooleanOperation.Difference" />
    Difference = 2,

    /// <inheritdoc cref="PolygonClipper.BooleanOperation.Xor" />
    Xor = 3
}
