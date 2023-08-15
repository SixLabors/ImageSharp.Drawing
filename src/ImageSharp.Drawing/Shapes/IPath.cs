// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a logic path that can be drawn.
/// </summary>
public interface IPath
{
    /// <summary>
    /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
    /// </summary>
    PathTypes PathType { get; }

    /// <summary>
    /// Gets the bounds enclosing the path.
    /// </summary>
    RectangleF Bounds { get; }

    /// <summary>
    /// Converts the <see cref="IPath" /> into a simple linear path.
    /// </summary>
    /// <returns>Returns the current <see cref="IPath" /> as simple linear path.</returns>
    IEnumerable<ISimplePath> Flatten();

    /// <summary>
    /// Transforms the path using the specified matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>A new path with the matrix applied to it.</returns>
    IPath Transform(Matrix3x2 matrix);

    /// <summary>
    /// Returns this path with all figures closed.
    /// </summary>
    /// <returns>A new close <see cref="IPath"/>.</returns>
    IPath AsClosedPath();
}
