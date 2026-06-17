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
    /// Creates a normalized positive-winding polygon representation of the specified path.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>
    /// A path whose contours represent the normalized filled area of <paramref name="path"/> using positive winding.
    /// </returns>
    public static IPath Normalize(this IPath path) => NormalizedShapeGenerator.NormalizeShape(path);
}
