// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public partial class WebGPUDrawingBackendTests
{
    /// <summary>
    /// Gets the COLR blend modes used by realistic brush-rendering parity tests.
    /// </summary>
    public static TheoryData<PixelColorBlendingMode, PixelAlphaCompositionMode> NewGraphicsOptionsModePairs { get; } =
    new()
    {
        { PixelColorBlendingMode.ColorDodge, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.ColorBurn, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.SoftLight, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Difference, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Exclusion, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Hue, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Saturation, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Color, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Luminosity, PixelAlphaCompositionMode.SrcOver },
        { PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.Plus }
    };

    /// <summary>
    /// Gets every new color blend mode paired with every public alpha composition mode.
    /// </summary>
    public static TheoryData<PixelColorBlendingMode, PixelAlphaCompositionMode> NewColorBlendCompositionPairs { get; }
        = CreateNewColorBlendCompositionPairs();

    /// <summary>
    /// Verifies that WebGPU produces the same pixels as the default CPU backend for every new blend/composition pairing.
    /// </summary>
    /// <param name="colorMode">The color blend mode to test.</param>
    /// <param name="alphaMode">The alpha composition mode to test.</param>
    [WebGPUTheory]
    [MemberData(nameof(NewColorBlendCompositionPairs))]
    public void NewColorBlendModes_WithEveryAlphaCompositionMode_MatchDefaultOutput(
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                // 0.2 is exactly representable in the scene's 16-bit BlendPercentage
                // encoding, so both backends compose with the identical opacity.
                BlendPercentage = 0.2F,
                ColorBlendingMode = colorMode,
                AlphaCompositionMode = alphaMode
            }
        };

        using Image<Rgba32> baseImage = new(8, 8, new Rgba32(42, 106, 181, 173));
        using Image<Rgba32> defaultImage = baseImage.Clone();

        // An image source reaches both backends as identical stored bytes: the CPU brush
        // samples them directly and the GPU samples the same texels from the image atlas.
        // A solid brush cannot make that guarantee, because the CPU quantizes its color
        // through the target pixel format while the scene wire carries associated binary16.
        using Image<Rgba32> sourceImage = new(8, 8, new Rgba32(218, 71, 133, 157));
        ImageBrush<Rgba32> brush = new(sourceImage, new RectangleF(0, 0, 8, 8), Point.Empty);
        RectanglePolygon rectangle = new(0, 0, 8, 8);

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rectangle);

        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            backend,
            drawingOptions,
            DrawAction,
            baseImage);

        // A zero tolerance makes the CPU output the reference for every rendered pixel.
        AssertBackendPairSimilarity(defaultImage, webGPUImage, 0F);
    }

    /// <summary>
    /// Verifies that binary16 targets store the same blended pixels as the default CPU renderer.
    /// </summary>
    /// <param name="colorMode">The color blend mode to test.</param>
    /// <param name="alphaMode">The alpha composition mode to test.</param>
    [WebGPUTheory]
    [MemberData(nameof(NewGraphicsOptionsModePairs))]
    public void NewBlendModes_SolidBrush_RgbaHalfTargetMatchesDefaultOutput(
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                // Full opacity isolates target-pixel conversion and blend math from the scene
                // format's established 16-bit BlendPercentage encoding.
                BlendPercentage = 1F,
                ColorBlendingMode = colorMode,
                AlphaCompositionMode = alphaMode
            }
        };

        // Binary-exact components prevent the helper's initial-image upload from introducing a
        // pre-blend half-precision difference between the two backends.
        RgbaHalf backdrop = Color.FromScaledVector(new Vector4(0.25F, 0.5F, 0.75F, 1F)).ToPixel<RgbaHalf>();
        using Image<RgbaHalf> baseImage = new(8, 8, backdrop);
        using Image<RgbaHalf> defaultImage = baseImage.Clone();
        SolidBrush brush = Brushes.Solid(Color.FromScaledVector(new Vector4(0.75F, 0.25F, 0.625F, 0.625F)));
        RectanglePolygon rectangle = new(-1, -1, 10, 10);

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rectangle);

        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<RgbaHalf> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            backend,
            WebGPUTextureFormat.Rgba16Float,
            drawingOptions,
            DrawAction,
            baseImage);

        // The CPU image is the executable reference, including its binary16 storage conversion.
        AssertBackendPairSimilarity(defaultImage, webGPUImage, 0F);
    }

    /// <summary>
    /// Verifies realistic solid-brush rendering for each new blend mode without requiring unapproved golden images.
    /// </summary>
    [WebGPUTheory]
    [WithBasicTestPatternImages(nameof(NewGraphicsOptionsModePairs), 384, 256, PixelTypes.Rgba32)]
    public void NewBlendModes_SolidBrush_MatchDefaultOutput<TPixel>(
        TestImageProvider<TPixel> provider,
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectanglePolygon polygon = new(26.5F, 18.25F, 324.5F, 208.75F);

        // The scene transports brush colors as associated binary16, so the interior
        // exactness assertion below requires source components that encoding holds
        // losslessly; 0.2 is likewise exact in the 16-bit BlendPercentage encoding.
        Brush brush = Brushes.Solid(Color.FromScaledVector(new Vector4(0.75F, 0.25F, 0.625F, 0.625F)));

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = true,
                BlendPercentage = 0.2F,
                ColorBlendingMode = colorMode,
                AlphaCompositionMode = alphaMode
            }
        };

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, polygon);

        using Image<TPixel> baseImage = provider.GetImage();
        using Image<TPixel> defaultImage = baseImage.Clone();
        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<TPixel> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            backend,
            drawingOptions,
            DrawAction,
            baseImage);

        DebugSaveBackendPair(provider, $"{colorMode}_{alphaMode}", defaultImage, webGPUImage);

        // Antialiased edge coverage is tolerance-based between the backends, matching the
        // established FillPath_WithGraphicsOptionsModes contract for identical scenes.
        AssertBackendPairSimilarity(defaultImage, webGPUImage, 0.125F);

        // Interior pixels carry full coverage on both backends, so the compositor itself
        // must reproduce the CPU renderer exactly there.
        AssertBackendPairSimilarityInRegion(defaultImage, webGPUImage, new Rectangle(28, 20, 294, 186), 0F);
    }

    /// <summary>
    /// Verifies realistic image-brush rendering for each new blend mode without requiring unapproved golden images.
    /// </summary>
    [WebGPUTheory]
    [WithBasicTestPatternImages(nameof(NewGraphicsOptionsModePairs), 384, 256, PixelTypes.Rgba32)]
    public void NewBlendModes_ImageBrush_MatchDefaultOutput<TPixel>(
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

        using WebGPUDrawingBackend backend = new();
        using Image<TPixel> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            backend,
            drawingOptions,
            DrawAction,
            baseImage);

        DebugSaveBackendPair(provider, $"{colorMode}_{alphaMode}", defaultImage, webGPUImage);

        // Antialiased edge coverage is tolerance-based between the backends, matching the
        // established FillPath_WithGraphicsOptionsModes contract for identical scenes.
        AssertBackendPairSimilarity(defaultImage, webGPUImage, 0.125F);

        // Interior pixels carry full coverage on both backends, so the compositor itself
        // must reproduce the CPU renderer exactly there.
        AssertBackendPairSimilarityInRegion(defaultImage, webGPUImage, new Rectangle(28, 20, 294, 186), 0F);
    }

    /// <summary>
    /// Verifies gradient-backed rendering for each new blend mode. Gradients take a separate
    /// shader source path (ramp texture sampling with per-draw blend flags) that solid fills
    /// never execute.
    /// </summary>
    /// <param name="colorMode">The color blend mode to test.</param>
    /// <param name="alphaMode">The alpha composition mode to test.</param>
    [WebGPUTheory]
    [MemberData(nameof(NewGraphicsOptionsModePairs))]
    public void NewBlendModes_LinearGradientBrush_MatchDefaultOutput(
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                // 0.2 is exactly representable in the scene's 16-bit BlendPercentage encoding.
                BlendPercentage = 0.2F,
                ColorBlendingMode = colorMode,
                AlphaCompositionMode = alphaMode
            }
        };

        using Image<Rgba32> baseImage = new(96, 64, new Rgba32(42, 106, 181, 173));
        using Image<Rgba32> defaultImage = baseImage.Clone();

        // Identical stops make the ramp constant: the multi-color ramp's t-snapping noise
        // is tolerance-based by the established gradient contract, and the nonlinear blend
        // curves would amplify it past any fixed tolerance. A constant ramp keeps the full
        // gradient shader path while permitting an exact comparison for every mode, and a
        // byte-grid stop color survives the ramp's texel storage on both backends.
        Color stopColor = Color.FromPixel(new Rgba32(218, 71, 133, 157));
        Brush brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(96, 64),
            GradientRepetitionMode.None,
            new ColorStop(0, stopColor),
            new ColorStop(1, stopColor));
        RectanglePolygon rectangle = new(0, 0, 96, 64);

        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rectangle);

        RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
            defaultImage.Width,
            defaultImage.Height,
            backend,
            drawingOptions,
            DrawAction,
            baseImage);

        // A zero tolerance makes the CPU output the reference for every rendered pixel.
        AssertBackendPairSimilarity(defaultImage, webGPUImage, 0F);
    }

    /// <summary>
    /// Verifies the singular ColorDodge and ColorBurn branches with channel values that hit
    /// every explicit branch on both backends, including their precedence order. Source
    /// components are scaled values the binary16 scene encoding holds losslessly, so the
    /// zero and one singular inputs reach both backends bit-identically.
    /// </summary>
    /// <param name="backdropR">The backdrop red component.</param>
    /// <param name="backdropG">The backdrop green component.</param>
    /// <param name="backdropB">The backdrop blue component.</param>
    /// <param name="backdropA">The backdrop alpha component.</param>
    /// <param name="sourceR">The scaled source red component.</param>
    /// <param name="sourceG">The scaled source green component.</param>
    /// <param name="sourceB">The scaled source blue component.</param>
    /// <param name="sourceA">The scaled source alpha component.</param>
    [WebGPUTheory]
    [InlineData(0, 255, 128, 200, 1F, 0F, 0.78125F, 0.859375F)]
    [InlineData(128, 200, 255, 173, 1F, 1F, 0F, 0.6875F)]
    public void ColorDodgeAndColorBurn_WithSingularChannelValues_MatchDefaultOutput(
        byte backdropR,
        byte backdropG,
        byte backdropB,
        byte backdropA,
        float sourceR,
        float sourceG,
        float sourceB,
        float sourceA)
    {
        Rgba32 backdrop = new(backdropR, backdropG, backdropB, backdropA);
        SolidBrush brush = Brushes.Solid(Color.FromScaledVector(new Vector4(sourceR, sourceG, sourceB, sourceA)));
        RectanglePolygon rectangle = new(0, 0, 8, 8);
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rectangle);

        foreach (PixelColorBlendingMode colorMode in new[] { PixelColorBlendingMode.ColorDodge, PixelColorBlendingMode.ColorBurn })
        {
            DrawingOptions drawingOptions = new()
            {
                GraphicsOptions = new GraphicsOptions
                {
                    Antialias = false,
                    BlendPercentage = 0.2F,
                    ColorBlendingMode = colorMode,
                    AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver
                }
            };

            using Image<Rgba32> baseImage = new(8, 8, backdrop);
            using Image<Rgba32> defaultImage = baseImage.Clone();
            RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

            using WebGPUDrawingBackend backend = new();
            using Image<Rgba32> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
                defaultImage.Width,
                defaultImage.Height,
                backend,
                drawingOptions,
                DrawAction,
                baseImage);

            // A zero tolerance makes the CPU output the reference for every rendered pixel.
            AssertBackendPairSimilarity(defaultImage, webGPUImage, 0F);
        }
    }

    /// <summary>
    /// Verifies that Difference and Exclusion match the CPU renderer independently and produce
    /// distinct results from each other for the same inputs.
    /// </summary>
    [WebGPUFact]
    public void DifferenceAndExclusion_MatchDefaultOutputAndDiffer()
    {
        using Image<Rgba32> baseImage = new(8, 8, new Rgba32(42, 106, 181, 173));

        // Wire-exact source components and BlendPercentage keep the exactness assertion
        // within the binary16 scene transport contract.
        SolidBrush brush = Brushes.Solid(Color.FromScaledVector(new Vector4(0.75F, 0.25F, 0.625F, 0.625F)));
        RectanglePolygon rectangle = new(0, 0, 8, 8);
        void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rectangle);

        Image<Rgba32> RenderPair(PixelColorBlendingMode colorMode)
        {
            DrawingOptions drawingOptions = new()
            {
                GraphicsOptions = new GraphicsOptions
                {
                    Antialias = false,
                    BlendPercentage = 0.2F,
                    ColorBlendingMode = colorMode,
                    AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver
                }
            };

            Image<Rgba32> defaultImage = baseImage.Clone();
            RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

            using WebGPUDrawingBackend backend = new();
            using Image<Rgba32> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
                defaultImage.Width,
                defaultImage.Height,
                backend,
                drawingOptions,
                DrawAction,
                baseImage);

            // A zero tolerance makes the CPU output the reference for every rendered pixel.
            AssertBackendPairSimilarity(defaultImage, webGPUImage, 0F);
            return defaultImage;
        }

        using Image<Rgba32> difference = RenderPair(PixelColorBlendingMode.Difference);
        using Image<Rgba32> exclusion = RenderPair(PixelColorBlendingMode.Exclusion);

        // The chosen inputs separate |Cb - Cs| from Cb + Cs - 2CbCs, proving the two modes
        // are implemented independently rather than aliased to one formula.
        Assert.NotEqual(difference[4, 4], exclusion[4, 4]);
    }

    /// <summary>
    /// Verifies transparent-backdrop and zero-alpha-source inputs for each new blend mode.
    /// </summary>
    /// <param name="colorMode">The color blend mode to test.</param>
    /// <param name="alphaMode">The alpha composition mode to test.</param>
    [WebGPUTheory]
    [MemberData(nameof(NewGraphicsOptionsModePairs))]
    public void NewBlendModes_TransparentAndZeroAlphaInputs_MatchDefaultOutput(
        PixelColorBlendingMode colorMode,
        PixelAlphaCompositionMode alphaMode)
    {
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                BlendPercentage = 0.73F,
                ColorBlendingMode = colorMode,
                AlphaCompositionMode = alphaMode
            }
        };

        RectanglePolygon rectangle = new(0, 0, 8, 8);

        void AssertScenario(Rgba32 backdrop, Rgba32 source)
        {
            SolidBrush brush = Brushes.Solid(Color.FromPixel(source));
            void DrawAction(DrawingCanvas canvas) => canvas.Fill(brush, rectangle);

            using Image<Rgba32> baseImage = new(8, 8, backdrop);
            using Image<Rgba32> defaultImage = baseImage.Clone();
            RenderWithDefaultBackend(defaultImage, drawingOptions, DrawAction);

            using WebGPUDrawingBackend backend = new();
            using Image<Rgba32> webGPUImage = RenderWithNativeSurfaceWebGpuBackend(
                defaultImage.Width,
                defaultImage.Height,
                backend,
                drawingOptions,
                DrawAction,
                baseImage);

            // A zero tolerance makes the CPU output the reference for every rendered pixel.
            AssertBackendPairSimilarity(defaultImage, webGPUImage, 0F);
        }

        // Transparent black backdrop: the composed result must come from the source alone.
        AssertScenario(new Rgba32(0, 0, 0, 0), new Rgba32(218, 71, 133, 157));

        // Zero-alpha source: source-over and plus must leave the backdrop untouched.
        AssertScenario(new Rgba32(42, 106, 181, 173), new Rgba32(218, 71, 133, 0));
    }

    /// <summary>
    /// Creates the complete cross-product used to exercise composition independently of color blending.
    /// </summary>
    /// <returns>The color blend and alpha composition mode pairs.</returns>
    private static TheoryData<PixelColorBlendingMode, PixelAlphaCompositionMode> CreateNewColorBlendCompositionPairs()
    {
        PixelColorBlendingMode[] colorModes =
        [
            PixelColorBlendingMode.ColorDodge,
            PixelColorBlendingMode.ColorBurn,
            PixelColorBlendingMode.SoftLight,
            PixelColorBlendingMode.Difference,
            PixelColorBlendingMode.Exclusion,
            PixelColorBlendingMode.Hue,
            PixelColorBlendingMode.Saturation,
            PixelColorBlendingMode.Color,
            PixelColorBlendingMode.Luminosity
        ];

        TheoryData<PixelColorBlendingMode, PixelAlphaCompositionMode> pairs = new();
        foreach (PixelColorBlendingMode colorMode in colorModes)
        {
            foreach (PixelAlphaCompositionMode alphaMode in Enum.GetValues<PixelAlphaCompositionMode>())
            {
                pairs.Add(colorMode, alphaMode);
            }
        }

        return pairs;
    }
}
