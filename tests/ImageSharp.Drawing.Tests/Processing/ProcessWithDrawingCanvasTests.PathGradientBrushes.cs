// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    private static readonly ImageComparer PathGradientTolerantComparer = ImageComparer.TolerantPercentage(0.01f);

    [Theory]
    [WithBlankImage(10, 10, PixelTypes.Rgba32)]
    public void FillPathGradientBrushFillRectangleWithDifferentColors<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            PathGradientTolerantComparer,
            image =>
            {
                PointF[] points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
                Color[] colors = [Color.Black, Color.Red, Color.Yellow, Color.Green];

                PathGradientBrush brush = new(points, colors);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
                image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            });

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPathGradientBrushFillTriangleWithDifferentColors<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            PathGradientTolerantComparer,
            image =>
            {
                PointF[] points = [new(10, 0), new(20, 20), new(0, 20)];
                Color[] colors = [Color.Red, Color.Green, Color.Blue];

                PathGradientBrush brush = new(points, colors);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
                image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            });

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.HalfSingle)]
    public void FillPathGradientBrushFillTriangleWithGreyscale<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            ImageComparer.TolerantPercentage(0.02f),
            image =>
            {
                PointF[] points = [new(10, 0), new(20, 20), new(0, 20)];

                Color c1 = Color.FromPixel(new HalfSingle(-1));
                Color c2 = Color.FromPixel(new HalfSingle(0));
                Color c3 = Color.FromPixel(new HalfSingle(1));

                Color[] colors = [c1, c2, c3];

                PathGradientBrush brush = new(points, colors);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
                image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            });

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void FillPathGradientBrushFillTriangleWithDifferentColorsCenter<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            PathGradientTolerantComparer,
            image =>
            {
                PointF[] points = [new(10, 0), new(20, 20), new(0, 20)];
                Color[] colors = [Color.Red, Color.Green, Color.Blue];

                PathGradientBrush brush = new(points, colors, Color.White);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
                image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            });

    [Theory]
    [WithBlankImage(10, 10, PixelTypes.Rgba32)]
    public void FillPathGradientBrushFillRectangleWithSingleColor<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        PointF[] points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Color[] colors = [Color.Red];

        PathGradientBrush brush = new(points, colors);

        image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
        image.ComparePixelBufferTo(Color.Red);
    }

    [Theory]
    [WithBlankImage(10, 10, PixelTypes.Rgba32)]
    public void FillPathGradientBrushShouldRotateTheColorsWhenThereAreMorePoints<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            PathGradientTolerantComparer,
            image =>
            {
                PointF[] points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
                Color[] colors = [Color.Red, Color.Yellow];

                PathGradientBrush brush = new(points, colors);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
                image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            });

    [Theory]
    [WithBlankImage(10, 10, PixelTypes.Rgba32)]
    public void FillPathGradientBrushFillWithCustomCenterColor<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            PathGradientTolerantComparer,
            image =>
            {
                PointF[] points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
                Color[] colors = [Color.Black, Color.Red, Color.Yellow, Color.Green];

                PathGradientBrush brush = new(points, colors, Color.White);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
                image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            });

    [Fact]
    public void FillPathGradientBrushShouldThrowArgumentNullExceptionWhenLinesAreNull()
    {
        Color[] colors = [Color.Black, Color.Red, Color.Yellow, Color.Green];

        PathGradientBrush Create() => new(null, colors, Color.White);

        Assert.Throws<ArgumentNullException>(Create);
    }

    [Fact]
    public void FillPathGradientBrushShouldThrowArgumentOutOfRangeExceptionWhenLessThan3PointsAreGiven()
    {
        PointF[] points = [new(0, 0), new(10, 0)];
        Color[] colors = [Color.Black, Color.Red, Color.Yellow, Color.Green];

        PathGradientBrush Create() => new(points, colors, Color.White);

        Assert.Throws<ArgumentOutOfRangeException>(Create);
    }

    [Fact]
    public void FillPathGradientBrushShouldThrowArgumentNullExceptionWhenColorsAreNull()
    {
        PointF[] points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

        PathGradientBrush Create() => new(points, null, Color.White);

        Assert.Throws<ArgumentNullException>(Create);
    }

    [Fact]
    public void FillPathGradientBrushShouldThrowArgumentOutOfRangeExceptionWhenEmptyColorArrayIsGiven()
    {
        PointF[] points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Color[] colors = [];

        PathGradientBrush Create() => new(points, colors, Color.White);

        Assert.Throws<ArgumentOutOfRangeException>(Create);
    }

    [Theory]
    [WithBlankImage(100, 100, PixelTypes.Rgba32)]
    public void FillPathGradientBrushFillComplex<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            new TolerantImageComparer(0.2f),
            image =>
            {
                Star star = new(50, 50, 5, 20, 45);
                PointF[] points = star.Points.ToArray();
                Color[] colors =
                [
                    Color.Red, Color.Yellow, Color.Green, Color.Blue, Color.Purple,
                    Color.Red, Color.Yellow, Color.Green, Color.Blue, Color.Purple
                ];

                PathGradientBrush brush = new(points, colors, Color.White);
                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            appendSourceFileOrDescription: false,
            appendPixelTypeToFileName: false);
}
