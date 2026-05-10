// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 0, 0, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, -20, -20, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, -20, -100, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 20, 20, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 40, 60, 0.2)]
    public void ClipOffset<TPixel>(TestImageProvider<TPixel> provider, float dx, float dy, float sizeMult)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        FormattableString testDetails = $"offset_x{dx}_y{dy}";
        provider.RunValidatingProcessorTest(
            x => x.Paint(canvas =>
            {
                Rectangle bounds = canvas.Bounds;
                int outerRadii = (int)(Math.Min(bounds.Width, bounds.Height) * sizeMult);
                Star star = new(new PointF(bounds.Width / 2F, bounds.Height / 2F), 5, outerRadii / 2F, outerRadii);
                Matrix4x4 builder = Matrix4x4.CreateTranslation(dx, dy, 0);
                canvas.Apply(star.Transform(builder), ctx => ctx.DetectEdges());
            }),
            testOutputDetails: testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithFile(TestImages.Png.Ducky, PixelTypes.Rgba32)]
    public void ClipConstrainsOperationToClipBounds<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            x => x.Paint(canvas =>
            {
                Rectangle bounds = canvas.Bounds;
                RectangleF rect = new(0, 0, bounds.Width / 2F, bounds.Height / 2F);
                RectangularPolygon clipRect = new(rect);
                canvas.Apply(clipRect, ctx => ctx.Flip(FlipMode.Vertical));
            }),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    [Fact]
    public void ClipIssue250VerticalHorizontalCountShouldMatch()
    {
        PathCollection clip = new(new RectangularPolygon(new PointF(24, 16), new PointF(777, 385)));

        Path vertical = new(new LinearLineSegment(new PointF(26, 384), new PointF(26, 163)));
        Path horizontal = new(new LinearLineSegment(new PointF(26, 163), new PointF(176, 163)));

        IPath reverse = vertical.Clip(clip);
        int verticalCount = vertical.Clip(reverse).Flatten().Select(x => x.Points).Count();

        reverse = horizontal.Clip(clip);
        int horizontalCount = horizontal.Clip(reverse).Flatten().Select(x => x.Points).Count();

        Assert.Equal(verticalCount, horizontalCount);
    }
}
