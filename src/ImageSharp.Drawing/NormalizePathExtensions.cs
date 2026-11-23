// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.PolygonGeometry;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Provides extension methods to <see cref="IPath"/> that allow paths to be normalized.
/// </summary>
public static class NormalizePathExtensions
{
    /// <summary>
    /// Resolves self-intersections and overlaps using the non-zero fill rule.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>
    /// A path whose contours enclose the regions of <paramref name="path"/> with non-zero winding.
    /// </returns>
    public static IPath Normalize(this IPath path) => NormalizedShapeGenerator.NormalizeShape(path);
}
