// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#if !OS_Linux
// WebGPU is failing in our CI environment in Ubuntu with
// WebGPU adapter request failed with status 'Unavailable'
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

[GroupOutput("Drawing")]
public class WebGPUDrawingBackendTests
{
    public static TheoryData<PixelColorBlendingMode, PixelAlphaCompositionMode> GraphicsOptionsModePairs { get; } =
    new()
    {
        { PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Multiply, PixelAlphaCompositionMode.SrcAtop },
        { PixelColorBlendingMode.Add, PixelAlphaCompositionMode.Src },
        { PixelColorBlendingMode.Subtract, PixelAlphaCompositionMode.DestOut },
        { PixelColorBlendingMode.Screen, PixelAlphaCompositionMode.DestOver },
        { PixelColorBlendingMode.Darken, PixelAlphaCompositionMode.DestAtop },
        { PixelColorBlendingMode.Lighten, PixelAlphaCompositionMode.DestIn },
        { PixelColorBlendingMode.Overlay, PixelAlphaCompositionMode.SrcIn },
        { PixelColorBlendingMode.HardLight, PixelAlphaCompositionMode.Xor },
        { PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.Clear }
    };

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

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(polygon, brush);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendTriplet(provider, "FillPath", defaultImage, cpuRegionImage, nativeSurfaceImage);

        Assert.True(cpuRegionBackend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(cpuRegionBackend.TestingPrepareCoverageCallCount, cpuRegionBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, cpuRegionBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        if (cpuRegionBackend.TestingIsGPUReady)
        {
            Assert.True(cpuRegionBackend.TestingGPUPrepareCoverageCallCount > 0);
            Assert.True(cpuRegionBackend.TestingGPUCompositeCoverageCallCount + cpuRegionBackend.TestingFallbackCompositeCoverageCallCount > 0);
        }

        Assert.True(nativeSurfaceBackend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(nativeSurfaceBackend.TestingPrepareCoverageCallCount, nativeSurfaceBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, nativeSurfaceBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);

        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 1F);
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

        RectangularPolygon polygon = new(36.5F, 26.25F, 312.5F, 188.5F);
        Brush clearBrush = Brushes.Solid(Color.White);

        using Image<TPixel> foreground = provider.GetImage();
        Brush brush = new ImageBrush(foreground, new RectangleF(32, 24, 192, 144), new Point(13, -9));
        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Clear(clearBrush);
            canvas.Fill(polygon, brush);
        }

        using Image<TPixel> defaultImage = new(384, 256);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = new(384, 256);
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            (Action<DrawingCanvas<TPixel>>)DrawAction);

        DebugSaveBackendTriplet(provider, "FillPath_ImageBrush", defaultImage, cpuRegionImage, nativeSurfaceImage);

        Assert.True(cpuRegionBackend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(cpuRegionBackend.TestingPrepareCoverageCallCount, cpuRegionBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, cpuRegionBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        if (cpuRegionBackend.TestingIsGPUReady)
        {
            Assert.True(cpuRegionBackend.TestingGPUCompositeCoverageCallCount > 0);
        }

        Assert.True(nativeSurfaceBackend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(nativeSurfaceBackend.TestingPrepareCoverageCallCount, nativeSurfaceBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, nativeSurfaceBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        if (nativeSurfaceBackend.TestingIsGPUReady)
        {
            Assert.True(nativeSurfaceBackend.TestingGPUCompositeCoverageCallCount > 0);
        }

        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 1F);
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
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(path, brush);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendTriplet(provider, "FillPath_NonZeroNestedContours", defaultImage, cpuRegionImage, nativeSurfaceImage);

        Assert.True(cpuRegionBackend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(cpuRegionBackend.TestingPrepareCoverageCallCount, cpuRegionBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, cpuRegionBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(cpuRegionBackend);

        Assert.True(nativeSurfaceBackend.TestingPrepareCoverageCallCount > 0);
        Assert.Equal(nativeSurfaceBackend.TestingPrepareCoverageCallCount, nativeSurfaceBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, nativeSurfaceBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);

        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);

        // Non-zero winding semantics must still match on an interior point.
        Assert.Equal(defaultImage[128, 128], cpuRegionImage[128, 128]);
        Assert.Equal(defaultImage[128, 128], nativeSurfaceImage[128, 128]);

        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.5F);
    }

    [Theory]
    [WithBasicTestPatternImages(nameof(GraphicsOptionsModePairs), 384, 256, PixelTypes.Rgba32)]
    public void FillPath_WithGraphicsOptionsModes_SolidBrush_MatchesDefaultOutput<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectangularPolygon polygon = new(26.5F, 18.25F, 324.5F, 208.75F);
        Brush brush = Brushes.Solid(Color.OrangeRed.WithAlpha(0.78F));

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = true,
                BlendPercentage = 0.73F,
                ColorBlendingMode = colorMode,
                AlphaCompositionMode = alphaMode
            }
        };

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(polygon, brush);

        using Image<TPixel> baseImage = provider.GetImage();
        using Image<TPixel> defaultImage = baseImage.Clone();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = baseImage.Clone();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            baseImage);

        DebugSaveBackendTriplet(
            provider,
            $"FillPath_GraphicsOptions_SolidBrush_{colorMode}_{alphaMode}",
            defaultImage,
            cpuRegionImage,
            nativeSurfaceImage);

        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.1F);
    }

    [Theory]
    [WithBasicTestPatternImages(nameof(GraphicsOptionsModePairs), 384, 256, PixelTypes.Rgba32)]
    public void FillPath_WithGraphicsOptionsModes_ImageBrush_MatchesDefaultOutput<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectangularPolygon polygon = new(26.5F, 18.25F, 324.5F, 208.75F);

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = true,
                BlendPercentage = 0.73F,
                ColorBlendingMode = colorMode,
                AlphaCompositionMode = alphaMode
            }
        };

        using Image<TPixel> foreground = provider.GetImage();
        Brush brush = new ImageBrush(foreground, new RectangleF(32, 24, 192, 144), new Point(13, -9));
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(polygon, brush);

        using Image<TPixel> baseImage = provider.GetImage();
        using Image<TPixel> defaultImage = baseImage.Clone();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = baseImage.Clone();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            baseImage);

        DebugSaveBackendTriplet(
            provider,
            $"FillPath_GraphicsOptions_ImageBrush_{colorMode}_{alphaMode}",
            defaultImage,
            cpuRegionImage,
            nativeSurfaceImage);

        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.1F);
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
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.DrawText(textOptions, text, brush, pen);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendTriplet(provider, "DrawText", defaultImage, cpuRegionImage, nativeSurfaceImage);

        Assert.True(cpuRegionBackend.TestingPrepareCoverageCallCount > 0);
        Assert.True(cpuRegionBackend.TestingCompositeCoverageCallCount >= cpuRegionBackend.TestingPrepareCoverageCallCount);
        Assert.Equal(cpuRegionBackend.TestingPrepareCoverageCallCount, cpuRegionBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, cpuRegionBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(cpuRegionBackend);

        Assert.True(nativeSurfaceBackend.TestingPrepareCoverageCallCount > 0);
        Assert.True(nativeSurfaceBackend.TestingCompositeCoverageCallCount >= nativeSurfaceBackend.TestingPrepareCoverageCallCount);
        Assert.Equal(nativeSurfaceBackend.TestingPrepareCoverageCallCount, nativeSurfaceBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, nativeSurfaceBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);

        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.2F);
        Rectangle textRegion = Rectangle.Intersect(
            new Rectangle(0, 0, defaultImage.Width, defaultImage.Height),
            new Rectangle(8, 12, defaultImage.Width - 16, Math.Min(220, defaultImage.Height - 12)));
        AssertBackendTripletSimilarityInRegion(defaultImage, cpuRegionImage, nativeSurfaceImage, textRegion, 0.03F);
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

        RectangularPolygon polygon = new(48.25F, 63.5F, 401.25F, 302.75F);
        Brush brush = Brushes.Solid(Color.Black);
        Brush clearBrush = Brushes.Solid(Color.White);
        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Clear(clearBrush);
            canvas.Fill(polygon, brush);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendTriplet(provider, "FillPath_NativeSurfaceParity", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.5F);
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
        Rectangle region = new(72, 64, 320, 240);
        RectangularPolygon localPolygon = new(16.25F, 24.5F, 250.5F, 160.75F);
        Brush brush = Brushes.Solid(Color.Black);
        Brush clearBrush = Brushes.Solid(Color.White);
        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Clear(clearBrush);

            using DrawingCanvas<TPixel> regionCanvas = canvas.CreateRegion(region);
            regionCanvas.Fill(localPolygon, brush);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendTriplet(provider, "FillPath_NativeSurfaceSubregionParity", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.5F);
    }

    [Theory]
    [WithBlankImage(220, 160, PixelTypes.Rgba32)]
    public void Process_WithWebGPUBackend_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();
        IPath blurPath = CreateBlurEllipsePath();
        IPath pixelatePath = CreatePixelateTrianglePath();
        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            DrawProcessScenario(canvas);
            canvas.Process(blurPath, ctx => ctx.GaussianBlur(6F));
            canvas.Process(pixelatePath, ctx => ctx.Pixelate(10));
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            (Action<DrawingCanvas<TPixel>>)DrawAction);

        DebugSaveBackendTriplet(provider, "Process", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.5F);
    }

    [Theory]
    [WithBasicTestPatternImages(420, 220, PixelTypes.Rgba32)]
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
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.DrawText(textOptions, text, brush, null);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        RenderWithCpuRegionWebGpuBackend(cpuRegionImage, cpuRegionBackend, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendTriplet(provider, "RepeatedGlyphs", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 2F);

        Assert.InRange(cpuRegionBackend.TestingPrepareCoverageCallCount, 1, 20);
        Assert.True(cpuRegionBackend.TestingCompositeCoverageCallCount >= cpuRegionBackend.TestingPrepareCoverageCallCount);
        Assert.Equal(cpuRegionBackend.TestingPrepareCoverageCallCount, cpuRegionBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, cpuRegionBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(cpuRegionBackend);

        Assert.InRange(nativeSurfaceBackend.TestingPrepareCoverageCallCount, 1, 20);
        Assert.True(nativeSurfaceBackend.TestingCompositeCoverageCallCount >= nativeSurfaceBackend.TestingPrepareCoverageCallCount);
        Assert.Equal(nativeSurfaceBackend.TestingPrepareCoverageCallCount, nativeSurfaceBackend.TestingReleaseCoverageCallCount);
        Assert.Equal(0, nativeSurfaceBackend.TestingLiveCoverageCount);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);

        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
    }

    [Theory]
    [WithBlankImage(1200, 280, PixelTypes.Rgba32)]
    public void DrawText_WithRepeatedGlyphs_AfterClear_UsesBlendFastPath<TPixel>(TestImageProvider<TPixel> provider)
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

        DrawingOptions clearOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src,
                ColorBlendingMode = PixelColorBlendingMode.Normal,
                BlendPercentage = 1F
            }
        };

        const int glyphCount = 200;
        string text = new('A', glyphCount);
        Brush drawBrush = Brushes.Solid(Color.HotPink);
        Brush clearBrush = Brushes.Solid(Color.White);
        using Image<TPixel> defaultImage = provider.GetImage();
        using (DrawingCanvas<TPixel> defaultClearCanvas = new(Configuration.Default, GetFrameRegion(defaultImage), clearOptions))
        {
            defaultClearCanvas.Fill(clearBrush);
            defaultClearCanvas.Flush();
        }

        using (DrawingCanvas<TPixel> defaultDrawCanvas = new(Configuration.Default, GetFrameRegion(defaultImage), drawingOptions))
        {
            defaultDrawCanvas.DrawText(textOptions, text, drawBrush, null);
            defaultDrawCanvas.Flush();
        }

        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        Configuration cpuRegionConfiguration = Configuration.Default.Clone();
        cpuRegionConfiguration.SetDrawingBackend(cpuRegionBackend);

        using (DrawingCanvas<TPixel> cpuRegionClearCanvas = new(cpuRegionConfiguration, GetFrameRegion(cpuRegionImage), clearOptions))
        {
            cpuRegionClearCanvas.Fill(clearBrush);
            cpuRegionClearCanvas.Flush();
        }

        int cpuRegionComputeBatchesBeforeDraw = cpuRegionBackend.TestingComputePathBatchCount;
        using (DrawingCanvas<TPixel> cpuRegionDrawCanvas = new(cpuRegionConfiguration, GetFrameRegion(cpuRegionImage), drawingOptions))
        {
            cpuRegionDrawCanvas.DrawText(textOptions, text, drawBrush, null);
            cpuRegionDrawCanvas.Flush();
        }

        int cpuRegionComputeBatchesFromDraw = cpuRegionBackend.TestingComputePathBatchCount - cpuRegionComputeBatchesBeforeDraw;

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        Assert.True(
            WebGPUTestNativeSurfaceAllocator.TryCreate<TPixel>(
                nativeSurfaceBackend,
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
            Configuration nativeSurfaceConfiguration = Configuration.Default.Clone();
            nativeSurfaceConfiguration.SetDrawingBackend(nativeSurfaceBackend);
            Rectangle targetBounds = defaultImage.Bounds;

            using (DrawingCanvas<TPixel> nativeSurfaceClearCanvas =
                   new(nativeSurfaceConfiguration, new NativeSurfaceOnlyFrame<TPixel>(targetBounds, nativeSurface), clearOptions))
            {
                nativeSurfaceClearCanvas.Fill(clearBrush);
                nativeSurfaceClearCanvas.Flush();
            }

            int nativeSurfaceComputeBatchesBeforeDraw = nativeSurfaceBackend.TestingComputePathBatchCount;
            using (DrawingCanvas<TPixel> nativeSurfaceDrawCanvas =
                   new(nativeSurfaceConfiguration, new NativeSurfaceOnlyFrame<TPixel>(targetBounds, nativeSurface), drawingOptions))
            {
                nativeSurfaceDrawCanvas.DrawText(textOptions, text, drawBrush, null);
                nativeSurfaceDrawCanvas.Flush();
            }

            int nativeSurfaceComputeBatchesFromDraw =
                nativeSurfaceBackend.TestingComputePathBatchCount - nativeSurfaceComputeBatchesBeforeDraw;

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
                    nativeSurfaceBackend,
                    textureHandle,
                    defaultImage.Width,
                    defaultImage.Height,
                    out Image<TPixel> nativeSurfaceImage,
                    out string readError),
                readError);

            using (nativeSurfaceImage)
            {
                DebugSaveBackendTriplet(provider, "RepeatedGlyphs_AfterClear", defaultImage, cpuRegionImage, nativeSurfaceImage);
                AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 2F);
            }

            AssertGpuPathWhenRequired(cpuRegionBackend);
            AssertGpuPathWhenRequired(nativeSurfaceBackend);

            if (cpuRegionBackend.TestingIsGPUReady)
            {
                Assert.True(
                    cpuRegionComputeBatchesFromDraw > 0,
                    "Expected repeated-glyph draw batch to execute via tiled compute composition on the CPURegion pipeline.");
            }

            if (nativeSurfaceBackend.TestingIsGPUReady)
            {
                Assert.True(
                    nativeSurfaceComputeBatchesFromDraw > 0,
                    "Expected repeated-glyph draw batch to execute via tiled compute composition on the NativeSurface pipeline.");
            }
        }
        finally
        {
            WebGPUTestNativeSurfaceAllocator.Release(textureHandle, textureViewHandle);
        }
    }

    private static void RenderWithDefaultBackend<TPixel>(Image<TPixel> image, DrawingOptions options, Action<DrawingCanvas<TPixel>> drawAction)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using DrawingCanvas<TPixel> canvas = new(Configuration.Default, GetFrameRegion(image), options);
        drawAction(canvas);
        canvas.Flush();
    }

    private static EllipsePolygon CreateBlurEllipsePath()
        => new(new PointF(55, 40), new SizeF(110, 80));

    private static void DrawProcessScenario<TPixel>(DrawingCanvas<TPixel> canvas)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        canvas.Clear(Brushes.Solid(Color.White));

        canvas.Draw(Pens.Solid(Color.DimGray, 3), new Rectangle(10, 10, 220, 140));
        canvas.DrawEllipse(Pens.Solid(Color.CornflowerBlue, 6), new PointF(120, 80), new SizeF(110, 70));
        canvas.DrawArc(
            Pens.Solid(Color.ForestGreen, 4),
            new PointF(120, 80),
            new SizeF(90, 46),
            rotation: 15,
            startAngle: -25,
            sweepAngle: 220);
        canvas.DrawLine(
            Pens.Solid(Color.OrangeRed, 5),
            new PointF(18, 140),
            new PointF(76, 28),
            new PointF(166, 126),
            new PointF(222, 20));
        canvas.DrawBezier(
            Pens.Solid(Color.MediumVioletRed, 4),
            new PointF(20, 80),
            new PointF(70, 18),
            new PointF(168, 144),
            new PointF(220, 78));
    }

    private static IPath CreatePixelateTrianglePath()
    {
        PathBuilder pathBuilder = new();
        pathBuilder.AddLine(110, 80, 220, 80);
        pathBuilder.AddLine(220, 80, 165, 160);
        pathBuilder.AddLine(165, 160, 110, 80);
        pathBuilder.CloseAllFigures();
        return pathBuilder.Build();
    }

    private static void RenderWithCpuRegionWebGpuBackend<TPixel>(
        Image<TPixel> image,
        WebGPUDrawingBackend backend,
        DrawingOptions options,
        Action<DrawingCanvas<TPixel>> drawAction)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);
        using DrawingCanvas<TPixel> canvas = new(configuration, GetFrameRegion(image), options);
        drawAction(canvas);
        canvas.Flush();
    }

    private static Image<TPixel> RenderWithNativeSurfaceWebGpuBackend<TPixel>(
        int width,
        int height,
        WebGPUDrawingBackend backend,
        DrawingOptions options,
        Action<DrawingCanvas<TPixel>> drawAction,
        Image<TPixel> initialImage = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.True(
            WebGPUTestNativeSurfaceAllocator.TryCreate<TPixel>(
                backend,
                width,
                height,
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
            Rectangle targetBounds = new(0, 0, width, height);

            using DrawingCanvas<TPixel> canvas =
                new(configuration, new NativeSurfaceOnlyFrame<TPixel>(targetBounds, nativeSurface), options);
            if (initialImage is not null)
            {
                Assert.True(
                    WebGPUTestNativeSurfaceAllocator.TryWriteTexture(
                        backend,
                        textureHandle,
                        width,
                        height,
                        initialImage,
                        out string uploadError),
                    uploadError);
            }

            drawAction(canvas);
            canvas.Flush();

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
                    backend,
                    textureHandle,
                    width,
                    height,
                    out Image<TPixel> image,
                    out string readError),
                readError);

            return image;
        }
        finally
        {
            WebGPUTestNativeSurfaceAllocator.Release(textureHandle, textureViewHandle);
        }
    }

    private static void DebugSaveBackendTriplet<TPixel>(
        TestImageProvider<TPixel> provider,
        string testName,
        Image<TPixel> defaultImage,
        Image<TPixel> cpuRegionImage,
        Image<TPixel> nativeSurfaceImage)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        defaultImage.DebugSave(
            provider,
            $"{testName}_Default",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        cpuRegionImage.DebugSave(
            provider,
            $"{testName}_WebGPU_CPURegion",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        nativeSurfaceImage.DebugSave(
            provider,
            $"{testName}_WebGPU_NativeSurface",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(0.0003F);
        defaultImage.CompareToReferenceOutput(
            tolerantComparer,
            provider,
            $"{testName}_Default",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        cpuRegionImage.CompareToReferenceOutput(
            tolerantComparer,
            provider,
            $"{testName}_WebGPU_CPURegion",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        nativeSurfaceImage.CompareToReferenceOutput(
            tolerantComparer,
            provider,
            $"{testName}_WebGPU_NativeSurface",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    private static void AssertBackendTripletSimilarity<TPixel>(
        Image<TPixel> defaultImage,
        Image<TPixel> cpuRegionImage,
        Image<TPixel> nativeSurfaceImage,
        float defaultTolerancePercent)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ImageComparer.TolerantPercentage(0.01F).VerifySimilarity(cpuRegionImage, nativeSurfaceImage);
        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(defaultTolerancePercent);
        tolerantComparer.VerifySimilarity(defaultImage, cpuRegionImage);
        tolerantComparer.VerifySimilarity(defaultImage, nativeSurfaceImage);
    }

    private static void AssertBackendTripletSimilarityInRegion<TPixel>(
        Image<TPixel> defaultImage,
        Image<TPixel> cpuRegionImage,
        Image<TPixel> nativeSurfaceImage,
        Rectangle region,
        float defaultTolerancePercent)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> defaultRegion = defaultImage.Clone(ctx => ctx.Crop(region));
        using Image<TPixel> cpuRegion = cpuRegionImage.Clone(ctx => ctx.Crop(region));
        using Image<TPixel> nativeRegion = nativeSurfaceImage.Clone(ctx => ctx.Crop(region));
        AssertBackendTripletSimilarity(defaultRegion, cpuRegion, nativeRegion, defaultTolerancePercent);
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
}
#endif
