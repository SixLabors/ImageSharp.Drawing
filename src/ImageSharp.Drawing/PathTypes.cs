// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Describes the different type of paths.
/// </summary>
public enum PathTypes
{
    /// <summary>
    /// Denotes a path containing a single simple open path
    /// </summary>
    Open,

    /// <summary>
    /// Denotes a path describing a single simple closed shape
    /// </summary>
    Closed,

    /// <summary>
    /// Denotes a path containing one or more child paths that could be open or closed.
    /// </summary>
    Mixed
}
