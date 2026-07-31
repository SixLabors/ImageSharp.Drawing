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
    [WithSolidFilledImages(200, 200, 224, 232, 240, PixelTypes.Rgba32)]
    public void FillPathGradientBrushTrianglePreservesAlphaRepresentation<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points = [new(100, 0), new(200, 200), new(0, 200)];
        Rgba32[] colors = [new(255, 0, 0, 211), new(0, 255, 0, 0), new(0, 0, 255, 73)];

        AssertPathGradientAlphaRepresentation(provider, points, colors, null);
    }

    [Theory]
    [WithSolidFilledImages(200, 200, 224, 232, 240, PixelTypes.Rgba32)]
    public void FillPathGradientBrushGeneralCasePreservesAlphaRepresentation<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points = [new(0, 0), new(200, 0), new(200, 200), new(0, 200)];
        Rgba32[] colors = [new(255, 64, 0, 223), new(0, 255, 64, 0), new(64, 0, 255, 79), new(191, 128, 64, 161)];

        AssertPathGradientAlphaRepresentation(provider, points, colors, new Rgba32(128, 191, 255, 117));
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.HalfSingle)]
    public void FillPathGradientBrushFillTriangleWithHalfSingleRedChannel<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            ImageComparer.TolerantPercentage(0.02f),
            image =>
            {
                PointF[] points = [new(10, 0), new(20, 20), new(0, 20)];

                // HalfSingle represents DXGI_FORMAT_R16_FLOAT, so its single component is red.
                // Use the finite binary16 endpoints to cover the complete scaled [0, 1] range.
                Color c1 = Color.FromPixel(new HalfSingle((float)Half.MinValue));
                Color c2 = Color.FromPixel(new HalfSingle(0));
                Color c3 = Color.FromPixel(new HalfSingle((float)Half.MaxValue));

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
                StarPolygon star = new(50, 50, 5, 20, 45);
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

    private static void AssertPathGradientAlphaRepresentation<TPixel>(TestImageProvider<TPixel> provider, PointF[] points, Rgba32[] colors, Rgba32? centerColor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[] unassociatedColors = new Color[colors.Length];
        Color[] associatedColors = new Color[colors.Length];

        for (int i = 0; i < colors.Length; i++)
        {
            unassociatedColors[i] = Color.FromPixel(colors[i]);
            associatedColors[i] = ToAssociatedColor(unassociatedColors[i]);
        }

        PathGradientBrush unassociatedBrush;
        PathGradientBrush associatedBrush;

        if (centerColor.HasValue)
        {
            Color unassociatedCenterColor = Color.FromPixel(centerColor.Value);
            unassociatedBrush = new PathGradientBrush(points, unassociatedColors, unassociatedCenterColor);
            associatedBrush = new PathGradientBrush(points, associatedColors, ToAssociatedColor(unassociatedCenterColor));
        }
        else
        {
            unassociatedBrush = new PathGradientBrush(points, unassociatedColors);
            associatedBrush = new PathGradientBrush(points, associatedColors);
        }

        Rgba32 background = new(224, 232, 240, 255);
        Rgba32P associatedBackground = Rgba32P.FromRgba32(background);
        using Image<TPixel> unassociatedImage = provider.GetImage();
        using Image<TPixel> associatedInputImage = provider.GetImage();
        using Image<Rgba32P> unassociatedInputAssociatedDestinationImage = new(unassociatedImage.Width, unassociatedImage.Height, associatedBackground);
        using Image<Rgba32P> associatedDestinationImage = new(unassociatedImage.Width, unassociatedImage.Height, associatedBackground);

        unassociatedImage.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(unassociatedBrush)));
        associatedInputImage.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(associatedBrush)));
        unassociatedInputAssociatedDestinationImage.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(unassociatedBrush)));
        associatedDestinationImage.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(associatedBrush)));

        unassociatedImage.DebugSave(provider, "unassociated-input-rgba32", appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        associatedInputImage.DebugSave(provider, "associated-input-rgba32", appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        unassociatedInputAssociatedDestinationImage.DebugSave(provider, "unassociated-input-rgba32p", appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        associatedDestinationImage.DebugSave(provider, "associated-input-rgba32p", appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

        AssertAssociationSimilarity(unassociatedImage, associatedInputImage);
        AssertAssociationSimilarity(unassociatedInputAssociatedDestinationImage, associatedDestinationImage);
    }
}
