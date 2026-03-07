// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.


// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Rasterization;

public class DefaultRasterizerRegressionTests
{
    [Fact]
    public void EmitsCoverageForSubpixelThinRectangle()
    {
        RectangularPolygon path = new(0.3F, 0.2F, 0.7F, 1.423F);
        RasterizerOptions options = new(new Rectangle(0, 0, 12, 20), IntersectionRule.EvenOdd, RasterizationMode.Antialiased, RasterizerSamplingOrigin.PixelBoundary, 0.5f);
        float[] coverage = new float[options.Interest.Width * options.Interest.Height];
        int width = options.Interest.Width;
        int top = options.Interest.Top;
        int dirtyRows = 0;
        float maxCoverage = 0F;

        DefaultRasterizer.RasterizeRows(path, options, Configuration.Default.MemoryAllocator, CaptureRow);

        Assert.True(dirtyRows > 0);
        Assert.True(maxCoverage > 0F);

        void CaptureRow(int y, int startX, Span<float> rowCoverage)
        {
            int row = y - top;
            rowCoverage.CopyTo(coverage.AsSpan((row * width) + startX, rowCoverage.Length));
            dirtyRows++;

            for (int i = 0; i < rowCoverage.Length; i++)
            {
                if (rowCoverage[i] > maxCoverage)
                {
                    maxCoverage = rowCoverage[i];
                }
            }
        }
    }

    [Fact]
    public void RasterizesFractionalRectangleCoverageDeterministically()
    {
        RectangularPolygon path = new(0.25F, 0.25F, 1F, 1F);
        RasterizerOptions options = new(new Rectangle(0, 0, 2, 2), IntersectionRule.NonZero, RasterizationMode.Antialiased, RasterizerSamplingOrigin.PixelBoundary, 0.5f);

        float[] coverage = Rasterize(path, options);
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
        RasterizerOptions options = new(new Rectangle(0, 0, 2, 2), IntersectionRule.NonZero, RasterizationMode.Aliased, RasterizerSamplingOrigin.PixelBoundary, 0.5f);

        float[] coverage = Rasterize(path, options);
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
        RasterizerOptions options = new(new Rectangle(0, 0, (int.MaxValue / 2) + 1, 1), IntersectionRule.NonZero, RasterizationMode.Antialiased, RasterizerSamplingOrigin.PixelBoundary, 0.5f);

        void Rasterize() =>
            DefaultRasterizer.RasterizeRows(
                path,
                options,
                Configuration.Default.MemoryAllocator,
                static (int y, int startX, Span<float> coverage) => { });

        ImageProcessingException exception = Assert.Throws<ImageProcessingException>(Rasterize);
        Assert.Contains("too large", exception.Message);
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
}
