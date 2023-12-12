// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class FillImageBrushTests
{
    [Fact]
    public void DoesNotDisposeImage()
    {
        using (Image<Rgba32> src = new(5, 5))
        {
            ImageBrush brush = new(src);
            using (Image<Rgba32> dest = new(10, 10))
            {
                dest.Mutate(c => c.Fill(brush, new Rectangle(0, 0, 10, 10)));
                dest.Mutate(c => c.Fill(brush, new Rectangle(0, 0, 10, 10)));
            }
        }
    }

    [Theory]
    [WithTestPatternImage(200, 200, PixelTypes.Rgba32 | PixelTypes.Bgra32)]
    public void UseBrushOfDifferentPixelType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = provider.PixelType == PixelTypes.Rgba32
                                   ? Image.Load<Bgra32>(data)
                                   : Image.Load<Rgba32>(data);

        ImageBrush brush = new(overlay);
        background.Mutate(c => c.Fill(brush));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(200, 200, PixelTypes.Rgba32)]
    public void CanDrawLandscapeImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        overlay.Mutate(c => c.Crop(new Rectangle(0, 0, 125, 90)));

        ImageBrush brush = new(overlay);
        background.Mutate(c => c.Fill(brush));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(200, 200, PixelTypes.Rgba32)]
    public void CanDrawPortraitImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        overlay.Mutate(c => c.Crop(new Rectangle(0, 0, 90, 125)));

        var brush = new ImageBrush(overlay);
        background.Mutate(c => c.Fill(brush));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(400, 400, PixelTypes.Rgba32)]
    public void CanOffsetImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        var brush = new ImageBrush(overlay);
        background.Mutate(c => c.Fill(brush, new RectangularPolygon(0, 0, 400, 200)));
        background.Mutate(c => c.Fill(brush, new RectangularPolygon(-100, 200, 500, 200)));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(400, 400, PixelTypes.Rgba32)]
    public void CanOffsetViaBrushImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        var brush = new ImageBrush(overlay);
        var brushOffset = new ImageBrush(overlay, new Point(100, 0));
        background.Mutate(c => c.Fill(brush, new RectangularPolygon(0, 0, 400, 200)));
        background.Mutate(c => c.Fill(brushOffset, new RectangularPolygon(0, 200, 400, 200)));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(1000, 1000, "White", PixelTypes.Rgba32)]
    public void CanDrawOffsetImage<TPixel>(TestImageProvider<TPixel> provider)
    where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();

        using Image templateImage = Image.Load<Rgba32>(data);
        using Image finalTexture = BuildMultiRowTexture(templateImage);

        finalTexture.Mutate(c => c.Resize(100, 200));

        ImageBrush brush = new(finalTexture);
        background.Mutate(c => c.Fill(brush));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);

        Image BuildMultiRowTexture(Image sourceTexture)
        {
            int halfWidth = sourceTexture.Width / 2;

            Image final = sourceTexture.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new Size(templateImage.Width, templateImage.Height * 2),
                Position = AnchorPositionMode.TopLeft,
                Mode = ResizeMode.Pad,
            })
            .DrawImage(templateImage, new Point(halfWidth, sourceTexture.Height), new Rectangle(0, 0, halfWidth, sourceTexture.Height), 1)
            .DrawImage(templateImage, new Point(0, templateImage.Height), new Rectangle(halfWidth, 0, halfWidth, sourceTexture.Height), 1));
            return final;
        }
    }

    [Theory]
    [WithSolidFilledImages(1000, 1000, "White", PixelTypes.Rgba32)]
    public void CanDrawNegativeOffsetImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        overlay.Mutate(c => c.Resize(100, 100));

        ImageBrush halfBrush = new(overlay, new RectangleF(50, 0, 50, 100));
        ImageBrush fullBrush = new(overlay);
        background.Mutate(c => DrawFull(c, new Size(100, 100), fullBrush, halfBrush, background.Width, background.Height));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    private static void DrawFull(IImageProcessingContext ctx, Size size, ImageBrush brush, ImageBrush halfBrush, int width, int height)
    {
        int j = 0;
        while (j < height)
        {
            bool half = false;
            int limitWidth = width;
            int i = 0;
            if ((j / size.Height) % 2 != 0)
            {
                half = true;
            }

            while (i < limitWidth)
            {
                if (half)
                {
                    ctx.Fill(halfBrush, new RectangleF(i, j, size.Width / 2f, size.Height));
                    i += (int)(size.Width / 2f);
                    half = false;
                }
                else
                {
                    ctx.Fill(brush, new RectangleF(new PointF(i, j), size));
                    i += size.Width;
                }
            }

            j += size.Height;
        }
    }
}
