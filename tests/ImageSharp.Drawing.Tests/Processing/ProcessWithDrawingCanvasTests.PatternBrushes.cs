// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithPercent10<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
        };

        VerifyFloodFillPattern(provider, Brushes.Percent10(Color.HotPink, Color.LimeGreen), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithPercent10Transparent<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue }
        };

        VerifyFloodFillPattern(provider, Brushes.Percent10(Color.HotPink), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithPercent20<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen }
        };

        VerifyFloodFillPattern(provider, Brushes.Percent20(Color.HotPink, Color.LimeGreen), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithPercent20Transparent<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue }
        };

        VerifyFloodFillPattern(provider, Brushes.Percent20(Color.HotPink), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithHorizontal<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
        };

        VerifyFloodFillPattern(provider, Brushes.Horizontal(Color.HotPink, Color.LimeGreen), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithHorizontalTransparent<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue }
        };

        VerifyFloodFillPattern(provider, Brushes.Horizontal(Color.HotPink), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithMin<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink }
        };

        VerifyFloodFillPattern(provider, Brushes.Min(Color.HotPink, Color.LimeGreen), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithMinTransparent<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink }
        };

        VerifyFloodFillPattern(provider, Brushes.Min(Color.HotPink), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithVertical<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen }
        };

        VerifyFloodFillPattern(provider, Brushes.Vertical(Color.HotPink, Color.LimeGreen), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithVerticalTransparent<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue }
        };

        VerifyFloodFillPattern(provider, Brushes.Vertical(Color.HotPink), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithForwardDiagonal<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
        };

        VerifyFloodFillPattern(provider, Brushes.ForwardDiagonal(Color.HotPink, Color.LimeGreen), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithForwardDiagonalTransparent<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.Blue, Color.Blue, Color.HotPink },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue }
        };

        VerifyFloodFillPattern(provider, Brushes.ForwardDiagonal(Color.HotPink), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithBackwardDiagonal<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink }
        };

        VerifyFloodFillPattern(provider, Brushes.BackwardDiagonal(Color.HotPink, Color.LimeGreen), expectedPattern);
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithBackwardDiagonalTransparent<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.HotPink }
        };

        VerifyFloodFillPattern(provider, Brushes.BackwardDiagonal(Color.HotPink), expectedPattern);
    }

    private static void VerifyFloodFillPattern<TPixel>(
        TestImageProvider<TPixel> provider,
        Brush brush,
        Color[,] expectedPattern)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            ImageComparer.Exact,
            image =>
            {
                image.Mutate(ctx => ctx.Paint(canvas =>
                {
                    canvas.Fill(Brushes.Solid(Color.Blue));
                    canvas.Fill(brush);
                }));

                AssertPattern(image, expectedPattern);
            },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    private static void AssertPattern<TPixel>(Image<TPixel> image, Color[,] expectedPattern)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int rows = expectedPattern.GetLength(0);
        int columns = expectedPattern.GetLength(1);

        TPixel[,] expectedPixels = new TPixel[rows, columns];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                expectedPixels[y, x] = expectedPattern[y, x].ToPixel<TPixel>();
            }
        }

        Buffer2D<TPixel> pixels = image.GetRootFramePixelBuffer();
        for (int y = 0; y < image.Height; y++)
        {
            Span<TPixel> row = pixels.DangerousGetRowSpan(y);
            int patternY = y % rows;
            for (int x = 0; x < image.Width; x++)
            {
                TPixel expected = expectedPixels[patternY, x % columns];
                Assert.Equal(expected, row[x]);
            }
        }
    }
}
