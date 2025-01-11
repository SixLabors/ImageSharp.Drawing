// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class FillOutsideBoundsTests
{
    [Theory]
    [InlineData(-100)] // Crash
    [InlineData(-99)] // Fine
    [InlineData(99)] // Fine
    [InlineData(100)] // Crash
    public void DrawRectactangleOutsideBoundsDrawingArea(int xpos)
    {
        int width = 100;
        int height = 100;

        using (var image = new Image<Rgba32>(width, height, Color.Red.ToPixel<Rgba32>()))
        {
            var rectangle = new Rectangle(xpos, 0, width, height);

            image.Mutate(x => x.Fill(Color.Black, rectangle));
        }
    }

    public static TheoryData<int, int> CircleCoordinates { get; } = new TheoryData<int, int>()
    {
        { -110, -60 }, { 0, -60 }, { 110, -60 },
        { -110, -50 }, { 0, -50 }, { 110, -50 },
        { -110, -49 }, { 0, -49 }, { 110, -49 },
        { -110, -20 }, { 0, -20 }, { 110, -20 },
        { -110, -50 }, { 0, -60 }, { 110, -60 },
        { -110, 0 }, { -99, 0 }, { 0, 0 }, { 99, 0 }, { 110, 0 },
    };

    [Theory]
    [WithSolidFilledImages(nameof(CircleCoordinates), 100, 100, nameof(Color.Red), PixelTypes.Rgba32)]
    public void DrawCircleOutsideBoundsDrawingArea(TestImageProvider<Rgba32> provider, int xpos, int ypos)
    {
        int width = 100;
        int height = 100;

        using Image<Rgba32> image = provider.GetImage();
        var circle = new EllipsePolygon(xpos, ypos, width, height);

        provider.RunValidatingProcessorTest(
            x => x.Fill(Color.Black, circle),
            $"({xpos}_{ypos})",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
