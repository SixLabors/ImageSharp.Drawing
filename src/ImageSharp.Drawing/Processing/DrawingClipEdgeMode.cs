// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Identifies how rectangle clip edges contribute coverage.
/// </summary>
public enum DrawingClipEdgeMode : byte
{
    /// <summary>
    /// The clip has hard device-pixel edges.
    /// </summary>
    Hard = 0,

    /// <summary>
    /// The clip participates in antialias coverage.
    /// </summary>
    Antialiased = 1
}
