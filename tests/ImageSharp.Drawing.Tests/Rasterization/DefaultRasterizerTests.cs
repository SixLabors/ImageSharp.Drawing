// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Rasterization;

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
            .Transform(Matrix4x4.CreateScale(200F));

        Rectangle interest = Rectangle.Ceiling(path.Bounds);
        RasterizerOptions options = new(interest, rule, RasterizationMode.Antialiased, RasterizerSamplingOrigin.PixelBoundary, 0.5f);

        float[] expected = RasterizePreparedBands(path, options);
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
            RasterizationMode.Antialiased,
            RasterizerSamplingOrigin.PixelCenter,
            0.5f);

        float[] expected = RasterizePreparedBands(path, options);
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

    private static float[] RasterizePreparedBands(IPath path, in RasterizerOptions options)
    {
        int width = options.Interest.Width;
        int height = options.Interest.Height;
        float[] coverage = new float[width * height];
        int top = options.Interest.Top;

        PreparedGeometry geometry = PreparedGeometry.Create(path);
        if (geometry.SegmentCount == 0 || width <= 0 || height <= 0)
        {
            return coverage;
        }

        int[] segmentIndices = new int[geometry.SegmentCount];
        for (int i = 0; i < segmentIndices.Length; i++)
        {
            segmentIndices[i] = i;
        }

        int bandHeight = DefaultRasterizer.PreferredRowHeight;
        int firstBandIndex = FloorDiv(options.Interest.Top, bandHeight);
        int lastBandIndex = FloorDiv(options.Interest.Bottom - 1, bandHeight);
        int bandCount = (lastBandIndex - firstBandIndex) + 1;

        using IMemoryOwner<DefaultRasterizer.RasterLineData> lineOwner =
            Configuration.Default.MemoryAllocator.Allocate<DefaultRasterizer.RasterLineData>(geometry.SegmentCount);
        using IMemoryOwner<int> startCoverOwner =
            Configuration.Default.MemoryAllocator.Allocate<int>(bandHeight);

        DefaultRasterizer.WorkerScratch? scratch = null;
        try
        {
            for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
            {
                if (!DefaultRasterizer.TryBuildRasterizableBand(
                    geometry,
                    segmentIndices,
                    0,
                    0,
                    in options,
                    bandIndex,
                    lineOwner.Memory.Span,
                    startCoverOwner.Memory.Span,
                    out DefaultRasterizer.RasterizableBandInfo rasterizableBandInfo))
                {
                    continue;
                }

                if (scratch is null ||
                    !scratch.CanReuse(
                        rasterizableBandInfo.WordsPerRow,
                        rasterizableBandInfo.CoverStride,
                        rasterizableBandInfo.Width,
                        rasterizableBandInfo.BandHeight))
                {
                    scratch?.Dispose();
                    scratch = DefaultRasterizer.WorkerScratch.Create(
                        Configuration.Default.MemoryAllocator,
                        rasterizableBandInfo.WordsPerRow,
                        rasterizableBandInfo.CoverStride,
                        rasterizableBandInfo.Width,
                        rasterizableBandInfo.BandHeight);
                }

                DefaultRasterizer.RasterizableBand rasterizableBand = rasterizableBandInfo.CreateRasterizableBand(
                    lineOwner.Memory.Span,
                    startCoverOwner.Memory.Span);
                DefaultRasterizer.Context context = scratch.CreateContext(
                    rasterizableBand.Width,
                    rasterizableBand.WordsPerRow,
                    rasterizableBand.CoverStride,
                    rasterizableBand.BandHeight,
                    rasterizableBand.IntersectionRule,
                    rasterizableBand.RasterizationMode,
                    rasterizableBand.AntialiasThreshold);
                DefaultRasterizer.ExecuteRasterizableBand(ref context, in rasterizableBand, scratch.Scanline, CaptureRow);
            }
        }
        finally
        {
            scratch?.Dispose();
        }

        return coverage;

        void CaptureRow(int y, int startX, Span<float> rowCoverage)
        {
            int row = y - top;
            rowCoverage.CopyTo(coverage.AsSpan((row * width) + startX, rowCoverage.Length));
        }
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
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
