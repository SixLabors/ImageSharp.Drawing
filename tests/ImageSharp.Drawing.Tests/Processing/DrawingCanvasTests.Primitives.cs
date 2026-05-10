// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(50, 50, PixelTypes.Rgba32)]
    public void FillRectangle_AliasedRendersFullCorners<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int x = 10;
        const int y = 10;
        const int w = 30;
        const int h = 20;

        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false }
        };

        using Image<TPixel> target = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options))
        {
            canvas.Clear(Brushes.Solid(Color.Black));
            canvas.Fill(Brushes.Solid(Color.White), new Rectangle(x, y, w, h));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        // Verify all four corner pixels are fully white.
        Rgba32 topLeft = target[x, y].ToRgba32();
        Rgba32 topRight = target[x + w - 1, y].ToRgba32();
        Rgba32 bottomLeft = target[x, y + h - 1].ToRgba32();
        Rgba32 bottomRight = target[x + w - 1, y + h - 1].ToRgba32();

        Assert.Equal(255, topLeft.R);
        Assert.Equal(255, topRight.R);
        Assert.Equal(255, bottomLeft.R);
        Assert.Equal(255, bottomRight.R);

        // Verify pixels just outside each corner are still black.
        Assert.Equal(0, target[x - 1, y].ToRgba32().R);
        Assert.Equal(0, target[x, y - 1].ToRgba32().R);
        Assert.Equal(0, target[x + w, y].ToRgba32().R);
        Assert.Equal(0, target[x + w - 1, y - 1].ToRgba32().R);
        Assert.Equal(0, target[x - 1, y + h - 1].ToRgba32().R);
        Assert.Equal(0, target[x, y + h].ToRgba32().R);
        Assert.Equal(0, target[x + w, y + h - 1].ToRgba32().R);
        Assert.Equal(0, target[x + w - 1, y + h].ToRgba32().R);

        // Verify interior pixel count matches expected area.
        int whiteCount = 0;
        target.ProcessPixelRows(accessor =>
        {
            for (int row = 0; row < accessor.Height; row++)
            {
                Span<TPixel> span = accessor.GetRowSpan(row);
                for (int col = 0; col < span.Length; col++)
                {
                    if (span[col].ToRgba32().R == 255)
                    {
                        whiteCount++;
                    }
                }
            }
        });

        Assert.Equal(w * h, whiteCount);
    }

    [Theory]
    [WithBlankImage(50, 50, PixelTypes.Rgba32)]
    public void DrawRectangle_AliasedRendersFullCorners<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // A 2px pen centered on the rectangle edge places 1px inside and 1px outside.
        // For a rect at (10,10) size 30x20, the outer stroke boundary is (9,9)-(40,30)
        // and the inner boundary is (11,11)-(38,28).
        // Miter join ensures corners are fully filled (bevel would cut them).
        const int x = 10;
        const int y = 10;
        const int w = 30;
        const int h = 20;
        const float penWidth = 2;

        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false, AntialiasThreshold = 0.01F }
        };

        SolidPen pen = new(new PenOptions(Color.White, penWidth, null)
        {
            StrokeOptions = new StrokeOptions { LineJoin = LineJoin.Miter }
        });

        using Image<TPixel> target = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options))
        {
            canvas.Clear(Brushes.Solid(Color.Black));
            canvas.Draw(pen, new Rectangle(x, y, w, h));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        // Outer corners of the stroke (1px outside the rect edge).
        Assert.Equal(255, target[x - 1, y - 1].ToRgba32().R);
        Assert.Equal(255, target[x + w, y - 1].ToRgba32().R);
        Assert.Equal(255, target[x - 1, y + h].ToRgba32().R);
        Assert.Equal(255, target[x + w, y + h].ToRgba32().R);

        // Inner corners of the stroke (1px inside the rect edge).
        Assert.Equal(255, target[x, y].ToRgba32().R);
        Assert.Equal(255, target[x + w - 1, y].ToRgba32().R);
        Assert.Equal(255, target[x, y + h - 1].ToRgba32().R);
        Assert.Equal(255, target[x + w - 1, y + h - 1].ToRgba32().R);

        // Well outside the stroke boundary should be black.
        Assert.Equal(0, target[x - 3, y - 3].ToRgba32().R);
        Assert.Equal(0, target[x + w + 2, y - 3].ToRgba32().R);
        Assert.Equal(0, target[x - 3, y + h + 2].ToRgba32().R);
        Assert.Equal(0, target[x + w + 2, y + h + 2].ToRgba32().R);

        // Interior of the rectangle (well inside the stroke) should be black.
        Assert.Equal(0, target[x + (w / 2), y + (h / 2)].ToRgba32().R);
    }

    [Theory]
    [WithBlankImage(240, 160, PixelTypes.Rgba32)]
    public void DrawPrimitiveHelpers_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            canvas.Draw(Pens.Solid(Color.DimGray, 3), new Rectangle(10, 10, 220, 140));
            canvas.DrawEllipse(Pens.Solid(Color.CornflowerBlue, 6), new PointF(120, 80), new SizeF(110, 70));
            canvas.DrawArc(
                Pens.Solid(Color.ForestGreen, 4),
                new PointF(120, 80),
                new SizeF(90, 46),
                rotation: 15,
                startAngle: -25,
                sweepAngle: 220);
            canvas.DrawLine(
                Pens.Solid(Color.OrangeRed, 5),
                new PointF(18, 140),
                new PointF(76, 28),
                new PointF(166, 126),
                new PointF(222, 20));
            canvas.DrawBezier(
                Pens.Solid(Color.MediumVioletRed, 4),
                new PointF(20, 80),
                new PointF(70, 18),
                new PointF(168, 144),
                new PointF(220, 78));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(240, 160, PixelTypes.Rgba32)]
    public void FillPrimitiveHelpers_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            canvas.FillArc(
                Brushes.Solid(Color.CornflowerBlue),
                new PointF(78, 58),
                new SizeF(48, 34),
                rotation: 15,
                startAngle: -30,
                sweepAngle: 240);

            canvas.FillPie(Brushes.Solid(Color.Goldenrod), new PointF(150, 70), new SizeF(36, 36), startAngle: 20, sweepAngle: 240);
            canvas.FillPie(Brushes.Solid(Color.MediumSeaGreen), new PointF(184, 107), new SizeF(30, 21), startAngle: -35, sweepAngle: 220);
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(360, 220, PixelTypes.Rgba32)]
    public void RoundedRectanglePolygon_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            // Filled shape proves the rounded rectangle contributes a closed fill region.
            canvas.Fill(
                Brushes.Solid(Color.CornflowerBlue.WithAlpha(0.75F)),
                new RoundedRectanglePolygon(18, 18, 138, 76, 18));

            // Dashed stroke covers the original issue request: rounded corners must remain a normal strokable path.
            canvas.Draw(
                Pens.Dash(Color.DarkSlateBlue, 5F),
                new RoundedRectanglePolygon(190, 18, 132, 76, 24));

            // Elliptical radii exercise independent x/y corner curvature.
            canvas.Fill(
                Brushes.Solid(Color.MediumSeaGreen.WithAlpha(0.78F)),
                new RoundedRectanglePolygon(26, 124, 118, 58, new SizeF(28, 12)));

            // Oversized radii should scale down to a capsule-like shape within the supplied bounds.
            canvas.Draw(
                Pens.Solid(Color.OrangeRed, 6F),
                new RoundedRectanglePolygon(188, 122, 138, 62, 90));

            canvas.Draw(Pens.Solid(Color.Black.WithAlpha(0.5F), 2F), new Rectangle(8, 8, 344, 204));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(240, 160, PixelTypes.Rgba32)]
    public void DrawPieHelpers_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            canvas.DrawPie(Pens.Solid(Color.DarkSlateBlue, 6), new PointF(77, 75), new SizeF(43, 43), startAngle: -40, sweepAngle: 250);
            canvas.DrawPie(Pens.Solid(Color.OrangeRed, 4), new PointF(167, 70), new SizeF(35, 26), startAngle: 35, sweepAngle: -210);
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
