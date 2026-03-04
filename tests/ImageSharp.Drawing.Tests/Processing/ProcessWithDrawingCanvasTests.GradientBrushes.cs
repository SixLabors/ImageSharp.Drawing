// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    private static readonly ImageComparer EllipticGradientTolerantComparer = ImageComparer.TolerantPercentage(0.01F);

    [Theory]
    [WithBlankImage(10, 10, PixelTypes.Rgba32)]
    public void FillEllipticGradientBrushWithEqualColorsReturnsUnicolorImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color red = Color.Red;

        using Image<TPixel> image = provider.GetImage();

        EllipticGradientBrush unicolorEllipticGradientBrush =
            new(
                new Point(0, 0),
                new Point(10, 0),
                1.0F,
                GradientRepetitionMode.None,
                new ColorStop(0, red),
                new ColorStop(1, red));

        DrawingOptions options = new();
        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Fill(unicolorEllipticGradientBrush)));
        image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

        // No reference image needed: the whole output should be a single color.
        image.ComparePixelBufferTo(red);
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.2)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.6)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 2.0)]
    public void FillEllipticGradientBrushAxisParallelEllipsesWithDifferentRatio<TPixel>(TestImageProvider<TPixel> provider, float ratio)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        Color yellow = Color.Yellow;
        Color red = Color.Red;
        Color black = Color.Black;

        EllipticGradientBrush brush = new(
            new Point(image.Width / 2, image.Height / 2),
            new Point(image.Width / 2, image.Width * 2 / 3),
            ratio,
            GradientRepetitionMode.None,
            new ColorStop(0, yellow),
            new ColorStop(1, red),
            new ColorStop(1, black));

        FormattableString outputDetails = $"{ratio:F2}";
        DrawingOptions options = new();
        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Fill(brush)));
        image.DebugSave(provider, outputDetails, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            EllipticGradientTolerantComparer,
            provider,
            outputDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 30)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 30)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 30)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 30)]
    public void FillEllipticGradientBrushRotatedEllipsesWithDifferentRatio<TPixel>(
        TestImageProvider<TPixel> provider,
        float ratio,
        float rotationInDegree)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        Color yellow = Color.Yellow;
        Color red = Color.Red;
        Color black = Color.Black;

        Point center = new(image.Width / 2, image.Height / 2);

        double rotation = Math.PI * rotationInDegree / 180.0;
        double cos = Math.Cos(rotation);
        double sin = Math.Sin(rotation);

        int offsetY = image.Height / 6;
        int axisX = center.X + (int)-(offsetY * sin);
        int axisY = center.Y + (int)(offsetY * cos);

        EllipticGradientBrush brush = new(
            center,
            new Point(axisX, axisY),
            ratio,
            GradientRepetitionMode.None,
            new ColorStop(0, yellow),
            new ColorStop(1, red),
            new ColorStop(1, black));

        FormattableString outputDetails = $"{ratio:F2}_AT_{rotationInDegree:00}deg";
        DrawingOptions options = new();
        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Fill(brush)));
        image.DebugSave(provider, outputDetails, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            EllipticGradientTolerantComparer,
            provider,
            outputDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
