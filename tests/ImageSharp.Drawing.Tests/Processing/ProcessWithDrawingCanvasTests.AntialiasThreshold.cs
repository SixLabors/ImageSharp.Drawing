// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_AliasedWithDefaultThreshold<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        EllipsePolygon circle = new(50, 50, 40);
        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));

        int whitePixels = CountPixelsAbove(image, 250);
        int partialPixels = CountPixelsBetween(image, 1, 250);

        // Aliased mode should produce no partial-coverage pixels.
        Assert.Equal(0, partialPixels);
        Assert.True(whitePixels > 0, "Expected some white pixels from the filled circle.");
    }

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_AliasedLowThreshold_ProducesMorePixelsThanHighThreshold<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        EllipsePolygon circle = new(50, 50, 40);

        DrawingOptions lowOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false, AntialiasThreshold = 0.1F }
        };

        DrawingOptions highOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false, AntialiasThreshold = 0.9F }
        };

        using Image<TPixel> lowImage = provider.GetImage();
        lowImage.Mutate(ctx => ctx.ProcessWithCanvas(lowOptions, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));
        int lowCount = CountPixelsAbove(lowImage, 250);

        using Image<TPixel> highImage = provider.GetImage();
        highImage.Mutate(ctx => ctx.ProcessWithCanvas(highOptions, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));
        int highCount = CountPixelsAbove(highImage, 250);

        // A lower threshold includes more edge pixels, so the fill area should be larger.
        Assert.True(lowCount > highCount, $"Low threshold ({lowCount} pixels) should produce more pixels than high threshold ({highCount} pixels).");
    }

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_AntialiasedIgnoresThreshold<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        EllipsePolygon circle = new(50, 50, 40);

        DrawingOptions options1 = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true, AntialiasThreshold = 0.1F }
        };

        DrawingOptions options2 = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true, AntialiasThreshold = 0.9F }
        };

        using Image<TPixel> image1 = provider.GetImage();
        image1.Mutate(ctx => ctx.ProcessWithCanvas(options1, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));

        using Image<TPixel> image2 = provider.GetImage();
        image2.Mutate(ctx => ctx.ProcessWithCanvas(options2, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));

        // In antialiased mode the threshold is irrelevant; images should be identical.
        ImageComparer.Exact.VerifySimilarity(image1, image2);
    }

    private static int CountPixelsAbove<TPixel>(Image<TPixel> image, byte threshold)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 rgba = row[x].ToRgba32();
                    if (rgba.R > threshold)
                    {
                        count++;
                    }
                }
            }
        });

        return count;
    }

    private static int CountPixelsBetween<TPixel>(Image<TPixel> image, byte low, byte high)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 rgba = row[x].ToRgba32();
                    if (rgba.R >= low && rgba.R < high)
                    {
                        count++;
                    }
                }
            }
        });

        return count;
    }
}
