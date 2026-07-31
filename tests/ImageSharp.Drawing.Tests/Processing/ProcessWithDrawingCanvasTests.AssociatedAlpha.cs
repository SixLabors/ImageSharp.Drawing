// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    /// <summary>
    /// Verifies every Drawing blend and composition selection against paired associated and
    /// unassociated destinations.
    /// </summary>
    /// <param name="blending">The color blending mode.</param>
    /// <param name="composition">The alpha composition mode.</param>
    [Theory]
    [MemberData(nameof(BlendingsModes))]
    public void SolidBrushPreservesAlphaRepresentationForAllModeCombinations(
        PixelColorBlendingMode blending,
        PixelAlphaCompositionMode composition)
    {
        Rgba32 source = new(211, 47, 139, 149);
        Color unassociatedSource = Color.FromPixel(source);
        Color associatedSource = ToAssociatedColor(unassociatedSource);
        DrawingOptions options = CreateBlendOptions(blending, composition);

        AssertBrushAssociationSimilarityForAllFormats(Brushes.Solid(unassociatedSource), Brushes.Solid(associatedSource), options);
    }

    /// <summary>
    /// Verifies linear gradients use CSS Color 4 associated-alpha interpolation.
    /// </summary>
    [Fact]
    public void LinearGradientUsesCssColor4AlphaInterpolation()
        => AssertGradientAssociationSimilarity(GradientKind.Linear, GradientRepetitionMode.Repeat, false);

    /// <summary>
    /// Verifies radial gradients use CSS Color 4 associated-alpha interpolation.
    /// </summary>
    [Fact]
    public void RadialGradientUsesCssColor4AlphaInterpolation()
        => AssertGradientAssociationSimilarity(GradientKind.Radial, GradientRepetitionMode.Reflect, false);

    /// <summary>
    /// Verifies elliptic gradients use CSS Color 4 associated-alpha interpolation.
    /// </summary>
    [Fact]
    public void EllipticGradientUsesCssColor4AlphaInterpolation()
        => AssertGradientAssociationSimilarity(GradientKind.Elliptic, GradientRepetitionMode.DontFill, false);

    /// <summary>
    /// Verifies sweep gradients use CSS Color 4 associated-alpha interpolation.
    /// </summary>
    [Fact]
    public void SweepGradientUsesCssColor4AlphaInterpolation()
        => AssertGradientAssociationSimilarity(GradientKind.Sweep, GradientRepetitionMode.None, false);

    /// <summary>
    /// Verifies equal gradient stops use the same representation-independent result as the
    /// general interpolation path.
    /// </summary>
    [Fact]
    public void EqualGradientStopsPreserveAlphaRepresentation()
        => AssertGradientAssociationSimilarity(GradientKind.Linear, GradientRepetitionMode.None, true);

    /// <summary>
    /// Verifies the midpoint oracle defined by CSS Color 4: interpolate associated components,
    /// then unassociate only when storing into a straight-alpha destination.
    /// </summary>
    [Fact]
    public void LinearGradientMidpointMatchesCssColor4()
    {
        ColorStop[] stops =
        [
            new(0F, Color.FromPixel(new Rgba32(255, 0, 0, 255))),
            new(1F, Color.FromPixel(new Rgba32(0, 0, 255, 0)))
        ];
        LinearGradientBrush brush = new(new PointF(0, 0), new PointF(1, 0), GradientRepetitionMode.None, stops);
        using Image<Rgba32> unassociated = new(1, 1);
        using Image<Rgba32P> associated = new(1, 1);

        unassociated.Mutate(context => context.Paint(canvas => canvas.Fill(brush)));
        associated.Mutate(context => context.Paint(canvas => canvas.Fill(brush)));

        // At the sole pixel center t is exactly 0.5. Associated interpolation between opaque
        // red (1, 0, 0, 1) and transparent blue (0, 0, 0, 0) yields (0.5, 0, 0, 0.5).
        // The straight destination unassociates that value to (1, 0, 0, 0.5).
        Assert.Equal(new Rgba32(255, 0, 0, 128), unassociated[0, 0]);
        Assert.Equal(new Rgba32P(128, 0, 0, 128), associated[0, 0]);
    }

    /// <summary>
    /// Verifies associated destinations compare associated color components.
    /// </summary>
    [Fact]
    public void RecolorBrushAssociatedDestinationUsesAssociatedComponents()
    {
        Rgba32 background = new(0, 255, 0, 26);
        Color source = Color.FromPixel(new Rgba32(255, 0, 0, 26));
        Color target = Color.FromPixel(new Rgba32(0, 0, 255, 211));
        const float threshold = 0.1F;
        RecolorBrush brush = new(source, target, threshold);
        DrawingOptions options = CreateBlendOptions(PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.Src);
        using Image<Rgba32P> actual = RenderRecolor<Rgba32P>(background, brush, options);
        Rgba32P backgroundPixel = Color.FromPixel(background).ToPixel<Rgba32P>();
        Rgba32P targetPixel = target.ToPixel<Rgba32P>();
        float scaledThreshold = threshold * 4F;
        float distance = Vector4.DistanceSquared(backgroundPixel.ToScaledVector4(), source.ToScaledVector4(PixelAlphaRepresentation.Associated));
        float amount = (scaledThreshold - distance) / scaledThreshold;
        PixelBlender<Rgba32P> blender = PixelOperations<Rgba32P>.Instance.GetPixelBlender(options.GraphicsOptions);
        Rgba32P expected = blender.Blend(backgroundPixel, targetPixel, amount);

        // The straight RGB distance is two, but multiplying both colors by their low alpha
        // reduces the native associated distance enough for the pixel to be recolored.
        Assert.Equal(expected, actual[4, 4]);
        Assert.NotEqual(backgroundPixel, actual[4, 4]);
    }

    /// <summary>
    /// Verifies unassociated destinations compare unassociated color components.
    /// </summary>
    [Fact]
    public void RecolorBrushUnassociatedDestinationUsesUnassociatedComponents()
    {
        Rgba32 background = new(0, 255, 0, 26);
        Color source = Color.FromPixel(new Rgba32(255, 0, 0, 26));
        Color target = Color.FromPixel(new Rgba32(0, 0, 255, 211));
        RecolorBrush brush = new(source, target, 0.1F);
        DrawingOptions options = CreateBlendOptions(PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.Src);
        using Image<Rgba32> actual = RenderRecolor<Rgba32>(background, brush, options);

        // The native straight RGB distance is two, which exceeds the scaled threshold of 0.4.
        Assert.Equal(background, actual[4, 4]);
    }

    /// <summary>
    /// Verifies a high-precision source key is not quantized through the destination pixel
    /// format before comparison.
    /// </summary>
    [Fact]
    public void RecolorBrushSourceKeyIsNotQuantizedThroughDestinationFormat()
    {
        Color source = Color.FromScaledVector(new Vector4(0.5F, 0F, 0F, 1F));
        Rgba32 quantizedSource = source.ToPixel<Rgba32>();
        const float threshold = 0.0000005F;
        RecolorBrush brush = new(source, Color.Lime, threshold);
        DrawingOptions options = CreateBlendOptions(PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.Src);
        using Image<Rgba32> actual = RenderRecolor<Rgba32>(quantizedSource, brush, options);
        float directDistance = Vector4.DistanceSquared(quantizedSource.ToScaledVector4(), source.ToScaledVector4(PixelAlphaRepresentation.Unassociated));

        // Quantizing 0.5 to Rgba32 produces 128/255. That pixel would be an exact match if the
        // renderer first converted its key to Rgba32, while the direct Color distance remains
        // just outside this deliberately narrow threshold.
        Assert.Equal(new Rgba32(128, 0, 0, 255), quantizedSource);
        Assert.True(directDistance > threshold * 4F);
        Assert.Equal(quantizedSource, actual[4, 4]);
    }

    /// <summary>
    /// Verifies Recolor uses the selected color blending and alpha composition modes for its
    /// replacement blend in every supported alpha representation.
    /// </summary>
    [Fact]
    public void RecolorBrushUsesConfiguredBlenderForAllFormats()
    {
        Rgba32 source = new(61, 137, 223, 255);
        Rgba32 target = new(229, 83, 37, 109);
        DrawingOptions options = CreateBlendOptions(PixelColorBlendingMode.Multiply, PixelAlphaCompositionMode.SrcOver);

        AssertRecolorUsesConfiguredBlender<Rgba32>(source, target, options);
        AssertRecolorUsesConfiguredBlender<Rgba32P>(source, target, options);
        AssertRecolorUsesConfiguredBlender<Bgra32>(source, target, options);
        AssertRecolorUsesConfiguredBlender<Bgra32P>(source, target, options);
        AssertRecolorUsesConfiguredBlender<Argb32>(source, target, options);
        AssertRecolorUsesConfiguredBlender<Argb32P>(source, target, options);
        AssertRecolorUsesConfiguredBlender<Abgr32>(source, target, options);
        AssertRecolorUsesConfiguredBlender<Abgr32P>(source, target, options);
        AssertRecolorUsesConfiguredBlender<NormalizedByte4>(source, target, options);
        AssertRecolorUsesConfiguredBlender<NormalizedByte4P>(source, target, options);
        AssertRecolorUsesConfiguredBlender<RgbaHalf>(source, target, options);
        AssertRecolorUsesConfiguredBlender<RgbaHalfP>(source, target, options);

        AssertRecolorUsesConfiguredBlender<Rgb24>(source, target, options);
    }

    /// <summary>
    /// Verifies pattern colors and destinations preserve their alpha representation.
    /// </summary>
    [Fact]
    public void PatternBrushPreservesAlphaRepresentation()
    {
        Color foreground = Color.FromPixel(new Rgba32(227, 41, 113, 149));
        Color background = Color.FromPixel(new Rgba32(29, 191, 73, 67));
        bool[,] pattern = { { true, false }, { false, true } };
        PatternBrush unassociatedBrush = new(foreground, background, pattern);
        PatternBrush associatedBrush = new(ToAssociatedColor(foreground), ToAssociatedColor(background), pattern);

        AssertBrushAssociationSimilarityForAllFormats(unassociatedBrush, associatedBrush, new DrawingOptions());
    }

    /// <summary>
    /// Verifies image brushes preserve source and destination alpha representation for all CPU format pairs.
    /// </summary>
    [Fact]
    public void ImageBrushPreservesAlphaRepresentation()
    {
        AssertImageBrushAssociationSimilarity<Rgba32, Rgba32P>();
        AssertImageBrushAssociationSimilarity<Bgra32, Bgra32P>();
        AssertImageBrushAssociationSimilarity<Argb32, Argb32P>();
        AssertImageBrushAssociationSimilarity<Abgr32, Abgr32P>();
        AssertImageBrushAssociationSimilarity<NormalizedByte4, NormalizedByte4P>();
        AssertImageBrushAssociationSimilarity<RgbaHalf, RgbaHalfP>();
    }

    /// <summary>
    /// Verifies image drawing preserves source and destination alpha representation for all CPU format pairs.
    /// </summary>
    [Fact]
    public void DrawImagePreservesAlphaRepresentation()
    {
        AssertDrawImageAssociationSimilarity<Rgba32, Rgba32P>();
        AssertDrawImageAssociationSimilarity<Bgra32, Bgra32P>();
        AssertDrawImageAssociationSimilarity<Argb32, Argb32P>();
        AssertDrawImageAssociationSimilarity<Abgr32, Abgr32P>();
        AssertDrawImageAssociationSimilarity<NormalizedByte4, NormalizedByte4P>();
        AssertDrawImageAssociationSimilarity<RgbaHalf, RgbaHalfP>();
    }

    /// <summary>
    /// Verifies clipped drawing preserves associated destination storage.
    /// </summary>
    [Fact]
    public void ClipPreservesAlphaRepresentation()
    {
        EllipsePolygon clip = new(new PointF(24, 24), new SizeF(34, 28));
        Brush brush = Brushes.Solid(Color.FromPixel(new Rgba32(231, 47, 113, 149)));

        AssertCanvasSceneAssociationSimilarity(canvas =>
        {
            canvas.Save();
            canvas.Clip(ClipOperation.Intersection, clip);
            canvas.Fill(brush, new RectanglePolygon(4, 6, 40, 36));
            canvas.Restore();
        });
    }

    /// <summary>
    /// Verifies layer isolation and restore preserve associated destination storage.
    /// </summary>
    [Fact]
    public void SaveLayerPreservesAlphaRepresentation()
    {
        Brush brush = Brushes.Solid(Color.FromPixel(new Rgba32(37, 211, 89, 173)));

        AssertCanvasSceneAssociationSimilarity(canvas =>
        {
            canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.55F }, new Rectangle(6, 8, 36, 32));
            canvas.Fill(brush, new EllipsePolygon(new PointF(25, 23), new SizeF(30, 26)));
            canvas.Restore();
        });
    }

    /// <summary>
    /// Verifies glyph coverage and brush blending preserve associated destination storage.
    /// </summary>
    [Fact]
    public void TextPreservesAlphaRepresentation()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24F);
        RichTextOptions options = new(font) { Origin = new PointF(3, 8) };
        Brush brush = Brushes.Solid(Color.FromPixel(new Rgba32(227, 61, 149, 181)));

        AssertCanvasSceneAssociationSimilarity(canvas => canvas.DrawText(options, "Alpha", brush, pen: null));
    }

    /// <summary>
    /// Verifies processing a queued canvas region preserves associated destination storage.
    /// </summary>
    [Fact]
    public void ApplyPreservesAlphaRepresentation()
    {
        Brush brush = Brushes.Solid(Color.FromPixel(new Rgba32(41, 193, 227, 157)));

        AssertCanvasSceneAssociationSimilarity(canvas =>
        {
            canvas.Fill(brush, new EllipsePolygon(new PointF(24, 24), new SizeF(32, 30)));
            canvas.Apply(new Rectangle(8, 8, 32, 32), context => context.Invert());
        });
    }

    /// <summary>
    /// Verifies backdrop filtering preserves associated destination storage.
    /// </summary>
    [Fact]
    public void BackdropLayerEffectPreservesAlphaRepresentation()
    {
        Brush brush = Brushes.Solid(Color.FromPixel(new Rgba32(217, 79, 31, 163)));
        BackdropLayerEffect effect = new BackdropAcrylicLayerEffect(1F, Color.FromPixel(new Rgba32(73, 149, 239, 91)));

        AssertCanvasSceneAssociationSimilarity(canvas =>
        {
            canvas.Fill(brush, new EllipsePolygon(new PointF(20, 20), new SizeF(30, 28)));
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(12, 10, 30, 32), effect);
            canvas.Restore();
        });
    }

    /// <summary>
    /// Verifies foreground layer effects preserve associated destination storage.
    /// </summary>
    [Fact]
    public void LayerEffectPreservesAlphaRepresentation()
    {
        Brush brush = Brushes.Solid(Color.FromPixel(new Rgba32(237, 173, 43, 187)));

        // Keep this comparison to one native quantization boundary. The separate blurred test
        // covers the multi-pass processor, whose TPixel intermediate necessarily quantizes
        // associated and unassociated 8-bit storage at different points.
        LayerEffect effect = new DropShadowLayerEffect(new Point(3, 2), 0F, Color.FromPixel(new Rgba32(31, 59, 211, 109)));

        AssertCanvasSceneAssociationSimilarity(canvas =>
        {
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(6, 6, 36, 34), effect);
            canvas.Fill(brush, new RoundedRectanglePolygon(new RectangleF(12, 12, 24, 20), 5F));
            canvas.Restore();
        });
    }

    /// <summary>
    /// Verifies a blurred foreground layer effect preserves native channel layout and associated
    /// storage while producing identical alpha in associated and unassociated destinations.
    /// </summary>
    [Fact]
    public void BlurredLayerEffectPreservesAssociationAndChannelLayout()
    {
        Brush brush = Brushes.Solid(Color.FromPixel(new Rgba32(237, 173, 43, 187)));
        LayerEffect effect = new DropShadowLayerEffect(new Point(3, 2), 1F, Color.FromPixel(new Rgba32(31, 59, 211, 109)));
        CanvasAction draw = canvas =>
        {
            canvas.SaveLayer(new GraphicsOptions(), new Rectangle(6, 6, 36, 34), effect);
            canvas.Fill(brush, new RoundedRectanglePolygon(new RectangleF(12, 12, 24, 20), 5F));
            canvas.Restore();
        };

        using Image<Rgba32> rgba = RenderCanvasScene<Rgba32>(draw);
        using Image<Bgra32> bgra = RenderCanvasScene<Bgra32>(draw);
        using Image<Rgba32P> rgbaAssociated = RenderCanvasScene<Rgba32P>(draw);
        using Image<Bgra32P> bgraAssociated = RenderCanvasScene<Bgra32P>(draw);

        // Gaussian blur stores its horizontal pass in the native TPixel before the vertical pass.
        // Associated and unassociated 8-bit formats therefore cross different quantization
        // lattices more than once. ImageSharp's processor tests verify the floating-point
        // association math; this integration test compares each native representation exactly
        // and requires alpha, which does not depend on RGB association, to remain identical.
        for (int y = 0; y < rgba.Height; y++)
        {
            for (int x = 0; x < rgba.Width; x++)
            {
                Rgba32 rgbaPixel = rgba[x, y];
                Bgra32 bgraPixel = bgra[x, y];
                Rgba32P rgbaAssociatedPixel = rgbaAssociated[x, y];
                Bgra32P bgraAssociatedPixel = bgraAssociated[x, y];

                Assert.Equal(rgbaPixel, new Rgba32(bgraPixel.R, bgraPixel.G, bgraPixel.B, bgraPixel.A));
                Assert.Equal(rgbaAssociatedPixel, new Rgba32P(bgraAssociatedPixel.R, bgraAssociatedPixel.G, bgraAssociatedPixel.B, bgraAssociatedPixel.A));
                Assert.Equal(rgbaPixel.A, rgbaAssociatedPixel.A);
                Assert.Equal(bgraPixel.A, bgraAssociatedPixel.A);
            }
        }

        AssertAssociatedStorage(rgbaAssociated);
        AssertAssociatedStorage(bgraAssociated);
    }

    /// <summary>
    /// Creates equivalent gradient brushes from associated and unassociated color inputs and
    /// verifies their output across every paired destination format.
    /// </summary>
    /// <param name="kind">The gradient family to render.</param>
    /// <param name="repetitionMode">The repetition mode to exercise.</param>
    /// <param name="equalStops">Whether both stops describe the same color.</param>
    private static void AssertGradientAssociationSimilarity(GradientKind kind, GradientRepetitionMode repetitionMode, bool equalStops)
    {
        Rgba32[] colors = equalStops
            ? [new(197, 71, 149, 137), new(197, 71, 149, 137)]
            : [new(239, 37, 19, 211), new(17, 227, 83, 0), new(43, 79, 241, 73)];

        ColorStop[] unassociatedStops = new ColorStop[colors.Length];
        ColorStop[] associatedStops = new ColorStop[colors.Length];

        for (int i = 0; i < colors.Length; i++)
        {
            float ratio = (float)i / (colors.Length - 1);
            unassociatedStops[i] = new ColorStop(ratio, Color.FromPixel(colors[i]));
            associatedStops[i] = new ColorStop(ratio, ToAssociatedColor(unassociatedStops[i].Color));
        }

        Brush unassociatedBrush = CreateGradient(kind, repetitionMode, unassociatedStops);
        Brush associatedBrush = CreateGradient(kind, repetitionMode, associatedStops);
        DrawingOptions options = CreateBlendOptions(PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.Src);

        // Src composition isolates gradient interpolation and destination encoding from an
        // additional source-over quantization boundary in the canvas blender.
        AssertBrushAssociationSimilarityForAllFormats(unassociatedBrush, associatedBrush, options);
        AssertBrushDestinationAssociationSimilarityForAllFormats(unassociatedBrush, options);
        AssertBrushDestinationAssociationSimilarityForAllFormats(associatedBrush, options);
    }

    /// <summary>
    /// Creates one gradient family for the shared alpha-representation test matrix.
    /// </summary>
    /// <param name="kind">The gradient family.</param>
    /// <param name="repetitionMode">The repetition mode.</param>
    /// <param name="colorStops">The color stops.</param>
    /// <returns>The configured gradient brush.</returns>
    private static Brush CreateGradient(GradientKind kind, GradientRepetitionMode repetitionMode, ColorStop[] colorStops)
        => kind switch
        {
            GradientKind.Linear => new LinearGradientBrush(new PointF(-4, 2), new PointF(12, 6), repetitionMode, colorStops),
            GradientKind.Radial => new RadialGradientBrush(new PointF(4, 4), 5F, repetitionMode, colorStops),
            GradientKind.Elliptic => new EllipticGradientBrush(new PointF(4, 4), new PointF(9, 4), 0.6F, repetitionMode, colorStops),
            _ => new SweepGradientBrush(new PointF(4, 4), -45F, 315F, repetitionMode, colorStops)
        };

    /// <summary>
    /// Verifies one brush against every associated and unassociated pixel-format pair.
    /// </summary>
    /// <param name="unassociatedBrush">The brush created from unassociated colors.</param>
    /// <param name="associatedBrush">The brush created from associated colors.</param>
    /// <param name="options">The drawing options.</param>
    private static void AssertBrushAssociationSimilarityForAllFormats(Brush unassociatedBrush, Brush associatedBrush, DrawingOptions options)
    {
        AssertBrushAssociationSimilarity<Rgba32, Rgba32P>(unassociatedBrush, associatedBrush, options);
        AssertBrushAssociationSimilarity<Bgra32, Bgra32P>(unassociatedBrush, associatedBrush, options);
        AssertBrushAssociationSimilarity<Argb32, Argb32P>(unassociatedBrush, associatedBrush, options);
        AssertBrushAssociationSimilarity<Abgr32, Abgr32P>(unassociatedBrush, associatedBrush, options);
        AssertBrushAssociationSimilarity<NormalizedByte4, NormalizedByte4P>(unassociatedBrush, associatedBrush, options);
        AssertBrushAssociationSimilarity<RgbaHalf, RgbaHalfP>(unassociatedBrush, associatedBrush, options);
    }

    /// <summary>
    /// Verifies one logical brush through both input-color representations and both destination
    /// representations for a pixel-format pair.
    /// </summary>
    /// <typeparam name="TUnassociated">The unassociated destination format.</typeparam>
    /// <typeparam name="TAssociated">The associated destination format.</typeparam>
    /// <param name="unassociatedBrush">The brush created from unassociated colors.</param>
    /// <param name="associatedBrush">The brush created from associated colors.</param>
    /// <param name="options">The drawing options.</param>
    private static void AssertBrushAssociationSimilarity<TUnassociated, TAssociated>(Brush unassociatedBrush, Brush associatedBrush, DrawingOptions options)
        where TUnassociated : unmanaged, IPixel<TUnassociated>
        where TAssociated : unmanaged, IPixel<TAssociated>
    {
        using Image<TUnassociated> unassociatedDestination = RenderBrush<TUnassociated>(unassociatedBrush, options);
        using Image<TUnassociated> associatedInput = RenderBrush<TUnassociated>(associatedBrush, options);
        using Image<TAssociated> associatedDestination = RenderBrush<TAssociated>(unassociatedBrush, options);
        using Image<TAssociated> associatedInputAndDestination = RenderBrush<TAssociated>(associatedBrush, options);

        AssertAssociationSimilarity(unassociatedDestination, associatedInput);
        AssertAssociationSimilarity(associatedDestination, associatedInputAndDestination);

        AssertAssociatedStorage(associatedDestination);
        AssertAssociatedStorage(associatedInputAndDestination);
    }

    /// <summary>
    /// Compares one brush directly across every associated and unassociated destination pair.
    /// </summary>
    /// <param name="brush">The brush to render.</param>
    /// <param name="options">The drawing options.</param>
    private static void AssertBrushDestinationAssociationSimilarityForAllFormats(Brush brush, DrawingOptions options)
    {
        AssertBrushDestinationAssociationSimilarity<Rgba32, Rgba32P>(brush, options);
        AssertBrushDestinationAssociationSimilarity<Bgra32, Bgra32P>(brush, options);
        AssertBrushDestinationAssociationSimilarity<Argb32, Argb32P>(brush, options);
        AssertBrushDestinationAssociationSimilarity<Abgr32, Abgr32P>(brush, options);
        AssertBrushDestinationAssociationSimilarity<NormalizedByte4, NormalizedByte4P>(brush, options);
        AssertBrushDestinationAssociationSimilarity<RgbaHalf, RgbaHalfP>(brush, options);
    }

    /// <summary>
    /// Compares one brush directly across an associated and unassociated destination pair.
    /// </summary>
    /// <typeparam name="TUnassociated">The unassociated destination format.</typeparam>
    /// <typeparam name="TAssociated">The associated destination format.</typeparam>
    /// <param name="brush">The brush to render.</param>
    /// <param name="options">The drawing options.</param>
    private static void AssertBrushDestinationAssociationSimilarity<TUnassociated, TAssociated>(Brush brush, DrawingOptions options)
        where TUnassociated : unmanaged, IPixel<TUnassociated>
        where TAssociated : unmanaged, IPixel<TAssociated>
    {
        using Image<TUnassociated> unassociated = RenderBrush<TUnassociated>(brush, options);
        using Image<TAssociated> associated = RenderBrush<TAssociated>(brush, options);

        AssertAssociationSimilarity(unassociated, associated);
        AssertAssociatedStorage(associated);
    }

    /// <summary>
    /// Renders a brush over the same logical translucent background in the requested pixel format.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="brush">The brush to render.</param>
    /// <param name="options">The drawing options.</param>
    /// <returns>The rendered image.</returns>
    private static Image<TPixel> RenderBrush<TPixel>(Brush brush, DrawingOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rgba32 background = new(31, 87, 143, 113);
        Image<TPixel> image = new(8, 8, Color.FromPixel(background).ToPixel<TPixel>());

        image.Mutate(context => context.Paint(options, canvas => canvas.Fill(brush)));

        return image;
    }

    /// <summary>
    /// Renders a recolor brush over alternating matching and non-matching pixels.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="source">The logical matching color.</param>
    /// <param name="brush">The recolor brush.</param>
    /// <param name="options">The drawing options.</param>
    /// <returns>The rendered image.</returns>
    private static Image<TPixel> RenderRecolor<TPixel>(Rgba32 source, RecolorBrush brush, DrawingOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = new(8, 8);
        Rgba32 other = new(19, 211, 101, 197);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = Color.FromPixel(((x + y) & 1) == 0 ? source : other).ToPixel<TPixel>();
                }
            }
        });

        image.Mutate(context => context.Paint(options, canvas => canvas.Fill(brush)));

        return image;
    }

    /// <summary>
    /// Compares Recolor output with an independently selected pixel blender for one format.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="source">The logical destination and matching color.</param>
    /// <param name="target">The logical replacement color.</param>
    /// <param name="options">The drawing options selecting the blender.</param>
    private static void AssertRecolorUsesConfiguredBlender<TPixel>(Rgba32 source, Rgba32 target, DrawingOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RecolorBrush brush = new(Color.FromPixel(source), Color.FromPixel(target), 0.01F);
        using Image<TPixel> actual = RenderRecolor<TPixel>(source, brush, options);
        TPixel sourcePixel = Color.FromPixel(source).ToPixel<TPixel>();
        TPixel targetPixel = Color.FromPixel(target).ToPixel<TPixel>();
        PixelBlender<TPixel> blender = PixelOperations<TPixel>.Instance.GetPixelBlender(options.GraphicsOptions);

        // An exact key match has replacement amount one. Full shape coverage then applies the
        // same configured blender a second time with the DrawingOptions blend percentage.
        TPixel overlay = blender.Blend(sourcePixel, targetPixel, 1F);
        TPixel expected = blender.Blend(sourcePixel, overlay, options.GraphicsOptions.BlendPercentage);

        Assert.Equal(expected, actual[4, 4]);
    }

    /// <summary>
    /// Verifies all four image-brush source and destination association combinations for one format pair.
    /// </summary>
    /// <typeparam name="TUnassociated">The unassociated source and destination format.</typeparam>
    /// <typeparam name="TAssociated">The associated source and destination format.</typeparam>
    private static void AssertImageBrushAssociationSimilarity<TUnassociated, TAssociated>()
        where TUnassociated : unmanaged, IPixel<TUnassociated>
        where TAssociated : unmanaged, IPixel<TAssociated>
    {
        using Image<TUnassociated> unassociatedSource = CreateAssociationSource<TUnassociated>();
        using Image<TAssociated> associatedSource = CreateAssociationSource<TAssociated>();
        using Image<TUnassociated> unassociatedSourceAndDestination = RenderImageBrush<TUnassociated, TUnassociated>(unassociatedSource);
        using Image<TUnassociated> associatedSourceUnassociatedDestination = RenderImageBrush<TAssociated, TUnassociated>(associatedSource);
        using Image<TAssociated> unassociatedSourceAssociatedDestination = RenderImageBrush<TUnassociated, TAssociated>(unassociatedSource);
        using Image<TAssociated> associatedSourceAndDestination = RenderImageBrush<TAssociated, TAssociated>(associatedSource);

        AssertAssociationSimilarity(unassociatedSourceAndDestination, associatedSourceUnassociatedDestination);
        AssertAssociationSimilarity(unassociatedSourceAssociatedDestination, associatedSourceAndDestination);
        AssertAssociatedStorage(unassociatedSourceAssociatedDestination);
        AssertAssociatedStorage(associatedSourceAndDestination);
    }

    /// <summary>
    /// Verifies all four image-drawing source and destination association combinations for one format pair.
    /// </summary>
    /// <typeparam name="TUnassociated">The unassociated source and destination format.</typeparam>
    /// <typeparam name="TAssociated">The associated source and destination format.</typeparam>
    private static void AssertDrawImageAssociationSimilarity<TUnassociated, TAssociated>()
        where TUnassociated : unmanaged, IPixel<TUnassociated>
        where TAssociated : unmanaged, IPixel<TAssociated>
    {
        using Image<TUnassociated> unassociatedSource = CreateAssociationSource<TUnassociated>();
        using Image<TAssociated> associatedSource = CreateAssociationSource<TAssociated>();
        using Image<TUnassociated> normalizedAssociatedSource = associatedSource.CloneAs<TUnassociated>();
        using Image<TUnassociated> unassociatedSourceAndDestination = RenderDrawImage<TUnassociated, TUnassociated>(unassociatedSource);
        using Image<TUnassociated> associatedSourceUnassociatedDestination = RenderDrawImage<TAssociated, TUnassociated>(associatedSource);
        using Image<TAssociated> unassociatedSourceAssociatedDestination = RenderDrawImage<TUnassociated, TAssociated>(unassociatedSource);
        using Image<TAssociated> associatedSourceAndDestination = RenderDrawImage<TAssociated, TAssociated>(associatedSource);

        // DrawImage normalizes a source whose format differs from its destination before resampling it.
        // Verify that representation conversion independently so a source-conversion failure cannot be mistaken for a resampling failure.
        AssertAssociationSimilarity(unassociatedSource, normalizedAssociatedSource);
        AssertAssociationSimilarity(unassociatedSourceAndDestination, associatedSourceUnassociatedDestination);
        AssertAssociationSimilarity(unassociatedSourceAssociatedDestination, associatedSourceAndDestination);
        AssertAssociatedStorage(unassociatedSourceAssociatedDestination);
        AssertAssociatedStorage(associatedSourceAndDestination);
    }

    /// <summary>
    /// Creates a translucent source image in the requested representation.
    /// </summary>
    /// <typeparam name="TPixel">The source pixel format.</typeparam>
    /// <returns>The populated source image.</returns>
    private static Image<TPixel> CreateAssociationSource<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rgba32[] colors = [new(239, 37, 19, 211), new(17, 227, 83, 0), new(43, 79, 241, 73), new(191, 113, 47, 157)];
        Image<TPixel> image = new(4, 4);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = Color.FromPixel(colors[(x + y) % colors.Length]).ToPixel<TPixel>();
                }
            }
        });

        return image;
    }

    /// <summary>
    /// Renders an image brush into the requested destination representation.
    /// </summary>
    /// <typeparam name="TSource">The source pixel format.</typeparam>
    /// <typeparam name="TDestination">The destination pixel format.</typeparam>
    /// <param name="source">The source image.</param>
    /// <returns>The rendered destination.</returns>
    private static Image<TDestination> RenderImageBrush<TSource, TDestination>(Image<TSource> source)
        where TSource : unmanaged, IPixel<TSource>
        where TDestination : unmanaged, IPixel<TDestination>
    {
        Image<TDestination> destination = new(8, 8, Color.FromPixel(new Rgba32(31, 87, 143, 113)).ToPixel<TDestination>());
        ImageBrush<TSource> brush = new(source, WrapMode.Mirror, WrapMode.Repeat);

        destination.Mutate(context => context.Paint(canvas => canvas.Fill(brush)));

        return destination;
    }

    /// <summary>
    /// Draws an image into the requested destination representation.
    /// </summary>
    /// <typeparam name="TSource">The source pixel format.</typeparam>
    /// <typeparam name="TDestination">The destination pixel format.</typeparam>
    /// <param name="source">The source image.</param>
    /// <returns>The rendered destination.</returns>
    private static Image<TDestination> RenderDrawImage<TSource, TDestination>(Image<TSource> source)
        where TSource : unmanaged, IPixel<TSource>
        where TDestination : unmanaged, IPixel<TDestination>
    {
        Image<TDestination> destination = new(8, 8, Color.FromPixel(new Rgba32(31, 87, 143, 113)).ToPixel<TDestination>());

        destination.Mutate(context => context.Paint(canvas => canvas.DrawImage(source, source.Bounds, destination.Bounds)));

        return destination;
    }

    /// <summary>
    /// Verifies one canvas scene against the two direct 8-bit associated destination layouts.
    /// </summary>
    /// <param name="draw">The scene to render.</param>
    private static void AssertCanvasSceneAssociationSimilarity(CanvasAction draw)
    {
        using Image<Rgba32> rgba = RenderCanvasScene<Rgba32>(draw);
        using Image<Rgba32P> rgbaAssociated = RenderCanvasScene<Rgba32P>(draw);
        using Image<Bgra32> bgra = RenderCanvasScene<Bgra32>(draw);
        using Image<Bgra32P> bgraAssociated = RenderCanvasScene<Bgra32P>(draw);

        AssertAssociationSimilarity(rgba, rgbaAssociated);
        AssertAssociationSimilarity(bgra, bgraAssociated);
        AssertAssociatedStorage(rgbaAssociated);
        AssertAssociatedStorage(bgraAssociated);
    }

    /// <summary>
    /// Renders one canvas scene into the requested destination representation.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="draw">The scene to render.</param>
    /// <returns>The rendered image.</returns>
    private static Image<TPixel> RenderCanvasScene<TPixel>(CanvasAction draw)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        TPixel background = Color.FromPixel(new Rgba32(29, 83, 137, 107)).ToPixel<TPixel>();
        Image<TPixel> image = new(48, 48, background);

        image.Mutate(context => context.Paint(draw));

        return image;
    }

    /// <summary>
    /// Verifies that crossing an 8-bit association boundary changes the canonical associated
    /// color by at most one byte while preserving alpha exactly.
    /// </summary>
    /// <typeparam name="TExpected">The expected image pixel format.</typeparam>
    /// <typeparam name="TActual">The actual image pixel format.</typeparam>
    /// <param name="expected">The expected image.</param>
    /// <param name="actual">The actual image.</param>
    private static void AssertAssociationSimilarity<TExpected, TActual>(Image<TExpected> expected, Image<TActual> actual)
        where TExpected : unmanaged, IPixel<TExpected>
        where TActual : unmanaged, IPixel<TActual>
    {
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (int y = 0; y < expectedAccessor.Height; y++)
            {
                Span<TExpected> expectedRow = expectedAccessor.GetRowSpan(y);
                Span<TActual> actualRow = actualAccessor.GetRowSpan(y);

                for (int x = 0; x < expectedRow.Length; x++)
                {
                    Rgba32P expectedValue = Color.FromPixel(expectedRow[x]).ToPixel<Rgba32P>();
                    Rgba32P actualValue = Color.FromPixel(actualRow[x]).ToPixel<Rgba32P>();

                    // Unassociation divides component quantization error by alpha, so comparing
                    // straight values would manufacture larger differences at low alpha. The
                    // canonical associated byte representation retains the strict one-byte
                    // component limit without floating-point normalization error.
                    int redDifference = Math.Abs(expectedValue.R - actualValue.R);
                    int greenDifference = Math.Abs(expectedValue.G - actualValue.G);
                    int blueDifference = Math.Abs(expectedValue.B - actualValue.B);

                    string failure = $"{typeof(TExpected).Name} -> {typeof(TActual).Name} at ({x}, {y}): expected {expectedValue}, actual {actualValue}.";
                    Assert.True(redDifference <= 1, failure);
                    Assert.True(greenDifference <= 1, failure);
                    Assert.True(blueDifference <= 1, failure);
                    Assert.Equal(expectedValue.A, actualValue.A);
                }
            }
        });
    }

    /// <summary>
    /// Creates an associated color without introducing an intermediate pixel quantization.
    /// </summary>
    /// <param name="color">The unassociated color.</param>
    /// <returns>The same color expressed with associated components.</returns>
    private static Color ToAssociatedColor(Color color)
        => Color.FromScaledVector(color.ToScaledVector4(PixelAlphaRepresentation.Associated), PixelAlphaRepresentation.Associated);

    /// <summary>
    /// Verifies that every stored associated color component is bounded by its alpha component.
    /// </summary>
    /// <typeparam name="TPixel">The associated pixel format.</typeparam>
    /// <param name="image">The associated image.</param>
    private static void AssertAssociatedStorage<TPixel>(Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
        => image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    TPixel pixel = row[x];
                    Vector4 value = pixel.ToScaledVector4();
                    Assert.True(value.X <= value.W && value.Y <= value.W && value.Z <= value.W, $"{typeof(TPixel).Name} at ({x}, {y}) stored {value}.");
                }
            }
        });

    /// <summary>
    /// Identifies the gradient family used by the shared representation test.
    /// </summary>
    private enum GradientKind
    {
        Linear,
        Radial,
        Elliptic,
        Sweep
    }
}
