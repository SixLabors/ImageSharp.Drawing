// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    public static readonly TheoryData<string, byte, float> DrawBezierData =
        new()
        {
            { "White", 255, 1.5F },
            { "Red", 255, 3F },
            { "HotPink", 255, 5F },
            { "HotPink", 150, 5F },
            { "White", 255, 15F },
        };

    [Theory]
    [WithSolidFilledImages(nameof(DrawBezierData), 300, 450, "Blue", PixelTypes.Rgba32)]
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
        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.DrawBezier(Pens.Solid(color, 5F), points)));
        image.DebugSave(
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
