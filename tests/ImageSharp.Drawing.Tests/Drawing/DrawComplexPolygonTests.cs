// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class DrawComplexPolygonTests
{
    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, false, false, false)]
    //[WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, true, false, false)]
    //[WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, false, true, false)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, false, false, true)]
    public void DrawComplexPolygon<TPixel>(TestImageProvider<TPixel> provider, bool overlap, bool transparent, bool dashed)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Polygon simplePath = new(new LinearLineSegment(
            new Vector2(10, 10),
            new Vector2(200, 150),
            new Vector2(50, 300)));

        Polygon hole1 = new(new LinearLineSegment(
            new Vector2(37, 85),
            overlap ? new Vector2(130, 40) : new Vector2(93, 85),
            new Vector2(65, 137)));

        IPath clipped = simplePath.Clip(hole1);

        Color color = Color.White;
        if (transparent)
        {
            color = color.WithAlpha(150 / 255F);
        }

        string testDetails = string.Empty;
        if (overlap)
        {
            testDetails += "_Overlap";
        }

        if (transparent)
        {
            testDetails += "_Transparent";
        }

        if (dashed)
        {
            testDetails += "_Dashed";
        }

        Pen pen = dashed ? Pens.Dash(color, 5f) : Pens.Solid(color, 5f);

        // clipped = new RectangularPolygon(RectangleF.FromLTRB(60, 260, 200, 280));

        provider.RunValidatingProcessorTest(
            x => x.Draw(pen, clipped),
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
