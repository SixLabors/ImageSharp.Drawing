// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan;

public class DefaultRasterizerRegressionTests
{
    [Fact]
    public void EmitsCoverageForSubpixelThinRectangle()
    {
        RectangularPolygon path = new(0.3F, 0.2F, 0.7F, 1.423F);
        RasterizerOptions options = new(new Rectangle(0, 0, 12, 20), IntersectionRule.EvenOdd);
        CaptureState state = new(new float[options.Interest.Width * options.Interest.Height], options.Interest.Width, options.Interest.Top);

        DefaultRasterizer.Instance.Rasterize(path, options, Configuration.Default.MemoryAllocator, ref state, CaptureScanline);

        Assert.True(state.DirtyRows > 0);
        Assert.True(state.MaxCoverage > 0F);
    }

    [Fact]
    public void RasterizesFractionalRectangleCoverageDeterministically()
    {
        RectangularPolygon path = new(0.25F, 0.25F, 1F, 1F);
        RasterizerOptions options = new(new Rectangle(0, 0, 2, 2), IntersectionRule.NonZero);

        float[] coverage = Rasterize(DefaultRasterizer.Instance, path, options);
        float[] expected =
        [
            0.5625F, 0.1875F,
            0.1875F, 0.0625F
        ];

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], coverage[i], 3);
        }
    }

    [Fact]
    public void AliasedMode_EmitsBinaryCoverage()
    {
        RectangularPolygon path = new(0.25F, 0.25F, 1F, 1F);
        RasterizerOptions options = new(new Rectangle(0, 0, 2, 2), IntersectionRule.NonZero, RasterizationMode.Aliased);

        float[] coverage = Rasterize(DefaultRasterizer.Instance, path, options);
        float[] expected =
        [
            1F, 0F,
            0F, 0F
        ];

        Assert.Equal(expected, coverage);
    }

    [Fact]
    public void ThrowsForInterestTooWideForCoverStrideMath()
    {
        RectangularPolygon path = new(0F, 0F, 1F, 1F);
        RasterizerOptions options = new(new Rectangle(0, 0, (int.MaxValue / 2) + 1, 1), IntersectionRule.NonZero);
        NoopState state = default;

        void Rasterize() =>
            DefaultRasterizer.Instance.Rasterize(
                path,
                options,
                Configuration.Default.MemoryAllocator,
                ref state,
                static (int y, Span<float> scanline, ref NoopState localState) => { });

        ImageProcessingException exception = Assert.Throws<ImageProcessingException>(Rasterize);
        Assert.Contains("too large", exception.Message);
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

    private static void CaptureScanline(int y, Span<float> scanline, ref CaptureState state)
    {
        int row = y - state.Top;
        scanline.CopyTo(state.Coverage.AsSpan(row * state.Width, state.Width));
        state.DirtyRows++;

        for (int i = 0; i < scanline.Length; i++)
        {
            if (scanline[i] > state.MaxCoverage)
            {
                state.MaxCoverage = scanline[i];
            }
        }
    }

    private struct CaptureState
    {
        public CaptureState(float[] coverage, int width, int top)
        {
            this.Coverage = coverage;
            this.Width = width;
            this.Top = top;
            this.DirtyRows = 0;
            this.MaxCoverage = 0F;
        }

        public float[] Coverage { get; }

        public int Width { get; }

        public int Top { get; }

        public int DirtyRows { get; set; }

        public float MaxCoverage { get; set; }
    }

    private struct NoopState
    {
    }
}
