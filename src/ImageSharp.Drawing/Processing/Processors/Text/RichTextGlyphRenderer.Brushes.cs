// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;

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
    /// <param name="transform">A transform to apply to the brush coordinates.</param>
    /// <param name="brush">The resulting brush, or <see langword="null"/> if the paint is unsupported.</param>
    /// <returns><see langword="true"/> if a brush could be created; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateBrush([NotNullWhen(true)] Paint? paint, Matrix3x2 transform, [NotNullWhen(true)] out Brush? brush)
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
    /// <param name="transform">The transform to apply to the gradient points.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns><see langword="true"/> if created; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreateLinearGradientBrush(LinearGradientPaint paint, Matrix3x2 transform, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop's alpha).
        ColorStop[] stops = ToColorStops(paint.Stops, paint.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(paint.Spread);

        PointF p0 = paint.P0;
        PointF p1 = paint.P1;
        PointF? p2 = paint.P2;

        // Apply any transform defined on the paint.
        if (!transform.IsIdentity)
        {
            p0 = Vector2.Transform(p0, transform);
            p1 = Vector2.Transform(p1, transform);

            if (p2.HasValue)
            {
                p2 = Vector2.Transform(p2.Value, transform);
            }
        }

        if (p2.HasValue)
        {
            brush = new LinearGradientBrush(p0, p1, p2.Value, mode, stops);
            return true;
        }

        brush = new LinearGradientBrush(p0, p1, mode, stops);
        return true;
    }

    /// <summary>
    /// Creates a <see cref="RadialGradientBrush"/> from a <see cref="RadialGradientPaint"/>.
    /// </summary>
    /// <param name="paint">The radial gradient paint.</param>
    /// <param name="transform">The transform to apply to the gradient center point.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns><see langword="true"/> if created; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreateRadialGradientBrush(RadialGradientPaint paint, Matrix3x2 transform, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop's alpha).
        ColorStop[] stops = ToColorStops(paint.Stops, paint.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(paint.Spread);

        // Apply any transform defined on the paint.
        PointF center0 = paint.Center0;
        PointF center1 = paint.Center1;
        if (!transform.IsIdentity)
        {
            center0 = Vector2.Transform(center0, transform);
            center1 = Vector2.Transform(center1, transform);
        }

        brush = new RadialGradientBrush(center0, paint.Radius0, center1, paint.Radius1, mode, stops);
        return true;
    }

    /// <summary>
    /// Creates a <see cref="SweepGradientBrush"/> from a <see cref="SweepGradientPaint"/>.
    /// </summary>
    /// <param name="paint">The sweep gradient paint.</param>
    /// <param name="transform">The transform to apply to the gradient center point.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns><see langword="true"/> if created; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreateSweepGradientBrush(SweepGradientPaint paint, Matrix3x2 transform, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop's alpha).
        ColorStop[] stops = ToColorStops(paint.Stops, paint.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(paint.Spread);

        // Apply any transform defined on the paint.
        PointF center = paint.Center;
        if (!transform.IsIdentity)
        {
            center = Vector2.Transform(center, transform);
        }

        brush = new SweepGradientBrush(center, paint.StartAngle, paint.EndAngle, mode, stops);
        return true;
    }

    /// <summary>
    /// Maps an <see cref="SpreadMethod"/> to <see cref="GradientRepetitionMode"/>.
    /// </summary>
    /// <param name="spread">The spread method.</param>
    /// <returns>The repetition mode.</returns>
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
    /// <returns>An array of <see cref="ColorStop"/>.</returns>
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
    /// <returns>The ImageSharp color.</returns>
    private static Color ToColor(in GlyphColor c, float opacity)
    {
        float a = Math.Clamp(c.A / 255f * Math.Clamp(opacity, 0f, 1f), 0f, 1f);
        byte aa = (byte)MathF.Round(a * 255f);
        return Color.FromPixel(new Rgba32(c.R, c.G, c.B, aa));
    }
}
