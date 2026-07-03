// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Contains a collection of common pen styles.
/// </summary>
public static class Pens
{
    // Pattern segment lengths are multiples of the stroke width (see Pen remarks):
    // alternating filled and empty sections starting with a filled one.
    private static readonly float[] DashDotPattern = [3f, 1f, 1f, 1f];
    private static readonly float[] DashDotDotPattern = [3f, 1f, 1f, 1f, 1f, 1f];
    private static readonly float[] DottedPattern = [1f, 1f];
    private static readonly float[] DashedPattern = [3f, 1f];
    internal static readonly float[] EmptyPattern = [];

    /// <summary>
    /// Creates a solid pen without any drawing pattern.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static SolidPen Solid(Color color) => new(color);

    /// <summary>
    /// Creates a solid pen without any drawing pattern.
    /// </summary>
    /// <param name="brush">The brush.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static SolidPen Solid(Brush brush) => new(brush);

    /// <summary>
    /// Creates a solid pen without any drawing pattern.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static SolidPen Solid(Color color, float width) => new(color, width);

    /// <summary>
    /// Creates a solid pen without any drawing pattern.
    /// </summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static SolidPen Solid(Brush brush, float width) => new(brush, width);

    /// <summary>
    /// Creates a pen with a 'Dash' drawing pattern.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen Dash(Color color, float width) => new(color, width, DashedPattern);

    /// <summary>
    /// Creates a pen with a 'Dash' drawing pattern.
    /// </summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen Dash(Brush brush, float width) => new(brush, width, DashedPattern);

    /// <summary>
    /// Creates a pen with a 'Dot' drawing pattern.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen Dot(Color color, float width) => new(color, width, DottedPattern);

    /// <summary>
    /// Creates a pen with a 'Dot' drawing pattern.
    /// </summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen Dot(Brush brush, float width) => new(brush, width, DottedPattern);

    /// <summary>
    /// Creates a pen with a 'Dash Dot' drawing pattern.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen DashDot(Color color, float width) => new(color, width, DashDotPattern);

    /// <summary>
    /// Creates a pen with a 'Dash Dot' drawing pattern.
    /// </summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen DashDot(Brush brush, float width) => new(brush, width, DashDotPattern);

    /// <summary>
    /// Creates a pen with a 'Dash Dot Dot' drawing pattern.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen DashDotDot(Color color, float width) => new(color, width, DashDotDotPattern);

    /// <summary>
    /// Creates a pen with a 'Dash Dot Dot' drawing pattern.
    /// </summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <returns>The <see cref="Pen"/>.</returns>
    public static PatternPen DashDotDot(Brush brush, float width) => new(brush, width, DashDotDotPattern);
}
