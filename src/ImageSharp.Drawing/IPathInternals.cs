// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// An interface for internal operations we don't want to expose on <see cref="IPath"/>.
/// </summary>
internal interface IPathInternals : IPath
{
    /// <summary>
    /// Returns information about a point at a given distance along a path.
    /// </summary>
    /// <param name="distance">The distance along the path to return details for.</param>
    /// <returns>
    /// The segment information.
    /// </returns>
    SegmentInfo PointAlongPath(float distance);
}
