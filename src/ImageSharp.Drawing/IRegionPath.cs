// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a path backed by integer region rectangles.
/// </summary>
internal interface IRegionPath : IPath
{
    /// <summary>
    /// Gets the non-overlapping rectangles that describe the region.
    /// </summary>
    public IReadOnlyList<Rectangle> Rectangles { get; }
}
