// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
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

    [WebGPUTheory]
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

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, polygon);

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

    [WebGPUTheory]
    [WithSolidFilledImages(512, 512, "White", PixelTypes.Rgba32)]
    public void FillPath_AliasedWithThreshold_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false, AntialiasThreshold = 0.25F }
        };

        EllipsePolygon ellipse = new(256, 256, 200, 150);
        Brush brush = Brushes.Solid(Color.Black);

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendTriplet(provider, "FillPath_AliasedThreshold", defaultImage, cpuRegionImage, nativeSurfaceImage);

        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 1F);
    }

    [WebGPUTheory]
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
            canvas.Fill(brush, polygon);
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

    [WebGPUTheory]
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
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, path);

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

    [WebGPUTheory]
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

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, polygon);

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

    [WebGPUTheory]
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
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, polygon);

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

    [WebGPUTheory]
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

        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.007F);
        Rectangle textRegion = Rectangle.Intersect(
            new Rectangle(0, 0, defaultImage.Width, defaultImage.Height),
            new Rectangle(8, 12, defaultImage.Width - 16, Math.Min(220, defaultImage.Height - 12)));
        AssertBackendTripletSimilarityInRegion(defaultImage, cpuRegionImage, nativeSurfaceImage, textRegion, 0.009F);
    }

    [WebGPUTheory]
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
            canvas.Fill(brush, polygon);
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

    [WebGPUTheory]
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
            regionCanvas.Fill(brush, localPolygon);
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

    [WebGPUTheory]
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

        // Differences are visually allowable so use a higher threshold here.
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.0516F);
    }

    [WebGPUTheory]
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

    [WebGPUTheory]
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
                defaultImage.Width,
                defaultImage.Height,
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
                   new(nativeSurfaceConfiguration, new NativeCanvasFrame<TPixel>(targetBounds, nativeSurface), clearOptions))
            {
                nativeSurfaceClearCanvas.Fill(clearBrush);
                nativeSurfaceClearCanvas.Flush();
            }

            int nativeSurfaceComputeBatchesBeforeDraw = nativeSurfaceBackend.TestingComputePathBatchCount;
            using (DrawingCanvas<TPixel> nativeSurfaceDrawCanvas =
                   new(nativeSurfaceConfiguration, new NativeCanvasFrame<TPixel>(targetBounds, nativeSurface), drawingOptions))
            {
                nativeSurfaceDrawCanvas.DrawText(textOptions, text, drawBrush, null);
                nativeSurfaceDrawCanvas.Flush();
            }

            int nativeSurfaceComputeBatchesFromDraw =
                nativeSurfaceBackend.TestingComputePathBatchCount - nativeSurfaceComputeBatchesBeforeDraw;

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
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
                width,
                height,
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
                new(configuration, new NativeCanvasFrame<TPixel>(targetBounds, nativeSurface), options);
            if (initialImage is not null)
            {
                Assert.True(
                    WebGPUTestNativeSurfaceAllocator.TryWriteTexture(
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
        Image<TPixel> nativeSurfaceImage,
        float tolerantPercentage = 0.0003F)
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

        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(tolerantPercentage);
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

    private static void DebugSaveBackendTripletNoRef<TPixel>(
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

    [WebGPUTheory]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32)]
    public void DrawPath_Stroke_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        PathBuilder pb = new();
        pb.AddLine(new PointF(30, 50), new PointF(370, 250));
        pb.AddLine(new PointF(370, 250), new PointF(200, 20));
        pb.CloseFigure();
        IPath path = pb.Build();
        Pen pen = Pens.Solid(Color.DarkBlue, 4F);
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Draw(pen, path);

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

        DebugSaveBackendTriplet(provider, "DrawPath_Stroke", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    public static TheoryData<LineJoin> LineJoinValues { get; } = new()
    {
        LineJoin.Miter,
        LineJoin.MiterRevert,
        LineJoin.MiterRound,
        LineJoin.Bevel,
        LineJoin.Round
    };

    [WebGPUTheory]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineJoin.Miter)]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineJoin.MiterRevert)]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineJoin.MiterRound)]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineJoin.Bevel)]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineJoin.Round)]
    public void DrawPath_Stroke_LineJoin_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider, LineJoin lineJoin)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        // Sharp angles to exercise join behavior.
        PathBuilder pb = new();
        pb.AddLine(new PointF(30, 250), new PointF(100, 30));
        pb.AddLine(new PointF(100, 30), new PointF(170, 250));
        pb.AddLine(new PointF(170, 250), new PointF(240, 30));
        pb.AddLine(new PointF(240, 30), new PointF(370, 150));
        IPath path = pb.Build();

        Pen pen = new SolidPen(new PenOptions(Color.DarkBlue, 12F)
        {
            StrokeOptions = new StrokeOptions { LineJoin = lineJoin }
        });

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Draw(pen, path);

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

        DebugSaveBackendTriplet(provider, $"DrawPath_Stroke_LineJoin_{lineJoin}", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.01F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineCap.Butt)]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineCap.Square)]
    [WithSolidFilledImages(400, 300, "White", PixelTypes.Rgba32, LineCap.Round)]
    public void DrawPath_Stroke_LineCap_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider, LineCap lineCap)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        // Open path to exercise cap behavior at endpoints.
        PathBuilder pb = new();
        pb.AddLine(new PointF(50, 150), new PointF(200, 50));
        pb.AddLine(new PointF(200, 50), new PointF(350, 150));
        IPath path = pb.Build();

        Pen pen = new SolidPen(new PenOptions(Color.DarkBlue, 16F)
        {
            StrokeOptions = new StrokeOptions { LineCap = lineCap }
        });

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Draw(pen, path);

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

        DebugSaveBackendTriplet(provider, $"DrawPath_Stroke_LineCap_{lineCap}", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.01F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(512, 512, "White", PixelTypes.Rgba32)]
    public void FillPath_MultipleSeparatePaths_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        Brush brush = Brushes.Solid(Color.Black);
        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            for (int i = 0; i < 20; i++)
            {
                float x = 20 + (i * 24);
                float y = 20 + (i * 22);
                canvas.Fill(brush, new RectangularPolygon(x, y, 80, 60));
            }
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

        DebugSaveBackendTriplet(provider, "FillPath_MultipleSeparate", defaultImage, cpuRegionImage, nativeSurfaceImage);

        Assert.True(cpuRegionBackend.TestingPrepareCoverageCallCount >= 20);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 1F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_EvenOddRule_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true },
            ShapeOptions = new ShapeOptions
            {
                IntersectionRule = IntersectionRule.EvenOdd
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

        // Inner contour with same winding — EvenOdd should create a hole.
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
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, path);

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

        DebugSaveBackendTriplet(provider, "FillPath_EvenOdd", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);

        // EvenOdd with same winding inner contour should create a hole at center.
        Assert.Equal(defaultImage[128, 128], cpuRegionImage[128, 128]);
        Assert.Equal(defaultImage[128, 128], nativeSurfaceImage[128, 128]);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.5F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(800, 600, "White", PixelTypes.Rgba32)]
    public void FillPath_LargeTileCount_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        // Large polygon spanning most of the image to exercise many tiles.
        Brush brush = Brushes.Solid(Color.Black);
        EllipsePolygon ellipse = new(new PointF(400, 300), new SizeF(700, 500));
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendTriplet(provider, "FillPath_LargeTileCount", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 1F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(300, 200, "White", PixelTypes.Rgba32)]
    public void MultipleFlushes_OnSameBackend_ProduceCorrectResults<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        Brush redBrush = Brushes.Solid(Color.Red);
        Brush blueBrush = Brushes.Solid(Color.Blue);
        RectangularPolygon rect1 = new(20, 20, 120, 80);
        RectangularPolygon rect2 = new(160, 100, 120, 80);

        // Default backend: two separate flushes
        using Image<TPixel> defaultImage = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas1 = new(Configuration.Default, GetFrameRegion(defaultImage), drawingOptions))
        {
            canvas1.Fill(redBrush, rect1);
            canvas1.Flush();
        }

        using (DrawingCanvas<TPixel> canvas2 = new(Configuration.Default, GetFrameRegion(defaultImage), drawingOptions))
        {
            canvas2.Fill(blueBrush, rect2);
            canvas2.Flush();
        }

        // WebGPU backend: two separate flushes reusing the same backend
        using Image<TPixel> cpuRegionImage = provider.GetImage();
        using WebGPUDrawingBackend cpuRegionBackend = new();
        Configuration cpuConfig = Configuration.Default.Clone();
        cpuConfig.SetDrawingBackend(cpuRegionBackend);

        using (DrawingCanvas<TPixel> canvas1 = new(cpuConfig, GetFrameRegion(cpuRegionImage), drawingOptions))
        {
            canvas1.Fill(redBrush, rect1);
            canvas1.Flush();
        }

        using (DrawingCanvas<TPixel> canvas2 = new(cpuConfig, GetFrameRegion(cpuRegionImage), drawingOptions))
        {
            canvas2.Fill(blueBrush, rect2);
            canvas2.Flush();
        }

        // Native surface: two separate flushes reusing same backend
        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        Assert.True(
            WebGPUTestNativeSurfaceAllocator.TryCreate<TPixel>(
                defaultImage.Width,
                defaultImage.Height,
                out NativeSurface nativeSurface,
                out nint textureHandle,
                out nint textureViewHandle,
                out string createError),
            createError);

        try
        {
            Configuration nativeConfig = Configuration.Default.Clone();
            nativeConfig.SetDrawingBackend(nativeSurfaceBackend);
            Rectangle targetBounds = defaultImage.Bounds;

            // Upload initial white content
            using Image<TPixel> initialImage = provider.GetImage();
            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryWriteTexture(
                    textureHandle,
                    defaultImage.Width,
                    defaultImage.Height,
                    initialImage,
                    out string uploadError),
                uploadError);

            using (DrawingCanvas<TPixel> canvas1 =
                   new(nativeConfig, new NativeCanvasFrame<TPixel>(targetBounds, nativeSurface), drawingOptions))
            {
                canvas1.Fill(redBrush, rect1);
                canvas1.Flush();
            }

            using (DrawingCanvas<TPixel> canvas2 =
                   new(nativeConfig, new NativeCanvasFrame<TPixel>(targetBounds, nativeSurface), drawingOptions))
            {
                canvas2.Fill(blueBrush, rect2);
                canvas2.Flush();
            }

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
                    textureHandle,
                    defaultImage.Width,
                    defaultImage.Height,
                    out Image<TPixel> nativeSurfaceImage,
                    out string readError),
                readError);

            using (nativeSurfaceImage)
            {
                DebugSaveBackendTriplet(provider, "MultipleFlushes", defaultImage, cpuRegionImage, nativeSurfaceImage);
                AssertCoverageExecutionAccounting(cpuRegionBackend);
                AssertCoverageExecutionAccounting(nativeSurfaceBackend);
                AssertGpuPathWhenRequired(cpuRegionBackend);
                AssertGpuPathWhenRequired(nativeSurfaceBackend);
                AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 1F);
            }
        }
        finally
        {
            WebGPUTestNativeSurfaceAllocator.Release(textureHandle, textureViewHandle);
        }
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithLinearGradientBrush_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        EllipsePolygon ellipse = new(128, 128, 100);
        Brush brush = new LinearGradientBrush(
            new PointF(28, 28),
            new PointF(228, 228),
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(0.5F, Color.Green),
            new ColorStop(1, Color.Blue));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, ellipse);

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

        // MacOS on CI has some outliers with this test, so using a slightly higher tolerance here to avoid noise.
        DebugSaveBackendTriplet(provider, "FillPath_LinearGradient", defaultImage, cpuRegionImage, nativeSurfaceImage, tolerantPercentage: 0.0007F);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithLinearGradientBrush_Repeat_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = new LinearGradientBrush(
            new PointF(64, 64),
            new PointF(128, 128),
            GradientRepetitionMode.Repeat,
            new ColorStop(0, Color.Yellow),
            new ColorStop(1, Color.Purple));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_LinearGradient_Repeat", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithRadialGradientBrush_SingleCircle_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = new RadialGradientBrush(
            new PointF(128, 128),
            100F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.White),
            new ColorStop(1, Color.DarkRed));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_RadialGradient_Single", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithRadialGradientBrush_TwoCircle_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = new RadialGradientBrush(
            new PointF(100, 100),
            20F,
            new PointF(128, 128),
            110F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Yellow),
            new ColorStop(1, Color.Navy));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_RadialGradient_TwoCircle", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithEllipticGradientBrush_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = new EllipticGradientBrush(
            new PointF(128, 128),
            new PointF(228, 128),
            0.6F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Cyan),
            new ColorStop(1, Color.Magenta));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_EllipticGradient", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithSweepGradientBrush_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        EllipsePolygon ellipse = new(128, 128, 100);
        Brush brush = new SweepGradientBrush(
            new PointF(128, 128),
            0F,
            360F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(0.33F, Color.Green),
            new ColorStop(0.67F, Color.Blue),
            new ColorStop(1, Color.Red));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendTriplet(provider, "FillPath_SweepGradient", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithSweepGradientBrush_PartialArc_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = new SweepGradientBrush(
            new PointF(128, 128),
            45F,
            270F,
            GradientRepetitionMode.Reflect,
            new ColorStop(0, Color.Orange),
            new ColorStop(1, Color.Teal));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        // MacOS on CI has some outliers with this test, so using a slightly higher tolerance here to avoid noise.
        DebugSaveBackendTriplet(
            provider,
            "FillPath_SweepGradient_PartialArc",
            defaultImage,
            cpuRegionImage,
            nativeSurfaceImage,
            tolerantPercentage: 0.0280F);

        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithPatternBrush_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = Brushes.Horizontal(Color.Black, Color.White);

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_PatternBrush_Horizontal", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithPatternBrush_Diagonal_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        EllipsePolygon ellipse = new(128, 128, 100);
        Brush brush = Brushes.ForwardDiagonal(Color.DarkGreen, Color.LightGray);

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendTriplet(provider, "FillPath_PatternBrush_Diagonal", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "Red", PixelTypes.Rgba32)]
    public void FillPath_WithRecolorBrush_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = new RecolorBrush(Color.Red, Color.Blue, 0.5F);

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_RecolorBrush", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithLinearGradientBrush_ThreePoint_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(16, 16, 224, 224);
        Brush brush = new LinearGradientBrush(
            new PointF(64, 128),
            new PointF(192, 128),
            new PointF(128, 64),
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Coral),
            new ColorStop(1, Color.SteelBlue));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_LinearGradient_ThreePoint", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithEllipticGradientBrush_Reflect_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectangularPolygon rect = new(8, 8, 240, 240);
        Brush brush = new EllipticGradientBrush(
            new PointF(128, 128),
            new PointF(180, 160),
            0.4F,
            GradientRepetitionMode.Reflect,
            new ColorStop(0, Color.Gold),
            new ColorStop(0.5F, Color.DarkViolet),
            new ColorStop(1, Color.White));

        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendTriplet(provider, "FillPath_EllipticGradient_Reflect", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(500, 400, "Black", PixelTypes.Rgba32)]
    public void CanApplyPerspectiveTransform_StarWarsCrawl<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 32);

        const string text = @"A long time ago in a galaxy
far, far away....

It is a period of civil war.
Rebel spaceships, striking
from a hidden base, have won
their first victory against
the evil Galactic Empire.";

        RichTextOptions textOptions = new(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextAlignment = TextAlignment.Center,
            Origin = new PointF(250, 360)
        };

        const float originX = 250;
        const float originY = 380;
        Matrix4x4 toOrigin = Matrix4x4.CreateTranslation(-originX, -originY, 0);
        Matrix4x4 taperMatrix = Matrix4x4.Identity;
        taperMatrix.M24 = -0.003F;
        Matrix4x4 fromOrigin = Matrix4x4.CreateTranslation(originX, originY, 0);
        Matrix4x4 perspective = toOrigin * taperMatrix * fromOrigin;

        DrawingOptions perspectiveOptions = new() { Transform = perspective };

        // Star Destroyer geometry.
        PointF[] sternFace =
        [
            new(0, 0), new(300, 0), new(300, 80), new(0, 80),
        ];

        RectangularPolygon sternHighlightRect = new(4, 4, 292, 72);

        EllipsePolygon thrusterLeft = new(50, 40, 42, 42);
        EllipsePolygon thrusterCenter = new(150, 40, 48, 48);
        EllipsePolygon thrusterRight = new(250, 40, 42, 42);

        ProjectiveTransformBuilder transformBuilder = new();

        Rectangle sternBounds = new(0, 0, 300, 80);
        Matrix4x4 sternTransform = transformBuilder
            .AppendQuadDistortion(
                topLeft: new PointF(50, 80),
                topRight: new PointF(400, 90),
                bottomRight: new PointF(390, 135),
                bottomLeft: new PointF(60, 140))
            .BuildMatrix(sternBounds);

        PointF[] bottomHull =
        [
            new(0, 0), new(300, 0), new(150, 80),
        ];

        EllipsePolygon hullDome = new(117, 80, 96, 96);

        Rectangle hullBounds = new(0, 0, 300, 80);
        Matrix4x4 hullTransform = transformBuilder.Clear()
            .AppendQuadDistortion(
                topLeft: new PointF(60, 140),
                topRight: new PointF(390, 135),
                bottomRight: new PointF(300, 160),
                bottomLeft: new PointF(-30, 170))
            .BuildMatrix(hullBounds);

        PointF[] towerStem =
        [
            new(14, 8), new(26, 8), new(26, 20), new(14, 20),
        ];

        PointF[] towerTop =
        [
            new(0, 0), new(40, 0), new(40, 10), new(0, 10),
        ];

        Rectangle towerBounds = new(0, 0, 40, 20);
        Matrix4x4 towerTransform = transformBuilder.Clear()
            .AppendQuadDistortion(
                topLeft: new PointF(175, 66),
                topRight: new PointF(240, 68),
                bottomRight: new PointF(238, 85),
                bottomLeft: new PointF(177, 84))
            .BuildMatrix(towerBounds);

        Color sternColorLeft = Color.FromPixel(new Rgba32(70, 75, 85, 255));
        Color sternColorRight = Color.FromPixel(new Rgba32(35, 38, 45, 255));
        Color hullColorLeft = Color.FromPixel(new Rgba32(85, 90, 100, 255));
        Color hullColorRight = Color.FromPixel(new Rgba32(45, 50, 58, 255));
        Color highlightColorLeft = Color.FromPixel(new Rgba32(135, 140, 150, 255));
        Color highlightColorRight = Color.FromPixel(new Rgba32(65, 70, 80, 255));
        Color thrusterInnerGlow = Color.White;
        Color thrusterOuterGlow = Color.Blue;

        LinearGradientBrush sternBrush = new(
            new PointF(0, 40),
            new PointF(300, 40),
            GradientRepetitionMode.None,
            new ColorStop(0, sternColorLeft),
            new ColorStop(1, sternColorRight));

        LinearGradientBrush hullBrush = new(
            new PointF(0, 40),
            new PointF(300, 40),
            GradientRepetitionMode.None,
            new ColorStop(0, hullColorLeft),
            new ColorStop(1, hullColorRight));

        LinearGradientBrush highlightBrush = new(
            new PointF(0, 40),
            new PointF(300, 40),
            GradientRepetitionMode.None,
            new ColorStop(0, highlightColorLeft),
            new ColorStop(1, highlightColorRight));

        LinearGradientBrush towerBrush = new(
            new PointF(0, 10),
            new PointF(40, 10),
            GradientRepetitionMode.None,
            new ColorStop(0, sternColorLeft),
            new ColorStop(1, sternColorRight));

        LinearGradientBrush towerTopBrush = new(
            new PointF(0, 5),
            new PointF(40, 5),
            GradientRepetitionMode.None,
            new ColorStop(0, highlightColorLeft),
            new ColorStop(1, highlightColorRight));

        LinearGradientBrush domeBrush = new(
            new PointF(21, 80),
            new PointF(213, 80),
            GradientRepetitionMode.None,
            new ColorStop(0, highlightColorLeft),
            new ColorStop(1, highlightColorRight));

        EllipticGradientBrush thrusterBrushLeft = new(
            new PointF(50, 40),
            new PointF(50 + 42, 40),
            1f,
            GradientRepetitionMode.None,
            new ColorStop(0, thrusterInnerGlow),
            new ColorStop(.75F, thrusterOuterGlow));

        EllipticGradientBrush thrusterBrushCenter = new(
            new PointF(150, 40),
            new PointF(150 + 48, 40),
            1f,
            GradientRepetitionMode.None,
            new ColorStop(0, thrusterInnerGlow),
            new ColorStop(.75F, thrusterOuterGlow));

        EllipticGradientBrush thrusterBrushRight = new(
            new PointF(250, 40),
            new PointF(250 + 42, 40),
            1f,
            GradientRepetitionMode.None,
            new ColorStop(0, thrusterInnerGlow),
            new ColorStop(.75F, thrusterOuterGlow));

        DrawingOptions sternOptions = new() { Transform = sternTransform };
        DrawingOptions hullOptions = new() { Transform = hullTransform };
        DrawingOptions towerOptions = new() { Transform = towerTransform };

        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            // Bottom hull (draw first — behind stern).
            canvas.Save(hullOptions);
            canvas.Fill(highlightBrush, new Polygon(bottomHull));
            canvas.Restore();

            // Stern face with thrusters, highlight, and dome.
            canvas.Save(sternOptions);
            canvas.Fill(domeBrush, hullDome);
            canvas.Draw(Pens.Solid(highlightColorRight, 2), hullDome);
            canvas.Fill(sternBrush, new Polygon(sternFace));
            canvas.Draw(Pens.Solid(highlightColorLeft, 2), sternHighlightRect);
            canvas.Fill(thrusterBrushLeft, thrusterLeft);
            canvas.Fill(thrusterBrushCenter, thrusterCenter);
            canvas.Fill(thrusterBrushRight, thrusterRight);
            canvas.Draw(Pens.Solid(highlightColorLeft, 2), thrusterLeft);
            canvas.Draw(Pens.Solid(highlightColorLeft, 2), thrusterCenter);
            canvas.Draw(Pens.Solid(highlightColorLeft, 2), thrusterRight);
            canvas.Restore();

            // Bridge tower.
            canvas.Save(towerOptions);
            canvas.Fill(towerTopBrush, new Polygon(towerTop));
            canvas.Fill(towerBrush, new Polygon(towerStem));
            canvas.Restore();

            // Text crawl with perspective.
            canvas.Save(perspectiveOptions);
            canvas.DrawText(textOptions, text, Brushes.Solid(Color.Yellow), pen: null);
            canvas.Restore();
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

        DebugSaveBackendTriplet(provider, "StarWarsCrawl", defaultImage, cpuRegionImage, nativeSurfaceImage);
        AssertCoverageExecutionAccounting(cpuRegionBackend);
        AssertCoverageExecutionAccounting(nativeSurfaceBackend);
        AssertGpuPathWhenRequired(cpuRegionBackend);
        AssertGpuPathWhenRequired(nativeSurfaceBackend);
        AssertBackendTripletSimilarity(defaultImage, cpuRegionImage, nativeSurfaceImage, 0.005F);
    }

    private static Buffer2DRegion<TPixel> GetFrameRegion<TPixel>(Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(image.Frames.RootFrame.PixelBuffer, image.Bounds);
}
