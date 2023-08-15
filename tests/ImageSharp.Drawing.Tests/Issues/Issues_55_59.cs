// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

// https://github.com/SixLabors/ImageSharp.Drawing/issues/55
// https://github.com/SixLabors/ImageSharp.Drawing/issues/59
public class Issues_55_59
{
    [Fact]
    public void SimplifyOutOfRangeExceptionDrawLines()
    {
        PointF[] line = new[]
        {
            new PointF(1, 48),
            new PointF(5, 77),
            new PointF(35, 0),
            new PointF(33, 8),
            new PointF(11, 23)
        };

        using var image = new Image<Rgba32>(100, 100);
        image.Mutate(imageContext => imageContext.DrawLine(new Rgba32(255, 0, 0), 1, line));
    }

    [Fact]
    public void SimplifyOutOfRangeExceptionDraw()
    {
        var path = new Path(
            new LinearLineSegment(new PointF(592.0153f, 1156.238f), new PointF(592.4992f, 1157.138f)),
            new LinearLineSegment(new PointF(592.4992f, 1157.138f), new PointF(593.3998f, 1156.654f)),
            new LinearLineSegment(new PointF(593.3998f, 1156.654f), new PointF(592.916f, 1155.754f)),
            new LinearLineSegment(new PointF(592.916f, 1155.754f), new PointF(592.0153f, 1156.238f)));

        using var image = new Image<Rgba32>(2000, 2000);
        image.Mutate(imageContext => imageContext.Draw(new Rgba32(255, 0, 0), 1, path));
    }
}
