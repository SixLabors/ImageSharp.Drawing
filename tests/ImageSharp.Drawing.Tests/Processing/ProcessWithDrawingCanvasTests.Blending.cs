// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    public static IEnumerable<object[]> BlendingsModes { get; } = GetAllModeCombinations();

    private static IEnumerable<object[]> GetAllModeCombinations()
    {
        foreach (object composition in Enum.GetValues(typeof(PixelAlphaCompositionMode)))
        {
            foreach (object blending in Enum.GetValues(typeof(PixelColorBlendingMode)))
            {
                yield return [blending, composition];
            }
        }
    }

    [Theory]
    [WithBlankImage(nameof(BlendingsModes), 250, 250, PixelTypes.Rgba32)]
    public void BlendingsDarkBlueRectBlendHotPinkRect<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode blending,
        PixelAlphaCompositionMode composition)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        int scaleX = image.Width / 100;
        int scaleY = image.Height / 100;

        DrawingOptions blendOptions = CreateBlendOptions(blending, composition);

        image.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
        {
            canvas.Fill(new Rectangle(0 * scaleX, 40 * scaleY, 100 * scaleX, 20 * scaleY), Brushes.Solid(Color.DarkBlue));
        }));

        image.Mutate(ctx => ctx.ProcessWithCanvas(blendOptions, canvas =>
        {
            canvas.Fill(new Rectangle(20 * scaleX, 0 * scaleY, 30 * scaleX, 100 * scaleY), Brushes.Solid(Color.HotPink));
        }));

        VerifyImage(provider, blending, composition, image);
    }

    [Theory]
    [WithBlankImage(nameof(BlendingsModes), 250, 250, PixelTypes.Rgba32)]
    public void BlendingsDarkBlueRectBlendHotPinkRectBlendTransparentEllipse<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode blending,
        PixelAlphaCompositionMode composition)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        int scaleX = image.Width / 100;
        int scaleY = image.Height / 100;

        DrawingOptions blendOptions = CreateBlendOptions(blending, composition);

        image.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
        {
            canvas.Fill(new Rectangle(0 * scaleX, 40 * scaleY, 100 * scaleX, 20 * scaleY), Brushes.Solid(Color.DarkBlue));
        }));

        image.Mutate(ctx => ctx.ProcessWithCanvas(blendOptions, canvas =>
        {
            canvas.Fill(new Rectangle(20 * scaleX, 0 * scaleY, 30 * scaleX, 100 * scaleY), Brushes.Solid(Color.HotPink));
        }));

        image.Mutate(ctx => ctx.ProcessWithCanvas(blendOptions, canvas =>
        {
            canvas.Fill(new EllipsePolygon(40 * scaleX, 50 * scaleY, 50 * scaleX, 50 * scaleY), Brushes.Solid(Color.Transparent));
        }));

        VerifyImage(provider, blending, composition, image);
    }

    [Theory]
    [WithBlankImage(nameof(BlendingsModes), 250, 250, PixelTypes.Rgba32)]
    public void BlendingsDarkBlueRectBlendHotPinkRectBlendSemiTransparentRedEllipse<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode blending,
        PixelAlphaCompositionMode composition)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        int scaleX = image.Width / 100;
        int scaleY = image.Height / 100;

        DrawingOptions blendOptions = CreateBlendOptions(blending, composition);
        Color transparentRed = Color.Red.WithAlpha(0.5F);

        image.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
        {
            // Keep legacy shape coordinates identical to the original test.
            canvas.Fill(new Rectangle(0 * scaleX, 40, 100 * scaleX, 20 * scaleY), Brushes.Solid(Color.DarkBlue));
        }));

        image.Mutate(ctx => ctx.ProcessWithCanvas(blendOptions, canvas =>
        {
            canvas.Fill(new Rectangle(20 * scaleX, 0, 30 * scaleX, 100 * scaleY), Brushes.Solid(Color.HotPink));
        }));

        image.Mutate(ctx => ctx.ProcessWithCanvas(blendOptions, canvas =>
        {
            canvas.Fill(new EllipsePolygon(40 * scaleX, 50 * scaleY, 50 * scaleX, 50 * scaleY), Brushes.Solid(transparentRed));
        }));

        VerifyImage(provider, blending, composition, image);
    }

    [Theory]
    [WithBlankImage(nameof(BlendingsModes), 250, 250, PixelTypes.Rgba32)]
    public void BlendingsDarkBlueRectBlendBlackEllipse<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode blending,
        PixelAlphaCompositionMode composition)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> destinationImage = provider.GetImage();
        using Image<TPixel> sourceImage = provider.GetImage();

        int scaleX = destinationImage.Width / 100;
        int scaleY = destinationImage.Height / 100;

        DrawingOptions blendOptions = CreateBlendOptions(blending, composition);

        destinationImage.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
        {
            canvas.Fill(new Rectangle(0 * scaleX, 40 * scaleY, 100 * scaleX, 20 * scaleY), Brushes.Solid(Color.DarkBlue));
        }));

        sourceImage.Mutate(ctx => ctx.ProcessWithCanvas(canvas =>
        {
            canvas.Fill(new EllipsePolygon(40 * scaleX, 50 * scaleY, 50 * scaleX, 50 * scaleY), Brushes.Solid(Color.Black));
        }));

        destinationImage.Mutate(ctx => ctx.ProcessWithCanvas(blendOptions, canvas =>
        {
            canvas.DrawImage(
                sourceImage,
                sourceImage.Bounds,
                new RectangleF(0, 0, destinationImage.Width, destinationImage.Height));
        }));

        VerifyImage(provider, blending, composition, destinationImage);
    }

    private static DrawingOptions CreateBlendOptions(
        PixelColorBlendingMode blending,
        PixelAlphaCompositionMode composition) =>
        new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = true,
                ColorBlendingMode = blending,
                AlphaCompositionMode = composition
            }
        };

    private static void VerifyImage<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode blending,
        PixelAlphaCompositionMode composition,
        Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        image.DebugSave(
            provider,
            new { composition, blending },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        ImageComparer comparer = ImageComparer.TolerantPercentage(0.01F, 3);
        image.CompareFirstFrameToReferenceOutput(
            comparer,
            provider,
            new { composition, blending },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
