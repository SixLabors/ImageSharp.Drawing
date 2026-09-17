// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

/// <content>
/// Utilities to translate format-agnostic paints (from Fonts) into ImageSharp.Drawing brushes.
/// </content>
internal sealed partial class RichTextGlyphRenderer
{
    /// <summary>
    /// Attempts to create an ImageSharp.Drawing <see cref="Brush"/> from a <see cref="Paint"/>.
    /// </summary>
    /// <param name="paint">The paint definition coming from the interpreter.</param>
    /// <param name="transform">The transform from the glyph's space to the drawing, applied after the paint's own transform.</param>
    /// <param name="brush">The resulting brush, or <see langword="null"/> if the paint is unsupported.</param>
    /// <returns>
    /// <see langword="true"/> if a brush could be created; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryCreateBrush([NotNullWhen(true)] Paint? paint, Matrix4x4 transform, [NotNullWhen(true)] out Brush? brush)
    {
        brush = null;

        if (paint is null)
        {
            return false;
        }

        switch (paint)
        {
            case SolidPaint sp:
                brush = new SolidBrush(ToColor(sp.Color, sp.Opacity));
                return true;

            case LinearGradientPaint lg:
                return TryCreateLinearGradientBrush(lg, transform, out brush);
            case RadialGradientPaint rg:
                return TryCreateRadialGradientBrush(rg, transform, out brush);
            case SweepGradientPaint sg:
                return TryCreateSweepGradientBrush(sg, transform, out brush);
            default:
                return false;
        }
    }

    /// <summary>
    /// Creates a <see cref="LinearGradientBrush"/> from a <see cref="LinearGradientPaint"/>.
    /// </summary>
    /// <param name="paint">The linear gradient paint.</param>
    /// <param name="transform">The transform applied after the paint's own transform.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns>
    /// <see langword="true"/> if created; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool TryCreateLinearGradientBrush(LinearGradientPaint paint, Matrix4x4 transform, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop's alpha).
        ColorStop[] stops = ToColorStops(paint.Stops, paint.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(paint.Spread);

        // The geometry stays in the paint's space. The brush maps every sample through the
        // inverse of the composed transform, so a skew or a non-uniform scale in the paint's
        // transform keeps its shape.
        Matrix4x4 gradientTransform = new Matrix4x4(paint.Transform) * transform;
        PointF p0 = paint.P0;
        PointF p1 = paint.P1;
        if (paint.P2.HasValue)
        {
            brush = new LinearGradientBrush(p0, p1, paint.P2.Value, mode, gradientTransform, stops);
            return true;
        }

        brush = new LinearGradientBrush(p0, p1, mode, gradientTransform, stops);
        return true;
    }

    /// <summary>
    /// Creates a <see cref="RadialGradientBrush"/> from a <see cref="RadialGradientPaint"/>.
    /// </summary>
    /// <param name="paint">The radial gradient paint.</param>
    /// <param name="transform">The transform applied after the paint's own transform.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns>
    /// <see langword="true"/> if created; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool TryCreateRadialGradientBrush(RadialGradientPaint paint, Matrix4x4 transform, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop's alpha).
        ColorStop[] stops = ToColorStops(paint.Stops, paint.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(paint.Spread);

        // The circles stay in the paint's space. The brush maps every sample through the
        // inverse of the composed transform, so a skew or a non-uniform scale in the paint's
        // transform draws an ellipse instead of a circle.
        Matrix4x4 gradientTransform = new Matrix4x4(paint.Transform) * transform;
        brush = new RadialGradientBrush(paint.Center0, paint.Radius0, paint.Center1, paint.Radius1, mode, gradientTransform, stops);
        return true;
    }

    /// <summary>
    /// Creates a <see cref="SweepGradientBrush"/> from a <see cref="SweepGradientPaint"/>.
    /// </summary>
    /// <param name="paint">The sweep gradient paint.</param>
    /// <param name="transform">The transform applied after the paint's own transform.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns>
    /// <see langword="true"/> if created; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool TryCreateSweepGradientBrush(SweepGradientPaint paint, Matrix4x4 transform, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop's alpha).
        ColorStop[] stops = ToColorStops(paint.Stops, paint.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(paint.Spread);

        // The center and angles stay in the paint's space. The brush maps every sample through
        // the inverse of the composed transform.
        Matrix4x4 gradientTransform = new Matrix4x4(paint.Transform) * transform;
        brush = new SweepGradientBrush(paint.Center, paint.StartAngle, paint.EndAngle, mode, gradientTransform, stops);
        return true;
    }

    /// <summary>
    /// Maps an <see cref="SpreadMethod"/> to <see cref="GradientRepetitionMode"/>.
    /// </summary>
    /// <param name="spread">The spread method.</param>
    /// <returns>
    /// The repetition mode.
    /// </returns>
    private static GradientRepetitionMode MapSpread(SpreadMethod spread)
        => spread switch
        {
            SpreadMethod.Reflect => GradientRepetitionMode.Reflect,
            SpreadMethod.Repeat => GradientRepetitionMode.Repeat,

            // Pad extends edge colors, which matches 'None' (not 'DontFill').
            _ => GradientRepetitionMode.None,
        };

    /// <summary>
    /// Converts gradient stops and applies a paint opacity multiplier.
    /// </summary>
    /// <param name="stops">The source stops.</param>
    /// <param name="paintOpacity">The paint opacity in range [0,1].</param>
    /// <returns>
    /// An array of <see cref="ColorStop"/>.
    /// </returns>
    private static ColorStop[] ToColorStops(ReadOnlySpan<GradientStop> stops, float paintOpacity)
    {
        if (stops.Length == 0)
        {
            return [];
        }

        ColorStop[] result = new ColorStop[stops.Length];

        for (int i = 0; i < stops.Length; i++)
        {
            GradientStop s = stops[i];
            Color c = ToColor(s.Color, paintOpacity);
            result[i] = new ColorStop(s.Offset, c);
        }

        return result;
    }

    /// <summary>
    /// Converts a <see cref="GlyphColor"/> with an additional opacity multiplier to ImageSharp <see cref="Color"/>.
    /// </summary>
    /// <param name="c">The glyph color.</param>
    /// <param name="opacity">The opacity multiplier in range [0,1].</param>
    /// <returns>
    /// The ImageSharp color.
    /// </returns>
    private static Color ToColor(in GlyphColor c, float opacity)
    {
        float a = Math.Clamp(c.A / 255f * Math.Clamp(opacity, 0f, 1f), 0f, 1f);
        byte aa = (byte)MathF.Round(a * 255f);
        return Color.FromPixel(new Rgba32(c.R, c.G, c.B, aa));
    }
}
