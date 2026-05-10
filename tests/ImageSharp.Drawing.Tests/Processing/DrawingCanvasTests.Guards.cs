// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Fact]
    public void RestoreTo_InvalidCount_Throws()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(
            provider,
            target,
            new DrawingOptions());

        ArgumentOutOfRangeException low = Assert.Throws<ArgumentOutOfRangeException>(() => canvas.RestoreTo(0));
        ArgumentOutOfRangeException high = Assert.Throws<ArgumentOutOfRangeException>(() => canvas.RestoreTo(2));

        Assert.Equal("saveCount", low.ParamName);
        Assert.Equal("saveCount", high.ParamName);
    }

    [Fact]
    public void Dispose_ThenOperations_ThrowObjectDisposedException()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(96, 96);
        using Image<Rgba32> source = new(24, 24);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(
            provider,
            target,
            new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 16);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(10, 28)
        };

        canvas.Dispose();

        Assert.Throws<ObjectDisposedException>(() => canvas.Fill(Brushes.Solid(Color.Black)));
        Assert.Throws<ObjectDisposedException>(() => canvas.Draw(Pens.Solid(Color.Black, 2F), new Rectangle(8, 8, 60, 60)));
        Assert.Throws<ObjectDisposedException>(() => canvas.DrawText(textOptions, "Disposed", Brushes.Solid(Color.DarkBlue), pen: null));
        Assert.Throws<ObjectDisposedException>(() => canvas.DrawImage(source, source.Bounds, new RectangleF(12, 12, 48, 48)));
        Assert.Throws<ObjectDisposedException>(canvas.Flush);
    }
}
