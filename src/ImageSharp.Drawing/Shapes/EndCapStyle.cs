// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// The style to apply to the end cap when generating an outline.
/// </summary>
public enum EndCapStyle
{
    /// <summary>
    /// The outline stops exactly at the end of the path.
    /// </summary>
    Butt = 0,

    /// <summary>
    /// The outline extends with a rounded style passed the end of the path.
    /// </summary>
    Round = 1,

    /// <summary>
    /// The outlines ends squared off passed the end of the path.
    /// </summary>
    Square = 2,

    /// <summary>
    /// The outline is treated as a polygon.
    /// </summary>
    Polygon = 3,

    /// <summary>
    /// The outlines ends are joined and the path treated as a polyline
    /// </summary>
    Joined = 4
}

/// <summary>
/// Specifies the shape to be used at the ends of open lines or paths when stroking.
/// </summary>
internal enum LineCap
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
