// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

[GroupOutput("Drawing")]
public class WebGPUDrawingBackendTests
{
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
            backend.GpuCompositeCoverageCallCount + backend.CpuCompositeCoverageCallCount);
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
            $"WebGPU initialization did not succeed. Reason='{backend.LastGpuInitializationFailure}'. Prepare(total/gpu/fallback)={backend.PrepareCoverageCallCount}/{backend.GpuPrepareCoverageCallCount}/{backend.FallbackPrepareCoverageCallCount}, Composite(total/gpu/cpu)={backend.CompositeCoverageCallCount}/{backend.GpuCompositeCoverageCallCount}/{backend.CpuCompositeCoverageCallCount}");
        Assert.True(
            backend.GpuPrepareCoverageCallCount > 0,
            $"No GPU coverage preparation calls were observed. Prepare(total/gpu/fallback)={backend.PrepareCoverageCallCount}/{backend.GpuPrepareCoverageCallCount}/{backend.FallbackPrepareCoverageCallCount}");
        Assert.True(
            backend.GpuCompositeCoverageCallCount > 0,
            $"No GPU composite calls were observed. Composite(total/gpu/cpu)={backend.CompositeCoverageCallCount}/{backend.GpuCompositeCoverageCallCount}/{backend.CpuCompositeCoverageCallCount}");
        Assert.Equal(
            0,
            backend.FallbackPrepareCoverageCallCount);
        Assert.Equal(
            0,
            backend.CpuCompositeCoverageCallCount);
    }
}
