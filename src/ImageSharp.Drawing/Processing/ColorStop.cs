// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// A struct that defines a single color stop: a color pinned to a position along a gradient.
/// </summary>
[DebuggerDisplay("ColorStop({Ratio} -> {Color}")]
public readonly struct ColorStop
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ColorStop" /> struct.
    /// </summary>
    /// <param name="ratio">
    /// The position of the stop along the gradient, where 0 is the start and 1 is the end of the gradient.
    /// </param>
    /// <param name="color">The color of the gradient at the stop position.</param>
    public ColorStop(float ratio, in Color color)
    {
        this.Ratio = ratio;
        this.Color = color;
    }

    /// <summary>
    /// Gets the position of the stop along the gradient, where 0 is the start and 1 is the end.
    /// </summary>
    public float Ratio { get; }

    /// <summary>
    /// Gets the color of the gradient at the stop position.
    /// </summary>
    public Color Color { get; }
}
