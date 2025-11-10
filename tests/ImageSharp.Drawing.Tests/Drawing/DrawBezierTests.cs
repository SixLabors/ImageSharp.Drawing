// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class DrawBezierTests
{
    public static readonly TheoryData<string, byte, float> DrawPathData
        = new()
        {
        { "White", 255, 1.5f },
        { "Red", 255, 3 },
        { "HotPink", 255, 5 },
        { "HotPink", 150, 5 },
        { "White", 255, 15 },
    };

    [Theory]
    [WithSolidFilledImages(nameof(DrawPathData), 300, 450, "Blue", PixelTypes.Rgba32)]
    public void DrawBeziers<TPixel>(TestImageProvider<TPixel> provider, string colorName, byte alpha, float thickness)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points =
        [
            new Vector2(10, 400),
            new Vector2(30, 10),
            new Vector2(240, 30),
            new Vector2(300, 400)
        ];

        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha / 255F);

        FormattableString testDetails = $"{colorName}_A{alpha}_T{thickness}";

        provider.RunValidatingProcessorTest(
            x => x.DrawBeziers(color, 5f, points),
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
