// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class SolidBezierTests
{
    [Theory]
    [WithBlankImage(500, 500, PixelTypes.Rgba32)]
    public void FilledBezier<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        {
            new Vector2(10, 400),
            new Vector2(30, 10),
            new Vector2(240, 30),
            new Vector2(300, 400)
        };

        Color blue = Color.Blue;
        Color hotPink = Color.HotPink;

        using (Image<TPixel> image = provider.GetImage())
        {
            image.Mutate(x => x.BackgroundColor(blue));
            image.Mutate(x => x.Fill(hotPink, new Polygon(new CubicBezierLineSegment(simplePath))));
            image.DebugSave(provider);
            image.CompareToReferenceOutput(provider);
        }
    }

    [Theory]
    [WithBlankImage(500, 500, PixelTypes.Rgba32)]
    public void OverlayByFilledPolygonOpacity<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        {
            new Vector2(10, 400),
            new Vector2(30, 10),
            new Vector2(240, 30),
            new Vector2(300, 400)
        };

        Color color = Color.HotPink.WithAlpha(150 / 255F);

        using (var image = provider.GetImage() as Image<Rgba32>)
        {
            image.Mutate(x => x.BackgroundColor(Color.Blue));
            image.Mutate(x => x.Fill(color, new Polygon(new CubicBezierLineSegment(simplePath))));
            image.DebugSave(provider);
            image.CompareToReferenceOutput(provider);
        }
    }
}
