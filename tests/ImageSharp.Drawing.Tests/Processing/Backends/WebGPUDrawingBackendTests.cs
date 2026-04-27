// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

[GroupOutput("Drawing")]
public partial class WebGPUDrawingBackendTests
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.05F);
        AssertBackendPairReferenceOutputs(provider, "FillPath", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_UncontainedGeometry_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        PathBuilder pathBuilder = new();
        pathBuilder.AddLines(
        [
            new PointF(-96, 128.5F),
            new PointF(128.5F, -88),
            new PointF(352, 128.5F),
            new PointF(128.5F, 344)
        ]);
        pathBuilder.CloseFigure();

        IPath path = pathBuilder.Build();
        Brush brush = Brushes.Solid(Color.MediumPurple);
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, path);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_UncontainedGeometry", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.3F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_UncontainedGeometry", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_AliasedThreshold", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_AliasedThreshold", defaultImage, nativeSurfaceImage);
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
        Brush brush = new ImageBrush<TPixel>(foreground, new RectangleF(32, 24, 192, 144), new Point(13, -9));
        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Clear(clearBrush);
            canvas.Fill(brush, polygon);
        }

        using Image<TPixel> defaultImage = new(384, 256);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend<TPixel>(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction);

        DebugSaveBackendPair(provider, "FillPath_ImageBrush", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_ImageBrush", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_NonZeroNestedContours", defaultImage, nativeSurfaceImage);

        // Non-zero winding semantics must still match on an interior point.
        Assert.Equal(defaultImage[128, 128], nativeSurfaceImage[128, 128]);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.5F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_NonZeroNestedContours", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            baseImage);

        DebugSaveBackendPair(
            provider,
            $"FillPath_GraphicsOptions_SolidBrush_{colorMode}_{alphaMode}",
            defaultImage,
            nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.1F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"FillPath_GraphicsOptions_SolidBrush_{colorMode}_{alphaMode}",
            defaultImage,
            nativeSurfaceImage);
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
        Brush brush = new ImageBrush<TPixel>(foreground, new RectangleF(32, 24, 192, 144), new Point(13, -9));
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, polygon);

        using Image<TPixel> baseImage = provider.GetImage();
        using Image<TPixel> defaultImage = baseImage.Clone();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            baseImage);

        DebugSaveBackendPair(
            provider,
            $"FillPath_GraphicsOptions_ImageBrush_{colorMode}_{alphaMode}",
            defaultImage,
            nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.1F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"FillPath_GraphicsOptions_ImageBrush_{colorMode}_{alphaMode}",
            defaultImage,
            nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "DrawText", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0122F);
        Rectangle textRegion = Rectangle.Intersect(
            new Rectangle(0, 0, defaultImage.Width, defaultImage.Height),
            new Rectangle(8, 12, defaultImage.Width - 16, Math.Min(220, defaultImage.Height - 12)));
        AssertBackendPairSimilarityInRegion(defaultImage, nativeSurfaceImage, textRegion, 0.0157F);
        AssertBackendPairReferenceOutputs(provider, "DrawText", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_NativeSurfaceParity", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.5F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_NativeSurfaceParity", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_NativeSurfaceSubregionParity", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.5F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_NativeSurfaceSubregionParity", defaultImage, nativeSurfaceImage);
    }

    /// <summary>
    /// Verifies that a later full-frame fill on the same native WebGPU surface fully replaces the previous frame contents.
    /// </summary>
    [WebGPUTheory]
    [WithBlankImage(256, 192, PixelTypes.Rgba32)]
    public void Fill_AfterPreviousFrameOnNativeSurface_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false }
        };

        Brush firstBackground = Brushes.Solid(Color.DarkSlateBlue);
        Brush firstFill = Brushes.Solid(Color.OrangeRed);
        Brush secondBackground = Brushes.Solid(Color.MidnightBlue);
        Brush secondFill = Brushes.Solid(Color.LimeGreen);
        RectangularPolygon firstRect = new(18, 26, 176, 92);
        RectangularPolygon secondRect = new(96, 54, 42, 38);

        void DrawFirstFrame(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(firstBackground);
            canvas.Fill(firstFill, firstRect);
        }

        void DrawSecondFrame(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(secondBackground);
            canvas.Fill(secondFill, secondRect);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawFirstFrame);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawSecondFrame);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using WebGPURenderTarget<TPixel> renderTarget = new(defaultImage.Width, defaultImage.Height);
        Configuration nativeSurfaceConfiguration = Configuration.Default.Clone();
        nativeSurfaceConfiguration.SetDrawingBackend(nativeSurfaceBackend);

        using (DrawingCanvas<TPixel> firstCanvas =
               new(nativeSurfaceConfiguration, drawingOptions, nativeSurfaceBackend, renderTarget.NativeFrame))
        {
            DrawFirstFrame(firstCanvas);
            firstCanvas.Flush();
        }

        using (DrawingCanvas<TPixel> secondCanvas =
               new(nativeSurfaceConfiguration, drawingOptions, nativeSurfaceBackend, renderTarget.NativeFrame))
        {
            DrawSecondFrame(secondCanvas);
            secondCanvas.Flush();
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.Readback();
        DebugSaveBackendPair(provider, "Fill_RepeatedFrames", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0F);
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
            canvas.Apply(blurPath, ctx => ctx.GaussianBlur(6F));
            canvas.Apply(pixelatePath, ctx => ctx.Pixelate(10));
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend<TPixel>(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction);

        DebugSaveBackendPair(provider, "Process", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0516F);
        AssertBackendPairReferenceOutputs(provider, "Process", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "RepeatedGlyphs", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 2F);
        AssertBackendPairReferenceOutputs(provider, "RepeatedGlyphs", defaultImage, nativeSurfaceImage);
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
        using (DrawingCanvas<TPixel> defaultClearCanvas = defaultImage.CreateCanvas(Configuration.Default, clearOptions))
        {
            defaultClearCanvas.Fill(clearBrush);
            defaultClearCanvas.Flush();
        }

        using (DrawingCanvas<TPixel> defaultDrawCanvas = defaultImage.CreateCanvas(Configuration.Default, drawingOptions))
        {
            defaultDrawCanvas.DrawText(textOptions, text, drawBrush, null);
            defaultDrawCanvas.Flush();
        }

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using WebGPURenderTarget<TPixel> renderTarget = new(defaultImage.Width, defaultImage.Height);
        Configuration nativeSurfaceConfiguration = Configuration.Default.Clone();
        nativeSurfaceConfiguration.SetDrawingBackend(nativeSurfaceBackend);

        using (DrawingCanvas<TPixel> nativeSurfaceClearCanvas =
               new(nativeSurfaceConfiguration, clearOptions, nativeSurfaceBackend, renderTarget.NativeFrame))
        {
            nativeSurfaceClearCanvas.Fill(clearBrush);
            nativeSurfaceClearCanvas.Flush();
        }

        using (DrawingCanvas<TPixel> nativeSurfaceDrawCanvas =
               new(nativeSurfaceConfiguration, drawingOptions, nativeSurfaceBackend, renderTarget.NativeFrame))
        {
            nativeSurfaceDrawCanvas.DrawText(textOptions, text, drawBrush, null);
            nativeSurfaceDrawCanvas.Flush();
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.Readback();
        DebugSaveBackendPair(provider, "RepeatedGlyphs_AfterClear", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 2F);
        AssertBackendPairReferenceOutputs(provider, "RepeatedGlyphs_AfterClear", defaultImage, nativeSurfaceImage);
    }

    private static void RenderWithDefaultBackend<TPixel>(Image<TPixel> image, DrawingOptions options, Action<DrawingCanvas<TPixel>> drawAction)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using DrawingCanvas<TPixel> canvas = image.CreateCanvas(Configuration.Default, options);
        drawAction(canvas);
        canvas.Flush();
    }

    private static IPath CreateLargeSceneDenseRectangleGridPath()
    {
        const int gridSize = 260;
        const int pitch = 2;
        const int rectangleSize = 1;

        PathBuilder pathBuilder = new();
        for (int y = 0; y < gridSize; y++)
        {
            int top = y * pitch;
            for (int x = 0; x < gridSize; x++)
            {
                pathBuilder.AddRectangle(x * pitch, top, rectangleSize, rectangleSize);
            }
        }

        return pathBuilder.Build();
    }

    private static Rectangle[] CreateClipReduceLayerBounds(int layerCount, Rectangle targetBounds)
    {
        Rectangle[] layerBounds = new Rectangle[layerCount];
        for (int i = 0; i < layerCount; i++)
        {
            int width = 18 + ((i * 7) % 22);
            int height = 16 + ((i * 11) % 24);
            int x = (i * 17) % Math.Max(1, targetBounds.Width - width + 1);
            int y = ((i * 23) + ((i / 8) * 7)) % Math.Max(1, targetBounds.Height - height + 1);
            layerBounds[i] = new Rectangle(x, y, width, height);
        }

        return layerBounds;
    }

    private static IPath CreateClipReduceLayerLocalPath(int layerIndex, Rectangle layerBounds)
    {
        int insetX = 1 + (layerIndex % 4);
        int insetY = 1 + ((layerIndex / 4) % 4);
        int widthTrim = 1 + ((layerIndex * 3) % 5);
        int heightTrim = 1 + ((layerIndex * 5) % 5);
        int innerWidth = Math.Max(4, layerBounds.Width - insetX - widthTrim);
        int innerHeight = Math.Max(4, layerBounds.Height - insetY - heightTrim);

        return (layerIndex & 1) == 0
            ? new RectangularPolygon(insetX, insetY, innerWidth, innerHeight)
            : new EllipsePolygon(
                insetX + (innerWidth / 2F),
                insetY + (innerHeight / 2F),
                innerWidth / 2F,
                innerHeight / 2F);
    }

    private static SolidBrush CreateClipReduceLayerBrush(int layerIndex)
        => Brushes.Solid((layerIndex & 3) switch
        {
            0 => Color.Red.WithAlpha(0.55F),
            1 => Color.CornflowerBlue.WithAlpha(0.5F),
            2 => Color.LimeGreen.WithAlpha(0.45F),
            _ => Color.Goldenrod.WithAlpha(0.5F)
        });

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

    private static Image<TPixel> RenderWithNativeSurfaceWebGpuBackend<TPixel>(
        int width,
        int height,
        WebGPUDrawingBackend backend,
        DrawingOptions options,
        Action<DrawingCanvas<TPixel>> drawAction,
        Image<TPixel> initialImage = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using WebGPURenderTarget<TPixel> renderTarget = new(width, height);
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);
        Rectangle targetBounds = new(0, 0, width, height);

        if (initialImage is not null)
        {
            using DrawingCanvas<TPixel> initialCanvas =
                new(configuration, new DrawingOptions(), backend, renderTarget.NativeFrame);
            initialCanvas.DrawImage(initialImage, initialImage.Bounds, targetBounds);
            initialCanvas.Flush();
        }

        using DrawingCanvas<TPixel> canvas = new(configuration, options, backend, renderTarget.NativeFrame);
        drawAction(canvas);
        canvas.Flush();

        return renderTarget.Readback();
    }

    private static void DebugSaveBackendPair<TPixel>(
        TestImageProvider<TPixel> provider,
        string testName,
        Image<TPixel> defaultImage,
        Image<TPixel> nativeSurfaceImage)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        defaultImage.DebugSave(
            provider,
            $"{testName}_Default",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        nativeSurfaceImage.DebugSave(
            provider,
            $"{testName}_WebGPU_NativeSurface",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    private static void AssertBackendPairSimilarity<TPixel>(
        Image<TPixel> defaultImage,
        Image<TPixel> nativeSurfaceImage,
        float defaultTolerancePercent)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(defaultTolerancePercent);
        tolerantComparer.VerifySimilarity(defaultImage, nativeSurfaceImage);
    }

    private static void AssertBackendPairReferenceOutputs<TPixel>(
        TestImageProvider<TPixel> provider,
        string testName,
        Image<TPixel> defaultImage,
        Image<TPixel> nativeSurfaceImage,
        float tolerantPercentage = 0.0003F)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(tolerantPercentage);
        defaultImage.CompareToReferenceOutput(
            tolerantComparer,
            provider,
            $"{testName}_Default",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        nativeSurfaceImage.CompareToReferenceOutput(
            tolerantComparer,
            provider,
            $"{testName}_WebGPU_NativeSurface",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    private static void AssertBackendPairSimilarityInRegion<TPixel>(
        Image<TPixel> defaultImage,
        Image<TPixel> nativeSurfaceImage,
        Rectangle region,
        float defaultTolerancePercent)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> defaultRegion = defaultImage.Clone(ctx => ctx.Crop(region));
        using Image<TPixel> nativeRegion = nativeSurfaceImage.Clone(ctx => ctx.Crop(region));
        AssertBackendPairSimilarity(defaultRegion, nativeRegion, defaultTolerancePercent);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "DrawPath_Stroke", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.01F);
        AssertBackendPairReferenceOutputs(provider, "DrawPath_Stroke", defaultImage, nativeSurfaceImage);
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
        IPath outline = path.GenerateOutline(pen.StrokeWidth, pen.StrokeOptions);
        void DrawReference(DrawingCanvas<TPixel> canvas) => canvas.Fill(pen.StrokeFill, outline);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        using Image<TPixel> referenceImage = provider.GetImage();
        RenderWithDefaultBackend(referenceImage, drawingOptions, DrawReference);

        using Image<TPixel> defaultComparisonImage = CreateJoinComparisonImage(referenceImage, defaultImage);
        using Image<TPixel> nativeSurfaceComparisonImage = CreateJoinComparisonImage(referenceImage, nativeSurfaceImage);

        DebugSaveBackendPair(
            provider,
            $"DrawPath_Stroke_LineJoin_{lineJoin}",
            defaultComparisonImage,
            nativeSurfaceComparisonImage);

        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.01F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"DrawPath_Stroke_LineJoin_{lineJoin}",
            defaultComparisonImage,
            nativeSurfaceComparisonImage);

        static Image<TPixel> CreateJoinComparisonImage(Image<TPixel> reference, Image<TPixel> rendered)
        {
            Image<TPixel> comparison = new(reference.Width, reference.Height * 2, Color.White.ToPixel<TPixel>());
            comparison.Mutate(ctx => ctx
                .DrawImage(reference, new Point(0, 0), 1F)
                .DrawImage(rendered, new Point(0, reference.Height), 1F));
            return comparison;
        }
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
        IPath outline = path.GenerateOutline(pen.StrokeWidth, pen.StrokeOptions);
        void DrawReference(DrawingCanvas<TPixel> canvas) => canvas.Fill(pen.StrokeFill, outline);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        using Image<TPixel> referenceImage = provider.GetImage();
        RenderWithDefaultBackend(referenceImage, drawingOptions, DrawReference);

        using Image<TPixel> defaultComparisonImage = CreateLineCapComparisonImage(referenceImage, defaultImage);
        using Image<TPixel> nativeSurfaceComparisonImage = CreateLineCapComparisonImage(referenceImage, nativeSurfaceImage);

        DebugSaveBackendPair(
            provider,
            $"DrawPath_Stroke_LineCap_{lineCap}",
            defaultComparisonImage,
            nativeSurfaceComparisonImage);

        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0103F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"DrawPath_Stroke_LineCap_{lineCap}",
            defaultComparisonImage,
            nativeSurfaceComparisonImage);

        static Image<TPixel> CreateLineCapComparisonImage(Image<TPixel> reference, Image<TPixel> rendered)
        {
            Image<TPixel> comparison = new(reference.Width, reference.Height * 2, Color.White.ToPixel<TPixel>());
            comparison.Mutate(ctx => ctx
                .DrawImage(reference, new Point(0, 0), 1F)
                .DrawImage(rendered, new Point(0, reference.Height), 1F));
            return comparison;
        }
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_MultipleSeparate", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_MultipleSeparate", defaultImage, nativeSurfaceImage);
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

        // Inner contour with same winding; EvenOdd should create a hole.
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_EvenOdd", defaultImage, nativeSurfaceImage);

        // EvenOdd with same winding inner contour should create a hole at center.
        Assert.Equal(defaultImage[128, 128], nativeSurfaceImage[128, 128]);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.5F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_EvenOdd", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_LargeTileCount", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_LargeTileCount", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(520, 520, "White", PixelTypes.Rgba32)]
    public void FillPath_LargeScene_UsesLargePathScan_AndMatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false }
        };

        Brush brush = Brushes.Solid(Color.Black);
        IPath denseGrid = CreateLargeSceneDenseRectangleGridPath();
        void DrawAction(DrawingCanvas<TPixel> canvas) => canvas.Fill(brush, denseGrid);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_LargeScene", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_LargeScene", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_ManyLayers_UsesClipReduce_AndMatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int layerCount = 130;
        DrawingOptions drawingOptions = new();

        using Image<TPixel> defaultImage = provider.GetImage();
        Rectangle[] layerBounds = CreateClipReduceLayerBounds(layerCount, defaultImage.Bounds);

        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            for (int i = 0; i < layerBounds.Length; i++)
            {
                Rectangle layerBoundsLocal = layerBounds[i];
                canvas.SaveLayer(new GraphicsOptions(), layerBoundsLocal);
                canvas.Fill(CreateClipReduceLayerBrush(i), CreateClipReduceLayerLocalPath(i, layerBoundsLocal));
                canvas.Restore();
            }
        }

        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "SaveLayer_ClipReduce", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "SaveLayer_ClipReduce", defaultImage, nativeSurfaceImage, 0.0006F);
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

        // Default backend: two separate flushes.
        using Image<TPixel> defaultImage = provider.GetImage();
        using (DrawingCanvas<TPixel> canvas1 = defaultImage.CreateCanvas(Configuration.Default, drawingOptions))
        {
            canvas1.Fill(redBrush, rect1);
            canvas1.Flush();
        }

        using (DrawingCanvas<TPixel> canvas2 = defaultImage.CreateCanvas(Configuration.Default, drawingOptions))
        {
            canvas2.Fill(blueBrush, rect2);
            canvas2.Flush();
        }

        // Native surface: two separate flushes reusing same backend.
        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using WebGPURenderTarget<TPixel> renderTarget = new(defaultImage.Width, defaultImage.Height);
        Configuration nativeConfig = Configuration.Default.Clone();
        nativeConfig.SetDrawingBackend(nativeSurfaceBackend);

        using (DrawingCanvas<TPixel> canvas1 =
               new(nativeConfig, drawingOptions, nativeSurfaceBackend, renderTarget.NativeFrame))
        {
            canvas1.Clear(Brushes.Solid(Color.White));
            canvas1.Fill(redBrush, rect1);
            canvas1.Flush();
        }

        using (DrawingCanvas<TPixel> canvas2 =
               new(nativeConfig, drawingOptions, nativeSurfaceBackend, renderTarget.NativeFrame))
        {
            canvas2.Fill(blueBrush, rect2);
            canvas2.Flush();
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.Readback();
        DebugSaveBackendPair(provider, "MultipleFlushes", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "MultipleFlushes", defaultImage, nativeSurfaceImage);
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
        DebugSaveBackendPair(provider, "FillPath_LinearGradient", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.03F);
        AssertBackendPairReferenceOutputs(
            provider,
            "FillPath_LinearGradient",
            defaultImage,
            nativeSurfaceImage,
            tolerantPercentage: 0.0007F);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_LinearGradient_Repeat", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.02F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_LinearGradient_Repeat", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_RadialGradient_Single", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.02F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_RadialGradient_Single", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_RadialGradient_TwoCircle", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0171F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_RadialGradient_TwoCircle", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_EllipticGradient", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.035F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_EllipticGradient", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_SweepGradient", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0304F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_SweepGradient", defaultImage, nativeSurfaceImage);
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
        DebugSaveBackendPair(
            provider,
            "FillPath_SweepGradient_PartialArc",
            defaultImage,
            nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0280F);
        AssertBackendPairReferenceOutputs(
            provider,
            "FillPath_SweepGradient_PartialArc",
            defaultImage,
            nativeSurfaceImage,
            tolerantPercentage: 0.0280F);
    }

    [WebGPUTheory]
    [WithBasicTestPatternImages(384, 256, PixelTypes.Rgba32)]
    public void FillPath_WithPathGradientBrush_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        Rectangle region = new(72, 40, 240, 176);
        RectangularPolygon localPolygon = new(12, 10, 216, 156);
        EllipsePolygon persistedShape = new(new PointF(176, 128), new SizeF(320, 176));
        Brush persistedBrush = Brushes.Solid(Color.DarkSlateBlue);
        Brush brush = new PathGradientBrush(
        [
            new PointF(108, 6),
            new PointF(206, 54),
            new PointF(192, 142),
            new PointF(78, 170),
            new PointF(10, 82)
        ],
        [
            Color.Red,
            Color.Gold,
            Color.LimeGreen,
            Color.DeepSkyBlue,
            Color.BlueViolet
        ],
        Color.White);

        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(persistedBrush, persistedShape);
            canvas.Flush();

            using DrawingCanvas<TPixel> regionCanvas = canvas.CreateRegion(region);
            regionCanvas.Fill(brush, localPolygon);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_PathGradient", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.01F);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_PatternBrush_Horizontal", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.045F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_PatternBrush_Horizontal", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_PatternBrush_Diagonal", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_PatternBrush_Diagonal", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_RecolorBrush", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_RecolorBrush", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_LinearGradient_ThreePoint", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0065F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_LinearGradient_ThreePoint", defaultImage, nativeSurfaceImage);
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "FillPath_EllipticGradient_Reflect", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0398F);
        AssertBackendPairReferenceOutputs(provider, "FillPath_EllipticGradient_Reflect", defaultImage, nativeSurfaceImage);
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
                topLeft: new PointF(70, 80),
                topRight: new PointF(380, 90),
                bottomRight: new PointF(400, 135),
                bottomLeft: new PointF(50, 140))
            .BuildMatrix(sternBounds);

        PointF[] bottomHull =
        [
            new(0, 0), new(300, 0), new(150, 80),
        ];

        EllipsePolygon hullDome = new(117, 80, 96, 96);

        Rectangle hullBounds = new(0, 0, 300, 80);
        Matrix4x4 hullTransform = transformBuilder.Clear()
            .AppendQuadDistortion(
                topLeft: new PointF(50, 140),
                topRight: new PointF(400, 135),
                bottomRight: new PointF(310, 170),
                bottomLeft: new PointF(-40, 170))
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
            // Bottom hull (draw first, behind stern).
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

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "StarWarsCrawl", defaultImage, nativeSurfaceImage);

        // This test has a lot of text and gradients which can be a bit more variable across
        // platforms, so using a higher tolerance here to avoid noise.
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.0474F);
        AssertBackendPairReferenceOutputs(provider, "StarWarsCrawl", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_FullOpacity_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Red);
        RectangularPolygon polygon = new(10, 10, 80, 80);

        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            canvas.SaveLayer();
            canvas.Fill(brush, polygon);
            canvas.Restore();
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "SaveLayer_FullOpacity", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "SaveLayer_FullOpacity", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_HalfOpacity_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Red);
        RectangularPolygon polygon = new(10, 10, 80, 80);

        void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.5f });
            canvas.Fill(brush, polygon);
            canvas.Restore();
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "SaveLayer_HalfOpacity", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "SaveLayer_HalfOpacity", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_NestedLayers_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Outer layer: red fill.
            canvas.SaveLayer();
            canvas.Fill(Brushes.Solid(Color.Red), new RectangularPolygon(0, 0, 128, 128));

            // Inner layer: blue fill over center.
            canvas.SaveLayer();
            canvas.Fill(Brushes.Solid(Color.Blue), new RectangularPolygon(32, 32, 64, 64));
            canvas.Restore(); // Composites blue onto red.

            canvas.Restore(); // Composites red+blue onto white.
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "SaveLayer_NestedLayers", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "SaveLayer_NestedLayers", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_WithBlendMode_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Red), new RectangularPolygon(20, 20, 88, 88));

            canvas.SaveLayer(new GraphicsOptions
            {
                ColorBlendingMode = PixelColorBlendingMode.Multiply,
                AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver,
                BlendPercentage = 1f
            });

            canvas.Fill(Brushes.Solid(Color.Blue), new RectangularPolygon(40, 40, 88, 88));
            canvas.Restore();
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "SaveLayer_WithBlendMode", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "SaveLayer_WithBlendMode", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_WithBounds_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Layer restricted to a sub-region; draw within the layer's local bounds.
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(16, 16, 96, 96));
            canvas.Fill(Brushes.Solid(Color.Green), new RectangularPolygon(0, 0, 96, 96));
            canvas.Restore();
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "SaveLayer_WithBounds", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "SaveLayer_WithBounds", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_MixedSaveAndSaveLayer_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas<TPixel> canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            int before = canvas.SaveCount;
            canvas.Save();              // plain save
            canvas.SaveLayer();         // layer
            canvas.Save();              // plain save

            canvas.Fill(Brushes.Solid(Color.Green), new RectangularPolygon(0, 0, 128, 128));

            canvas.RestoreTo(before);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, "SaveLayer_MixedSaveAndSaveLayer", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 1F);
        AssertBackendPairReferenceOutputs(provider, "SaveLayer_MixedSaveAndSaveLayer", defaultImage, nativeSurfaceImage);
    }

}

