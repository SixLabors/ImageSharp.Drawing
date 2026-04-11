// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Specifies the shape to be used at the ends of open lines or paths when stroking.
/// </summary>
public enum LineCap
{
    /// <summary>
    /// The stroke ends exactly at the endpoint.
    /// No extension is added beyond the path's end coordinates.
    /// </summary>
    Butt,

    /// <summary>
    /// The stroke extends beyond the endpoint by half the line width,
    /// producing a square edge.
    /// </summary>
    Square,

    /// <summary>
    /// The stroke ends with a semicircular cap,
    /// extending beyond the endpoint by half the line width.
    /// </summary>
    Round
}
