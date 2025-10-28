// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Shapes.Text;

internal static class TextUtilities
{
    public static IntersectionRule MapFillRule(FillRule fillRule)
        => fillRule switch
        {
            FillRule.EvenOdd => IntersectionRule.EvenOdd,
            FillRule.NonZero => IntersectionRule.NonZero,
            _ => IntersectionRule.NonZero,
        };

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

    public static DrawingOptions CloneOrReturnForRules(
        this DrawingOptions drawingOptions,
        IntersectionRule intersectionRule,
        PixelAlphaCompositionMode compositionMode,
        PixelColorBlendingMode colorBlendingMode)
    {
        if (drawingOptions.ShapeOptions.IntersectionRule == intersectionRule &&
            drawingOptions.GraphicsOptions.AlphaCompositionMode == compositionMode &&
            drawingOptions.GraphicsOptions.ColorBlendingMode == colorBlendingMode)
        {
            return drawingOptions;
        }

        ShapeOptions shapeOptions = drawingOptions.ShapeOptions.DeepClone();
        shapeOptions.IntersectionRule = intersectionRule;

        GraphicsOptions graphicsOptions = drawingOptions.GraphicsOptions.DeepClone();
        graphicsOptions.AlphaCompositionMode = compositionMode;
        graphicsOptions.ColorBlendingMode = colorBlendingMode;

        return new DrawingOptions(graphicsOptions, shapeOptions, drawingOptions.Transform);
    }
}
