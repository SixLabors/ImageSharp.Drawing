// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Provides options for boolean clipping operations.
/// </summary>
/// <remarks>
/// All clipping operations except for Difference are commutative.
/// </remarks>
public enum ClippingOperation
{
    /// <summary>
    /// No clipping is performed.
    /// </summary>
    None,

    /// <summary>
    /// Clips regions covered by both subject and clip polygons.
    /// </summary>
    Intersection,

    /// <summary>
    /// Clips regions covered by subject or clip polygons, or both polygons.
    /// </summary>
    Union,

    /// <summary>
    /// Clips regions covered by subject, but not clip polygons.
    /// </summary>
    Difference,

    /// <summary>
    /// Clips regions covered by subject or clip polygons, but not both.
    /// </summary>
    Xor
}
