// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    [Fact]
    public void FillImageBrushDoesNotDisposeImage()
    {
        using (Image<Rgba32> source = new(5, 5))
        {
            ImageBrush brush = new(source);
            using (Image<Rgba32> destination = new(10, 10))
            {
                destination.Mutate(ctx => ctx.ProcessWithCanvas(canvas => canvas.Fill(brush, new Rectangle(0, 0, 10, 10))));
                destination.Mutate(ctx => ctx.ProcessWithCanvas(canvas => canvas.Fill(brush, new Rectangle(0, 0, 10, 10))));
            }
        }
    }

    [Theory]
    [WithTestPatternImage(200, 200, PixelTypes.Rgba32 | PixelTypes.Bgra32)]
    public void FillImageBrushUseBrushOfDifferentPixelType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = provider.PixelType == PixelTypes.Rgba32
                                  ? Image.Load<Bgra32>(data)
                                  : Image.Load<Rgba32>(data);

        ImageBrush brush = new(overlay);
        background.Mutate(ctx => ctx.ProcessWithCanvas(canvas => canvas.Fill(brush)));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(200, 200, PixelTypes.Rgba32)]
    public void FillImageBrushCanDrawLandscapeImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        overlay.Mutate(ctx => ctx.Crop(new Rectangle(0, 0, 125, 90)));

        ImageBrush brush = new(overlay);
        background.Mutate(ctx => ctx.ProcessWithCanvas(canvas => canvas.Fill(brush)));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(200, 200, PixelTypes.Rgba32)]
    public void FillImageBrushCanDrawPortraitImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        overlay.Mutate(ctx => ctx.Crop(new Rectangle(0, 0, 90, 125)));

        ImageBrush brush = new(overlay);
        background.Mutate(ctx => ctx.ProcessWithCanvas(canvas => canvas.Fill(brush)));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(400, 400, PixelTypes.Rgba32)]
    public void FillImageBrushCanOffsetImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        ImageBrush brush = new(overlay);
        background.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
        {
            canvas.Fill(brush, new Rectangle(0, 0, 400, 200));
            canvas.Fill(brush, new Rectangle(-100, 200, 500, 200));
        }));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithTestPatternImage(400, 400, PixelTypes.Rgba32)]
    public void FillImageBrushCanOffsetViaBrushImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        ImageBrush brush = new(overlay);
        ImageBrush brushOffset = new(overlay, new Point(100, 0));

        background.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
        {
            canvas.Fill(brush, new Rectangle(0, 0, 400, 200));
            canvas.Fill(brushOffset, new Rectangle(0, 200, 400, 200));
        }));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(1000, 1000, "White", PixelTypes.Rgba32)]
    public void FillImageBrushCanDrawOffsetImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();

        using Image templateImage = Image.Load<Rgba32>(data);
        using Image finalTexture = BuildMultiRowTexture(templateImage);

        finalTexture.Mutate(ctx => ctx.Resize(100, 200));

        ImageBrush brush = new(finalTexture);
        background.Mutate(ctx => ctx.ProcessWithCanvas(canvas => canvas.Fill(brush)));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);

        Image BuildMultiRowTexture(Image sourceTexture)
        {
            int halfWidth = sourceTexture.Width / 2;

            Image final = sourceTexture.Clone(ctx => ctx.Resize(new ResizeOptions
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
    public void FillImageBrushCanDrawNegativeOffsetImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = TestFile.Create(TestImages.Png.Ducky).Bytes;
        using Image<TPixel> background = provider.GetImage();
        using Image overlay = Image.Load<Rgba32>(data);

        overlay.Mutate(ctx => ctx.Resize(100, 100));

        ImageBrush halfBrush = new(overlay, new RectangleF(50, 0, 50, 100));
        ImageBrush fullBrush = new(overlay);

        background.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
            FillImageBrushDrawFull(canvas, new Size(100, 100), fullBrush, halfBrush, background.Width, background.Height)));

        background.DebugSave(provider, appendSourceFileOrDescription: false);
        background.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    private static void FillImageBrushDrawFull(
        IDrawingCanvas canvas,
        Size size,
        ImageBrush brush,
        ImageBrush halfBrush,
        int width,
        int height)
    {
        int y = 0;
        while (y < height)
        {
            bool half = (y / size.Height) % 2 != 0;
            int x = 0;
            while (x < width)
            {
                if (half)
                {
                    int halfWidth = size.Width / 2;
                    canvas.Fill(halfBrush, new Rectangle(x, y, halfWidth, size.Height));
                    x += halfWidth;
                    half = false;
                }
                else
                {
                    canvas.Fill(brush, new Rectangle(x, y, size.Width, size.Height));
                    x += size.Width;
                }
            }

            y += size.Height;
        }
    }
}
