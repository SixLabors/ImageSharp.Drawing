// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    public static TheoryData<int, int> FillOutsideBoundsCircleCoordinates { get; } = new()
    {
        { -110, -60 }, { 0, -60 }, { 110, -60 },
        { -110, -50 }, { 0, -50 }, { 110, -50 },
        { -110, -49 }, { 0, -49 }, { 110, -49 },
        { -110, -20 }, { 0, -20 }, { 110, -20 },
        { -110, -50 }, { 0, -60 }, { 110, -60 },
        { -110, 0 }, { -99, 0 }, { 0, 0 }, { 99, 0 }, { 110, 0 },
    };

    [Theory]
    [InlineData(-100)]
    [InlineData(-99)]
    [InlineData(99)]
    [InlineData(100)]
    public void FillOutsideBoundsDrawRectactangleOutsideBoundsDrawingArea(int xpos)
    {
        int width = 100;
        int height = 100;

        using Image<Rgba32> image = new(width, height, Color.Red.ToPixel<Rgba32>());

        Rectangle rectangle = new(xpos, 0, width, height);
        image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(Brushes.Solid(Color.Black), rectangle)));
    }

    [Theory]
    [WithSolidFilledImages(nameof(FillOutsideBoundsCircleCoordinates), 100, 100, nameof(Color.Red), PixelTypes.Rgba32)]
    public void FillOutsideBoundsDrawCircleOutsideBoundsDrawingArea(TestImageProvider<Rgba32> provider, int xpos, int ypos)
    {
        int width = 100;
        int height = 100;

        EllipsePolygon circle = new(xpos, ypos, width, height);

        provider.RunValidatingProcessorTest(
            ctx => ctx.Paint(canvas => canvas.Fill(Brushes.Solid(Color.Black), circle)),
            $"({xpos}_{ypos})",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
