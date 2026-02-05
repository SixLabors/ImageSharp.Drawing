// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

/// <summary>
/// Specifies how a vertex should be joined with adjacent paths during polygon operations.
/// </summary>
internal enum JoinWith
{
    /// <summary>
    /// No joining operation.
    /// </summary>
    None,

    /// <summary>
    /// Join with the left adjacent path.
    /// </summary>
    Left,

    /// <summary>
    /// Join with the right adjacent path.
    /// </summary>
    Right
}
