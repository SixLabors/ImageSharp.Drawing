// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan;

public class DefaultRasterizerTests
{
    [Theory]
    [InlineData(IntersectionRule.EvenOdd)]
    [InlineData(IntersectionRule.NonZero)]
    public void MatchesDefaultRasterizer_ForLargeSelfIntersectingPath(IntersectionRule rule)
    {
        IPath path = PolygonFactory.CreatePolygon(
            (1, 4),
            (1, 3),
            (3, 3),
            (3, 2),
            (2, 2),
            (2, 4),
            (1, 4),
            (1, 1),
            (4, 1),
            (4, 4),
            (3, 4),
            (3, 5),
            (2, 5),
            (2, 4),
            (1, 4))
            .Transform(Matrix3x2.CreateScale(200F));

        Rectangle interest = Rectangle.Ceiling(path.Bounds);
        RasterizerOptions options = new(interest, rule);

        float[] expected = RasterizeSequential(path, options);
        float[] actual = Rasterize(path, options);

        AssertCoverageEqual(expected, actual);
    }

    [Fact]
    public void MatchesDefaultRasterizer_ForPixelCenterSampling()
    {
        RectangularPolygon path = new(20.2F, 30.4F, 700.1F, 540.6F);
        Rectangle interest = Rectangle.Ceiling(path.Bounds);
        RasterizerOptions options = new(
            interest,
            IntersectionRule.NonZero,
            samplingOrigin: RasterizerSamplingOrigin.PixelCenter);

        float[] expected = RasterizeSequential(path, options);
        float[] actual = Rasterize(path, options);

        AssertCoverageEqual(expected, actual);
    }

    private static float[] Rasterize(IPath path, in RasterizerOptions options)
    {
        int width = options.Interest.Width;
        int height = options.Interest.Height;
        float[] coverage = new float[width * height];
        int top = options.Interest.Top;
        DefaultRasterizer.RasterizeRows(path, options, Configuration.Default.MemoryAllocator, CaptureRow);

        return coverage;

        void CaptureRow(int y, int startX, Span<float> rowCoverage)
        {
            int row = y - top;
            rowCoverage.CopyTo(coverage.AsSpan((row * width) + startX, rowCoverage.Length));
        }
    }

    private static float[] RasterizeSequential(IPath path, in RasterizerOptions options)
    {
        int width = options.Interest.Width;
        int height = options.Interest.Height;
        float[] coverage = new float[width * height];
        int top = options.Interest.Top;
        DefaultRasterizer.RasterizeRowsSequential(path, options, Configuration.Default.MemoryAllocator, CaptureRow);

        return coverage;

        void CaptureRow(int y, int startX, Span<float> rowCoverage)
        {
            int row = y - top;
            rowCoverage.CopyTo(coverage.AsSpan((row * width) + startX, rowCoverage.Length));
        }
    }

    private static void AssertCoverageEqual(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 6);
        }
    }
}
