// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class DrawPathTests
{
    public static readonly TheoryData<string, byte, float> DrawPathData =
        new()
        {
            { "White", 255, 1.5f },
            { "Red", 255, 3 },
            { "HotPink", 255, 5 },
            { "HotPink", 150, 5 },
            { "White", 255, 15 },
        };

    [Theory]
    [WithSolidFilledImages(nameof(DrawPathData), 300, 600, "Blue", PixelTypes.Rgba32)]
    public void DrawPath<TPixel>(TestImageProvider<TPixel> provider, string colorName, byte alpha, float thickness)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        LinearLineSegment linearSegment = new(
            new Vector2(10, 10),
            new Vector2(200, 150),
            new Vector2(50, 300));
        CubicBezierLineSegment bezierSegment = new(
            new Vector2(50, 300),
            new Vector2(500, 500),
            new Vector2(60, 10),
            new Vector2(10, 400));

        ArcLineSegment ellipticArcSegment1 = new(new Vector2(10, 400), new Vector2(150, 450), new SizeF((float)Math.Sqrt(5525), 40), GeometryUtilities.RadianToDegree((float)Math.Atan2(25, 70)), true, true);
        ArcLineSegment ellipticArcSegment2 = new(new(150, 450), new(149F, 450), new SizeF(140, 70), 0, true, true);

        Path path = new(linearSegment, bezierSegment, ellipticArcSegment1, ellipticArcSegment2);

        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha / 255f);

        FormattableString testDetails = $"{colorName}_A{alpha}_T{thickness}";

        provider.RunValidatingProcessorTest(
            x => x.Draw(color, thickness, path),
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(256, 256, "Black", PixelTypes.Rgba32)]
    public void PathExtendingOffEdgeOfImageShouldNotBeCropped<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = Color.White;
        SolidPen pen = Pens.Solid(color, 5f);

        provider.RunValidatingProcessorTest(
            x =>
                {
                    for (int i = 0; i < 300; i += 20)
                    {
                        PointF[] points = new PointF[] { new Vector2(100, 2), new Vector2(-10, i) };
                        x.DrawLine(pen, points);
                    }
                },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(40, 40, "White", PixelTypes.Rgba32)]
    public void DrawPathClippedOnTop<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points =
        {
            new PointF(10f, -10f),
            new PointF(20f, 20f),
            new PointF(30f, -30f)
        };

        IPath path = new PathBuilder().AddLines(points).Build();

        provider.VerifyOperation(
            image => image.Mutate(x => x.Draw(Color.Black, 1, path)),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 360)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 359)]
    public void DrawCircleUsingAddArc<TPixel>(TestImageProvider<TPixel> provider, float sweep)
    where TPixel : unmanaged, IPixel<TPixel>
    {
        IPath path = new PathBuilder().AddArc(new Point(150, 150), 50, 50, 0, 40, sweep).Build();

        provider.VerifyOperation(
            image => image.Mutate(x => x.Draw(Color.Black, 1, path)),
            testOutputDetails: $"{sweep}",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, true)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, false)]
    public void DrawCircleUsingArcTo<TPixel>(TestImageProvider<TPixel> provider, bool sweep)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Point origin = new(150, 150);
        IPath path = new PathBuilder().MoveTo(origin).ArcTo(50, 50, 0, true, sweep, origin).Build();

        provider.VerifyOperation(
            image => image.Mutate(x => x.Draw(Color.Black, 1, path)),
            testOutputDetails: $"{sweep}",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
