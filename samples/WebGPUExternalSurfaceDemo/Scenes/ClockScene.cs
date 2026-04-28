// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using Font = SixLabors.Fonts.Font;
using FontFamily = SixLabors.Fonts.FontFamily;
using FontStyle = SixLabors.Fonts.FontStyle;
using HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment;
using ISPath = SixLabors.ImageSharp.Drawing.Path;
using Pens = SixLabors.ImageSharp.Drawing.Processing.Pens;
using PointF = SixLabors.ImageSharp.PointF;
using Size = SixLabors.ImageSharp.Size;
using SizeF = SixLabors.ImageSharp.SizeF;
using SystemFonts = SixLabors.Fonts.SystemFonts;
using VerticalAlignment = SixLabors.Fonts.VerticalAlignment;

namespace WebGPUExternalSurfaceDemo.Scenes;

/// <summary>
/// Animated analog clock. Validates continuous render, curves, thin-stroke antialiasing, text rendering,
/// and canvas transforms against a constantly changing scene.
/// </summary>
internal sealed class ClockScene : RenderScene
{
    private static readonly Color BackgroundColor = Color.MidnightBlue;
    private static readonly Color DialColor = Color.WhiteSmoke;
    private static readonly Color DialRimColor = Color.DarkSlateGray;
    private static readonly Color MinorTickColor = Color.SlateGray;
    private static readonly Color MajorTickColor = Color.DarkSlateGray;
    private static readonly Color HourHandColor = Color.DarkSlateGray;
    private static readonly Color MinuteHandColor = Color.DarkSlateGray;
    private static readonly Color SecondHandColor = Color.OrangeRed;
    private static readonly Color NumeralColor = Color.DarkSlateGray;

    private readonly Font numeralFont;

    public ClockScene()
    {
        // Resolve the font family once. The actual font size is derived from the current framebuffer size
        // each frame so the clock remains proportional while resizing.
        FontFamily family = SystemFonts.Collection.Families.FirstOrDefault();
        this.numeralFont = family.Name is null
            ? SystemFonts.CreateFont(SystemFonts.Families.First().Name, 32f, FontStyle.Regular)
            : family.CreateFont(32f, FontStyle.Regular);
    }

    public override string DisplayName => "Clock";

    public override void Paint(DrawingCanvas<Bgra32> canvas, Size viewportSize, TimeSpan deltaTime)
    {
        // Background clear.
        canvas.Fill(
            Brushes.Solid(BackgroundColor),
            new RectangularPolygon(0, 0, viewportSize.Width, viewportSize.Height));

        // The scene is rebuilt from the framebuffer size each frame. That keeps resize handling simple
        // and demonstrates drawing directly in surface pixel coordinates.
        SizeF center = viewportSize / 2f;
        float cx = center.Width;
        float cy = center.Height;
        float radius = Math.Min(viewportSize.Width, viewportSize.Height) * 0.45f;

        // Dial.
        canvas.Fill(Brushes.Solid(DialColor), new EllipsePolygon(cx, cy, radius));
        canvas.Draw(Pens.Solid(DialRimColor, MathF.Max(radius * 0.015f, 1f)), new EllipsePolygon(cx, cy, radius));

        // Tick marks.
        float majorInner = radius * 0.88f;
        float minorInner = radius * 0.93f;
        float majorThickness = MathF.Max(radius * 0.012f, 1.5f);
        float minorThickness = MathF.Max(radius * 0.006f, 0.75f);

        for (int i = 0; i < 60; i++)
        {
            float angle = (MathF.Tau * i / 60f) - (MathF.PI / 2f);
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            bool major = i % 5 == 0;
            float inner = major ? majorInner : minorInner;

            PointF p0 = new(cx + (cos * inner), cy + (sin * inner));
            PointF p1 = new(cx + (cos * radius * 0.98f), cy + (sin * radius * 0.98f));

            canvas.DrawLine(
                Pens.Solid(major ? MajorTickColor : MinorTickColor, major ? majorThickness : minorThickness),
                p0,
                p1);
        }

        // Numerals.
        float numeralRadius = radius * 0.75f;
        float numeralSize = radius * 0.14f;
        Font font = this.numeralFont.Family.CreateFont(numeralSize, FontStyle.Bold);
        for (int hour = 1; hour <= 12; hour++)
        {
            // Text origin is placed at the numeral center and alignment keeps the glyphs centered there.
            float angle = (MathF.Tau * hour / 12f) - (MathF.PI / 2f);
            PointF origin = new(
                cx + (MathF.Cos(angle) * numeralRadius),
                cy + (MathF.Sin(angle) * numeralRadius));

            RichTextOptions textOptions = new(font)
            {
                Origin = origin,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            canvas.DrawText(textOptions, hour.ToString(CultureInfo.InvariantCulture), Brushes.Solid(NumeralColor), pen: null);
        }

        // Hands. Each hand is authored once as a canonical path pointing straight up
        // along -Y and then rotated + translated via Matrix4x4 to its live position.
        // The scene samples wall-clock time instead of integrating deltaTime, so missed
        // frames do not make the clock drift.
        DateTime now = DateTime.Now;
        float hourAngle = (MathF.Tau * (now.Hour % 12) / 12f) + (MathF.Tau * now.Minute / (12f * 60f));
        float minuteAngle = (MathF.Tau * now.Minute / 60f) + (MathF.Tau * now.Second / (60f * 60f));
        float secondAngle = (MathF.Tau * now.Second / 60f) + (MathF.Tau * now.Millisecond / (60f * 1000f));

        DrawHand(canvas, cx, cy, hourAngle, radius * 0.5f, radius * 0.028f, HourHandColor);
        DrawHand(canvas, cx, cy, minuteAngle, radius * 0.75f, radius * 0.018f, MinuteHandColor);
        DrawHand(canvas, cx, cy, secondAngle, radius * 0.85f, radius * 0.007f, SecondHandColor);

        // Center cap.
        canvas.Fill(Brushes.Solid(SecondHandColor), new EllipsePolygon(cx, cy, MathF.Max(radius * 0.025f, 2f)));
        canvas.Fill(Brushes.Solid(DialColor), new EllipsePolygon(cx, cy, MathF.Max(radius * 0.012f, 1f)));
    }

    private static void DrawHand(
        DrawingCanvas<Bgra32> canvas,
        float cx,
        float cy,
        float angleRadiansFromNoon,
        float length,
        float thickness,
        Color color)
    {
        // Canonical hand in local coordinates: straight line pointing up along −Y.
        // The canvas applies the rotate + translate transform below; the path itself is never moved.
        IPath handShape = new ISPath(new LinearLineSegment(
            new PointF(0, length * 0.15f),
            new PointF(0, -length)));

        DrawingOptions options = new()
        {
            Transform =
                Matrix4x4.CreateRotationZ(angleRadiansFromNoon) *
                Matrix4x4.CreateTranslation(cx, cy, 0),
        };

        canvas.Save(options);
        canvas.Draw(Pens.Solid(color, thickness), handShape);
        canvas.Restore();
    }
}
