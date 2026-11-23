// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class DefaultRasterizerStrokeTests
{
    // Half widths of 12, 100 and 500 pixel pens.
    public static TheoryData<float> RoundArcRadii { get; } = new() { 6F, 50F, 250F };

    [Theory]
    [MemberData(nameof(RoundArcRadii))]
    public void GetArcSubdivisionCount_IncreasesWithArcDetailScale(float radius)
    {
        double[] scales = [0.01D, 0.5D, 1D, 4D, 16D];
        int previous = DefaultRasterizer.GetArcSubdivisionCount(radius, Math.PI, scales[0]);
        for (int i = 1; i < scales.Length; i++)
        {
            int count = DefaultRasterizer.GetArcSubdivisionCount(radius, Math.PI, scales[i]);

            Assert.True(count > previous, $"Scale {scales[i]} gave {count} vertices, scale {scales[i - 1]} gave {previous}.");
            previous = count;
        }
    }

    [Theory]
    [InlineData(0D)]
    [InlineData(-1D)]
    public void GetArcSubdivisionCount_FloorsScaleAtOneHundredth(double arcDetailScale)
    {
        int floored = DefaultRasterizer.GetArcSubdivisionCount(6F, Math.PI, 0.01D);

        Assert.Equal(floored, DefaultRasterizer.GetArcSubdivisionCount(6F, Math.PI, arcDetailScale));
    }

    [Fact]
    public void GetArcSubdivisionCount_LargeRadius_StaysBelowGpuBound()
    {
        // path_lowering.wgsl clamps the count at 1024 interior vertices. A 1000 pixel pen at the
        // highest scale under test must stay below that so both backends emit the same count.
        int count = DefaultRasterizer.GetArcSubdivisionCount(500F, Math.PI, 16D);

        Assert.InRange(count, 1, 1023);
    }

    /// <summary>
    /// Checks that the CPU stroke joins the ends only when the path is closed.
    /// </summary>
    /// <param name="isClosed">Whether the path is closed.</param>
    /// <param name="endsMeet">Whether both ends have the same coordinates.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Stroke_UsesDeclaredClosure(bool isClosed, bool endsMeet)
    {
        using Image<Rgba32> image = new(64, 64, Color.White.ToPixel<Rgba32>());
        image.Mutate(context => context.Paint(canvas => DrawClosureTestPath(canvas, isClosed, endsMeet)));

        Rgba32 expected = (isClosed ? Color.Black : Color.White).ToPixel<Rgba32>();
        Assert.Equal(expected, image[21, 21]);
    }

    /// <summary>
    /// Draws the dashed ellipse from issue 430.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    internal static void DrawDashedEllipse(DrawingCanvas canvas)
    {
        IPath ellipse = new EllipsePolygon(128, 128, 200, 200);
        StrokeOptions stroke = new() { LineJoin = LineJoin.Miter, LineCap = LineCap.Butt };
        Pen dashed = new PatternPen(new PenOptions(Color.Black, 2, [4F, 2F, 1F, 2F]) { StrokeOptions = stroke });
        canvas.Draw(dashed, ellipse);
    }

    /// <summary>
    /// Draws a clipped point and segments with equal or close endpoints.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="cap">The stroke cap.</param>
    internal static void DrawPointAndSegments(DrawingCanvas canvas, LineCap cap)
    {
        Pen pen = new SolidPen(new PenOptions(Color.Black, 10) { StrokeOptions = new StrokeOptions { LineCap = cap } });
        Path first = new([new PointF(8, 8), new PointF(24, 24)]);
        Path second = new([new PointF(8, 24), new PointF(24, 8)]);
        IPath point = first.Clip(second);
        PointF zeroStart = new(32, 16);
        PointF zeroLineStart = new(48, 16);
        PointF shortStart = new(64, 16);
        PointF shortEnd = new(64.005F, 16.005F);
        PointF shortLineStart = new(80, 16);
        PointF shortLineEnd = new(80.005F, 16.005F);
        Path zeroPath = new([zeroStart, zeroStart]);
        Path shortPath = new([shortStart, shortEnd]);

        canvas.Draw(pen, point);
        canvas.Draw(pen, zeroPath);
        canvas.DrawLine(pen, zeroLineStart, zeroLineStart);

        // These short diagonal lines still give square caps a direction.
        canvas.Draw(pen, shortPath);
        canvas.DrawLine(pen, shortLineStart, shortLineEnd);
    }

    /// <summary>
    /// Draws a path with a miter corner at its start when it is closed.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="isClosed">Whether the path is closed.</param>
    /// <param name="endsMeet">Whether both ends have the same coordinates.</param>
    internal static void DrawClosureTestPath(DrawingCanvas canvas, bool isClosed, bool endsMeet)
    {
        PointF[] points =
        [
            new(24, 24),
            new(54, 24),
            new(24, 54),
            new(24, endsMeet ? 24 : 30)
        ];

        // Only the miter at the start covers pixel (21, 21). The open path
        // uses butt caps, even when the gap is shorter than the stroke width.
        IPath path = isClosed ? new Polygon(points) : new Path(points);
        SolidPen pen = new(new PenOptions(Color.Black, 10)
        {
            StrokeOptions = new StrokeOptions { LineCap = LineCap.Butt, LineJoin = LineJoin.Miter },
        });

        canvas.Draw(pen, path);
    }
}
