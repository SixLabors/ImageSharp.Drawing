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

// WebGPU image tolerances are intentionally set from cross-hardware runs with
// visual verification, allowing minor GPU floating-point differences without
// hiding visible rendering regressions.
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

        RectanglePolygon polygon = new(48.25F, 63.5F, 401.25F, 302.75F);
        Brush brush = Brushes.Solid(Color.Black);

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, polygon);

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceInitialImage = provider.GetImage();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            WebGPUTextureFormat.Rgba8Unorm,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0012F);
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
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, path);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        RectanglePolygon polygon = new(36.5F, 26.25F, 312.5F, 188.5F);
        Brush clearBrush = Brushes.Solid(Color.White);

        using Image<TPixel> foreground = provider.GetImage();
        Brush brush = new ImageBrush<TPixel>(foreground, new RectangleF(32, 24, 192, 144), new Point(13, -9));
        void DrawAction(DrawingCanvas canvas)
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.None, WrapMode.None)]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.Repeat, WrapMode.Repeat)]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.Mirror, WrapMode.Repeat)]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.Repeat, WrapMode.Mirror)]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.Mirror, WrapMode.Mirror)]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.Clamp, WrapMode.Clamp)]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.Clamp, WrapMode.Repeat)]
    [WithFile(TestImages.Png.Rainbow, PixelTypes.Rgba32, WrapMode.Mirror, WrapMode.Clamp)]
    public void FillPath_WithImageBrushWrapModes_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider, WrapMode wrapX, WrapMode wrapY)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        RectanglePolygon polygon = new(8F, 8F, 492F, 492F);
        Brush clearBrush = Brushes.Solid(Color.White);

        // The rainbow is an opaque diagonal gradient (colour varies along both axes), so every mode is
        // visibly distinct: Repeat tiles, Mirror reflects on each axis, Clamp stretches the edge colours,
        // and None leaves transparency. A uniform, symmetric, or transparent-bordered source would make
        // several modes indistinguishable. The region is inset 1px (sampling the interior) and is smaller
        // than the target, so it repeats several times across the fill.
        using Image<TPixel> foreground = provider.GetImage();
        Brush brush = new ImageBrush<TPixel>(foreground, new RectangleF(1, 1, foreground.Width - 2, foreground.Height - 2), new Point(20, 16), wrapX, wrapY);
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.Clear(clearBrush);
            canvas.Fill(brush, polygon);
        }

        using Image<TPixel> defaultImage = new(500, 500);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<TPixel> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend<TPixel>(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            drawingOptions,
            DrawAction);

        // The GPU backend must match the CPU backend for every wrap mode on both axes, and both must
        // match their committed reference outputs.
        DebugSaveBackendPair(provider, $"{wrapX}-{wrapY}", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, $"{wrapX}-{wrapY}", defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_WithNonZeroNestedContours_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true },
            IntersectionRule = IntersectionRule.NonZero
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
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, path);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);

        // Non-zero winding semantics must still match on an interior point.
        Assert.Equal(defaultImage[128, 128], nativeSurfaceImage[128, 128]);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithBasicTestPatternImages(nameof(GraphicsOptionsModePairs), 384, 256, PixelTypes.Rgba32)]
    public void FillPath_WithGraphicsOptionsModes_SolidBrush_MatchesDefaultOutput<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectanglePolygon polygon = new(26.5F, 18.25F, 324.5F, 208.75F);
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

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, polygon);

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
            $"{colorMode}_{alphaMode}",
            defaultImage,
            nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.125F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"{colorMode}_{alphaMode}",
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
        RectanglePolygon polygon = new(26.5F, 18.25F, 324.5F, 208.75F);

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
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, polygon);

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
            $"{colorMode}_{alphaMode}",
            defaultImage,
            nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.125F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"{colorMode}_{alphaMode}",
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
        void DrawAction(DrawingCanvas canvas) => canvas.DrawText(textOptions, text, brush, pen);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        Rectangle textRegion = Rectangle.Intersect(
            new Rectangle(0, 0, defaultImage.Width, defaultImage.Height),
            new Rectangle(8, 12, defaultImage.Width - 16, Math.Min(220, defaultImage.Height - 12)));
        AssertBackendPairSimilarityInRegion(defaultImage, nativeSurfaceImage, textRegion, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        RectanglePolygon polygon = new(48.25F, 63.5F, 401.25F, 302.75F);
        Brush brush = Brushes.Solid(Color.Black);
        Brush clearBrush = Brushes.Solid(Color.White);
        void DrawAction(DrawingCanvas canvas)
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0012F);
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
        RectanglePolygon localPolygon = new(16.25F, 24.5F, 250.5F, 160.75F);
        Brush brush = Brushes.Solid(Color.Black);
        Brush clearBrush = Brushes.Solid(Color.White);
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.Clear(clearBrush);

            using DrawingCanvas regionCanvas = canvas.CreateRegion(region);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0013F);
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
        RectanglePolygon firstRect = new(18, 26, 176, 92);
        RectanglePolygon secondRect = new(96, 54, 42, 38);

        void DrawFirstFrame(DrawingCanvas canvas)
        {
            canvas.Fill(firstBackground);
            canvas.Fill(firstFill, firstRect);
        }

        void DrawSecondFrame(DrawingCanvas canvas)
        {
            canvas.Fill(secondBackground);
            canvas.Fill(secondFill, secondRect);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawFirstFrame);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawSecondFrame);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using WebGPURenderTarget renderTarget = new(defaultImage.Width, defaultImage.Height);
        Configuration nativeSurfaceConfiguration = Configuration.Default.Clone();
        nativeSurfaceConfiguration.SetDrawingBackend(nativeSurfaceBackend);

        using (DrawingCanvas firstCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeSurfaceConfiguration,
                   drawingOptions,
                   nativeSurfaceBackend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            DrawFirstFrame(firstCanvas);
            firstCanvas.Flush();
        }

        using (DrawingCanvas secondCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeSurfaceConfiguration,
                   drawingOptions,
                   nativeSurfaceBackend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            DrawSecondFrame(secondCanvas);
            secondCanvas.Flush();
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.ReadbackImage<TPixel>();
        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0F);
    }

    [WebGPUTheory]
    [WithBlankImage(96, 72, PixelTypes.Bgra32)]
    public void CopyPixelsFrom_WithWebGPUBackend_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false }
        };

        Rectangle sourceBounds = new(0, 0, 42, 30);
        Rectangle targetBounds = new(0, 0, 96, 72);

        static void DrawSource(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(0, 0, 42, 30));
            canvas.Fill(Brushes.Solid(Color.Yellow), new RectanglePolygon(7, 6, 18, 12));
        }

        static void DrawTargetBeforeCopy(DrawingCanvas canvas)
            => canvas.Fill(Brushes.Solid(Color.Blue), new RectanglePolygon(0, 0, 96, 72));

        static void DrawTargetAfterCopy(DrawingCanvas canvas)
            => canvas.Fill(Brushes.Solid(Color.Green), new RectanglePolygon(14, 10, 8, 6));

        void DrawDefault(DrawingCanvas targetCanvas)
        {
            using Image<TPixel> sourceImage = new(sourceBounds.Width, sourceBounds.Height);
            using (DrawingCanvas sourceCanvas = sourceImage.Frames.RootFrame.CreateCanvas(Configuration.Default, drawingOptions))
            {
                DrawSource(sourceCanvas);
            }

            DrawTargetBeforeCopy(targetCanvas);

            using (DrawingCanvas sourceCanvas = sourceImage.Frames.RootFrame.CreateCanvas(Configuration.Default, drawingOptions))
            {
                targetCanvas.CopyPixelsFrom(sourceCanvas, sourceBounds, new Point(0, 0));
            }

            DrawTargetAfterCopy(targetCanvas);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawDefault);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using WebGPURenderTarget sourceRenderTarget = new(WebGPUTextureFormat.Bgra8Unorm, sourceBounds.Width, sourceBounds.Height);
        using WebGPURenderTarget targetRenderTarget = sourceRenderTarget.CreateRenderTarget(targetBounds.Width, targetBounds.Height);
        Configuration nativeSurfaceConfiguration = Configuration.Default.Clone();
        nativeSurfaceConfiguration.SetDrawingBackend(nativeSurfaceBackend);

        using (DrawingCanvas sourceCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeSurfaceConfiguration,
                   drawingOptions,
                   nativeSurfaceBackend,
                   sourceRenderTarget.Bounds,
                   sourceRenderTarget.Surface,
                   sourceRenderTarget.Surface.TargetDescriptor))
        {
            DrawSource(sourceCanvas);
        }

        using (DrawingCanvas targetCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeSurfaceConfiguration,
                   drawingOptions,
                   nativeSurfaceBackend,
                   targetRenderTarget.Bounds,
                   targetRenderTarget.Surface,
                   targetRenderTarget.Surface.TargetDescriptor))

        using (DrawingCanvas sourceCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeSurfaceConfiguration,
                   drawingOptions,
                   nativeSurfaceBackend,
                   sourceRenderTarget.Bounds,
                   sourceRenderTarget.Surface,
                   sourceRenderTarget.Surface.TargetDescriptor))
        {
            DrawTargetBeforeCopy(targetCanvas);
            targetCanvas.CopyPixelsFrom(sourceCanvas, sourceBounds, new Point(0, 0));
            DrawTargetAfterCopy(targetCanvas);
        }

        using Image<TPixel> nativeSurfaceImage = targetRenderTarget.ReadbackImage<TPixel>();
        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
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
        void DrawAction(DrawingCanvas canvas)
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.019F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(420, 160, "White", PixelTypes.Rgba32)]
    public void DrawText_WithDropShadowWriteBack_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 60);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(24, 30)
        };

        DrawingOptions drawingOptions = new();
        string text = "Shadow";
        Brush brush = Brushes.Solid(Color.Black);

        // Content draws into an effect layer; on restore the canvas slots the tinted, blurred
        // silhouette beneath the untouched content at the shadow offset, then composites text plus
        // shadow onto the white background.
        WebGPUDropShadowLayerEffect shadow = new(new Point(10, 10), 4F, Color.Firebrick);

        Rectangle region = new(0, 0, 420, 160);
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.SaveLayer(new GraphicsOptions(), region, shadow);
            canvas.DrawText(textOptions, text, brush, null);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(420, 200, "White", PixelTypes.Rgba32)]
    public void DrawText_WithBlurLayerEffect_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 48);
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Black);

        // The layer isolates the blur: the text inside the effect layer softens on restore while
        // the text outside it stays sharp.
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.DrawText(new RichTextOptions(font) { Origin = new PointF(24, 20) }, "Sharp", brush, null);
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 90, 420, 100), new WebGPUGaussianBlurLayerEffect(4F));
            canvas.DrawText(new RichTextOptions(font) { Origin = new PointF(24, 100) }, "Blurred", brush, null);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(420, 160, "White", PixelTypes.Rgba32)]
    public void DrawText_WithPolygonLayerEffectRegion_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 54);
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Black);

        // A polygon region confines the effect: the text crosses the triangle boundary, so the
        // glyphs inside the triangle blur on restore while the rest stay sharp.
        PathBuilder pathBuilder = new();
        pathBuilder.AddLine(210, 10, 400, 150);
        pathBuilder.AddLine(400, 150, 20, 150);
        pathBuilder.AddLine(20, 150, 210, 10);
        pathBuilder.CloseAllFigures();
        IPath triangle = pathBuilder.Build();

        void DrawAction(DrawingCanvas canvas)
        {
            canvas.SaveLayer(new GraphicsOptions(), triangle, new BlurLayerEffect(4F));
            canvas.DrawText(new RichTextOptions(font) { Origin = new PointF(24, 40) }, "Blurred middle", brush, null);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(420, 160, "White", PixelTypes.Rgba32)]
    public void DrawText_WithInnerShadowLayerEffect_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 84);
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Gold);

        // The shadow hugs the glyphs' top and left inside edges, clipped to the content itself.
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 0, 420, 160), new WebGPUInnerShadowLayerEffect(new Point(4, 4), 3F, Color.Black));
            canvas.DrawText(new RichTextOptions(font) { Origin = new PointF(24, 20) }, "Inset", brush, null);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(420, 160, "White", PixelTypes.Rgba32)]
    public void DrawText_WithGlowLayerEffect_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 72);
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Black);

        // The glow spreads evenly beneath the glyphs in all directions.
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 0, 420, 160), new WebGPUGlowLayerEffect(6F, Color.Red));
            canvas.DrawText(new RichTextOptions(font) { Origin = new PointF(24, 30) }, "Glow", brush, null);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
    }

    [WebGPUTheory]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "blur")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "brightness")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "contrast")]
    [WithFile(TestImages.Png.Ducky, PixelTypes.Rgba32, "drop-shadow")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "grayscale")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "hue-rotate")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "invert")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "opacity")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "sepia")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "saturate")]
    [WithFile(TestImages.Jpeg.Baseline.Balloon, PixelTypes.Rgba32, "acrylic")]
    public void DrawText_WithBackdropFilterLayerEffects_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider, string filter)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // The CSS cases mirror the MDN backdrop-filter examples, with percentages as fractions and
        // blur radii as Gaussian sigma, on the same balloon photograph MDN uses. Drop-shadow runs
        // on the duck instead: the backdrop's silhouette needs transparency to cast into, and an
        // opaque photograph has none. Acrylic is the non-CSS frosted-glass material.
        BackdropLayerEffect effect = filter switch
        {
            "blur" => new WebGPUBackdropGaussianBlurLayerEffect(2F),
            "brightness" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateBrightnessFilter(0.6F)),
            "contrast" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateContrastFilter(0.4F)),
            "drop-shadow" => new WebGPUBackdropDropShadowLayerEffect(new Point(4, 4), 5F, Color.Black.WithAlpha(.7F)),
            "grayscale" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateGrayscaleBt709Filter(0.3F)),
            "hue-rotate" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateHueFilter(120F)),
            "invert" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateInvertFilter(0.7F)),
            "opacity" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateOpacityFilter(0.2F)),
            "sepia" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateSepiaFilter(0.9F)),
            "saturate" => new WebGPUBackdropColorMatrixLayerEffect(KnownFilterMatrices.CreateSaturateFilter(0.8F)),
            "acrylic" => new WebGPUBackdropAcrylicLayerEffect(2F, Color.PeachPuff.WithAlpha(0.35F)),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Black);

        using Image<TPixel> defaultImage = provider.GetImage();

        // The layout scales with the source image so the balloon and duck images share one scene:
        // a caption behind the panel, a rounded-rectangle panel through the IPath overload, and the
        // filter's own name as the label rendered sharp above the filtered backdrop.
        int width = defaultImage.Width;
        int height = defaultImage.Height;
        Font captionFont = TestFontUtilities.GetFont(TestFonts.OpenSans, height / 6F);
        Font labelFont = TestFontUtilities.GetFont(TestFonts.OpenSans, height / 11F);
        RectangleF panelBounds = new(width / 10F, height / 5F, width * 4F / 5F, height * 11F / 20F);
        RoundedRectanglePolygon panel = new(panelBounds, panelBounds.Height / 10F);

        // The backdrop effect filters whatever is already on the canvas beneath the panel region -
        // the photograph and the caption glyphs the panel overlaps - then the panel's label renders
        // sharp above the filtered result.
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.DrawText(new RichTextOptions(captionFont) { Origin = new PointF(width / 20F, height / 15F) }, "Backdrop", brush, null);
            canvas.SaveLayer(new GraphicsOptions(), panel, effect);
            canvas.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(panelBounds.X + 15F, panelBounds.Y + (panelBounds.Height / 2F)) }, filter, brush, null);
            canvas.Restore();
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

        DebugSaveBackendPair(provider, filter, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.03F);

        // Reference outputs are rendered on one adapter; other conforming adapters round
        // within one LSB across a small fraction of pixels.
        AssertBackendPairReferenceOutputs(provider, filter, defaultImage, nativeSurfaceImage, 0.01F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(420, 160, "White", PixelTypes.Rgba32)]
    public void DrawText_WithColorMatrixLayerEffect_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 72);
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Red);

        // The hue rotation recolours the layer's text; content outside the layer would keep its
        // original colour.
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 0, 420, 160), new WebGPUColorMatrixLayerEffect(KnownFilterMatrices.CreateHueFilter(180F)));
            canvas.DrawText(new RichTextOptions(font) { Origin = new PointF(24, 30) }, "Recoloured", brush, null);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
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
        void DrawAction(DrawingCanvas canvas) => canvas.DrawText(textOptions, text, brush, null);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(clearBrush);
            canvas.Flush();
            canvas.Save(drawingOptions);
            canvas.DrawText(textOptions, text, drawBrush, null);
            canvas.Restore();
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(c => c.Paint(clearOptions, DrawAction));

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using WebGPURenderTarget renderTarget = new(defaultImage.Width, defaultImage.Height);
        Configuration nativeSurfaceConfiguration = Configuration.Default.Clone();
        nativeSurfaceConfiguration.SetDrawingBackend(nativeSurfaceBackend);

        using (DrawingCanvas nativeSurfaceCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeSurfaceConfiguration,
                   clearOptions,
                   nativeSurfaceBackend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            DrawAction(nativeSurfaceCanvas);
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.ReadbackImage<TPixel>();
        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    private static void RenderWithDefaultBackend<TPixel>(Image<TPixel> image, DrawingOptions options, CanvasAction drawAction)
        where TPixel : unmanaged, IPixel<TPixel> => image.Mutate(c => c.Paint(options, drawAction));

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
            ? new RectanglePolygon(insetX, insetY, innerWidth, innerHeight)
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

    private static void DrawProcessScenario(DrawingCanvas canvas)
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
        Action<DrawingCanvas> drawAction,
        Image<TPixel> initialImage = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using WebGPURenderTarget renderTarget = new(width, height);
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);
        Rectangle targetBounds = new(0, 0, width, height);

        if (initialImage is not null)
        {
            using DrawingCanvas initialCanvas = WebGPUCanvasFactory.CreateCanvas(
                       configuration,
                       new DrawingOptions(),
                       backend,
                       renderTarget.Bounds,
                       renderTarget.Surface,
                       renderTarget.Surface.TargetDescriptor);

            initialCanvas.DrawImage(initialImage, initialImage.Bounds, targetBounds);
        }

        using (DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
                   configuration,
                   options,
                   backend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            drawAction(canvas);
        }

        return renderTarget.ReadbackImage<TPixel>();
    }

    private static Image<TPixel> RenderWithNativeSurfaceWebGpuBackend<TPixel>(
        int width,
        int height,
        WebGPUDrawingBackend backend,
        WebGPUTextureFormat format,
        DrawingOptions options,
        Action<DrawingCanvas> drawAction,
        Image<TPixel> initialImage)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using WebGPURenderTarget renderTarget = new(format, width, height);
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);
        Rectangle targetBounds = new(0, 0, width, height);

        using (DrawingCanvas initialCanvas = WebGPUCanvasFactory.CreateCanvas(
                   configuration,
                   new DrawingOptions(),
                   backend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            initialCanvas.DrawImage(initialImage, initialImage.Bounds, targetBounds);
        }

        using (DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
                   configuration,
                   options,
                   backend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            drawAction(canvas);
        }

        return renderTarget.ReadbackImage<TPixel>();
    }

    private static void DebugSaveBackendPair<TPixel>(
        TestImageProvider<TPixel> provider,
        string testName,
        Image<TPixel> defaultImage,
        Image<TPixel> nativeSurfaceImage)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        string defaultDetails = CreateBackendPairDetails(testName, "Default");
        string nativeSurfaceDetails = CreateBackendPairDetails(testName, "WebGPU_NativeSurface");

        defaultImage.DebugSave(
            provider,
            defaultDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        nativeSurfaceImage.DebugSave(
            provider,
            nativeSurfaceDetails,
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
        string defaultDetails = CreateBackendPairDetails(testName, "Default");
        string nativeSurfaceDetails = CreateBackendPairDetails(testName, "WebGPU_NativeSurface");

        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(tolerantPercentage);
        defaultImage.CompareToReferenceOutput(
            tolerantComparer,
            provider,
            defaultDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        nativeSurfaceImage.CompareToReferenceOutput(
            tolerantComparer,
            provider,
            nativeSurfaceDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    private static string CreateBackendPairDetails(string testName, string role)
        => string.IsNullOrWhiteSpace(testName) ? role : $"{testName}_{role}";

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
        void DrawAction(DrawingCanvas canvas) => canvas.Draw(pen, path);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.015F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(1024, 800, "Black", PixelTypes.Rgba32)]
    public void ColorPickerSliderThumb_AvaloniaTemplate_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = CreateAvaloniaOptions(IntersectionRule.EvenOdd, true);

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Black));
            DrawThumb(canvas, new PointF(642F, 335F));
            DrawThumb(canvas, new PointF(875F, 359F));
            DrawThumb(canvas, new PointF(875F, 383F));
            DrawThumb(canvas, new PointF(875F, 407F));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarityInRegion(defaultImage, nativeSurfaceImage, new Rectangle(638, 331, 265, 104), 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);

        static void DrawThumb(DrawingCanvas canvas, PointF origin)
        {
            const float thumbSize = 24F;
            const float thumbBorderThickness = 5F;

            RectanglePolygon thumbClip = new(origin.X, origin.Y, thumbSize, thumbSize);
            _ = canvas.Save(CreateAvaloniaOptions(IntersectionRule.EvenOdd, false));
            canvas.Clip(thumbClip);

            RectangleF thumbBorderRectangle = new(origin.X + 2.5F, origin.Y + 2.5F, 19F, 19F);
            RoundedRectanglePolygon thumbBorder = new(thumbBorderRectangle, 9.5F);
            EllipsePolygon thumbEllipse = new(new PointF(origin.X + 12F, origin.Y + 12F), new SizeF(thumbSize, thumbSize));

            DrawAvaloniaFill(canvas, Brushes.Solid(Color.Transparent), thumbBorder);
            DrawAvaloniaStroke(canvas, Pens.Solid(Color.White, thumbBorderThickness), thumbBorder);
            DrawAvaloniaFill(canvas, Brushes.Solid(Color.Transparent), thumbEllipse);
            DrawAvaloniaStroke(canvas, Pens.Solid(Color.White, 1F), thumbEllipse);

            canvas.Restore();
        }

        static void DrawAvaloniaFill(DrawingCanvas canvas, Brush brush, IPath path)
        {
            _ = canvas.Save(CreateAvaloniaOptions(IntersectionRule.EvenOdd, true));
            canvas.Fill(brush, path);
            canvas.Restore();
        }

        static void DrawAvaloniaStroke(DrawingCanvas canvas, Pen pen, IPath path)
        {
            _ = canvas.Save(CreateAvaloniaOptions(IntersectionRule.EvenOdd, true));
            canvas.Draw(pen, path);
            canvas.Restore();
        }

        static DrawingOptions CreateAvaloniaOptions(IntersectionRule fillRule, bool antialias)
            => new()
            {
                GraphicsOptions = new GraphicsOptions { Antialias = antialias },
                IntersectionRule = fillRule
            };
    }

    [WebGPUTheory]
    [WithSolidFilledImages(300, 220, "Gray", PixelTypes.Rgba32)]
    public void BlurredBoxShadow_DifferenceClip_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true },
            IntersectionRule = IntersectionRule.EvenOdd
        };

        const float sigma = 4F;

        void DrawThumbWithShadow(DrawingCanvas canvas, PointF center)
        {
            EllipsePolygon contentPath = new(center, new SizeF(24F, 24F));
            EllipsePolygon shadowPath = new(new PointF(center.X, center.Y + 3F), new SizeF(24F, 24F));
            Rectangle layerBounds = new(
                (int)(center.X - 12F - 16F),
                (int)(center.Y - 12F - 16F),
                24 + 32,
                24 + 32 + 6);

            // Blurred outer box shadow: difference clip -> layer -> fill -> gaussian blur -> composite.
            _ = canvas.Save(drawingOptions);
            canvas.Clip(ClipOperation.Difference, contentPath);
            _ = canvas.SaveLayer(new GraphicsOptions(), layerBounds);
            canvas.Fill(Brushes.Solid(Color.Black.WithAlpha(0.55F)), shadowPath);
            canvas.Apply(layerBounds, ctx => ctx.GaussianBlur(sigma));
            canvas.Restore();
            canvas.Restore();

            canvas.Fill(Brushes.Solid(Color.White), contentPath);
            canvas.Fill(Brushes.Solid(Color.Red), new EllipsePolygon(center, new SizeF(8F, 8F)));
        }

        void DrawAction(DrawingCanvas canvas)
        {
            DrawThumbWithShadow(canvas, new PointF(70F, 60F));
            DrawThumbWithShadow(canvas, new PointF(150F, 60F));
            DrawThumbWithShadow(canvas, new PointF(230F, 60F));
            DrawThumbWithShadow(canvas, new PointF(110F, 150F));
            DrawThumbWithShadow(canvas, new PointF(190F, 150F));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32, LineCap.Square)]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32, LineCap.Round)]
    public void DrawPath_PointStroke_LineCap_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider, LineCap lineCap)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        PathBuilder pathBuilder = new();
        pathBuilder.AddLine(new PointF(128, 128), new PointF(128, 128));

        IPath path = pathBuilder.Build();
        Pen pen = new SolidPen(new PenOptions(Color.DarkBlue, 48F)
        {
            StrokeOptions = new StrokeOptions { LineCap = lineCap }
        });

        void DrawAction(DrawingCanvas canvas) => canvas.Draw(pen, path);

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

        float referenceTolerance = lineCap == LineCap.Square ? 0.0016F : 0.0003F;

        DebugSaveBackendPair(provider, $"{lineCap}", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, $"{lineCap}", defaultImage, nativeSurfaceImage, referenceTolerance);
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

        void DrawAction(DrawingCanvas canvas) => canvas.Draw(pen, path);
        IPath outline = path.GenerateOutline(pen.StrokeWidth, pen.StrokeOptions);
        void DrawReference(DrawingCanvas canvas) => canvas.Fill(pen.StrokeFill, outline);

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
            $"{lineJoin}",
            defaultComparisonImage,
            nativeSurfaceComparisonImage);

        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.015F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"{lineJoin}",
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

        void DrawAction(DrawingCanvas canvas) => canvas.Draw(pen, path);
        IPath outline = path.GenerateOutline(pen.StrokeWidth, pen.StrokeOptions);
        void DrawReference(DrawingCanvas canvas) => canvas.Fill(pen.StrokeFill, outline);

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
            $"{lineCap}",
            defaultComparisonImage,
            nativeSurfaceComparisonImage);

        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(
            provider,
            $"{lineCap}",
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
        void DrawAction(DrawingCanvas canvas)
        {
            for (int i = 0; i < 20; i++)
            {
                float x = 20 + (i * 24);
                float y = 20 + (i * 22);
                canvas.Fill(brush, new RectanglePolygon(x, y, 80, 60));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(256, 256, "White", PixelTypes.Rgba32)]
    public void FillPath_EvenOddRule_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true },
            IntersectionRule = IntersectionRule.EvenOdd
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
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, path);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);

        // EvenOdd with same winding inner contour should create a hole at center.
        Assert.Equal(defaultImage[128, 128], nativeSurfaceImage[128, 128]);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, denseGrid);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void OrderedPlan_GroupsOnlyDependencyIndependentApplies(bool offsetReadIntersectsEarlierWrite, int expectedFirstGroupCount)
    {
        using WebGPUDrawingBackend backend = new();
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);
        DrawingOptions drawingOptions = new();

        using WebGPURenderTarget renderTarget = new(96, 64);
        using DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
            configuration,
            drawingOptions,
            backend,
            renderTarget.Bounds,
            renderTarget.Surface,
            renderTarget.Surface.TargetDescriptor);

        canvas.Fill(Brushes.Solid(Color.White));
        canvas.Apply(new Rectangle(8, 8, 20, 20), context => context.Invert());

        if (offsetReadIntersectsEarlierWrite)
        {
            // The second write is disjoint, but its non-zero write-back offset moves the source
            // read onto the first write. The planner must therefore preserve a separate barrier.
            canvas.Apply(
                new Rectangle(8, 8, 20, 20),
                context => context.Invert(),
                new GraphicsOptions(),
                new Point(42, 0));
        }
        else
        {
            canvas.Apply(new Rectangle(50, 8, 20, 20), context => context.Invert());
        }

        using DrawingBackendScene scene = canvas.CreateScene();
        WebGPUDrawingBackendScene webGPUScene = Assert.IsType<WebGPUDrawingBackendScene>(scene);
        ReadOnlySpan<WebGPUSceneOperation> operations = webGPUScene.EncodedScene.OrderedOperations;
        int firstApplyIndex = 0;

        while (operations[firstApplyIndex].Kind != WebGPUSceneOperationKind.Apply)
        {
            firstApplyIndex++;
        }

        Assert.Equal(expectedFirstGroupCount, operations[firstApplyIndex].ApplyGroupCount);
        Assert.Equal(0, operations[firstApplyIndex].ApplyIndex);
        Assert.True(operations[firstApplyIndex].PendingStatusCapacity > 0);

        WebGPUSceneOperation secondApply = operations[firstApplyIndex + 1];
        Assert.Equal(WebGPUSceneOperationKind.Apply, secondApply.Kind);
        Assert.Equal(1, secondApply.ApplyIndex);
        Assert.Equal(offsetReadIntersectsEarlierWrite ? 1 : 0, secondApply.ApplyGroupCount);
    }

    [WebGPUTheory]
    [WithBlankImage(128, 96, PixelTypes.Rgba32)]
    public void ConsecutiveIndependentApplies_ShareReadbackAndMatchDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(8, 12, 40, 36));
            canvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(76, 44, 36, 40));

            // These disjoint source/write rectangles are encoded as one two-image barrier while
            // their processors and draw ranges remain in their original order.
            canvas.Apply(new Rectangle(8, 12, 40, 36), context => context.Invert());
            canvas.Apply(new Rectangle(76, 44, 36, 40), context => context.Invert());
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<TPixel> webGPUImage = RenderWithNativeSurfaceWebGpuBackend<TPixel>(
            defaultImage.Width,
            defaultImage.Height,
            backend,
            drawingOptions,
            DrawAction);

        DebugSaveBackendPair(provider, null, defaultImage, webGPUImage);
        AssertBackendPairSimilarity(defaultImage, webGPUImage, 0.005F);
    }

    [WebGPUTheory]
    [WithBlankImage(96, 64, PixelTypes.Rgba32)]
    public void ConsecutiveClippedApplies_ShareReadbackAndMatchDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(0, 8, 16, 24));
            canvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(80, 8, 16, 24));

            // Both reads extend beyond opposite target edges. Their packed rows retain the
            // destination offsets needed to reconstruct the full processor images.
            canvas.Apply(new Rectangle(-8, 8, 24, 24), context => context.Invert());
            canvas.Apply(new Rectangle(80, 8, 24, 24), context => context.Invert());
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<TPixel> webGPUImage = RenderWithNativeSurfaceWebGpuBackend<TPixel>(
            defaultImage.Width,
            defaultImage.Height,
            backend,
            drawingOptions,
            DrawAction);

        DebugSaveBackendPair(provider, null, defaultImage, webGPUImage);
        AssertBackendPairSimilarity(defaultImage, webGPUImage, 0.005F);
    }

    [WebGPUFact]
    public void OrderedPlan_SplitsApplyGroupsAtRenderAndLayerBoundaries()
    {
        using WebGPUDrawingBackend backend = new();
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);

        using WebGPURenderTarget renderTarget = new(96, 64);
        using DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
            configuration,
            new DrawingOptions(),
            backend,
            renderTarget.Bounds,
            renderTarget.Surface,
            renderTarget.Surface.TargetDescriptor);

        canvas.Fill(Brushes.Solid(Color.White));
        canvas.Apply(new Rectangle(4, 4, 16, 16), context => context.Invert());
        canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(28, 4, 16, 16));
        canvas.Apply(new Rectangle(52, 4, 16, 16), context => context.Invert());
        canvas.SaveLayer(new GraphicsOptions(), new Rectangle(4, 28, 40, 28));
        canvas.Apply(new Rectangle(4, 28, 16, 16), context => context.Invert());
        canvas.Restore();

        using DrawingBackendScene scene = canvas.CreateScene();
        WebGPUDrawingBackendScene webGPUScene = Assert.IsType<WebGPUDrawingBackendScene>(scene);
        ReadOnlySpan<WebGPUSceneOperation> operations = webGPUScene.EncodedScene.OrderedOperations;
        int applyCount = 0;
        bool sawBeginLayer = false;
        bool sawEndLayer = false;

        for (int i = 0; i < operations.Length; i++)
        {
            WebGPUSceneOperation operation = operations[i];
            sawBeginLayer |= operation.Kind == WebGPUSceneOperationKind.BeginLayer;
            sawEndLayer |= operation.Kind == WebGPUSceneOperationKind.EndLayer;

            if (operation.Kind == WebGPUSceneOperationKind.Apply)
            {
                Assert.Equal(1, operation.ApplyGroupCount);
                applyCount++;
            }
        }

        Assert.Equal(3, applyCount);
        Assert.True(sawBeginLayer);
        Assert.True(sawEndLayer);
    }

    [WebGPUTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConsecutiveIndependentApplies_ProcessorExceptionCommitsEarlierResults(int throwingApplyIndex)
    {
        using WebGPUDrawingBackend backend = new();
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);
        DrawingOptions drawingOptions = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };
        InvalidOperationException expectedException = new("Expected processor failure.");

        using WebGPURenderTarget renderTarget = new(128, 64);
        DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
            configuration,
            drawingOptions,
            backend,
            renderTarget.Bounds,
            renderTarget.Surface,
            renderTarget.Surface.TargetDescriptor);

        canvas.Fill(Brushes.Solid(Color.White));
        canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(8, 8, 24, 24));
        canvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(56, 8, 24, 24));
        canvas.Fill(Brushes.Solid(Color.Lime), new Rectangle(96, 8, 24, 24));

        Rectangle[] applyRectangles =
        [
            new Rectangle(8, 8, 24, 24),
            new Rectangle(56, 8, 24, 24),
            new Rectangle(96, 8, 24, 24)
        ];

        for (int i = 0; i < applyRectangles.Length; i++)
        {
            int applyIndex = i;
            canvas.Apply(
                applyRectangles[i],
                context =>
                {
                    if (applyIndex == throwingApplyIndex)
                    {
                        throw expectedException;
                    }

                    context.Invert();
                });
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(canvas.Dispose);
        Assert.Same(expectedException, exception);

        using Image<Rgba32> image = renderTarget.ReadbackImage<Rgba32>();
        Rgba32[] originalColors =
        [
            Color.Red.ToPixel<Rgba32>(),
            Color.Blue.ToPixel<Rgba32>(),
            Color.Lime.ToPixel<Rgba32>()
        ];
        Rgba32[] invertedColors =
        [
            Color.Cyan.ToPixel<Rgba32>(),
            Color.Yellow.ToPixel<Rgba32>(),
            Color.Magenta.ToPixel<Rgba32>()
        ];

        for (int i = 0; i < applyRectangles.Length; i++)
        {
            Rgba32 expectedColor = i < throwingApplyIndex ? invertedColors[i] : originalColors[i];
            Assert.Equal(expectedColor, image[applyRectangles[i].X + 8, applyRectangles[i].Y + 8]);
        }

        Assert.Equal(Color.White.ToPixel<Rgba32>(), image[44, 44]);
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

        void DrawAction(DrawingCanvas canvas)
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);

        // Reference outputs are rendered on one adapter; other conforming adapters round
        // within one LSB across a small fraction of pixels.
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.01F);
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
        RectanglePolygon rect1 = new(20, 20, 120, 80);
        RectanglePolygon rect2 = new(160, 100, 120, 80);
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(redBrush, rect1);
            canvas.Flush();
            canvas.Fill(blueBrush, rect2);
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(c => c.Paint(drawingOptions, DrawAction));

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using WebGPURenderTarget renderTarget = new(defaultImage.Width, defaultImage.Height);
        Configuration nativeConfig = Configuration.Default.Clone();
        nativeConfig.SetDrawingBackend(nativeSurfaceBackend);

        using (DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeConfig,
                   drawingOptions,
                   nativeSurfaceBackend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            DrawAction(canvas);
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.ReadbackImage<TPixel>();
        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(160, 120, "White", PixelTypes.Rgba32)]
    public void RetainedScene_MixedWithApplyBarriersAndRegularCommands_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false }
        };

        static void DrawRetainedScene(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(32, 22, 64, 52));
            canvas.Flush();
            canvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(72, 46, 48, 48));
        }

        static void DrawInlineFlow(DrawingCanvas canvas)
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Yellow), new Rectangle(0, 0, 48, 120));
            canvas.Fill(Brushes.Solid(Color.Purple), new Rectangle(16, 16, 24, 24));

            // The marker gives the pre-scene Apply barrier non-flat pixels for Invert to modify.
            canvas.Apply(new Rectangle(8, 8, 44, 44), ctx => ctx.Invert());
            DrawRetainedScene(canvas);
            canvas.Fill(Brushes.Solid(Color.Black), new Rectangle(40, 30, 24, 24));

            // The inline reference keeps the same ordering without retaining the middle scene.
            canvas.Apply(new Rectangle(32, 22, 88, 72), ctx => ctx.GaussianBlur(6F));
            DrawRetainedScene(canvas);
            canvas.Fill(Brushes.Solid(Color.Green), new Rectangle(88, 58, 44, 28));
        }

        static void DrawRetainedFlow(DrawingCanvas canvas, DrawingBackendScene retainedScene)
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Yellow), new Rectangle(0, 0, 48, 120));
            canvas.Fill(Brushes.Solid(Color.Purple), new Rectangle(16, 16, 24, 24));

            // The retained scene must replay between barriers exactly where RenderScene records it.
            canvas.Apply(new Rectangle(8, 8, 44, 44), ctx => ctx.Invert());
            canvas.RenderScene(retainedScene);
            canvas.Fill(Brushes.Solid(Color.Black), new Rectangle(40, 30, 24, 24));
            canvas.Apply(new Rectangle(32, 22, 88, 72), ctx => ctx.GaussianBlur(6F));
            canvas.RenderScene(retainedScene);
            canvas.Fill(Brushes.Solid(Color.Green), new Rectangle(88, 58, 44, 28));
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(c => c.Paint(drawingOptions, DrawInlineFlow));

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        Configuration nativeConfig = Configuration.Default.Clone();
        nativeConfig.SetDrawingBackend(nativeSurfaceBackend);

        using WebGPURenderTarget sceneRenderTarget = new(defaultImage.Width, defaultImage.Height);
        using DrawingCanvas nativeSceneCanvas = WebGPUCanvasFactory.CreateCanvas(
            nativeConfig,
            drawingOptions,
            nativeSurfaceBackend,
            sceneRenderTarget.Bounds,
            sceneRenderTarget.Surface,
            sceneRenderTarget.Surface.TargetDescriptor);

        // Create the scene through the WebGPU backend so the test covers retained encoding and replay.
        DrawRetainedScene(nativeSceneCanvas);

        using DrawingBackendScene nativeScene = nativeSceneCanvas.CreateScene();
        using WebGPURenderTarget renderTarget = new(defaultImage.Width, defaultImage.Height);
        using (DrawingCanvas nativeCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeConfig,
                   drawingOptions,
                   nativeSurfaceBackend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            DrawRetainedFlow(nativeCanvas, nativeScene);
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.ReadbackImage<TPixel>();
        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(160, 120, "White", PixelTypes.Rgba32)]
    public void RetainedScene_WithLayerCommands_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false }
        };

        static void DrawRetainedScene(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(24, 20, 92, 64));

            // Layer boundaries inside the retained scene must survive scene creation and replay.
            canvas.SaveLayer(
                new GraphicsOptions { BlendPercentage = 0.65F },
                new Rectangle(36, 26, 76, 52));

            canvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(54, 34, 76, 46));
            canvas.Restore();
            canvas.Fill(Brushes.Solid(Color.Green), new Rectangle(92, 62, 30, 28));
        }

        static void DrawInlineFlow(DrawingCanvas canvas)
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Orange), new Rectangle(0, 0, 52, 120));

            // The default reference draws the retained-scene contents inline at the same position.
            DrawRetainedScene(canvas);
            canvas.Fill(Brushes.Solid(Color.Black), new Rectangle(122, 8, 16, 96));
        }

        static void DrawRetainedFlow(DrawingCanvas canvas, DrawingBackendScene retainedScene)
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Orange), new Rectangle(0, 0, 52, 120));

            // RenderScene is the only difference from the inline reference above.
            canvas.RenderScene(retainedScene);
            canvas.Fill(Brushes.Solid(Color.Black), new Rectangle(122, 8, 16, 96));
        }

        using Image<TPixel> defaultImage = provider.GetImage();
        defaultImage.Mutate(c => c.Paint(drawingOptions, DrawInlineFlow));

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        Configuration nativeConfig = Configuration.Default.Clone();
        nativeConfig.SetDrawingBackend(nativeSurfaceBackend);

        using WebGPURenderTarget sceneRenderTarget = new(defaultImage.Width, defaultImage.Height);
        using DrawingCanvas nativeSceneCanvas = WebGPUCanvasFactory.CreateCanvas(
            nativeConfig,
            drawingOptions,
            nativeSurfaceBackend,
            sceneRenderTarget.Bounds,
            sceneRenderTarget.Surface,
            sceneRenderTarget.Surface.TargetDescriptor);

        // Create the scene through the WebGPU backend so retained layer commands are encoded.
        DrawRetainedScene(nativeSceneCanvas);

        using DrawingBackendScene nativeScene = nativeSceneCanvas.CreateScene();
        using WebGPURenderTarget renderTarget = new(defaultImage.Width, defaultImage.Height);
        using (DrawingCanvas nativeCanvas = WebGPUCanvasFactory.CreateCanvas(
                   nativeConfig,
                   drawingOptions,
                   nativeSurfaceBackend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            DrawRetainedFlow(nativeCanvas, nativeScene);
        }

        using Image<TPixel> nativeSurfaceImage = renderTarget.ReadbackImage<TPixel>();
        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, ellipse);

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
        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.045F);
        AssertBackendPairReferenceOutputs(
            provider,
            null,
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = new LinearGradientBrush(
            new PointF(64, 64),
            new PointF(128, 128),
            GradientRepetitionMode.Repeat,
            new ColorStop(0, Color.Yellow),
            new ColorStop(1, Color.Purple));

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUFact]
    public void FillPath_WithZeroLengthLinearGradient_MatchesDefaultEndColor()
    {
        Rgba32 background = new(17, 43, 89, 255);
        PointF endpoint = new(8, 8);
        Brush brush = new LinearGradientBrush(
            endpoint,
            endpoint,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 16, 16));

        using Image<Rgba32> defaultImage = new(16, 16, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(16, 16, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(16, 16, backend, drawingOptions, DrawAction, initialImage);

        // A zero-length linear axis is defined at t=1, so the end stop fills every pixel.
        Rgba32 expected = Color.Blue.ToPixel<Rgba32>();
        Assert.Equal(expected, defaultImage[0, 0]);
        Assert.Equal(defaultImage[0, 0], actual[0, 0]);
    }

    [WebGPUFact]
    public void FillPath_WithDontFillGradients_ComposesTransparentOutsideGradient()
    {
        Rgba32 background = new(17, 43, 89, 255);
        (Brush Brush, Point Sample)[] cases =
        [
            (
                new LinearGradientBrush(
                    new PointF(4, 8),
                    new PointF(12, 8),
                    GradientRepetitionMode.DontFill,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Blue)),
                new Point(0, 8)),
            (
                new RadialGradientBrush(
                    new PointF(8, 8),
                    3F,
                    GradientRepetitionMode.DontFill,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Blue)),
                new Point(0, 0)),
            (
                new EllipticGradientBrush(
                    new PointF(8, 8),
                    new PointF(12, 8),
                    0.5F,
                    GradientRepetitionMode.DontFill,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Blue)),
                new Point(0, 0)),
            (
                new SweepGradientBrush(
                    new PointF(8.5F, 8.5F),
                    0F,
                    90F,
                    GradientRepetitionMode.DontFill,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Blue)),
                new Point(8, 15))
        ];
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        static void DrawAction(DrawingCanvas canvas, Brush brush) => canvas.Fill(brush, new RectanglePolygon(0, 0, 16, 16));

        using WebGPUDrawingBackend backend = new();
        foreach ((Brush brush, Point sample) in cases)
        {
            using Image<Rgba32> defaultImage = new(16, 16, background);
            RenderWithDefaultBackend(defaultImage, drawingOptions, canvas => DrawAction(canvas, brush));

            using Image<Rgba32> initialImage = new(16, 16, background);
            using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(
                16,
                16,
                backend,
                drawingOptions,
                canvas => DrawAction(canvas, brush),
                initialImage);

            // DontFill returns a transparent brush sample outside the gradient. Src still
            // composes that sample, replacing a fully covered backdrop with transparency.
            Assert.Equal(default, defaultImage[sample.X, sample.Y]);
            Assert.Equal(defaultImage[sample.X, sample.Y], actual[sample.X, sample.Y]);
        }
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = new RadialGradientBrush(
            new PointF(128, 128),
            100F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.White),
            new ColorStop(1, Color.DarkRed));

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.029F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = new RadialGradientBrush(
            new PointF(100, 100),
            20F,
            new PointF(128, 128),
            110F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Yellow),
            new ColorStop(1, Color.Navy));

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.034F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUFact]
    public void FillPath_WithSwappedRadialDontFill_ComposesTransparentOutsideGradient()
    {
        Rgba32 background = new(17, 43, 89, 255);
        Brush brush = new RadialGradientBrush(
            new PointF(16, 16),
            10F,
            new PointF(18, 16),
            0F,
            GradientRepetitionMode.DontFill,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Lime));
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 32, 32));

        using Image<Rgba32> defaultImage = new(32, 32, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(32, 32, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(32, 32, backend, drawingOptions, DrawAction, initialImage);

        // Radius1 == 0 makes the conical evaluator swap its circles. The restored parameter
        // remains outside the finite cone, so DontFill supplies a transparent Src sample.
        Assert.Equal(default, defaultImage[0, 0]);
        Assert.Equal(defaultImage[0, 0], actual[0, 0]);
        Assert.NotEqual(background, actual[17, 16]);
    }

    [WebGPUTheory]
    [InlineData(GradientRepetitionMode.Repeat)]
    [InlineData(GradientRepetitionMode.Reflect)]
    public void FillPath_WithSwappedRadialRepetition_MatchesDefaultOutput(GradientRepetitionMode repetitionMode)
    {
        Rgba32 background = new(17, 43, 89, 255);
        Brush brush = new RadialGradientBrush(
            new PointF(16, 16),
            10F,
            new PointF(18, 16),
            0F,
            repetitionMode,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Lime));
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 32, 32));

        using Image<Rgba32> defaultImage = new(32, 32, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(32, 32, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(32, 32, backend, drawingOptions, DrawAction, initialImage);

        // The sampled point lies before the original start circle after the shader's
        // canonical circle swap. Repetition must therefore see the restored negative t.
        Rgba32 expected = Color.Red.ToPixel<Rgba32>();
        Assert.Equal(expected, defaultImage[0, 0]);
        Assert.Equal(defaultImage[0, 0], actual[0, 0]);
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = new EllipticGradientBrush(
            new PointF(128, 128),
            new PointF(228, 128),
            0.6F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Cyan),
            new ColorStop(1, Color.Magenta));

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.014F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [InlineData(GradientRepetitionMode.None, true)]
    [InlineData(GradientRepetitionMode.Repeat, true)]
    [InlineData(GradientRepetitionMode.Reflect, true)]
    [InlineData(GradientRepetitionMode.DontFill, true)]
    [InlineData(GradientRepetitionMode.None, false)]
    [InlineData(GradientRepetitionMode.Repeat, false)]
    [InlineData(GradientRepetitionMode.Reflect, false)]
    [InlineData(GradientRepetitionMode.DontFill, false)]
    public void FillPath_WithDegenerateEllipticGradient_MatchesDefaultOutput(
        GradientRepetitionMode repetitionMode,
        bool zeroReferenceAxis)
    {
        Rgba32 background = new(17, 43, 89, 255);
        PointF center = new(3.5F, 3.5F);
        PointF referenceAxisEnd = zeroReferenceAxis ? center : new PointF(5.5F, 5.5F);
        float axisRatio = zeroReferenceAxis ? 1F : 0F;
        Brush brush = new EllipticGradientBrush(
            center,
            referenceAxisEnd,
            axisRatio,
            repetitionMode,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Lime));
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 8, 8));

        using Image<Rgba32> defaultImage = new(8, 8, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(8, 8, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(8, 8, backend, drawingOptions, DrawAction, initialImage);

        Rgba32 transparent = default;
        Rgba32 lastStop = Color.Lime.ToPixel<Rgba32>();
        bool fillsOutsideCollapsedAxes = repetitionMode != GradientRepetitionMode.DontFill;

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                // The CPU equations produce NaN where a zero-radius division has a zero
                // numerator. With a point ellipse that is either local axis; with a line
                // ellipse it is the collapsed secondary axis, which is x == y here.
                bool isUndefined = zeroReferenceAxis ? x == 3 || y == 3 : x == y;
                Rgba32 expected = !isUndefined && fillsOutsideCollapsedAxes ? lastStop : transparent;

                Assert.Equal(expected, defaultImage[x, y]);
                Assert.Equal(defaultImage[x, y], actual[x, y]);
            }
        }
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

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.061F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = new SweepGradientBrush(
            new PointF(128, 128),
            45F,
            270F,
            GradientRepetitionMode.Reflect,
            new ColorStop(0, Color.Orange),
            new ColorStop(1, Color.Teal));

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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
            null,
            defaultImage,
            nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.029F);
        AssertBackendPairReferenceOutputs(
            provider,
            null,
            defaultImage,
            nativeSurfaceImage,
            tolerantPercentage: 0.0280F);
    }

    [WebGPUFact]
    public void FillPath_WithTinySweepDontFill_MatchesDefaultOutsideSweep()
    {
        Rgba32 background = new(17, 43, 89, 255);
        Brush brush = new SweepGradientBrush(
            new PointF(16.5F, 16.5F),
            0F,
            0.0001F,
            GradientRepetitionMode.DontFill,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 32, 32));

        using Image<Rgba32> defaultImage = new(32, 32, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(32, 32, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(32, 32, backend, drawingOptions, DrawAction, initialImage);

        // A quarter-turn direction lies outside this real but extremely small sweep.
        // Comparing the epsilon in turns would misclassify the interval as a full circle.
        Assert.Equal(background, defaultImage[16, 8]);
        Assert.Equal(defaultImage[16, 8], actual[16, 8]);
        Assert.NotEqual(background, actual[16, 16]);
    }

    [WebGPUFact]
    public void FillPath_WithSweepDontFillAtCenter_MatchesDefaultFirstColor()
    {
        Rgba32 background = new(17, 43, 89, 255);
        Brush brush = new SweepGradientBrush(
            new PointF(8.5F, 8.5F),
            90F,
            180F,
            GradientRepetitionMode.DontFill,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 16, 16));

        using Image<Rgba32> defaultImage = new(16, 16, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(16, 16, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(16, 16, backend, drawingOptions, DrawAction, initialImage);

        // The center has no angle and is defined as t=0 rather than being derived
        // from the configured angular interval.
        Rgba32 expected = Color.Red.ToPixel<Rgba32>();
        Assert.Equal(expected, defaultImage[8, 8]);
        Assert.Equal(defaultImage[8, 8], actual[8, 8]);
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
        RectanglePolygon localPolygon = new(12, 10, 216, 156);
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

        void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(persistedBrush, persistedShape);
            canvas.Flush();

            using DrawingCanvas regionCanvas = canvas.CreateRegion(region);
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.013F);
    }

    [WebGPUFact]
    public void FillPath_WithTriangularPathGradientOnEdgeExtension_MatchesDefaultOutput()
    {
        Rgba32 background = new(17, 43, 89, 255);
        Brush brush = new PathGradientBrush(
        [
            new PointF(0.5F, 0.5F),
            new PointF(2.5F, 0.5F),
            new PointF(0.5F, 2.5F)
        ],
        [
            Color.Red,
            Color.Lime,
            Color.Blue
        ]);
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 4, 3));

        using Image<Rgba32> defaultImage = new(4, 3, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(4, 3, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(4, 3, backend, drawingOptions, DrawAction, initialImage);

        // The CPU sign-product test accepts this extension of the triangle's horizontal edge.
        // Preserve that contract exactly instead of substituting a conventional inside test.
        Rgba32 expected = Color.Lime.ToPixel<Rgba32>();
        Assert.Equal(expected, defaultImage[3, 0]);
        Assert.Equal(defaultImage[3, 0], actual[3, 0]);
    }

    [WebGPUFact]
    public void FillPath_WithNearParallelPathGradientEdge_MatchesDefaultNoIntersection()
    {
        Rgba32 background = new(17, 43, 89, 255);
        Brush brush = new PathGradientBrush(
        [
            new PointF(12.5F, 8.4999875F),
            new PointF(14.5F, 8.5000125F),
            new PointF(-16F, 8.5F),
            new PointF(4F, 8.5F),
            new PointF(4F, 8.5F),
            new PointF(5F, 8.5F)
        ],
        [
            Color.Lime,
            Color.Lime,
            Color.Lime,
            Color.Lime,
            Color.Lime,
            Color.Lime
        ],
        Color.Lime);

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 32, 16));

        using Image<Rgba32> defaultImage = new(32, 16, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(32, 16, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(32, 16, backend, drawingOptions, DrawAction, initialImage);

        // The ray is 20 pixels long and the first edge rises by 0.000025 pixels, so their
        // cross product is about 0.0005. The CPU rejects that value inside its +/-0.001
        // parallel window; the former shader window of +/-0.000001 incorrectly accepted it.
        Rgba32 expected = Color.Transparent.ToPixel<Rgba32>();
        Assert.Equal(expected, defaultImage[8, 8]);
        Assert.Equal(defaultImage[8, 8], actual[8, 8]);
    }

    [WebGPUFact]
    public void FillPath_WithPathGradientIntersectionJustBehindSample_MatchesDefaultOutput()
    {
        Rgba32 background = new(17, 43, 89, 255);
        Brush brush = new PathGradientBrush(
        [
            new PointF(1F, 0.5001F),
            new PointF(7F, 0.5001F),
            new PointF(7F, 7F),
            new PointF(1F, 7F)
        ],
        [
            Color.Lime,
            Color.Lime,
            Color.Lime,
            Color.Lime
        ],
        Color.Lime);

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 8, 8));

        using Image<Rgba32> defaultImage = new(8, 8, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(8, 8, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(8, 8, backend, drawingOptions, DrawAction, initialImage);

        // The sample center is 0.0001 pixels above the first edge. Its ray intersection
        // parameter is about -0.000023, inside the CPU's strict (-0.001, 1.001) window.
        Rgba32 expected = Color.Lime.ToPixel<Rgba32>();
        Assert.Equal(expected, defaultImage[4, 0]);
        Assert.Equal(defaultImage[4, 0], actual[4, 0]);
    }

    [WebGPUFact]
    public void FillPath_WithNegativePathGradientEdgeParameter_UsesDistanceMagnitude()
    {
        Rgba32 background = new(17, 43, 89, 255);
        Color halfRed = Color.FromScaledVector(new Vector4(0.5F, 0F, 0F, 1F));
        Brush brush = new PathGradientBrush(
        [
            new PointF(10F, 10F),
            new PointF(20F, 10F),
            new PointF(-4.5F, -10F),
            new PointF(2F, 27F),
            new PointF(3.5F, -2F),
            new PointF(11.045F, 7F)
        ],
        [
            halfRed,
            Color.Red,
            halfRed,
            halfRed,
            halfRed,
            halfRed
        ],
        halfRed);

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 32, 32));

        using Image<Rgba32> defaultImage = new(32, 32, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(32, 32, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(32, 32, backend, drawingOptions, DrawAction, initialImage);

        // These vertices place the first-edge intersection at u=-0.00075, inside the CPU
        // endpoint tolerance. CPU edge interpolation measures distance and therefore uses
        // |u|; signed extrapolation lands on the opposite side of the Rgba32 rounding boundary.
        Rgba32 expected = new(128, 0, 0, 255);
        Assert.Equal(expected, defaultImage[8, 8]);
        Assert.Equal(defaultImage[8, 8], actual[8, 8]);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(320, 200, "White", PixelTypes.Rgba32)]
    public void FillPath_WithTranslucentGradientBrushes_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        Brush radialBrush = new RadialGradientBrush(
            new PointF(82, 100),
            68F,
            GradientRepetitionMode.None,
            new ColorStop(0F, Color.Orange.WithAlpha(0.95F)),
            new ColorStop(1F, Color.MediumVioletRed.WithAlpha(0.25F)));

        PointF[] pathGradientPoints =
        [
            new PointF(164, 20),
            new PointF(306, 20),
            new PointF(306, 180),
            new PointF(164, 180)
        ];

        Brush pathGradientBrush = new PathGradientBrush(
            pathGradientPoints,
            [
                Color.CornflowerBlue.WithAlpha(0.9F),
                Color.Gold.WithAlpha(0.2F),
                Color.LimeGreen.WithAlpha(0.75F),
                Color.BlueViolet.WithAlpha(0.35F)
            ],
            Color.DeepPink.WithAlpha(0.6F));

        // Unequal RGB and alpha values make straight-alpha interpolation visibly diverge from
        // CSS Color 4 associated-alpha interpolation. Axis-aligned bounds remove curved-edge
        // coverage noise while the two brushes cover the ramp and direct shader interpolation paths.
        void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(radialBrush, new RectanglePolygon(14, 20, 136, 160));
            canvas.Fill(pathGradientBrush, new RectanglePolygon(164, 20, 142, 160));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.032F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUFact]
    public void FillPath_WithSubEpsilonAlpha_RgbaHalfTargetPreservesColorAndAlpha()
    {
        const float sourceAlpha = 0.047F;
        const float smallestPackedBlendPercentage = 1F / 65535F;

        Color color = Color.FromScaledVector(new Vector4(0.8F, 0.4F, 0.2F, sourceAlpha));
        PointF[] points =
        [
            new PointF(0, 0),
            new PointF(32, 0),
            new PointF(32, 32),
            new PointF(0, 32)
        ];

        Brush brush = new PathGradientBrush(points, [color, color, color, color], color);
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                BlendPercentage = smallestPackedBlendPercentage
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, new RectanglePolygon(0, 0, 32, 32));

        using Image<RgbaHalf> defaultImage = new(32, 32);
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend nativeSurfaceBackend = new();
        using Image<RgbaHalf> nativeSurfaceInitialImage = new(32, 32);
        using Image<RgbaHalf> nativeSurfaceImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            nativeSurfaceBackend,
            WebGPUTextureFormat.Rgba16Float,
            drawingOptions,
            DrawAction,
            nativeSurfaceInitialImage);

        RgbaHalf expected = RgbaHalf.FromScaledVector4(new Vector4(0.8F, 0.4F, 0.2F, sourceAlpha * smallestPackedBlendPercentage));

        RgbaHalf defaultPixel = defaultImage[16, 16];
        RgbaHalf nativeSurfacePixel = nativeSurfaceImage[16, 16];

        // The smallest nonzero packed blend value reduces alpha to a binary16 subnormal. The
        // source alpha keeps that result away from a binary16 rounding boundary because WGSL
        // permits conversion to round in either direction. Both backends must therefore produce
        // the same exact pixel while preserving the nonzero alpha.
        Assert.Equal(expected, defaultPixel);
        Assert.Equal(expected, nativeSurfacePixel);
        Assert.NotEqual((Half)0F, nativeSurfacePixel.A);
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = Brushes.Horizontal(Color.Black, Color.White);

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, "Horizontal", defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, "Horizontal", defaultImage, nativeSurfaceImage);
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

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, ellipse);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = new RecolorBrush(Color.Red, Color.Blue, 0.5F);

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_PreservesFullPrecisionSourceKey()
    {
        Rgba32 background = new(128, 0, 0, 255);
        Color source = Color.FromScaledVector(new Vector4(128F / 255F, 0F, 0F, 1F));

        // The exact Rgba32 component differs from its nearest binary16 value by about 7.7e-6.
        // This threshold accepts the exact f32 key but rejects that prematurely rounded key.
        Brush brush = new RecolorBrush(source, Color.Blue, 1e-12F);
        DrawingOptions drawingOptions = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        using Image<Rgba32> defaultImage = new(8, 8, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, canvas => canvas.Fill(brush));

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(8, 8, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(8, 8, backend, drawingOptions, canvas => canvas.Fill(brush), initialImage);

        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), defaultImage[4, 4]);
        Assert.Equal(defaultImage[4, 4], actual[4, 4]);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_ObservesPriorDrawInTargetStorage()
    {
        Color firstColor = Color.FromScaledVector(new Vector4(0.5F, 0F, 0F, 1F));
        Color source = Color.FromPixel(new Rgba32(128, 0, 0, 255));

        // Rgba32 stores 0.5 as 128 / 255. The narrow threshold distinguishes that stored value
        // from the unquantized 0.5 retained between commands by the staged GPU pipeline.
        Brush brush = new RecolorBrush(source, Color.Blue, 1e-8F);
        DrawingOptions drawingOptions = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(firstColor));
            canvas.Fill(brush);
        }

        using Image<Rgba32> defaultImage = new(8, 8);
        RenderWithDefaultBackend(defaultImage, drawingOptions, Draw);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend<Rgba32>(8, 8, backend, drawingOptions, Draw);

        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), defaultImage[4, 4]);
        Assert.Equal(defaultImage[4, 4], actual[4, 4]);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_AssociatedTargetObservesStoredComponents()
    {
        Color firstColor = Color.FromScaledVector(new Vector4(1F, 0F, 0F, 0.5F));
        Color source = Color.FromScaledVector(new Vector4(1F, 0F, 0F, 128F / 255F));

        // The source becomes (128 / 255, 0, 0, 128 / 255) in associated space. The first
        // fill remains (0.5, 0, 0, 0.5) until its match-only Rgba32P storage round-trip.
        Brush brush = new RecolorBrush(source, Color.Blue, 1e-8F);
        DrawingOptions drawingOptions = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(firstColor));
            canvas.Fill(brush);
        }

        using Image<Rgba32P> defaultImage = new(8, 8);
        RenderWithDefaultBackend(defaultImage, drawingOptions, Draw);

        using WebGPUDrawingBackend backend = new();
        using WebGPURenderTarget renderTarget = new(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, 8, 8);
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);

        using (DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
                   configuration,
                   drawingOptions,
                   backend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            Draw(canvas);
        }

        using Image<Rgba32P> actual = renderTarget.ReadbackImage<Rgba32P>();

        Assert.Equal(Color.Blue.ToPixel<Rgba32P>(), defaultImage[4, 4]);
        Assert.Equal(defaultImage[4, 4], actual[4, 4]);
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

        RectanglePolygon rect = new(16, 16, 224, 224);
        Brush brush = new LinearGradientBrush(
            new PointF(64, 128),
            new PointF(192, 128),
            new PointF(128, 64),
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Coral),
            new ColorStop(1, Color.SteelBlue));

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.013F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        RectanglePolygon rect = new(8, 8, 240, 240);
        Brush brush = new EllipticGradientBrush(
            new PointF(128, 128),
            new PointF(180, 160),
            0.4F,
            GradientRepetitionMode.Reflect,
            new ColorStop(0, Color.Gold),
            new ColorStop(0.5F, Color.DarkViolet),
            new ColorStop(1, Color.White));

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rect);

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.08F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
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

        RectanglePolygon sternHighlightRect = new(4, 4, 292, 72);

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

        void DrawAction(DrawingCanvas canvas)
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);

        // This test has a lot of text and gradients which can be a bit more variable across
        // platforms, so using a higher tolerance here to avoid noise.
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.094F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0006F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_FullOpacity_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Red);
        RectanglePolygon polygon = new(10, 10, 80, 80);

        void DrawAction(DrawingCanvas canvas)
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUFact]
    public void SaveLayer_HalfTargetClearUsesLogicalTransparent()
    {
        Color layerColor = Color.FromScaledVector(new Vector4(0.75F, 0.25F, 0.5F, 0.5F));
        DrawingOptions drawingOptions = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        void DrawAction(DrawingCanvas canvas)
        {
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 0, 32, 32));
            canvas.Fill(Brushes.Solid(layerColor), new RectanglePolygon(12, 12, 8, 8));
            canvas.Restore();
        }

        using WebGPUDrawingBackend backend = new();
        using Image<RgbaHalf> initialImage = new(32, 32);
        using Image<RgbaHalf> actual = RenderWithNativeSurfaceWebGpuBackend(
            32,
            32,
            backend,
            WebGPUTextureFormat.Rgba16Float,
            drawingOptions,
            DrawAction,
            initialImage);

        // The untouched part of an isolated layer must remain binary16 transparent black.
        Assert.Equal(Vector4.Zero, actual[0, 0].ToScaledVector4());
        Assert.True(actual[16, 16].ToScaledVector4().W > 0F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_HalfOpacity_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();
        Brush brush = Brushes.Solid(Color.Red);
        RectanglePolygon polygon = new(10, 10, 80, 80);

        void DrawAction(DrawingCanvas canvas)
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.0767F);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_NestedLayers_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Outer layer: red fill.
            canvas.SaveLayer();
            canvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(0, 0, 128, 128));

            // Inner layer: blue fill over center.
            canvas.SaveLayer();
            canvas.Fill(Brushes.Solid(Color.Blue), new RectanglePolygon(32, 32, 64, 64));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_WithBlendMode_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(20, 20, 88, 88));

            canvas.SaveLayer(new GraphicsOptions
            {
                ColorBlendingMode = PixelColorBlendingMode.Multiply,
                AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver,
                BlendPercentage = 1f
            });

            canvas.Fill(Brushes.Solid(Color.Blue), new RectanglePolygon(40, 40, 88, 88));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_WithBounds_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Layer bounds restrict compositing without shifting canvas coordinates.
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(16, 16, 96, 96));
            canvas.Fill(Brushes.Solid(Color.Green), new RectanglePolygon(0, 0, 96, 96));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithBlankImage(120, 120, PixelTypes.Rgba32)]
    public void SaveLayer_GaussianBlur_OffCanvasLayerBounds_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            IPath clipPath = new EllipsePolygon(new PointF(30, 40), new SizeF(70, 60));

            canvas.Save();
            canvas.Clip(clipPath);
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(-16, 8, 88, 80));
            canvas.Fill(Brushes.Solid(Color.Black), new Rectangle(0, 20, 56, 42));
            canvas.Apply(new Rectangle(-16, 8, 88, 80), x => x.GaussianBlur(6F));
            canvas.Restore();
            canvas.Restore();
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.007F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithBlankImage(120, 120, PixelTypes.Rgba32)]
    public void SaveLayer_Apply_ProcessesLayerTarget_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Expected output: an inverted cyan layer rectangle with an inverted yellow center square.
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(20, 20, 80, 80));
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(20, 20, 80, 80));
            canvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(42, 42, 24, 24));
            canvas.Apply(new Rectangle(20, 20, 80, 80), x => x.Invert());
            canvas.Restore();
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithBlankImage(120, 120, PixelTypes.Rgba32)]
    public void SaveLayer_Apply_RespectsLayerBounds_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Expected output: only the bounded layer area is inverted; the oversized fill and Apply rects do not affect the white background.
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(30, 30, 50, 50));
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(0, 0, 120, 120));
            canvas.Apply(new Rectangle(0, 0, 120, 120), x => x.Invert());
            canvas.Restore();
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithBlankImage(120, 120, PixelTypes.Rgba32)]
    public void SaveLayer_Apply_ProcessesNestedLayer_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Expected output: a red outer layer with the bounded nested layer inverted from blue to yellow.
            canvas.SaveLayer();
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(0, 0, 120, 120));

            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(30, 30, 50, 50));
            canvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(30, 30, 50, 50));
            canvas.Apply(new Rectangle(30, 30, 50, 50), x => x.Invert());
            canvas.Restore();

            canvas.Restore();
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithBlankImage(120, 120, PixelTypes.Rgba32)]
    public void SaveLayer_Apply_CompositesLayerOpacityAfterProcessing_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            // Expected output: the red layer is inverted to cyan, then composited over white as a 50% opacity pale cyan rectangle.
            canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.5F }, new Rectangle(20, 20, 80, 80));
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(20, 20, 80, 80));
            canvas.Apply(new Rectangle(20, 20, 80, 80), x => x.Invert());
            canvas.Restore();
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.088F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(128, 128, "White", PixelTypes.Rgba32)]
    public void SaveLayer_MixedSaveAndSaveLayer_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));

            int before = canvas.SaveCount;
            canvas.Save();              // plain save
            canvas.SaveLayer();         // layer
            canvas.Save();              // plain save

            canvas.Fill(Brushes.Solid(Color.Green), new RectanglePolygon(0, 0, 128, 128));

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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.005F);
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage);
    }

    [WebGPUTheory]
    [WithSolidFilledImages(320, 220, "White", PixelTypes.Rgba32)]
    public void CreateRegion_NestedRegionsAndStateIsolation_MatchesDefaultOutput<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions drawingOptions = new();

        static void DrawAction(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.GhostWhite.WithAlpha(0.85F)), new Rectangle(12, 12, 296, 196));

            DrawingOptions rootOptions = new()
            {
                Transform = Matrix4x4.CreateTranslation(6F, 4F, 0)
            };

            IPath rootClip = new EllipsePolygon(new PointF(160, 110), new SizeF(252, 164));
            _ = canvas.Save(rootOptions);
            canvas.Clip(ClipOperation.Difference, rootClip);

            using (DrawingCanvas outerRegion = canvas.CreateRegion(new Rectangle(30, 24, 240, 156)))
            {
                outerRegion.Fill(Brushes.Solid(Color.LightBlue.WithAlpha(0.35F)), new Rectangle(0, 0, 240, 156));
                outerRegion.Draw(Pens.Solid(Color.DarkBlue, 3F), new Rectangle(0, 0, 240, 156));

                DrawingOptions outerOptions = new()
                {
                    Transform = new Matrix4x4(Matrix3x2.CreateRotation(0.18F, new Vector2(120, 78)))
                };

                _ = outerRegion.Save(outerOptions);
                outerRegion.Clip(ClipOperation.Difference, new RectanglePolygon(18, 14, 204, 128));

                outerRegion.Fill(Brushes.Solid(Color.MediumPurple.WithAlpha(0.35F)), new Rectangle(16, 16, 208, 124));

                using (DrawingCanvas innerRegion = outerRegion.CreateRegion(new Rectangle(52, 34, 132, 82)))
                {
                    innerRegion.Clear(Brushes.Solid(Color.LightGoldenrodYellow.WithAlpha(0.8F)));

                    DrawingOptions innerOptions = new()
                    {
                        Transform = new Matrix4x4(Matrix3x2.CreateSkew(0.18F, 0F))
                    };

                    _ = innerRegion.Save(innerOptions);
                    innerRegion.Clip(ClipOperation.Difference, new EllipsePolygon(new PointF(66, 41), new SizeF(102, 58)));

                    innerRegion.Fill(Brushes.Solid(Color.SeaGreen.WithAlpha(0.55F)), new Rectangle(0, 0, 132, 82));
                    innerRegion.DrawLine(
                        Pens.Solid(Color.DarkRed, 4F),
                        new PointF(0, 80),
                        new PointF(66, 0),
                        new PointF(132, 74));

                    innerRegion.Restore();

                    innerRegion.Draw(Pens.DashDot(Color.Black.WithAlpha(0.75F), 2F), new Rectangle(4, 4, 124, 74));
                }

                outerRegion.Restore();

                outerRegion.Fill(Brushes.Solid(Color.OrangeRed.WithAlpha(0.6F)), new Rectangle(8, 112, 90, 30));
                outerRegion.DrawLine(Pens.Solid(Color.Black, 3F), new PointF(8, 8), new PointF(232, 148));
            }

            canvas.RestoreTo(1);

            canvas.Draw(Pens.Solid(Color.DarkSlateGray, 3F), new Rectangle(8, 8, 304, 204));
            canvas.DrawLine(Pens.Dash(Color.Gray, 2F), new PointF(20, 200), new PointF(300, 20));
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

        DebugSaveBackendPair(provider, null, defaultImage, nativeSurfaceImage);
        AssertBackendPairSimilarity(defaultImage, nativeSurfaceImage, 0.108F);

        // Reference outputs are rendered on one adapter; other conforming adapters differ by
        // rounding and antialiased edge coverage on a small fraction of pixels.
        AssertBackendPairReferenceOutputs(provider, null, defaultImage, nativeSurfaceImage, 0.02F);
    }
}
