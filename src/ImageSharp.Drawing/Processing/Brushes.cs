// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// A collection of methods for creating generic brushes.
/// </summary>
public static partial class Brushes
{
    /// <summary>
    /// Creates a brush that paints a solid color.
    /// </summary>
    /// <param name="color">The brush color.</param>
    /// <returns>A new <see cref="SolidBrush"/>.</returns>
    public static SolidBrush Solid(Color color) => new(color);

    /// <summary>
    /// Creates a brush that paints horizontal line hatching using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Horizontal(Color foreColor)
        => new(foreColor, Color.Transparent, HorizontalPattern);

    /// <summary>
    /// Creates a brush that paints horizontal line hatching using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Horizontal(Color foreColor, Color backColor)
        => new(foreColor, backColor, HorizontalPattern);

    /// <summary>
    /// Creates a brush that paints horizontal line hatching for the minimum hatch style using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Min(Color foreColor)
        => new(foreColor, Color.Transparent, HorizontalPattern);

    /// <summary>
    /// Creates a brush that paints horizontal line hatching for the minimum hatch style using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Min(Color foreColor, Color backColor)
        => new(foreColor, backColor, HorizontalPattern);

    /// <summary>
    /// Creates a brush that paints vertical line hatching using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Vertical(Color foreColor)
        => new(foreColor, Color.Transparent, VerticalPattern);

    /// <summary>
    /// Creates a brush that paints vertical line hatching using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Vertical(Color foreColor, Color backColor)
        => new(foreColor, backColor, VerticalPattern);

    /// <summary>
    /// Creates a brush that paints diagonal line hatching from upper left to lower right using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush ForwardDiagonal(Color foreColor)
        => new(foreColor, Color.Transparent, ForwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints diagonal line hatching from upper left to lower right using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush ForwardDiagonal(Color foreColor, Color backColor)
        => new(foreColor, backColor, ForwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints diagonal line hatching from upper right to lower left using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush BackwardDiagonal(Color foreColor)
        => new(foreColor, Color.Transparent, BackwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints diagonal line hatching from upper right to lower left using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush BackwardDiagonal(Color foreColor, Color backColor)
        => new(foreColor, backColor, BackwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints intersecting horizontal and vertical line hatching using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Cross(Color foreColor) => new(foreColor, Color.Transparent, CrossPattern);

    /// <summary>
    /// Creates a brush that paints intersecting horizontal and vertical line hatching using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Cross(Color foreColor, Color backColor) => new(foreColor, backColor, CrossPattern);

    /// <summary>
    /// Creates a brush that paints intersecting forward and backward diagonal line hatching using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DiagonalCross(Color foreColor) => new(foreColor, Color.Transparent, DiagonalCrossPattern);

    /// <summary>
    /// Creates a brush that paints intersecting forward and backward diagonal line hatching using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DiagonalCross(Color foreColor, Color backColor) => new(foreColor, backColor, DiagonalCrossPattern);

    /// <summary>
    /// Creates a brush that paints a 5-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 5:95.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent05(Color foreColor) => new(foreColor, Color.Transparent, Percent05Pattern);

    /// <summary>
    /// Creates a brush that paints a 5-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 5:95.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent05(Color foreColor, Color backColor) => new(foreColor, backColor, Percent05Pattern);

    /// <summary>
    /// Creates a brush that paints a 10-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 10:90.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent10(Color foreColor)
        => new(foreColor, Color.Transparent, Percent10Pattern);

    /// <summary>
    /// Creates a brush that paints a 10-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 10:90.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent10(Color foreColor, Color backColor)
        => new(foreColor, backColor, Percent10Pattern);

    /// <summary>
    /// Creates a brush that paints a 20-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 20:80.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent20(Color foreColor)
        => new(foreColor, Color.Transparent, Percent20Pattern);

    /// <summary>
    /// Creates a brush that paints a 20-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 20:80.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent20(Color foreColor, Color backColor)
        => new(foreColor, backColor, Percent20Pattern);

    /// <summary>
    /// Creates a brush that paints a 25-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 25:75.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent25(Color foreColor) => new(foreColor, Color.Transparent, Percent25Pattern);

    /// <summary>
    /// Creates a brush that paints a 25-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 25:75.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent25(Color foreColor, Color backColor) => new(foreColor, backColor, Percent25Pattern);

    /// <summary>
    /// Creates a brush that paints a 30-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 30:70.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent30(Color foreColor) => new(foreColor, Color.Transparent, Percent30Pattern);

    /// <summary>
    /// Creates a brush that paints a 30-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 30:70.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent30(Color foreColor, Color backColor) => new(foreColor, backColor, Percent30Pattern);

    /// <summary>
    /// Creates a brush that paints a 40-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 40:60.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent40(Color foreColor) => new(foreColor, Color.Transparent, Percent40Pattern);

    /// <summary>
    /// Creates a brush that paints a 40-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 40:60.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent40(Color foreColor, Color backColor) => new(foreColor, backColor, Percent40Pattern);

    /// <summary>
    /// Creates a brush that paints a 50-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 50:50.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent50(Color foreColor) => new(foreColor, Color.Transparent, Percent50Pattern);

    /// <summary>
    /// Creates a brush that paints a 50-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 50:50.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent50(Color foreColor, Color backColor) => new(foreColor, backColor, Percent50Pattern);

    /// <summary>
    /// Creates a brush that paints a 60-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 60:40.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent60(Color foreColor) => new(foreColor, Color.Transparent, Percent60Pattern);

    /// <summary>
    /// Creates a brush that paints a 60-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 60:40.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent60(Color foreColor, Color backColor) => new(foreColor, backColor, Percent60Pattern);

    /// <summary>
    /// Creates a brush that paints a 70-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 70:30.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent70(Color foreColor) => new(foreColor, Color.Transparent, Percent70Pattern);

    /// <summary>
    /// Creates a brush that paints a 70-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 70:30.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent70(Color foreColor, Color backColor) => new(foreColor, backColor, Percent70Pattern);

    /// <summary>
    /// Creates a brush that paints a 75-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 75:25.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent75(Color foreColor) => new(foreColor, Color.Transparent, Percent75Pattern);

    /// <summary>
    /// Creates a brush that paints a 75-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 75:25.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent75(Color foreColor, Color backColor) => new(foreColor, backColor, Percent75Pattern);

    /// <summary>
    /// Creates a brush that paints an 80-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 80:20.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent80(Color foreColor) => new(foreColor, Color.Transparent, Percent80Pattern);

    /// <summary>
    /// Creates a brush that paints an 80-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 80:20.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent80(Color foreColor, Color backColor) => new(foreColor, backColor, Percent80Pattern);

    /// <summary>
    /// Creates a brush that paints a 90-percent hatch using the foreground color on a transparent background; the foreground-to-background color ratio is 90:10.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent90(Color foreColor) => new(foreColor, Color.Transparent, Percent90Pattern);

    /// <summary>
    /// Creates a brush that paints a 90-percent hatch using the specified foreground and background colors; the foreground-to-background color ratio is 90:10.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Percent90(Color foreColor, Color backColor) => new(foreColor, backColor, Percent90Pattern);

    /// <summary>
    /// Creates a brush that paints downward diagonal lines spaced more closely than ForwardDiagonal using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightDownwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, LightDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints downward diagonal lines spaced more closely than ForwardDiagonal using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightDownwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, LightDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints upward diagonal lines spaced more closely than BackwardDiagonal using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightUpwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, LightUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints upward diagonal lines spaced more closely than BackwardDiagonal using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightUpwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, LightUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints thicker downward diagonal lines spaced more closely than ForwardDiagonal using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkDownwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, DarkDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints thicker downward diagonal lines spaced more closely than ForwardDiagonal using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkDownwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, DarkDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints thicker upward diagonal lines spaced more closely than BackwardDiagonal using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkUpwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, DarkUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints thicker upward diagonal lines spaced more closely than BackwardDiagonal using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkUpwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, DarkUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints wide downward diagonal lines with ForwardDiagonal spacing using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush WideDownwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, WideDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints wide downward diagonal lines with ForwardDiagonal spacing using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush WideDownwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, WideDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints wide upward diagonal lines with BackwardDiagonal spacing using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush WideUpwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, WideUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints wide upward diagonal lines with BackwardDiagonal spacing using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush WideUpwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, WideUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints vertical lines spaced more closely than Vertical using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightVertical(Color foreColor) => new(foreColor, Color.Transparent, LightVerticalPattern);

    /// <summary>
    /// Creates a brush that paints vertical lines spaced more closely than Vertical using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightVertical(Color foreColor, Color backColor) => new(foreColor, backColor, LightVerticalPattern);

    /// <summary>
    /// Creates a brush that paints horizontal lines spaced more closely than Horizontal using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightHorizontal(Color foreColor) => new(foreColor, Color.Transparent, LightHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints horizontal lines spaced more closely than Horizontal using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LightHorizontal(Color foreColor, Color backColor) => new(foreColor, backColor, LightHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints narrow vertical lines spaced more closely than LightVertical using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush NarrowVertical(Color foreColor) => new(foreColor, Color.Transparent, NarrowVerticalPattern);

    /// <summary>
    /// Creates a brush that paints narrow vertical lines spaced more closely than LightVertical using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush NarrowVertical(Color foreColor, Color backColor) => new(foreColor, backColor, NarrowVerticalPattern);

    /// <summary>
    /// Creates a brush that paints narrow horizontal lines spaced more closely than LightHorizontal using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush NarrowHorizontal(Color foreColor) => new(foreColor, Color.Transparent, NarrowHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints narrow horizontal lines spaced more closely than LightHorizontal using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush NarrowHorizontal(Color foreColor, Color backColor) => new(foreColor, backColor, NarrowHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints thicker vertical lines spaced more closely than Vertical using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkVertical(Color foreColor) => new(foreColor, Color.Transparent, DarkVerticalPattern);

    /// <summary>
    /// Creates a brush that paints thicker vertical lines spaced more closely than Vertical using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkVertical(Color foreColor, Color backColor) => new(foreColor, backColor, DarkVerticalPattern);

    /// <summary>
    /// Creates a brush that paints thicker horizontal lines spaced more closely than Horizontal using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkHorizontal(Color foreColor) => new(foreColor, Color.Transparent, DarkHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints thicker horizontal lines spaced more closely than Horizontal using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DarkHorizontal(Color foreColor, Color backColor) => new(foreColor, backColor, DarkHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints dashed diagonal lines from upper left to lower right using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedDownwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, DashedDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints dashed diagonal lines from upper left to lower right using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedDownwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, DashedDownwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints dashed diagonal lines from upper right to lower left using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedUpwardDiagonal(Color foreColor) => new(foreColor, Color.Transparent, DashedUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints dashed diagonal lines from upper right to lower left using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedUpwardDiagonal(Color foreColor, Color backColor) => new(foreColor, backColor, DashedUpwardDiagonalPattern);

    /// <summary>
    /// Creates a brush that paints dashed horizontal lines using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedHorizontal(Color foreColor) => new(foreColor, Color.Transparent, DashedHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints dashed horizontal lines using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedHorizontal(Color foreColor, Color backColor) => new(foreColor, backColor, DashedHorizontalPattern);

    /// <summary>
    /// Creates a brush that paints dashed vertical lines using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedVertical(Color foreColor) => new(foreColor, Color.Transparent, DashedVerticalPattern);

    /// <summary>
    /// Creates a brush that paints dashed vertical lines using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DashedVertical(Color foreColor, Color backColor) => new(foreColor, backColor, DashedVerticalPattern);

    /// <summary>
    /// Creates a brush that paints a small confetti-style hatch using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SmallConfetti(Color foreColor) => new(foreColor, Color.Transparent, SmallConfettiPattern);

    /// <summary>
    /// Creates a brush that paints a small confetti-style hatch using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SmallConfetti(Color foreColor, Color backColor) => new(foreColor, backColor, SmallConfettiPattern);

    /// <summary>
    /// Creates a brush that paints a confetti-style hatch with larger pieces than SmallConfetti using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LargeConfetti(Color foreColor) => new(foreColor, Color.Transparent, LargeConfettiPattern);

    /// <summary>
    /// Creates a brush that paints a confetti-style hatch with larger pieces than SmallConfetti using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LargeConfetti(Color foreColor, Color backColor) => new(foreColor, backColor, LargeConfettiPattern);

    /// <summary>
    /// Creates a brush that paints horizontal lines formed from zigzags using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush ZigZag(Color foreColor) => new(foreColor, Color.Transparent, ZigZagPattern);

    /// <summary>
    /// Creates a brush that paints horizontal lines formed from zigzags using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush ZigZag(Color foreColor, Color backColor) => new(foreColor, backColor, ZigZagPattern);

    /// <summary>
    /// Creates a brush that paints horizontal lines formed from wave shapes using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Wave(Color foreColor) => new(foreColor, Color.Transparent, WavePattern);

    /// <summary>
    /// Creates a brush that paints horizontal lines formed from wave shapes using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Wave(Color foreColor, Color backColor) => new(foreColor, backColor, WavePattern);

    /// <summary>
    /// Creates a brush that paints staggered brick shapes running diagonally upward using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DiagonalBrick(Color foreColor) => new(foreColor, Color.Transparent, DiagonalBrickPattern);

    /// <summary>
    /// Creates a brush that paints staggered brick shapes running diagonally upward using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DiagonalBrick(Color foreColor, Color backColor) => new(foreColor, backColor, DiagonalBrickPattern);

    /// <summary>
    /// Creates a brush that paints staggered brick shapes arranged horizontally using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush HorizontalBrick(Color foreColor) => new(foreColor, Color.Transparent, HorizontalBrickPattern);

    /// <summary>
    /// Creates a brush that paints staggered brick shapes arranged horizontally using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush HorizontalBrick(Color foreColor, Color backColor) => new(foreColor, backColor, HorizontalBrickPattern);

    /// <summary>
    /// Creates a brush that paints a woven-material hatch using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Weave(Color foreColor) => new(foreColor, Color.Transparent, WeavePattern);

    /// <summary>
    /// Creates a brush that paints a woven-material hatch using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Weave(Color foreColor, Color backColor) => new(foreColor, backColor, WeavePattern);

    /// <summary>
    /// Creates a brush that paints a plaid-material hatch using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Plaid(Color foreColor) => new(foreColor, Color.Transparent, PlaidPattern);

    /// <summary>
    /// Creates a brush that paints a plaid-material hatch using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Plaid(Color foreColor, Color backColor) => new(foreColor, backColor, PlaidPattern);

    /// <summary>
    /// Creates a brush that paints a divot-style hatch using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Divot(Color foreColor) => new(foreColor, Color.Transparent, DivotPattern);

    /// <summary>
    /// Creates a brush that paints a divot-style hatch using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Divot(Color foreColor, Color backColor) => new(foreColor, backColor, DivotPattern);

    /// <summary>
    /// Creates a brush that paints intersecting horizontal and vertical dotted lines using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DottedGrid(Color foreColor) => new(foreColor, Color.Transparent, DottedGridPattern);

    /// <summary>
    /// Creates a brush that paints intersecting horizontal and vertical dotted lines using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DottedGrid(Color foreColor, Color backColor) => new(foreColor, backColor, DottedGridPattern);

    /// <summary>
    /// Creates a brush that paints intersecting forward and backward diagonal dotted lines using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DottedDiamond(Color foreColor) => new(foreColor, Color.Transparent, DottedDiamondPattern);

    /// <summary>
    /// Creates a brush that paints intersecting forward and backward diagonal dotted lines using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush DottedDiamond(Color foreColor, Color backColor) => new(foreColor, backColor, DottedDiamondPattern);

    /// <summary>
    /// Creates a brush that paints layered shingle shapes running diagonally downward using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Shingle(Color foreColor) => new(foreColor, Color.Transparent, ShinglePattern);

    /// <summary>
    /// Creates a brush that paints layered shingle shapes running diagonally downward using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Shingle(Color foreColor, Color backColor) => new(foreColor, backColor, ShinglePattern);

    /// <summary>
    /// Creates a brush that paints a trellis-style hatch using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Trellis(Color foreColor) => new(foreColor, Color.Transparent, TrellisPattern);

    /// <summary>
    /// Creates a brush that paints a trellis-style hatch using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Trellis(Color foreColor, Color backColor) => new(foreColor, backColor, TrellisPattern);

    /// <summary>
    /// Creates a brush that paints adjacent sphere-like shapes using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Sphere(Color foreColor) => new(foreColor, Color.Transparent, SpherePattern);

    /// <summary>
    /// Creates a brush that paints adjacent sphere-like shapes using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush Sphere(Color foreColor, Color backColor) => new(foreColor, backColor, SpherePattern);

    /// <summary>
    /// Creates a brush that paints intersecting horizontal and vertical lines spaced more closely than Cross using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SmallGrid(Color foreColor) => new(foreColor, Color.Transparent, SmallGridPattern);

    /// <summary>
    /// Creates a brush that paints intersecting horizontal and vertical lines spaced more closely than Cross using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SmallGrid(Color foreColor, Color backColor) => new(foreColor, backColor, SmallGridPattern);

    /// <summary>
    /// Creates a brush that paints a small checkerboard hatch using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SmallCheckerBoard(Color foreColor) => new(foreColor, Color.Transparent, SmallCheckerBoardPattern);

    /// <summary>
    /// Creates a brush that paints a small checkerboard hatch using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SmallCheckerBoard(Color foreColor, Color backColor) => new(foreColor, backColor, SmallCheckerBoardPattern);

    /// <summary>
    /// Creates a brush that paints a checkerboard hatch with larger squares than SmallCheckerBoard using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LargeCheckerBoard(Color foreColor) => new(foreColor, Color.Transparent, LargeCheckerBoardPattern);

    /// <summary>
    /// Creates a brush that paints a checkerboard hatch with larger squares than SmallCheckerBoard using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush LargeCheckerBoard(Color foreColor, Color backColor) => new(foreColor, backColor, LargeCheckerBoardPattern);

    /// <summary>
    /// Creates a brush that paints outlined diamond shapes formed by crossing diagonal lines using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush OutlinedDiamond(Color foreColor) => new(foreColor, Color.Transparent, OutlinedDiamondPattern);

    /// <summary>
    /// Creates a brush that paints outlined diamond shapes formed by crossing diagonal lines using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush OutlinedDiamond(Color foreColor, Color backColor) => new(foreColor, backColor, OutlinedDiamondPattern);

    /// <summary>
    /// Creates a brush that paints a filled diamond checkerboard hatch using the foreground color on a transparent background.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SolidDiamond(Color foreColor) => new(foreColor, Color.Transparent, SolidDiamondPattern);

    /// <summary>
    /// Creates a brush that paints a filled diamond checkerboard hatch using the specified foreground and background colors.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    /// <returns>A new <see cref="PatternBrush"/>.</returns>
    public static PatternBrush SolidDiamond(Color foreColor, Color backColor) => new(foreColor, backColor, SolidDiamondPattern);
}
