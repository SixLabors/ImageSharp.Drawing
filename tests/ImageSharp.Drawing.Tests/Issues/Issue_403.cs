// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

// https://github.com/SixLabors/ImageSharp.Drawing/issues/403
// Paths containing very large coordinates overflowed the 24.8 fixed-point midpoint
// computation in the rasterizer's recursive segment subdivider, so the segment never
// shrank and the recursion overflowed the stack.
public class Issue_403
{
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, 5_000_000F)]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, 500_000_000F)]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, 1E20F)]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, -5_000_000F)]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, float.NaN)]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, float.PositiveInfinity)]
    public void DrawLineWithHugeCoordinateDoesNotOverflow<TPixel>(TestImageProvider<TPixel> provider, float extreme)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points =
        [
            new(0, 0),
            new(extreme, 1),
            new(10, 10),
            new(5, 5),
            new(0, 0)
        ];

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(canvas =>
            canvas.DrawLine(new SolidPen(Color.Black, 4F), points)));

        image.DebugSave(provider, $"extreme-{extreme}", appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, 5_000_000F)]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32, 1E20F)]
    public void FillWithHugeCoordinateDoesNotOverflow<TPixel>(TestImageProvider<TPixel> provider, float extreme)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points =
        [
            new(0, 0),
            new(extreme, 1),
            new(10, 10),
            new(5, 5)
        ];

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(canvas =>
            canvas.Fill(Brushes.Solid(Color.Black), new Polygon(points))));

        image.DebugSave(provider, $"extreme-{extreme}", appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawLineWithHugeYCoordinateDoesNotOverflow<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points =
        [
            new(0, 0),
            new(1, 5_000_000),
            new(10, 10),
            new(5, 5),
            new(0, 0)
        ];

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(canvas =>
            canvas.DrawLine(new SolidPen(Color.Black, 4F), points)));

        image.DebugSave(provider, appendSourceFileOrDescription: false);
    }
}
