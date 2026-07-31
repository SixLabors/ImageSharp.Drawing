// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a boundary path exported from an integer region while retaining its rectangle-set form.
/// </summary>
/// <remarks>
/// Region clipping has two valid representations in this codebase: ordinary path geometry
/// and an exact integer rectangle set. <see cref="Region.ToPath()"/> must return an
/// <see cref="IPath"/>, but callers that are still in untransformed integer region space
/// can use this marker to continue exact region operations without converting the region
/// into generic polygon clipping. Once the path is transformed, the marker is dropped and
/// the result is treated as normal path geometry.
/// </remarks>
internal interface IRegionPath : IPath
{
    /// <summary>
    /// Gets the normalized non-overlapping rectangles that cover the same area as the boundary path.
    /// </summary>
    public IReadOnlyList<Rectangle> Rectangles { get; }
}
