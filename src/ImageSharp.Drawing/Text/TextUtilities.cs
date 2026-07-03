// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Text;

/// <summary>
/// Provides mapping helpers between SixLabors.Fonts rendering enums and their
/// ImageSharp.Drawing equivalents, plus cloning utilities that avoid allocating
/// new options instances when the requested rules already match.
/// </summary>
internal static class TextUtilities
{
    /// <summary>
    /// Maps a font <see cref="FillRule"/> to the equivalent <see cref="IntersectionRule"/>.
    /// </summary>
    /// <param name="fillRule">The font fill rule.</param>
    /// <returns>
    /// The equivalent intersection rule. Unrecognized values map to
    /// <see cref="IntersectionRule.NonZero"/>.
    /// </returns>
    public static IntersectionRule MapFillRule(FillRule fillRule)
        => fillRule switch
        {
            FillRule.EvenOdd => IntersectionRule.EvenOdd,
            FillRule.NonZero => IntersectionRule.NonZero,
            _ => IntersectionRule.NonZero,
        };

    /// <summary>
    /// Maps a font <see cref="CompositeMode"/> to the equivalent Porter-Duff
    /// <see cref="PixelAlphaCompositionMode"/>.
    /// </summary>
    /// <param name="mode">The font composite mode.</param>
    /// <returns>
    /// The equivalent alpha composition mode. Separable blend modes (Plus, Screen, etc.)
    /// carry no Porter-Duff alpha behavior of their own and map to
    /// <see cref="PixelAlphaCompositionMode.SrcOver"/>; their color contribution is
    /// handled by <see cref="MapBlendingMode(CompositeMode)"/>.
    /// </returns>
    public static PixelAlphaCompositionMode MapCompositionMode(CompositeMode mode)
        => mode switch
        {
            CompositeMode.Clear => PixelAlphaCompositionMode.Clear,
            CompositeMode.Src => PixelAlphaCompositionMode.Src,
            CompositeMode.Dest => PixelAlphaCompositionMode.Dest,
            CompositeMode.SrcOver => PixelAlphaCompositionMode.SrcOver,
            CompositeMode.DestOver => PixelAlphaCompositionMode.DestOver,
            CompositeMode.SrcIn => PixelAlphaCompositionMode.SrcIn,
            CompositeMode.DestIn => PixelAlphaCompositionMode.DestIn,
            CompositeMode.SrcOut => PixelAlphaCompositionMode.SrcOut,
            CompositeMode.DestOut => PixelAlphaCompositionMode.DestOut,
            CompositeMode.SrcAtop => PixelAlphaCompositionMode.SrcAtop,
            CompositeMode.DestAtop => PixelAlphaCompositionMode.DestAtop,
            CompositeMode.Xor => PixelAlphaCompositionMode.Xor,
            _ => PixelAlphaCompositionMode.SrcOver,
        };

    /// <summary>
    /// Maps a font <see cref="CompositeMode"/> to the equivalent <see cref="PixelColorBlendingMode"/>.
    /// </summary>
    /// <param name="mode">The font composite mode.</param>
    /// <returns>
    /// The equivalent color blending mode. Pure Porter-Duff modes and unsupported blend
    /// modes map to <see cref="PixelColorBlendingMode.Normal"/>.
    /// </returns>
    public static PixelColorBlendingMode MapBlendingMode(CompositeMode mode)
        => mode switch
        {
            CompositeMode.Plus => PixelColorBlendingMode.Add,
            CompositeMode.Screen => PixelColorBlendingMode.Screen,
            CompositeMode.Overlay => PixelColorBlendingMode.Overlay,
            CompositeMode.Darken => PixelColorBlendingMode.Darken,
            CompositeMode.Lighten => PixelColorBlendingMode.Lighten,
            CompositeMode.HardLight => PixelColorBlendingMode.HardLight,
            CompositeMode.Multiply => PixelColorBlendingMode.Multiply,

            // TODO: We do not support the following separate alpha blending modes:
            // - ColorDodge, ColorBurn, SoftLight, Difference, Exclusion
            // TODO: We do not support the non-alpha blending modes.
            // - Hue, Saturation, Color, Luminosity
            _ => PixelColorBlendingMode.Normal
        };

    /// <summary>
    /// Returns <paramref name="drawingOptions"/> unchanged when its rules already match the
    /// requested values; otherwise returns a deep clone with the requested rules applied.
    /// This avoids allocating per glyph layer in the common case where a layer uses
    /// the same modes as the surrounding text.
    /// </summary>
    /// <param name="drawingOptions">The source drawing options.</param>
    /// <param name="intersectionRule">The required intersection rule.</param>
    /// <param name="compositionMode">The required alpha composition mode.</param>
    /// <param name="colorBlendingMode">The required color blending mode.</param>
    /// <returns>
    /// The original instance when it already matches; otherwise a configured clone.
    /// </returns>
    public static DrawingOptions CloneOrReturnForRules(
        this DrawingOptions drawingOptions,
        IntersectionRule intersectionRule,
        PixelAlphaCompositionMode compositionMode,
        PixelColorBlendingMode colorBlendingMode)
    {
        if (drawingOptions.IntersectionRule == intersectionRule &&
            drawingOptions.GraphicsOptions.AlphaCompositionMode == compositionMode &&
            drawingOptions.GraphicsOptions.ColorBlendingMode == colorBlendingMode)
        {
            return drawingOptions;
        }

        GraphicsOptions graphicsOptions = drawingOptions.GraphicsOptions.DeepClone();
        graphicsOptions.AlphaCompositionMode = compositionMode;
        graphicsOptions.ColorBlendingMode = colorBlendingMode;

        return new DrawingOptions(graphicsOptions, intersectionRule, drawingOptions.Transform);
    }

    /// <summary>
    /// Returns <paramref name="graphicsOptions"/> unchanged when its blend modes already
    /// match the requested values; otherwise returns a deep clone with the requested
    /// modes applied.
    /// </summary>
    /// <param name="graphicsOptions">The source graphics options.</param>
    /// <param name="compositionMode">The required alpha composition mode.</param>
    /// <param name="colorBlendingMode">The required color blending mode.</param>
    /// <returns>
    /// The original instance when it already matches; otherwise a configured clone.
    /// </returns>
    public static GraphicsOptions CloneOrReturnForRules(
        this GraphicsOptions graphicsOptions,
        PixelAlphaCompositionMode compositionMode,
        PixelColorBlendingMode colorBlendingMode)
    {
        if (graphicsOptions.AlphaCompositionMode == compositionMode &&
            graphicsOptions.ColorBlendingMode == colorBlendingMode)
        {
            return graphicsOptions;
        }

        GraphicsOptions clone = graphicsOptions.DeepClone();
        clone.AlphaCompositionMode = compositionMode;
        clone.ColorBlendingMode = colorBlendingMode;
        return clone;
    }
}
