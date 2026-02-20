// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

[GroupOutput("Drawing")]
public class WebGPUDrawingBackendTests
{
    [Theory]
    [WithSolidFilledImages(512, 512, "White", PixelTypes.Rgba32)]
    public void FillPath_WithWebGPUCoverageBackend_MatchesDefaultOutput(TestImageProvider<Rgba32> provider)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon polygon = new(48.25F, 63.5F, 401.25F, 302.75F);
        Brush brush = Brushes.Solid(Color.Black);

        using Image<Rgba32> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, polygon));

        using Image<Rgba32> webGpuImage = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        webGpuImage.Configuration.SetDrawingBackend(backend);
        webGpuImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, polygon));

        Assert.True(backend.PrepareCoverageCallCount > 0);
        Assert.Equal(backend.PrepareCoverageCallCount, backend.ReleaseCoverageCallCount);
        Assert.Equal(0, backend.LiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        if (backend.IsGpuReady)
        {
            Assert.True(backend.GpuPrepareCoverageCallCount > 0);
            Assert.True(backend.GpuCompositeCoverageCallCount + backend.FallbackCompositeCoverageCallCount > 0);
        }

        ImageComparer comparer = ImageComparer.TolerantPercentage(0.5F);
        comparer.VerifySimilarity(defaultImage, webGpuImage);
    }

    [Theory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithNonZeroNestedContours_MatchesDefaultOutput(TestImageProvider<Rgba32> provider)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true },
            ShapeOptions = new ShapeOptions
            {
                IntersectionRule = IntersectionRule.NonZero
            }
        };

        PathBuilder pathBuilder = new();
        pathBuilder.StartFigure();
        pathBuilder.AddLines(
        [
            new PointF(16, 16),
            new PointF(240, 16),
            new PointF(240, 240),
            new PointF(16, 240)
        ]);
        pathBuilder.CloseFigure();

        // Inner contour keeps the same winding direction as outer contour.
        // Non-zero fill should therefore keep this region filled.
        pathBuilder.StartFigure();
        pathBuilder.AddLines(
        [
            new PointF(80, 80),
            new PointF(176, 80),
            new PointF(176, 176),
            new PointF(80, 176)
        ]);
        pathBuilder.CloseFigure();

        IPath path = pathBuilder.Build();
        Brush brush = Brushes.Solid(Color.Black);

        using Image<Rgba32> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, path));

        using Image<Rgba32> webGpuImage = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        webGpuImage.Configuration.SetDrawingBackend(backend);
        webGpuImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, path));

        Assert.True(backend.PrepareCoverageCallCount > 0);
        Assert.Equal(backend.PrepareCoverageCallCount, backend.ReleaseCoverageCallCount);
        Assert.Equal(0, backend.LiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        AssertGpuPathWhenRequired(backend);

        // WebGPU and CPU rasterization differ slightly on edge coverage quantization,
        // but non-zero winding semantics must still match.
        Assert.Equal(defaultImage[128, 128], webGpuImage[128, 128]);

        ImageComparer comparer = ImageComparer.TolerantPercentage(0.5F);
        comparer.VerifySimilarity(defaultImage, webGpuImage);
    }

    [Theory]
    [WithSolidFilledImages(1200, 280, "White", PixelTypes.Rgba32)]
    public void DrawText_WithWebGPUCoverageBackend_RendersAndReleasesPreparedCoverage(TestImageProvider<Rgba32> provider)
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 54);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(18, 28)
        };

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        string text = "Sphinx of black quartz, judge my vow\n0123456789";
        Brush brush = Brushes.Solid(Color.Black);
        Pen pen = Pens.Solid(Color.OrangeRed, 2F);

        using Image<Rgba32> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen));
        defaultImage.DebugSave(
            provider,
            "DefaultBackend_DrawText",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using Image<Rgba32> webGpuImage = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        webGpuImage.Configuration.SetDrawingBackend(backend);
        webGpuImage.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen));

        webGpuImage.DebugSave(
            provider,
            "WebGPUBackend_DrawText",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        Assert.True(backend.PrepareCoverageCallCount > 0);
        Assert.True(backend.CompositeCoverageCallCount >= backend.PrepareCoverageCallCount);
        Assert.Equal(backend.PrepareCoverageCallCount, backend.ReleaseCoverageCallCount);
        Assert.Equal(0, backend.LiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        AssertGpuPathWhenRequired(backend);

        ImageComparer comparer = ImageComparer.TolerantPercentage(4F);
        comparer.VerifySimilarity(defaultImage, webGpuImage);
    }

    [Theory]
    [WithSolidFilledImages(420, 220, "White", PixelTypes.Rgba32)]
    public void DrawText_WithRepeatedGlyphs_UsesCoverageCache(TestImageProvider<Rgba32> provider)
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 48);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(8, 8),
            WrappingLength = 400
        };

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        string text = new('A', 200);
        Brush brush = Brushes.Solid(Color.Black);

        using Image<Rgba32> image = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        image.Configuration.SetDrawingBackend(backend);

        image.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen: null));

        image.DebugSave(
            provider,
            "WebGPUBackend_RepeatedGlyphs",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        Assert.InRange(backend.PrepareCoverageCallCount, 1, 20);
        Assert.True(backend.CompositeCoverageCallCount >= backend.PrepareCoverageCallCount);
        Assert.Equal(backend.PrepareCoverageCallCount, backend.ReleaseCoverageCallCount);
        Assert.Equal(0, backend.LiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        AssertGpuPathWhenRequired(backend);
    }

    private static void AssertCoverageExecutionAccounting(WebGPUDrawingBackend backend)
    {
        Assert.Equal(
            backend.PrepareCoverageCallCount,
            backend.GpuPrepareCoverageCallCount + backend.FallbackPrepareCoverageCallCount);
        Assert.Equal(
            backend.CompositeCoverageCallCount,
            backend.GpuCompositeCoverageCallCount + backend.FallbackCompositeCoverageCallCount);
    }

    private static void AssertGpuPathWhenRequired(WebGPUDrawingBackend backend)
    {
        bool requireGpuPath = string.Equals(
            Environment.GetEnvironmentVariable("IMAGESHARP_REQUIRE_WEBGPU"),
            "1",
            StringComparison.Ordinal);

        if (!requireGpuPath)
        {
            return;
        }

        Assert.True(
            backend.IsGpuReady,
            $"WebGPU initialization did not succeed. Reason='{backend.LastGpuInitializationFailure}'. Prepare(total/gpu/fallback)={backend.PrepareCoverageCallCount}/{backend.GpuPrepareCoverageCallCount}/{backend.FallbackPrepareCoverageCallCount}, Composite(total/gpu/fallback)={backend.CompositeCoverageCallCount}/{backend.GpuCompositeCoverageCallCount}/{backend.FallbackCompositeCoverageCallCount}");
        Assert.True(
            backend.GpuPrepareCoverageCallCount > 0,
            $"No GPU coverage preparation calls were observed. Prepare(total/gpu/fallback)={backend.PrepareCoverageCallCount}/{backend.GpuPrepareCoverageCallCount}/{backend.FallbackPrepareCoverageCallCount}");
        Assert.True(
            backend.GpuCompositeCoverageCallCount > 0,
            $"No GPU composite calls were observed. Composite(total/gpu/fallback)={backend.CompositeCoverageCallCount}/{backend.GpuCompositeCoverageCallCount}/{backend.FallbackCompositeCoverageCallCount}");
        Assert.Equal(
            0,
            backend.FallbackPrepareCoverageCallCount);
        Assert.Equal(
            0,
            backend.FallbackCompositeCoverageCallCount);
    }
}
