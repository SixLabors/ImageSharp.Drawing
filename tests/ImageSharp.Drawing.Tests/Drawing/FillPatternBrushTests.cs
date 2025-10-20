// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

public class FillPatternBrushTests
{
    private void Test(string name, Color background, Brush brush, Color[,] expectedPattern)
    {
        string path = TestEnvironment.CreateOutputDirectory("Drawing", "FillPatternBrushTests");
        using (Image<Rgba32> image = new(20, 20))
        {
            image.Mutate(x => x.Fill(background).Fill(brush));

            image.Save($"{path}/{name}.png");

            Buffer2D<Rgba32> sourcePixels = image.GetRootFramePixelBuffer();

            // lets pick random spots to start checking
            Random r = new();
            DenseMatrix<Color> expectedPatternFast = new(expectedPattern);
            int xStride = expectedPatternFast.Columns;
            int yStride = expectedPatternFast.Rows;
            int offsetX = r.Next(image.Width / xStride) * xStride;
            int offsetY = r.Next(image.Height / yStride) * yStride;
            for (int x = 0; x < xStride; x++)
            {
                int actualX = x + offsetX;
                int actualY = y + offsetY;
                Rgba32 expected = expectedPatternFast[y, x].ToPixel<Rgba32>(); // inverted pattern
                Rgba32 actual = sourcePixels[actualX, actualY];
                if (expected != actual)
                {
                    int actualX = x + offsetX;
                    int actualY = y + offsetY;
                    Rgba32 expected = expectedPatternFast[y, x].ToPixel<Rgba32>(); // inverted pattern
                    Rgba32 actual = sourcePixels[actualX, actualY];
                    if (expected != actual)
                    {
                        Assert.True(false, $"Expected {expected} but found {actual} at ({actualX},{actualY})");
                    }
                }
            }
        }

        image.Mutate(x => x.Resize(80, 80, KnownResamplers.NearestNeighbor));
        image.Save($"{path}/{name}x4.png");
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithPercent10()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
        };

        Test(
            "Percent10",
            Color.Blue,
            Brushes.Percent10(Color.HotPink, Color.LimeGreen),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithPercent10Transparent()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue }
        };

        Test(
            "Percent10_Transparent",
            Color.Blue,
            Brushes.Percent10(Color.HotPink),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithPercent20()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen }
        };

        Test(
            "Percent20",
            Color.Blue,
            Brushes.Percent20(Color.HotPink, Color.LimeGreen),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithPercent20_transparent()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue }
        };

        Test(
            "Percent20_Transparent",
            Color.Blue,
            Brushes.Percent20(Color.HotPink),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithHorizontal()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
        };

        Test(
            "Horizontal",
            Color.Blue,
            Brushes.Horizontal(Color.HotPink, Color.LimeGreen),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithHorizontal_transparent()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue }
        };

        Test(
            "Horizontal_Transparent",
            Color.Blue,
            Brushes.Horizontal(Color.HotPink),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithMin()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink }
        };

        Test(
            "Min",
            Color.Blue,
            Brushes.Min(Color.HotPink, Color.LimeGreen),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithMin_transparent()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
        };

        Test(
            "Min_Transparent",
            Color.Blue,
            Brushes.Min(Color.HotPink),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithVertical()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen }
        };

        Test(
            "Vertical",
            Color.Blue,
            Brushes.Vertical(Color.HotPink, Color.LimeGreen),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithVertical_transparent()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue }
        };

        Test(
            "Vertical_Transparent",
            Color.Blue,
            Brushes.Vertical(Color.HotPink),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithForwardDiagonal()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
        };

        Test(
            "ForwardDiagonal",
            Color.Blue,
            Brushes.ForwardDiagonal(Color.HotPink, Color.LimeGreen),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithForwardDiagonal_transparent()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.Blue, Color.Blue, Color.Blue, Color.HotPink },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue }
        };

        Test(
            "ForwardDiagonal_Transparent",
            Color.Blue,
            Brushes.ForwardDiagonal(Color.HotPink),
            expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithBackwardDiagonal()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink }
        };

        Test(
             "BackwardDiagonal",
             Color.Blue,
             Brushes.BackwardDiagonal(Color.HotPink, Color.LimeGreen),
             expectedPattern);
    }

    [Fact]
    public void ImageShouldBeFloodFilledWithBackwardDiagonal_transparent()
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.HotPink }
        };

        Test(
            "BackwardDiagonal_Transparent",
            Color.Blue,
            Brushes.BackwardDiagonal(Color.HotPink),
            expectedPattern);
    }
}
