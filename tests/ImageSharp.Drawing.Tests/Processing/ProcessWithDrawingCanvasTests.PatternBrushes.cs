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
    private const int HatchGalleryColumns = 9;

    private const int HatchGalleryTileSize = 64;

    private static readonly Func<Color, Color, PatternBrush>[] HatchGalleryBrushes =
    [
        Brushes.Horizontal,
        Brushes.Vertical,
        Brushes.ForwardDiagonal,
        Brushes.BackwardDiagonal,
        Brushes.Cross,
        Brushes.DiagonalCross,
        Brushes.Percent05,
        Brushes.Percent10,
        Brushes.Percent20,
        Brushes.Percent25,
        Brushes.Percent30,
        Brushes.Percent40,
        Brushes.Percent50,
        Brushes.Percent60,
        Brushes.Percent70,
        Brushes.Percent75,
        Brushes.Percent80,
        Brushes.Percent90,
        Brushes.LightDownwardDiagonal,
        Brushes.LightUpwardDiagonal,
        Brushes.DarkDownwardDiagonal,
        Brushes.DarkUpwardDiagonal,
        Brushes.WideDownwardDiagonal,
        Brushes.WideUpwardDiagonal,
        Brushes.LightVertical,
        Brushes.LightHorizontal,
        Brushes.NarrowVertical,
        Brushes.NarrowHorizontal,
        Brushes.DarkVertical,
        Brushes.DarkHorizontal,
        Brushes.DashedDownwardDiagonal,
        Brushes.DashedUpwardDiagonal,
        Brushes.DashedHorizontal,
        Brushes.DashedVertical,
        Brushes.SmallConfetti,
        Brushes.LargeConfetti,
        Brushes.ZigZag,
        Brushes.Wave,
        Brushes.DiagonalBrick,
        Brushes.HorizontalBrick,
        Brushes.Weave,
        Brushes.Plaid,
        Brushes.Divot,
        Brushes.DottedGrid,
        Brushes.DottedDiamond,
        Brushes.Shingle,
        Brushes.Trellis,
        Brushes.Sphere,
        Brushes.SmallGrid,
        Brushes.SmallCheckerBoard,
        Brushes.LargeCheckerBoard,
        Brushes.OutlinedDiamond,
        Brushes.SolidDiamond,
        Brushes.Min
    ];

    [Theory]
    [WithBlankImage(HatchGalleryColumns * HatchGalleryTileSize, 6 * HatchGalleryTileSize, PixelTypes.Rgba32)]
    public void HatchPatternBrushes_RenderGallery<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            image => image.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(Color.White));

                for (int i = 0; i < HatchGalleryBrushes.Length; i++)
                {
                    int x = (i % HatchGalleryColumns) * HatchGalleryTileSize;
                    int y = (i / HatchGalleryColumns) * HatchGalleryTileSize;
                    Rectangle tile = new(x + 4, y + 4, HatchGalleryTileSize - 8, HatchGalleryTileSize - 8);
                    Color foreground = Color.FromPixel(new Rgba32((byte)(36 + ((i * 37) % 160)), 48, (byte)(96 + ((i * 19) % 120)), 220));
                    Color background = Color.FromPixel(new Rgba32((byte)(232 - ((i * 11) % 72)), (byte)(238 - ((i * 7) % 64)), 224, 255));

                    canvas.Fill(HatchGalleryBrushes[i](foreground, background), tile);
                    canvas.Draw(Pens.Solid(Color.DarkSlateGray, 1F), tile);
                }
            }),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPatternBrushImageShouldBeFloodFilledWithPercent10<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[,] expectedPattern = new Color[,]
        {
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
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
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue }
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
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
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
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue }
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
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
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
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue }
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
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
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
            { Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink, Color.HotPink },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue }
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
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
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
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue }
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
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink }
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
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink }
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
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.LimeGreen, Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen },
            { Color.HotPink, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen, Color.LimeGreen }
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
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.Blue, Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue },
            { Color.HotPink, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue }
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
