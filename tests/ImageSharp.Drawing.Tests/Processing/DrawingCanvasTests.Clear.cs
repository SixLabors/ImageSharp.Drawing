// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(256, 160, PixelTypes.Rgba32)]
    public void Clear_RegionAndPath_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        canvas.Fill(Brushes.Solid(Color.MidnightBlue.WithAlpha(0.95F)));
        canvas.Fill(Brushes.Solid(Color.Crimson.WithAlpha(0.8F)), new Rectangle(22, 16, 188, 118));
        canvas.DrawEllipse(Pens.Solid(Color.Gold, 5), new PointF(128, 80), new SizeF(140, 90));

        canvas.Clear(Brushes.Solid(Color.LightYellow.WithAlpha(0.45F)), new Rectangle(56, 36, 108, 64));
        IPath clearPath = new EllipsePolygon(new PointF(178, 80), new SizeF(74, 56));
        canvas.Clear(Brushes.Solid(Color.Transparent), clearPath);

        canvas.Draw(Pens.Solid(Color.Black, 3), new Rectangle(10, 10, 236, 140));
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(320, 200, PixelTypes.Rgba32)]
    public void Clear_WithClipPath_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Fill(Brushes.Solid(Color.MidnightBlue.WithAlpha(0.95F)), new Rectangle(0, 0, 320, 200));
        canvas.Fill(Brushes.Solid(Color.Crimson.WithAlpha(0.78F)), new Rectangle(26, 18, 268, 164));
        canvas.DrawEllipse(Pens.Solid(Color.Gold, 5F), new PointF(160, 100), new SizeF(196, 116));

        IPath clipPath = new EllipsePolygon(new PointF(160, 100), new SizeF(214, 126));
        _ = canvas.Save(new DrawingOptions(), clipPath);

        canvas.Clear(Brushes.Solid(Color.LightYellow.WithAlpha(0.85F)));
        canvas.Clear(Brushes.Solid(Color.MediumPurple.WithAlpha(0.72F)), new Rectangle(40, 24, 108, 72));
        canvas.Clear(Brushes.Solid(Color.LightSeaGreen.WithAlpha(0.8F)), new Rectangle(172, 96, 110, 70));
        canvas.Clear(Brushes.Solid(Color.Transparent), new EllipsePolygon(new PointF(164, 98), new SizeF(74, 48)));

        canvas.Restore();

        canvas.Draw(Pens.DashDot(Color.Black, 3F), clipPath);
        canvas.Draw(Pens.Solid(Color.Black, 2F), new Rectangle(8, 8, 304, 184));
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        // MacOs differs by a reported 0.0000. Go figure!
        target.CompareToReferenceOutput(ImageComparer.TolerantPercentage(0.0001F), provider, appendSourceFileOrDescription: false);
    }
}
