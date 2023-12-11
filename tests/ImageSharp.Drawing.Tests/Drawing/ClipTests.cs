// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class ClipTests
{
    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 0, 0, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, -20, -20, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, -20, -100, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 20, 20, 0.5)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 40, 60, 0.2)]
    public void Clip<TPixel>(TestImageProvider<TPixel> provider, float dx, float dy, float sizeMult)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        FormattableString testDetails = $"offset_x{dx}_y{dy}";
        provider.RunValidatingProcessorTest(
            x =>
            {
                Size size = x.GetCurrentSize();
                int outerRadii = (int)(Math.Min(size.Width, size.Height) * sizeMult);
                var star = new Star(new PointF(size.Width / 2, size.Height / 2), 5, outerRadii / 2, outerRadii);

                var builder = Matrix3x2.CreateTranslation(new Vector2(dx, dy));
                x.Clip(star.Transform(builder), x => x.DetectEdges());
            },
            testOutputDetails: testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Fact]
    public void Issue250_Vertical_Horizontal_Count_Should_Match()
    {
        var clip = new PathCollection(new RectangularPolygon(new PointF(24, 16), new PointF(777, 385)));

        var vert = new Path(new LinearLineSegment(new PointF(26, 384), new PointF(26, 163)));
        var horiz = new Path(new LinearLineSegment(new PointF(26, 163), new PointF(176, 163)));

        IPath reverse = vert.Clip(clip);
        IEnumerable<ReadOnlyMemory<PointF>> result1 = vert.Clip(reverse).Flatten().Select(x => x.Points);

        reverse = horiz.Clip(clip);
        IEnumerable<ReadOnlyMemory<PointF>> result2 = horiz.Clip(reverse).Flatten().Select(x => x.Points);

        bool same = result1.Count() == result2.Count();
    }
}
