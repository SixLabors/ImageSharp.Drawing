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
    public void FillPath_WithWebGPUCoverageBackend_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon polygon = new(48.25F, 63.5F, 401.25F, 302.75F);
        Brush brush = Brushes.Solid(Color.Black);

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, polygon));
        defaultImage.DebugSave(
            provider,
            "DefaultBackend_FillPath",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using Image<TPixel> webGpuImage = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        webGpuImage.Configuration.SetDrawingBackend(backend);
        webGpuImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, polygon));
        webGpuImage.DebugSave(
            provider,
            "WebGPUBackend_FillPath",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

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
    [WithBasicTestPatternImages(384, 256, PixelTypes.Rgba32)]
    public void FillPath_WithImageBrush_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
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

        RectangularPolygon polygon = new(36.5F, 26.25F, 312.5F, 188.5F);
        Brush clearBrush = Brushes.Solid(Color.White);

        using Image<TPixel> foreground = provider.GetImage();
        Brush brush = new ImageBrush(foreground, new RectangleF(32, 24, 192, 144), new Point(13, -9));

        using Image<TPixel> defaultImage = new(384, 256);
        using (DrawingCanvas<TPixel> defaultCanvas = new(Configuration.Default, GetFrameRegion(defaultImage)))
        {
            defaultCanvas.Fill(clearBrush, clearOptions);
            defaultCanvas.FillPath(polygon, brush, drawingOptions);
            defaultCanvas.Flush();
        }

        defaultImage.DebugSave(
            provider,
            "DefaultBackend_FillPath_ImageBrush",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using Image<TPixel> webGpuImage = new(384, 256);
        using WebGPUDrawingBackend backend = new();
        Configuration webGpuConfiguration = Configuration.Default.Clone();
        webGpuConfiguration.SetDrawingBackend(backend);

        using (DrawingCanvas<TPixel> webGpuCanvas = new(webGpuConfiguration, GetFrameRegion(webGpuImage)))
        {
            webGpuCanvas.Fill(clearBrush, clearOptions);
            webGpuCanvas.FillPath(polygon, brush, drawingOptions);
            webGpuCanvas.Flush();
        }

        webGpuImage.DebugSave(
            provider,
            "WebGPUBackend_FillPath_ImageBrush",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        Assert.True(backend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(backend.TestingPrepareCoverageCallCount, backend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, backend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        if (backend.TestingIsGPUReady)
        {
            Assert.True(backend.TestingGPUCompositeCoverageCallCount > 0);
        }

        AssertGpuPathWhenRequired(backend);

        ImageComparer comparer = ImageComparer.TolerantPercentage(1F);
        comparer.VerifySimilarity(defaultImage, webGpuImage);
    }

    [Theory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithNonZeroNestedContours_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
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

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, path));
        defaultImage.DebugSave(
            provider,
            "DefaultBackend_FillPath_NonZeroNestedContours",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using Image<TPixel> webGpuImage = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        webGpuImage.Configuration.SetDrawingBackend(backend);
        webGpuImage.Mutate(ctx => ctx.Fill(drawingOptions, brush, path));
        webGpuImage.DebugSave(
            provider,
            "WebGPUBackend_FillPath_NonZeroNestedContours",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        Assert.True(backend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(backend.TestingPrepareCoverageCallCount, backend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, backend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(backend);
        AssertGpuPathWhenRequired(backend);

        // WebGPU and CPU rasterization differ slightly on edge coverage quantization,
        // but non-zero winding semantics must still match.
        Assert.Equal(defaultImage[128, 128], webGpuImage[128, 128]);

        ImageComparer referenceComparer = ImageComparer.TolerantPercentage(0.5F);
        defaultImage.CompareToReferenceOutput(
            referenceComparer,
            provider,
            "FillPath_NonZeroNestedContours_Expected",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        webGpuImage.CompareToReferenceOutput(
            referenceComparer,
            provider,
            "FillPath_NonZeroNestedContours_Expected",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        ImageComparer comparer = ImageComparer.TolerantPercentage(0.5F);
        comparer.VerifySimilarity(defaultImage, webGpuImage);
    }

    [Theory]
    [WithSolidFilledImages(1200, 280, "White", PixelTypes.Rgba32)]
    public void DrawText_WithWebGPUCoverageBackend_RendersAndReleasesPreparedCoverage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
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

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen));
        defaultImage.DebugSave(
            provider,
            "DefaultBackend_DrawText",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using Image<TPixel> webGpuImage = provider.GetImage();
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
    public void FillPath_WithWebGPUCoverageBackend_NativeSurface_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
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

        using Image<TPixel> defaultImage = provider.GetImage();
        using (DrawingCanvas<TPixel> defaultCanvas = new(Configuration.Default, GetFrameRegion(defaultImage)))
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
            WebGPUTestNativeSurfaceAllocator.TryCreate<TPixel>(
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

            using DrawingCanvas<TPixel> canvas =
                new(configuration, new NativeSurfaceOnlyFrame<TPixel>(defaultImage.Bounds, nativeSurface));
            canvas.Fill(clearBrush, clearOptions);
            canvas.FillPath(polygon, brush, drawingOptions);
            canvas.Flush();

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
                    backend,
                    textureHandle,
                    defaultImage.Width,
                    defaultImage.Height,
                    out Image<TPixel> webGpuImage,
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
    public void FillPath_WithWebGPUCoverageBackend_NativeSurfaceSubregion_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
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

        using Image<TPixel> defaultImage = provider.GetImage();
        using DrawingCanvas<TPixel> defaultCanvas = new(Configuration.Default, GetFrameRegion(defaultImage));
        defaultCanvas.Fill(clearBrush, clearOptions);

        using (DrawingCanvas<TPixel> defaultRegionCanvas = defaultCanvas.CreateRegion(region))
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
            WebGPUTestNativeSurfaceAllocator.TryCreate<TPixel>(
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

            using DrawingCanvas<TPixel> canvas =
                new(configuration, new NativeSurfaceOnlyFrame<TPixel>(defaultImage.Bounds, nativeSurface));
            canvas.Fill(clearBrush, clearOptions);
            using (DrawingCanvas<TPixel> regionCanvas = canvas.CreateRegion(region))
            {
                regionCanvas.FillPath(localPolygon, brush, drawingOptions);
            }

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
                    backend,
                    textureHandle,
                    defaultImage.Width,
                    defaultImage.Height,
                    out Image<TPixel> webGpuImage,
                    out string readError),
                readError);

            using (webGpuImage)
            {
                webGpuImage.DebugSave(
                    provider,
                    "WebGPUBackend_FillPath_NativeSurfaceSubregionParity",
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
    [WithSolidFilledImages(420, 220, "White", PixelTypes.Rgba32)]
    public void DrawText_WithRepeatedGlyphs_UsesCoverageCache<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
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

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen: null));
        defaultImage.DebugSave(
            provider,
            "DefaultBackend_RepeatedGlyphs",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        using Image<TPixel> webGpuImage = provider.GetImage();
        using WebGPUDrawingBackend backend = new();
        webGpuImage.Configuration.SetDrawingBackend(backend);

        webGpuImage.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, text, brush, pen: null));

        webGpuImage.DebugSave(
            provider,
            "WebGPUBackend_RepeatedGlyphs",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

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

    private static Buffer2DRegion<TPixel> GetFrameRegion<TPixel>(Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

    private sealed class NativeSurfaceOnlyFrame<TPixel> : ICanvasFrame<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly NativeSurface surface;

        public NativeSurfaceOnlyFrame(Rectangle bounds, NativeSurface surface)
        {
            this.Bounds = bounds;
            this.surface = surface;
        }

        public Rectangle Bounds { get; }

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
