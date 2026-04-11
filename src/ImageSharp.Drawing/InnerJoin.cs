// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Specifies how inner corners of a stroked path or polygon are rendered
/// when the path turns sharply inward. These settings control how the interior
/// edge of the stroke is joined at such corners.
/// </summary>
public enum InnerJoin
{
    /// <summary>
    /// Joins inner corners by connecting the edges with a straight line,
    /// producing a flat, beveled appearance.
    /// </summary>
    Bevel,

    /// <summary>
    /// Joins inner corners by extending the inner edges until they meet at a sharp point.
    /// This can create long, narrow joins for acute angles.
    /// </summary>
    Miter,

    /// <summary>
    /// Joins inner corners with a notched appearance,
    /// forming a small cut or indentation at the join.
    /// </summary>
    Jag,

    /// <summary>
    /// Joins inner corners using a circular arc between the edges,
    /// creating a smooth, rounded interior transition.
    /// </summary>
    Round
}
