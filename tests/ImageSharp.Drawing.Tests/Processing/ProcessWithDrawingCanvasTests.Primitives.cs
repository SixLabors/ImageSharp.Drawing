// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
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

    public static readonly TheoryData<string, byte, float> DrawPathData =
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
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.DrawBezier(Pens.Solid(color, 5F), points)));
        image.DebugSave(
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.001F),
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(500, 500, PixelTypes.Rgba32)]
    public void SolidBezierFilledBezier<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 400),
            new Vector2(30, 10),
            new Vector2(240, 30),
            new Vector2(300, 400)
        ];

        Polygon polygon = new(new CubicBezierLineSegment(simplePath));
        SolidBrush brush = Brushes.Solid(Color.HotPink);

        provider.RunValidatingProcessorTest(
            ctx => ctx.Paint(canvas =>
            {
                canvas.Clear(Brushes.Solid(Color.Blue));
                canvas.Fill(brush, polygon);
            }));
    }

    [Theory]
    [WithBlankImage(500, 500, PixelTypes.Rgba32)]
    public void SolidBezierOverlayByFilledPolygonOpacity<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 400),
            new Vector2(30, 10),
            new Vector2(240, 30),
            new Vector2(300, 400)
        ];

        Polygon polygon = new(new CubicBezierLineSegment(simplePath));
        SolidBrush brush = Brushes.Solid(Color.HotPink.WithAlpha(150 / 255F));

        provider.RunValidatingProcessorTest(
            ctx => ctx.Paint(canvas =>
            {
                canvas.Clear(Brushes.Solid(Color.Blue));
                canvas.Fill(brush, polygon);
            }));
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1F, 2.5F, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 0.6F, 10F, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1F, 5F, false)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Bgr24, "Yellow", 1F, 10F, true)]
    public void DrawLines_Simple<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(color, thickness);
        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 1F, true)]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 5F, true)]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 1F, false)]
    [WithSolidFilledImages(30, 30, "White", PixelTypes.Rgba32, 5F, false)]
    public void DrawLinesInvalidPoints<TPixel>(TestImageProvider<TPixel> provider, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SolidPen pen = new(Color.Black, thickness);
        PointF[] path = [new Vector2(15F, 15F), new Vector2(15F, 15F)];
        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = antialias }
        };

        string aa = antialias ? string.Empty : "_NoAntialias";
        FormattableString outputDetails = $"T({thickness}){aa}";

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.DrawLine(pen, path)));
        image.DebugSave(provider, outputDetails, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.001F),
            provider,
            outputDetails,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1F, 5F, false)]
    public void DrawLines_Dash<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.Dash(color, thickness);
        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "LightGreen", 1F, 5F, false)]
    public void DrawLines_Dot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.Dot(color, thickness);
        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1F, 5F, false)]
    public void DrawLines_DashDot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.DashDot(color, thickness);
        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Black", 1F, 5F, false)]
    public void DrawLines_DashDotDot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        Pen pen = Pens.DashDotDot(color, thickness);
        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1F, 5F, true)]
    public void DrawLines_EndCapRound<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        PatternPen pen = new(new PenOptions(color, thickness, [3F, 3F])
        {
            StrokeOptions = new StrokeOptions { LineCap = LineCap.Round },
        });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1F, 5F, true)]
    public void DrawLines_EndCapButt<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        PatternPen pen = new(new PenOptions(color, thickness, [3F, 3F])
        {
            StrokeOptions = new StrokeOptions { LineCap = LineCap.Butt },
        });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1F, 5F, true)]
    public void DrawLines_EndCapSquare<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        PatternPen pen = new(new PenOptions(color, thickness, [3F, 3F])
        {
            StrokeOptions = new StrokeOptions { LineCap = LineCap.Square },
        });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1F, 10F, true)]
    public void DrawLines_JointStyleRound<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(new PenOptions(color, thickness)
        {
            StrokeOptions = new StrokeOptions { LineJoin = LineJoin.Round },
        });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1F, 10F, true)]
    public void DrawLines_JointStyleSquare<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(new PenOptions(color, thickness)
        {
            StrokeOptions = new StrokeOptions { LineJoin = LineJoin.Bevel },
        });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1F, 10F, true)]
    public void DrawLines_JointStyleMiter<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        SolidPen pen = new(new PenOptions(color, thickness)
        {
            StrokeOptions = new StrokeOptions { LineJoin = LineJoin.Miter },
        });

        DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, false, false, false)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, true, false, false)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, false, true, false)]
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

        Pen pen = dashed ? Pens.Dash(color, 5F) : Pens.Solid(color, 5F);
        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(pen, clipped)));
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

    [Theory]
    [WithSolidFilledImages(300, 400, "Blue", PixelTypes.Rgba32, false, false)]
    [WithSolidFilledImages(300, 400, "Blue", PixelTypes.Rgba32, true, false)]
    [WithSolidFilledImages(300, 400, "Blue", PixelTypes.Rgba32, false, true)]
    public void FillComplexPolygon_SolidFill<TPixel>(TestImageProvider<TPixel> provider, bool overlap, bool transparent)
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

        Color color = Color.HotPink;
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

        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(color), clipped)));
        image.DebugSave(
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.001F),
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1F, 2.5F, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 0.6F, 10F, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1F, 5F, false)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Bgr24, "Yellow", 1F, 10F, true)]
    public void DrawPolygon<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 10),
            new Vector2(200, 150),
            new Vector2(50, 300)
        ];

        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        IPath polygon = new Polygon(new LinearLineSegment(simplePath));
        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = antialias }
        };

        string aa = antialias ? string.Empty : "_NoAntialias";
        FormattableString outputDetails = $"{colorName}_A({alpha})_T({thickness}){aa}";

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(Pens.Solid(color, thickness), polygon)));
        image.DebugSave(provider, outputDetails, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.001F),
            provider,
            outputDetails,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32)]
    public void DrawPolygon_Transformed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 10),
            new Vector2(200, 150),
            new Vector2(50, 300)
        ];

        IPath polygon = new Polygon(new LinearLineSegment(simplePath));
        DrawingOptions options = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateSkew(
                GeometryUtilities.DegreeToRadian(-15),
                0,
                new Vector2(200, 200)))
        };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(Pens.Solid(Color.White, 2.5F), polygon)));
        image.DebugSave(provider);
        image.CompareToReferenceOutput(ImageComparer.TolerantPercentage(0.001F), provider);
    }

    [Theory]
    [WithBasicTestPatternImages(100, 100, PixelTypes.Rgba32)]
    public void DrawPolygonRectangular_Transformed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectangularPolygon polygon = new(25, 25, 50, 50);
        DrawingOptions options = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation((float)Math.PI / 4, new PointF(50, 50)))
        };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(Pens.Solid(Color.White, 2.5F), polygon)));
        image.DebugSave(provider);
        image.CompareToReferenceOutput(ImageComparer.TolerantPercentage(0.001F), provider);
    }

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
        ArcLineSegment ellipticArcSegment2 = new(new PointF(150, 450), new PointF(149F, 450), new SizeF(140, 70), 0, true, true);
        Path path = new(linearSegment, bezierSegment, ellipticArcSegment1, ellipticArcSegment2);

        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha / 255F);
        FormattableString testDetails = $"{colorName}_A{alpha}_T{thickness}";
        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(Pens.Solid(color, thickness), path)));
        image.DebugSave(
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.001F),
            provider,
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(256, 256, "Black", PixelTypes.Rgba32)]
    public void DrawPathExtendingOffEdgeOfImageShouldNotBeCropped<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SolidPen pen = Pens.Solid(Color.White, 5F);
        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas =>
        {
            for (int i = 0; i < 300; i += 20)
            {
                PointF[] points = [new Vector2(100, 2), new Vector2(-10, i)];
                canvas.DrawLine(pen, points);
            }
        }));
        image.DebugSave(
            provider,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.001F),
            provider,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(40, 40, "White", PixelTypes.Rgba32)]
    public void DrawPathClippedOnTop<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points =
        [
            new(10F, -10F),
            new(20F, 20F),
            new(30F, -30F)
        ];

        IPath path = new PathBuilder().AddLines(points).Build();
        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(Pens.Solid(Color.Black, 1F), path)));
        image.DebugSave(
            provider,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            provider,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 360F)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 359F)]
    public void DrawPathCircleUsingAddArc<TPixel>(TestImageProvider<TPixel> provider, float sweep)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        IPath path = new PathBuilder().AddArc(new Point(150, 150), 50, 50, 0, 40, sweep).Build();
        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(Pens.Solid(Color.Black, 1F), path)));
        image.DebugSave(
            provider,
            $"{sweep}",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            provider,
            $"{sweep}",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, true)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, false)]
    public void DrawPathCircleUsingArcTo<TPixel>(TestImageProvider<TPixel> provider, bool sweep)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Point origin = new(150, 150);
        IPath path = new PathBuilder().MoveTo(origin).ArcTo(50, 50, 0, true, sweep, origin).Build();
        DrawingOptions options = new();

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Draw(Pens.Solid(Color.Black, 1F), path)));
        image.DebugSave(
            provider,
            $"{sweep}",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            provider,
            $"{sweep}",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
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
        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = antialias }
        };

        string aa = antialias ? string.Empty : "_NoAntialias";
        FormattableString outputDetails = $"{colorName}_A({alpha})_T({thickness}){aa}";

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.DrawLine(pen, simplePath)));
        image.DebugSave(provider, outputDetails, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.001F),
            provider,
            outputDetails,
            appendSourceFileOrDescription: false);
    }
}
