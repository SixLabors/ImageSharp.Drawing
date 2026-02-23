// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Memory;
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

        Assert.True(backend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(backend.TestingPrepareCoverageCallCount, backend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, backend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        if (backend.TestingIsGPUReady)
        {
            Assert.True(backend.TestingGPUPrepareCoverageCallCount > 0);
            Assert.True(backend.TestingGPUCompositeCoverageCallCount + backend.TestingFallbackCompositeCoverageCallCount > 0);
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

        Assert.True(backend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(backend.TestingPrepareCoverageCallCount, backend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, backend.TestingLiveCoverageCount);
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

        Assert.True(backend.TestingPrepareCoverageCallCount > 0);
        Assert.True(backend.TestingCompositeCoverageCallCount >= backend.TestingPrepareCoverageCallCount);
        Assert.Equal(backend.TestingPrepareCoverageCallCount, backend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, backend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        AssertGpuPathWhenRequired(backend);

        ImageComparer comparer = ImageComparer.TolerantPercentage(4F);
        comparer.VerifySimilarity(defaultImage, webGpuImage);
    }

    [Theory]
    [WithSolidFilledImages(512, 512, "White", PixelTypes.Rgba32)]
    public void FillPath_WithWebGPUCoverageBackend_NativeSurface_MatchesDefaultOutput(TestImageProvider<Rgba32> provider)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };
        GraphicsOptions clearOptions = new()
        {
            Antialias = false,
            AlphaCompositionMode = PixelAlphaCompositionMode.Src,
            ColorBlendingMode = PixelColorBlendingMode.Normal,
            BlendPercentage = 1F
        };

        RectangularPolygon polygon = new(48.25F, 63.5F, 401.25F, 302.75F);
        Brush brush = Brushes.Solid(Color.Black);
        Brush clearBrush = Brushes.Solid(Color.White);

        using Image<Rgba32> defaultImage = provider.GetImage();
        using (DrawingCanvas<Rgba32> defaultCanvas = new(Configuration.Default, GetFrameRegion(defaultImage)))
        {
            defaultCanvas.Fill(clearBrush, clearOptions);
            defaultCanvas.FillPath(polygon, brush, drawingOptions);
            defaultCanvas.Flush();
        }

        defaultImage.DebugSave(
            provider,
            "DefaultBackend_FillPath_NativeSurfaceParity",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using WebGPUDrawingBackend backend = new();
        Assert.True(
            WebGPUTestNativeSurfaceAllocator.TryCreate<Rgba32>(
                backend,
                defaultImage.Width,
                defaultImage.Height,
                isSrgb: false,
                isPremultipliedAlpha: false,
                out NativeSurface nativeSurface,
                out nint textureHandle,
                out nint textureViewHandle,
                out string createError),
            createError);

        try
        {
            Configuration configuration = Configuration.Default.Clone();
            configuration.SetDrawingBackend(backend);

            using DrawingCanvas<Rgba32> canvas =
                new(configuration, new NativeSurfaceOnlyFrame<Rgba32>(defaultImage.Bounds, nativeSurface));
            canvas.Fill(clearBrush, clearOptions);
            canvas.FillPath(polygon, brush, drawingOptions);
            canvas.Flush();

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture<Rgba32>(
                    backend,
                    textureHandle,
                    defaultImage.Width,
                    defaultImage.Height,
                    out Image<Rgba32> webGpuImage,
                    out string readError),
                readError);

            using (webGpuImage)
            {
                webGpuImage.DebugSave(
                    provider,
                    "WebGPUBackend_FillPath_NativeSurfaceParity",
                    appendPixelTypeToFileName: false,
                    appendSourceFileOrDescription: false);

                ImageComparer comparer = ImageComparer.TolerantPercentage(0.5F);
                comparer.VerifySimilarity(defaultImage, webGpuImage);
            }
        }
        finally
        {
            WebGPUTestNativeSurfaceAllocator.Release(textureHandle, textureViewHandle);
        }
    }

    [Theory]
    [WithSolidFilledImages(512, 512, "White", PixelTypes.Rgba32)]
    public void FillPath_WithWebGPUCoverageBackend_NativeSurfaceSubregion_MatchesDefaultOutput(TestImageProvider<Rgba32> provider)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };
        GraphicsOptions clearOptions = new()
        {
            Antialias = false,
            AlphaCompositionMode = PixelAlphaCompositionMode.Src,
            ColorBlendingMode = PixelColorBlendingMode.Normal,
            BlendPercentage = 1F
        };

        Rectangle region = new(72, 64, 320, 240);
        RectangularPolygon localPolygon = new(16.25F, 24.5F, 250.5F, 160.75F);
        Brush brush = Brushes.Solid(Color.Black);
        Brush clearBrush = Brushes.Solid(Color.White);

        using Image<Rgba32> defaultImage = provider.GetImage();
        using DrawingCanvas<Rgba32> defaultCanvas = new(Configuration.Default, GetFrameRegion(defaultImage));
        defaultCanvas.Fill(clearBrush, clearOptions);

        using (DrawingCanvas<Rgba32> defaultRegionCanvas = defaultCanvas.CreateRegion(region))
        {
            defaultRegionCanvas.FillPath(localPolygon, brush, drawingOptions);
        }

        defaultImage.DebugSave(
            provider,
            "DefaultBackend_FillPath_NativeSurfaceSubregionParity",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using WebGPUDrawingBackend backend = new();
        Assert.True(
            WebGPUTestNativeSurfaceAllocator.TryCreate<Rgba32>(
                backend,
                defaultImage.Width,
                defaultImage.Height,
                isSrgb: false,
                isPremultipliedAlpha: false,
                out NativeSurface nativeSurface,
                out nint textureHandle,
                out nint textureViewHandle,
                out string createError),
            createError);

        try
        {
            Configuration configuration = Configuration.Default.Clone();
            configuration.SetDrawingBackend(backend);

            using DrawingCanvas<Rgba32> canvas =
                new(configuration, new NativeSurfaceOnlyFrame<Rgba32>(defaultImage.Bounds, nativeSurface));
            canvas.Fill(clearBrush, clearOptions);
            using (DrawingCanvas<Rgba32> regionCanvas = canvas.CreateRegion(region))
            {
                regionCanvas.FillPath(localPolygon, brush, drawingOptions);
            }

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
                    backend,
                    textureHandle,
                    defaultImage.Width,
                    defaultImage.Height,
                    out Image<Rgba32> webGpuImage,
                    out string readError),
                readError);

            using (webGpuImage)
            {
                webGpuImage.DebugSave(
                    provider,
                    "WebGPUBackend_FillPath_NativeSurfaceSubregionParity",
                    appendPixelTypeToFileName: false,
                    appendSourceFileOrDescription: false);

                int defaultCoveragePixels = CountNonBackgroundPixels(defaultImage, Color.White);
                int webGpuCoveragePixels = CountNonBackgroundPixels(webGpuImage, Color.White);
                Assert.True(defaultCoveragePixels > 0, "Default backend produced no subregion fill coverage.");
                Assert.True(webGpuCoveragePixels > 0, "WebGPU backend produced no subregion fill coverage.");

                ImageComparer comparer = ImageComparer.TolerantPercentage(0.5F);
                comparer.VerifySimilarity(defaultImage, webGpuImage);
            }
        }
        finally
        {
            WebGPUTestNativeSurfaceAllocator.Release(textureHandle, textureViewHandle);
        }
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

        using Image<Rgba32> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen: null));
        defaultImage.DebugSave(
            provider,
            "DefaultBackend_RepeatedGlyphs",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using Image<Rgba32> webGpuImage = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        webGpuImage.Configuration.SetDrawingBackend(backend);

        webGpuImage.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen: null));

        webGpuImage.DebugSave(
            provider,
            "WebGPUBackend_RepeatedGlyphs",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        int defaultCoveragePixels = CountNonBackgroundPixels(defaultImage, Color.White);
        int webGpuCoveragePixels = CountNonBackgroundPixels(webGpuImage, Color.White);
        Assert.True(defaultCoveragePixels > 0, "Default backend produced no text coverage.");
        Assert.True(
            webGpuCoveragePixels >= (defaultCoveragePixels * 9) / 10,
            $"WebGPU text coverage is too low. default={defaultCoveragePixels}, webgpu={webGpuCoveragePixels}");

        ImageComparer comparer = ImageComparer.TolerantPercentage(2F);
        comparer.VerifySimilarity(defaultImage, webGpuImage);

        Assert.InRange(backend.TestingPrepareCoverageCallCount, 1, 20);
        Assert.True(backend.TestingCompositeCoverageCallCount >= backend.TestingPrepareCoverageCallCount);
        Assert.Equal(backend.TestingPrepareCoverageCallCount, backend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, backend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        AssertGpuPathWhenRequired(backend);
    }

    private static void AssertCoverageExecutionAccounting(WebGPUDrawingBackend backend)
    {
        Assert.Equal(
            backend.TestingPrepareCoverageCallCount,
            backend.TestingGPUPrepareCoverageCallCount + backend.TestingFallbackPrepareCoverageCallCount);
        Assert.Equal(
            backend.TestingCompositeCoverageCallCount,
            backend.TestingGPUCompositeCoverageCallCount + backend.TestingFallbackCompositeCoverageCallCount);
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
            backend.TestingIsGPUReady,
            $"WebGPU initialization did not succeed. Reason='{backend.TestingLastGPUInitializationFailure}'. Prepare(total/gpu/fallback)={backend.TestingPrepareCoverageCallCount}/{backend.TestingGPUPrepareCoverageCallCount}/{backend.TestingFallbackPrepareCoverageCallCount}, Composite(total/gpu/fallback)={backend.TestingCompositeCoverageCallCount}/{backend.TestingGPUCompositeCoverageCallCount}/{backend.TestingFallbackCompositeCoverageCallCount}");
        Assert.True(
            backend.TestingGPUPrepareCoverageCallCount > 0,
            $"No GPU coverage preparation calls were observed. Prepare(total/gpu/fallback)={backend.TestingPrepareCoverageCallCount}/{backend.TestingGPUPrepareCoverageCallCount}/{backend.TestingFallbackPrepareCoverageCallCount}");
        Assert.True(
            backend.TestingGPUCompositeCoverageCallCount > 0,
            $"No GPU composite calls were observed. Composite(total/gpu/fallback)={backend.TestingCompositeCoverageCallCount}/{backend.TestingGPUCompositeCoverageCallCount}/{backend.TestingFallbackCompositeCoverageCallCount}");
        Assert.Equal(
            0,
            backend.TestingFallbackPrepareCoverageCallCount);
        Assert.Equal(
            0,
            backend.TestingFallbackCompositeCoverageCallCount);
    }

    private static int CountNonBackgroundPixels(Image<Rgba32> image, Color background)
    {
        Rgba32 bg = background.ToPixel<Rgba32>();
        Buffer2D<Rgba32> buffer = image.Frames.RootFrame.PixelBuffer;
        int count = 0;
        for (int y = 0; y < buffer.Height; y++)
        {
            Span<Rgba32> row = buffer.DangerousGetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                Rgba32 pixel = row[x];
                if (Math.Abs(pixel.R - bg.R) > 2 ||
                    Math.Abs(pixel.G - bg.G) > 2 ||
                    Math.Abs(pixel.B - bg.B) > 2 ||
                    Math.Abs(pixel.A - bg.A) > 2)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Buffer2DRegion<Rgba32> GetFrameRegion(Image<Rgba32> image)
        => new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

    private sealed class NativeSurfaceOnlyFrame<TPixel> : ICanvasFrame<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Rectangle bounds;
        private readonly NativeSurface surface;

        public NativeSurfaceOnlyFrame(Rectangle bounds, NativeSurface surface)
        {
            this.bounds = bounds;
            this.surface = surface;
        }

        public Rectangle Bounds => this.bounds;

        public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
        {
            region = default;
            return false;
        }

        public bool TryGetNativeSurface(out NativeSurface surface)
        {
            surface = this.surface;
            return true;
        }
    }
}
