// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
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
    /// <param name="brush">The resulting brush, or <see langword="null"/> if the paint is unsupported.</param>
    /// <returns><see langword="true"/> if a brush could be created; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateBrush(Paint? paint, [NotNullWhen(true)] out Brush? brush)
    {
        brush = null;

        if (paint is null)
        {
            return false;
        }

        // TODO: Do we need to apply the transform assigned to th underlying builder here?
        switch (paint)
        {
            case SolidPaint sp:
                brush = new SolidBrush(ToColor(sp.Color, sp.Opacity));
                return true;

            case LinearGradientPaint lg:
                return TryCreateLinearGradientBrush(lg, out brush);
            case RadialGradientPaint rg:
                return TryCreateRadialGradientBrush(rg, out brush);
            case SweepGradientPaint sg:
                return TryCreateSweepGradientBrush(sg, out brush);
            default:
                return false;
        }
    }

    /// <summary>
    /// Creates a <see cref="LinearGradientBrush"/> from a <see cref="LinearGradientPaint"/>.
    /// </summary>
    /// <param name="lg">The linear gradient paint.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns><see langword="true"/> if created; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreateLinearGradientBrush(LinearGradientPaint lg, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop’s alpha).
        ColorStop[] stops = ToColorStops(lg.Stops, lg.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(lg.Spread);

        PointF p0 = lg.P0;
        PointF p1 = lg.P1;

        // Degenerate gradient, fall back to solid using last stop.
        if (ApproximatelyEqual(p0, p1))
        {
            // TODO: Consider using this.currentColor instead?
            Color fallback = stops.Length > 0 ? stops[^1].Color : Color.Black;
            brush = new SolidBrush(fallback);
            return true;
        }

        brush = new LinearGradientBrush(p0, p1, mode, stops);
        return true;
    }

    /// <summary>
    /// Creates a <see cref="RadialGradientBrush"/> from a <see cref="RadialGradientPaint"/>.
    /// </summary>
    /// <param name="rg">The radial gradient paint.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns><see langword="true"/> if created; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreateRadialGradientBrush(RadialGradientPaint rg, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop’s alpha).
        ColorStop[] stops = ToColorStops(rg.Stops, rg.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(rg.Spread);

        brush = new RadialGradientBrush(rg.Center, rg.Radius, mode, stops);
        return true;
    }

    /// <summary>
    /// Creates a <see cref="SweepGradientBrush"/> from a <see cref="SweepGradientPaint"/>.
    /// </summary>
    /// <param name="sg">The sweep gradient paint.</param>
    /// <param name="brush">The resulting brush.</param>
    /// <returns><see langword="true"/> if created; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreateSweepGradientBrush(SweepGradientPaint sg, out Brush? brush)
    {
        // Map gradient stops (apply paint opacity multiplier to each stop’s alpha).
        ColorStop[] stops = ToColorStops(sg.Stops, sg.Opacity);

        // Map spread method.
        GradientRepetitionMode mode = MapSpread(sg.Spread);

        brush = new SweepGradientBrush(sg.Center, sg.StartAngle, sg.EndAngle, mode, stops);
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
        float a = Math.Clamp(c.Alpha / 255f * Math.Clamp(opacity, 0f, 1f), 0f, 1f);
        byte aa = (byte)MathF.Round(a * 255f);
        return Color.FromPixel(new Rgba32(c.Red, c.Green, c.Blue, aa));
    }

    /// <summary>
    /// Compares two points for near-equality.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <param name="eps">Tolerance.</param>
    /// <returns><see langword="true"/> if near-equal; otherwise <see langword="false"/>.</returns>
    private static bool ApproximatelyEqual(in PointF a, in PointF b, float eps = 1e-4f)
        => MathF.Abs(a.X - b.X) <= eps && MathF.Abs(a.Y - b.Y) <= eps;
}
