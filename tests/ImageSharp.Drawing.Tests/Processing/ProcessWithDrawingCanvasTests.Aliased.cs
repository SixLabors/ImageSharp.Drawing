// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    /// <summary>
    /// Verifies that aliased fills produce only fully covered or uncovered pixels.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_Aliased_ProducesOnlyBinaryCoverage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        EllipsePolygon circle = new(50, 50, 40);
        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));

        int whitePixels = CountPixelsAbove(image, 250);
        int partialPixels = CountPixelsBetween(image, 1, 250);

        Assert.Equal(0, partialPixels);
        Assert.True(whitePixels > 0, "Expected some white pixels from the filled circle.");
    }

    /// <summary>
    /// Verifies that antialiased fills produce partial edge coverage.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_Antialiased_ProducesPartialCoverage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        EllipsePolygon circle = new(50, 50, 40);
        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = true } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));

        Assert.True(CountPixelsBetween(image, 1, 250) > 0, "Expected partially covered edge pixels.");
    }

    /// <summary>
    /// Counts pixels whose red channel is above the specified value.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="image">The image to inspect.</param>
    /// <param name="cutoff">The exclusive lower limit.</param>
    /// <returns>The matching pixel count.</returns>
    private static int CountPixelsAbove<TPixel>(Image<TPixel> image, byte cutoff)
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
                    if (rgba.R > cutoff)
                    {
                        count++;
                    }
                }
            }
        });

        return count;
    }

    /// <summary>
    /// Counts pixels whose red channel is within the specified half-open range.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="image">The image to inspect.</param>
    /// <param name="low">The inclusive lower limit.</param>
    /// <param name="high">The exclusive upper limit.</param>
    /// <returns>The matching pixel count.</returns>
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
