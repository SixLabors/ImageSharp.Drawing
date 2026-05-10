// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_344
{
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void CanDrawWhereSegmentsOverlap<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            c => c.Paint(canvas =>
                {
                    Pen pen = Pens.Solid(Color.Aqua.WithAlpha(.3F), 1);
                    canvas.DrawLine(pen, new PointF(10, 10), new PointF(90, 10), new PointF(20, 10));
                }));

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void CanDrawWhereSegmentsOverlap_PathBuilder<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            c => c.Paint(canvas =>
            {
                PathBuilder pathBuilder = new();
                pathBuilder.MoveTo(10, 10);
                pathBuilder.LineTo(90, 10);
                pathBuilder.LineTo(20, 10);

                Pen pen = Pens.Solid(Color.Aqua.WithAlpha(.3F), 1);
                canvas.Draw(pen, pathBuilder);
            }));
}
