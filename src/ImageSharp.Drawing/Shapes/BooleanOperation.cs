// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Specifies the type of boolean operation to perform on polygons.
/// </summary>
public enum BooleanOperation
{
    /// <summary>
    /// The intersection operation, which results in the area common to both polygons.
    /// </summary>
    Intersection = 0,

    /// <summary>
    /// The union operation, which results in the combined area of both polygons.
    /// </summary>
    Union = 1,

    /// <summary>
    /// The difference operation, which subtracts the clipping polygon from the subject polygon.
    /// </summary>
    Difference = 2,

    /// <summary>
    /// The exclusive OR (XOR) operation, which results in the area covered by exactly one polygon,
    /// excluding the overlapping areas.
    /// </summary>
    Xor = 3
}
