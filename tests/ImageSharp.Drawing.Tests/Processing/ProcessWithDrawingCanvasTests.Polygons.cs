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
    public static TheoryData<bool, IntersectionRule> FillPolygon_Complex_Data { get; } =
        new()
        {
            { false, IntersectionRule.EvenOdd },
            { false, IntersectionRule.NonZero },
            { true, IntersectionRule.EvenOdd },
            { true, IntersectionRule.NonZero },
        };

    public static readonly TheoryData<bool, IntersectionRule> FillPolygon_EllipsePolygon_Data =
        new()
        {
            { false, IntersectionRule.EvenOdd },
            { false, IntersectionRule.NonZero },
            { true, IntersectionRule.EvenOdd },
            { true, IntersectionRule.NonZero },
        };

    [Theory]
    [WithSolidFilledImages(8, 12, nameof(Color.Black), PixelTypes.Rgba32, 0)]
    [WithSolidFilledImages(8, 12, nameof(Color.Black), PixelTypes.Rgba32, 8)]
    [WithSolidFilledImages(8, 12, nameof(Color.Black), PixelTypes.Rgba32, 16)]
    public void FillPolygon_Solid_Basic<TPixel>(TestImageProvider<TPixel> provider, int antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] polygon1 = PolygonFactory.CreatePointArray((2, 2), (6, 2), (6, 4), (2, 4));
        PointF[] polygon2 = PolygonFactory.CreatePointArray((2, 8), (4, 6), (6, 8), (4, 10));
        Polygon shape1 = new(new LinearLineSegment(polygon1));
        Polygon shape2 = new(new LinearLineSegment(polygon2));
        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = antialias > 0 }
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas =>
            {
                canvas.Fill(Brushes.Solid(Color.White), shape1);
                canvas.Fill(Brushes.Solid(Color.White), shape2);
            }),
            testOutputDetails: $"aa{antialias}",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 0.6f, true)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, false)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Bgr24, "Yellow", 1f, true)]
    public void FillPolygon_Solid<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 10), new Vector2(200, 150), new Vector2(50, 300)
        ];
        Polygon polygon = new(new LinearLineSegment(simplePath));
        Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = antialias }
        };

        string aa = antialias ? string.Empty : "_NoAntialias";
        FormattableString outputDetails = $"{colorName}_A{alpha}{aa}";

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(color), polygon)),
            outputDetails,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32)]
    public void FillPolygon_Solid_Transformed<TPixel>(TestImageProvider<TPixel> provider)
       where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 10), new Vector2(200, 150), new Vector2(50, 300)
        ];
        Polygon polygon = new(new LinearLineSegment(simplePath));
        DrawingOptions options = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateSkew(GeometryUtilities.DegreeToRadian(-15), 0, new Vector2(200, 200)))
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), polygon)));
    }

    [Theory]
    [WithBasicTestPatternImages(100, 100, PixelTypes.Rgba32)]
    public void FillPolygon_RectangularPolygon_Solid_Transformed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectangularPolygon polygon = new(25, 25, 50, 50);
        DrawingOptions options = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation((float)Math.PI / 4, new PointF(50, 50)))
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), polygon)));
    }

    [Theory]
    [WithBasicTestPatternImages(100, 100, PixelTypes.Rgba32)]
    public void FillPolygon_RectangularPolygon_Solid_TransformedUsingConfiguration<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectangularPolygon polygon = new(25, 25, 50, 50);
        DrawingOptions options = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation((float)Math.PI / 4, new PointF(50, 50)))
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), polygon)));
    }

    [Theory]
    [WithBasicTestPatternImages(nameof(FillPolygon_Complex_Data), 100, 100, PixelTypes.Rgba32)]
    public void FillPolygon_Complex<TPixel>(TestImageProvider<TPixel> provider, bool reverse, IntersectionRule intersectionRule)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] contour = PolygonFactory.CreatePointArray((20, 20), (80, 20), (80, 80), (20, 80));
        PointF[] hole = PolygonFactory.CreatePointArray((40, 40), (40, 60), (60, 60), (60, 40));

        if (reverse)
        {
            Array.Reverse(contour);
            Array.Reverse(hole);
        }

        ComplexPolygon polygon = new(
            new Path(new LinearLineSegment(contour)),
            new Path(new LinearLineSegment(hole)));

        DrawingOptions options = new()
        {
            ShapeOptions = new ShapeOptions { IntersectionRule = intersectionRule }
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), polygon)),
            testOutputDetails: $"Reverse({reverse})_IntersectionRule({intersectionRule})",
            comparer: ImageComparer.TolerantPercentage(0.01f),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(200, 200, PixelTypes.Rgba32, false)]
    [WithBasicTestPatternImages(200, 200, PixelTypes.Rgba32, true)]
    public void FillPolygon_Concave<TPixel>(TestImageProvider<TPixel> provider, bool reverse)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points =
        [
            new Vector2(8, 8),
            new Vector2(64, 8),
            new Vector2(64, 64),
            new Vector2(120, 64),
            new Vector2(120, 120),
            new Vector2(8, 120)
        ];
        if (reverse)
        {
            Array.Reverse(points);
        }

        Polygon polygon = new(new LinearLineSegment(points));
        Color color = Color.LightGreen;

        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.Fill(Brushes.Solid(color), polygon)),
            testOutputDetails: $"Reverse({reverse})",
            comparer: ImageComparer.TolerantPercentage(0.01f),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(64, 64, "Black", PixelTypes.Rgba32)]
    public void FillPolygon_StarCircle(TestImageProvider<Rgba32> provider)
    {
        EllipsePolygon circle = new(32, 32, 30);
        Star star = new(32, 32, 7, 10, 27);
        IPath shape = circle.Clip(star);

        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.Fill(Brushes.Solid(Color.White), shape)),
            comparer: ImageComparer.TolerantPercentage(0.01f),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(128, 128, "Black", PixelTypes.Rgba32, BooleanOperation.Intersection)]
    [WithSolidFilledImages(128, 128, "Black", PixelTypes.Rgba32, BooleanOperation.Union)]
    [WithSolidFilledImages(128, 128, "Black", PixelTypes.Rgba32, BooleanOperation.Difference)]
    [WithSolidFilledImages(128, 128, "Black", PixelTypes.Rgba32, BooleanOperation.Xor)]
    public void FillPolygon_StarCircle_AllOperations(TestImageProvider<Rgba32> provider, BooleanOperation operation)
    {
        IPath circle = new EllipsePolygon(36, 36, 36).Translate(28, 28);
        Star star = new(64, 64, 5, 24, 64);

        // See http://www.angusj.com/clipper2/Docs/Units/Clipper/Types/ClipType.htm for reference.
        ShapeOptions shapeOptions = new() { BooleanOperation = operation };
        IPath shape = star.Clip(shapeOptions, circle);
        DrawingOptions options = new() { ShapeOptions = shapeOptions };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas =>
            {
                canvas.Fill(Brushes.Solid(Color.DeepPink), circle);
                canvas.Fill(Brushes.Solid(Color.LightGray), star);
                canvas.Fill(Brushes.Solid(Color.ForestGreen), shape);
            }),
            testOutputDetails: operation.ToString(),
            comparer: ImageComparer.TolerantPercentage(0.01F),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32)]
    public void FillPolygon_Pattern<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 10), new Vector2(200, 150), new Vector2(50, 300)
        ];
        Polygon polygon = new(new LinearLineSegment(simplePath));
        PatternBrush brush = Brushes.Horizontal(Color.Yellow);

        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.Fill(brush, polygon)),
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, TestImages.Png.Ducky)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, TestImages.Bmp.Car)]
    public void FillPolygon_ImageBrush<TPixel>(TestImageProvider<TPixel> provider, string brushImageName)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 10), new Vector2(200, 50), new Vector2(50, 200)
        ];
        Polygon polygon = new(new LinearLineSegment(simplePath));

        using Image<TPixel> brushImage = Image.Load<TPixel>(TestFile.Create(brushImageName).Bytes);
        ImageBrush<TPixel> brush = new(brushImage);

        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.Fill(brush, polygon)),
            System.IO.Path.GetFileNameWithoutExtension(brushImageName),
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, TestImages.Png.Ducky)]
    [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, TestImages.Bmp.Car)]
    public void FillPolygon_ImageBrush_Rect<TPixel>(TestImageProvider<TPixel> provider, string brushImageName)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] simplePath =
        [
            new Vector2(10, 10), new Vector2(200, 50), new Vector2(50, 200)
        ];
        Polygon polygon = new(new LinearLineSegment(simplePath));

        using Image<TPixel> brushImage = Image.Load<TPixel>(TestFile.Create(brushImageName).Bytes);

        float top = brushImage.Height / 4F;
        float left = brushImage.Width / 4F;
        float height = top * 2;
        float width = left * 2;

        ImageBrush<TPixel> brush = new(brushImage, new RectangleF(left, top, width, height));

        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.Fill(brush, polygon)),
            System.IO.Path.GetFileNameWithoutExtension(brushImageName) + "_rect",
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(250, 250, PixelTypes.Rgba32)]
    public void FillPolygon_RectangularPolygon<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectangularPolygon polygon = new(10, 10, 190, 140);
        Color color = Color.White;

        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.Fill(Brushes.Solid(color), polygon)),
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(200, 200, PixelTypes.Rgba32, 3, 50, 0f)]
    [WithBasicTestPatternImages(200, 200, PixelTypes.Rgba32, 3, 60, 20f)]
    [WithBasicTestPatternImages(200, 200, PixelTypes.Rgba32, 3, 60, -180f)]
    [WithBasicTestPatternImages(200, 200, PixelTypes.Rgba32, 5, 70, 0f)]
    [WithBasicTestPatternImages(200, 200, PixelTypes.Rgba32, 7, 80, -180f)]
    public void FillPolygon_RegularPolygon<TPixel>(TestImageProvider<TPixel> provider, int vertices, float radius, float angleDeg)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RegularPolygon polygon = new(100, 100, vertices, radius, angleDeg);
        Color color = Color.Yellow;

        FormattableString testOutput = $"V({vertices})_R({radius})_Ang({angleDeg})";
        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.Fill(Brushes.Solid(color), polygon)),
            testOutput,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(nameof(FillPolygon_EllipsePolygon_Data), 200, 200, PixelTypes.Rgba32)]
    public void FillPolygon_EllipsePolygon<TPixel>(TestImageProvider<TPixel> provider, bool reverse, IntersectionRule intersectionRule)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        IPath polygon = new EllipsePolygon(100, 100, 80, 120);
        if (reverse)
        {
            polygon = polygon.Reverse();
        }

        Color color = Color.Azure;
        DrawingOptions options = new()
        {
            ShapeOptions = new ShapeOptions { IntersectionRule = intersectionRule }
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(color), polygon)),
            testOutputDetails: $"Reverse({reverse})_IntersectionRule({intersectionRule})",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(60, 60, "Blue", PixelTypes.Rgba32)]
    public void FillPolygon_IntersectionRules_OddEven<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Polygon poly = new(new LinearLineSegment(
            new PointF(10, 30),
            new PointF(10, 20),
            new PointF(50, 20),
            new PointF(50, 50),
            new PointF(20, 50),
            new PointF(20, 10),
            new PointF(30, 10),
            new PointF(30, 40),
            new PointF(40, 40),
            new PointF(40, 30),
            new PointF(10, 30)));

        DrawingOptions options = new()
        {
            ShapeOptions = new ShapeOptions { IntersectionRule = IntersectionRule.EvenOdd }
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.HotPink), poly)),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(60, 60, "Blue", PixelTypes.Rgba32)]
    public void FillPolygon_IntersectionRules_Nonzero<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Polygon poly = new(new LinearLineSegment(
            new PointF(10, 30),
            new PointF(10, 20),
            new PointF(50, 20),
            new PointF(50, 50),
            new PointF(20, 50),
            new PointF(20, 10),
            new PointF(30, 10),
            new PointF(30, 40),
            new PointF(40, 40),
            new PointF(40, 30),
            new PointF(10, 30)));

        DrawingOptions options = new()
        {
            ShapeOptions = new ShapeOptions { IntersectionRule = IntersectionRule.NonZero }
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.HotPink), poly)),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
