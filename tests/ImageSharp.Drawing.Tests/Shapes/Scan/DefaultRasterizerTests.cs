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

        float[] expected = Rasterize(ScanlineRasterizer.Instance, path, options);
        float[] actual = Rasterize(DefaultRasterizer.Instance, path, options);

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

        float[] expected = Rasterize(ScanlineRasterizer.Instance, path, options);
        float[] actual = Rasterize(DefaultRasterizer.Instance, path, options);

        AssertCoverageEqual(expected, actual);
    }

    private static float[] Rasterize(DefaultRasterizer rasterizer, IPath path, in RasterizerOptions options)
    {
        int width = options.Interest.Width;
        int height = options.Interest.Height;
        float[] coverage = new float[width * height];
        CaptureState state = new(coverage, width, options.Interest.Top);

        rasterizer.Rasterize(path, options, Configuration.Default.MemoryAllocator, ref state, CaptureScanline);

        return coverage;
    }

    private static float[] Rasterize(ScanlineRasterizer rasterizer, IPath path, in RasterizerOptions options)
    {
        int width = options.Interest.Width;
        int height = options.Interest.Height;
        float[] coverage = new float[width * height];
        CaptureState state = new(coverage, width, options.Interest.Top);

        rasterizer.Rasterize(path, options, Configuration.Default.MemoryAllocator, ref state, CaptureScanline);

        return coverage;
    }

    private static void CaptureScanline(int y, Span<float> scanline, ref CaptureState state)
    {
        int row = y - state.Top;
        scanline.CopyTo(state.Coverage.AsSpan(row * state.Width, state.Width));
    }

    private static void AssertCoverageEqual(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 6);
        }
    }

    private readonly struct CaptureState
    {
        public CaptureState(float[] coverage, int width, int top)
        {
            this.Coverage = coverage;
            this.Width = width;
            this.Top = top;
        }

        public float[] Coverage { get; }

        public int Width { get; }

        public int Top { get; }
    }
}
