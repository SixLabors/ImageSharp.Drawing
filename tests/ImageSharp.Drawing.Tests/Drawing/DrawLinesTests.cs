// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class DrawLinesTests
{
    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, 2.5, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 0.6f, 10, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, 5, false)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Bgr24, "Yellow", 1f, 10, true)]
    public void DrawLines_Simple<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(color, thickness);

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 1f, true)]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 5f, true)]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 1f, false)]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 5f, false)]
    public void DrawLinesInvalidPoints<TPixel>(TestImageProvider<TPixel> provider, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SolidPen pen = new(Color.Black, thickness);
        PointF[] path = [new Vector2(15f, 15f), new Vector2(15f, 15f)];

        GraphicsOptions options = new()
        {
            Antialias = antialias
        };

        string aa = antialias ? string.Empty : "_NoAntialias";
        FormattableString outputDetails = $"T({thickness}){aa}";

        provider.RunValidatingProcessorTest(
            c => c.SetGraphicsOptions(options).DrawLine(pen, path),
            outputDetails,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, 5, false)]
    public void DrawLines_Dash<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.Dash(color, thickness);

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "LightGreen", 1f, 5, false)]
    public void DrawLines_Dot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.Dot(color, thickness);

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 5, false)]
    public void DrawLines_DashDot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.DashDot(color, thickness);

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Black", 1f, 5, false)]
    public void DrawLines_DashDotDot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.DashDotDot(color, thickness);

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 5, true)]
    public void DrawLines_EndCapRound<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        PatternPen pen = new(new PenOptions(color, thickness, [3f, 3f]) { EndCapStyle = EndCapStyle.Round });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 5, true)]
    public void DrawLines_EndCapButt<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        PatternPen pen = new(new PenOptions(color, thickness, [3f, 3f]) { EndCapStyle = EndCapStyle.Butt });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 5, true)]
    public void DrawLines_EndCapSquare<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        PatternPen pen = new(new PenOptions(color, thickness, [3f, 3f]) { EndCapStyle = EndCapStyle.Square });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 10, true)]
    public void DrawLines_JointStyleRound<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(new PenOptions(color, thickness) { JointStyle = JointStyle.Round });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 10, true)]
    public void DrawLines_JointStyleSquare<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(new PenOptions(color, thickness) { JointStyle = JointStyle.Square });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 10, true)]
    public void DrawLines_JointStyleMiter<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(new PenOptions(color, thickness) { JointStyle = JointStyle.Miter });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    private static void DrawLinesImpl<TPixel>(
        TestImageProvider<TPixel> provider,
        string colorName,
        float alpha,
        float thickness,
        bool antialias,
        Pen pen)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath = [new Vector2(10, 10), new Vector2(200, 150), new Vector2(50, 300)];

        GraphicsOptions options = new()
        { Antialias = antialias };

        string aa = antialias ? string.Empty : "_NoAntialias";
        FormattableString outputDetails = $"{colorName}_A({alpha})_T({thickness}){aa}";

        provider.RunValidatingProcessorTest(
            c => c.SetGraphicsOptions(options).DrawLine(pen, simplePath),
            outputDetails,
            appendSourceFileOrDescription: false);
    }
}
