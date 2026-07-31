// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a collection of paths that can be enumerated and transformed as a single unit.
/// </summary>
public interface IPathCollection : IEnumerable<IPath>
{
    /// <summary>
    /// Gets the bounds enclosing all paths in the collection.
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Transforms all paths in the collection using the specified matrix.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>A new path collection with the matrix applied to it.</returns>
    public IPathCollection Transform(Matrix4x4 matrix);
}
