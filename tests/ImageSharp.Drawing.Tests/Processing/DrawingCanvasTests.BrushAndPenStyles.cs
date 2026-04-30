// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(320, 200, PixelTypes.Rgba32)]
    public void Fill_WithGradientAndPatternBrushes_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        Brush linearBrush = new LinearGradientBrush(
            new PointF(18, 22),
            new PointF(192, 140),
            GradientRepetitionMode.None,
            new ColorStop(0F, Color.LightYellow),
            new ColorStop(0.5F, Color.DeepSkyBlue.WithAlpha(0.85F)),
            new ColorStop(1F, Color.MediumBlue.WithAlpha(0.9F)));

        Brush radialBrush = new RadialGradientBrush(
            new PointF(238, 88),
            66F,
            GradientRepetitionMode.Reflect,
            new ColorStop(0F, Color.Orange.WithAlpha(0.95F)),
            new ColorStop(1F, Color.MediumVioletRed.WithAlpha(0.25F)));

        Brush hatchBrush = Brushes.ForwardDiagonal(Color.DarkSlateGray.WithAlpha(0.7F), Color.Transparent);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(linearBrush, new Rectangle(14, 14, 176, 126));
            canvas.Fill(radialBrush, new EllipsePolygon(new PointF(236, 90), new SizeF(132, 98)));
            canvas.Fill(hatchBrush, CreateClosedPathBuilder());
            canvas.Draw(Pens.DashDot(Color.Black, 3), new Rectangle(10, 10, 300, 180));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(320, 200, PixelTypes.Rgba32)]
    public void Draw_WithPatternAndGradientPens_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        Brush gradientBrush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(320, 0),
            GradientRepetitionMode.Repeat,
            new ColorStop(0F, Color.CornflowerBlue),
            new ColorStop(0.5F, Color.Gold),
            new ColorStop(1F, Color.MediumSeaGreen));

        Brush patternBrush = Brushes.Vertical(Color.DarkRed.WithAlpha(0.75F), Color.Transparent);
        Brush percentBrush = Brushes.Percent20(Color.DarkOrange.WithAlpha(0.85F), Color.Transparent);

        Pen dashPen = Pens.Dash(gradientBrush, 6F);
        Pen dotPen = Pens.Dot(patternBrush, 5F);
        Pen dashDotPen = Pens.DashDot(percentBrush, 4F);
        Pen dashDotDotPen = Pens.DashDotDot(Color.Black.WithAlpha(0.75F), 3F);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Draw(dashPen, new Rectangle(16, 14, 288, 170));
            canvas.DrawEllipse(dotPen, new PointF(162, 100), new SizeF(206, 116));
            canvas.DrawArc(dashDotPen, new PointF(160, 100), new SizeF(148, 84), rotation: 0, startAngle: 20, sweepAngle: 300);
            canvas.DrawLine(dashDotDotPen, new PointF(26, 174), new PointF(108, 22), new PointF(212, 164), new PointF(292, 26));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
