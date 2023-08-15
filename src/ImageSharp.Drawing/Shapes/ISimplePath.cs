// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a simple (non-composite) path defined by a series of points.
/// </summary>
public interface ISimplePath
{
    /// <summary>
    /// Gets a value indicating whether this instance is a closed path.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Gets the points that make this up as a simple linear path.
    /// </summary>
    ReadOnlyMemory<PointF> Points { get; }
}
