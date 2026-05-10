// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class TextBuilderTests
{
    [Fact]
    public void TextBuilder_Bounds_AreCorrect_Paths()
    {
        Vector2 position = new(5, 5);
        TextOptions options = new(TestFontUtilities.GetFont(TestFonts.OpenSans, 16))
        {
            Origin = position
        };

        const string text = "The quick brown fox jumps over the lazy fox";

        IPathCollection glyphs = TextBuilder.GeneratePaths(text, options);

        RectangleF builderBounds = glyphs.Bounds;

        FontRectangle directMeasured = TextMeasurer.MeasureBounds(text, options);
        FontRectangle measuredBounds = new(new Vector2(0, 0), directMeasured.Size + directMeasured.Location);

        Assert.Equal(measuredBounds.X, builderBounds.X);
        Assert.Equal(measuredBounds.Y, builderBounds.Y);
        Assert.Equal(measuredBounds.Width, builderBounds.Width);

        // TextMeasurer will measure the full lineheight of the string.
        // TextBuilder does not include line gaps following the descender since there
        // is no path to include.
        Assert.True(measuredBounds.Height >= builderBounds.Height);
    }

    [Fact]
    public void TextBuilder_Bounds_AreCorrect_Glyphs()
    {
        Vector2 position = new(5, 5);
        TextOptions options = new(TestFontUtilities.GetFont(TestFonts.OpenSans, 16))
        {
            Origin = position
        };

        const string text = "The quick brown fox jumps over the lazy fox";

        IReadOnlyList<GlyphPathCollection> glyphs = TextBuilder.GenerateGlyphs(text, options);

        RectangleF builderBounds = glyphs
            .Select(gp => gp.Bounds)
            .Aggregate(RectangleF.Empty, RectangleF.Union);

        FontRectangle directMeasured = TextMeasurer.MeasureBounds(text, options);
        FontRectangle measuredBounds = new(new Vector2(0, 0), directMeasured.Size + directMeasured.Location);

        Assert.Equal(measuredBounds.X, builderBounds.X);
        Assert.Equal(measuredBounds.Y, builderBounds.Y);
        Assert.Equal(measuredBounds.Width, builderBounds.Width);

        // TextMeasurer will measure the full lineheight of the string.
        // TextBuilder does not include line gaps following the descender since there
        // is no path to include.
        Assert.True(measuredBounds.Height >= builderBounds.Height);
    }

    [Fact]
    public void GlyphPathCollection_PreservesLayerMetadataAndFiltersPaths()
    {
        RectangularPolygon glyphPath = new(0, 0, 10, 12);
        RectangularPolygon paintedPath = new(20, 4, 6, 8);
        SolidPaint paint = new() { CompositeMode = CompositeMode.Multiply };

        GlyphPathCollection.Builder builder = new();
        builder.AddPath(glyphPath);
        builder.AddLayer(0, 1, null, FillRule.NonZero, glyphPath.Bounds, GlyphLayerKind.Glyph);
        builder.AddPath(paintedPath);
        builder.AddLayer(1, 1, paint, FillRule.EvenOdd, paintedPath.Bounds, GlyphLayerKind.Painted);

        GlyphPathCollection glyph = builder.Build();

        Assert.Equal(2, glyph.PathList.Count);
        Assert.Equal(2, glyph.LayerCount);
        Assert.Same(glyphPath, glyph.PathList[0]);
        Assert.Same(paintedPath, glyph.PathList[1]);

        GlyphLayerInfo glyphLayer = glyph.Layers[0];
        Assert.Equal(0, glyphLayer.StartIndex);
        Assert.Equal(1, glyphLayer.Count);
        Assert.Null(glyphLayer.Paint);
        Assert.Equal(IntersectionRule.NonZero, glyphLayer.IntersectionRule);
        Assert.Equal(PixelAlphaCompositionMode.SrcOver, glyphLayer.PixelAlphaCompositionMode);
        Assert.Equal(PixelColorBlendingMode.Normal, glyphLayer.PixelColorBlendingMode);
        Assert.Equal(GlyphLayerKind.Glyph, glyphLayer.Kind);
        Assert.Equal(glyphPath.Bounds, glyphLayer.Bounds);

        GlyphLayerInfo paintedLayer = glyph.Layers[1];
        Assert.Equal(1, paintedLayer.StartIndex);
        Assert.Equal(1, paintedLayer.Count);
        Assert.Same(paint, paintedLayer.Paint);
        Assert.Equal(IntersectionRule.EvenOdd, paintedLayer.IntersectionRule);
        Assert.Equal(PixelAlphaCompositionMode.SrcOver, paintedLayer.PixelAlphaCompositionMode);
        Assert.Equal(PixelColorBlendingMode.Multiply, paintedLayer.PixelColorBlendingMode);
        Assert.Equal(GlyphLayerKind.Painted, paintedLayer.Kind);
        Assert.Equal(paintedPath.Bounds, paintedLayer.Bounds);

        Assert.Equal(2, glyph.ToPathCollection().Count());
        Assert.Same(glyphPath, glyph.ToPathCollection(layer => layer.Kind == GlyphLayerKind.Glyph).Single());
        Assert.Same(paintedPath, glyph.GetLayerPaths(1).Single());
    }

    [Fact]
    public void GlyphPathCollection_Transform_TransformsPathsAndLayerBounds()
    {
        RectangularPolygon path = new(2, 3, 10, 5);

        GlyphPathCollection.Builder builder = new();
        builder.AddPath(path);
        builder.AddLayer(0, 1, null, FillRule.NonZero, path.Bounds);

        GlyphPathCollection glyph = builder.Build();
        Matrix4x4 matrix = Matrix4x4.CreateTranslation(7, 11, 0);

        GlyphPathCollection transformed = glyph.Transform(matrix);

        Assert.NotSame(glyph, transformed);
        Assert.Equal(new RectangleF(9, 14, 10, 5), transformed.Bounds);
        Assert.Equal(new RectangleF(9, 14, 10, 5), transformed.Layers[0].Bounds);
        Assert.Equal(glyph.Layers[0].StartIndex, transformed.Layers[0].StartIndex);
        Assert.Equal(glyph.Layers[0].Count, transformed.Layers[0].Count);
        Assert.Equal(glyph.Layers[0].Kind, transformed.Layers[0].Kind);
        Assert.Equal(new RectangleF(2, 3, 10, 5), glyph.Bounds);
    }

    [Fact]
    public void GlyphPathCollection_BuilderRejectsOutOfRangeLayerSpan()
    {
        GlyphPathCollection.Builder builder = new();
        builder.AddPath(new RectangularPolygon(0, 0, 10, 10));

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.AddLayer(1, 1, null, FillRule.NonZero, RectangleF.Empty));

        Assert.Equal("count", ex.ParamName);
    }

    [Theory]
    [InlineData(FillRule.NonZero, IntersectionRule.NonZero)]
    [InlineData(FillRule.EvenOdd, IntersectionRule.EvenOdd)]
    public void TextUtilities_MapsFillRules(FillRule fillRule, IntersectionRule intersectionRule)
        => Assert.Equal(intersectionRule, TextUtilities.MapFillRule(fillRule));

    [Theory]
    [InlineData(CompositeMode.Clear, PixelAlphaCompositionMode.Clear)]
    [InlineData(CompositeMode.Src, PixelAlphaCompositionMode.Src)]
    [InlineData(CompositeMode.Dest, PixelAlphaCompositionMode.Dest)]
    [InlineData(CompositeMode.SrcOver, PixelAlphaCompositionMode.SrcOver)]
    [InlineData(CompositeMode.DestOver, PixelAlphaCompositionMode.DestOver)]
    [InlineData(CompositeMode.SrcIn, PixelAlphaCompositionMode.SrcIn)]
    [InlineData(CompositeMode.DestIn, PixelAlphaCompositionMode.DestIn)]
    [InlineData(CompositeMode.SrcOut, PixelAlphaCompositionMode.SrcOut)]
    [InlineData(CompositeMode.DestOut, PixelAlphaCompositionMode.DestOut)]
    [InlineData(CompositeMode.SrcAtop, PixelAlphaCompositionMode.SrcAtop)]
    [InlineData(CompositeMode.DestAtop, PixelAlphaCompositionMode.DestAtop)]
    [InlineData(CompositeMode.Xor, PixelAlphaCompositionMode.Xor)]
    [InlineData(CompositeMode.Multiply, PixelAlphaCompositionMode.SrcOver)]
    public void TextUtilities_MapsAlphaCompositionModes(CompositeMode compositeMode, PixelAlphaCompositionMode alphaCompositionMode)
        => Assert.Equal(alphaCompositionMode, TextUtilities.MapCompositionMode(compositeMode));

    [Theory]
    [InlineData(CompositeMode.Plus, PixelColorBlendingMode.Add)]
    [InlineData(CompositeMode.Screen, PixelColorBlendingMode.Screen)]
    [InlineData(CompositeMode.Overlay, PixelColorBlendingMode.Overlay)]
    [InlineData(CompositeMode.Darken, PixelColorBlendingMode.Darken)]
    [InlineData(CompositeMode.Lighten, PixelColorBlendingMode.Lighten)]
    [InlineData(CompositeMode.HardLight, PixelColorBlendingMode.HardLight)]
    [InlineData(CompositeMode.Multiply, PixelColorBlendingMode.Multiply)]
    [InlineData(CompositeMode.SrcOver, PixelColorBlendingMode.Normal)]
    [InlineData(CompositeMode.ColorDodge, PixelColorBlendingMode.Normal)]
    public void TextUtilities_MapsColorBlendingModes(CompositeMode compositeMode, PixelColorBlendingMode colorBlendingMode)
        => Assert.Equal(colorBlendingMode, TextUtilities.MapBlendingMode(compositeMode));

    [Fact]
    public void TextUtilities_CloneOrReturnForRules_ReturnsDrawingOptionsWhenRulesMatch()
    {
        DrawingOptions options = new();

        DrawingOptions result = options.CloneOrReturnForRules(
            options.ShapeOptions.IntersectionRule,
            options.GraphicsOptions.AlphaCompositionMode,
            options.GraphicsOptions.ColorBlendingMode);

        Assert.Same(options, result);
    }

    [Fact]
    public void TextUtilities_CloneOrReturnForRules_ClonesDrawingOptionsWhenRulesDiffer()
    {
        DrawingOptions options = new()
        {
            Transform = Matrix4x4.CreateTranslation(12, 23, 0)
        };

        DrawingOptions result = options.CloneOrReturnForRules(
            IntersectionRule.EvenOdd,
            PixelAlphaCompositionMode.SrcIn,
            PixelColorBlendingMode.Multiply);

        Assert.NotSame(options, result);
        Assert.NotSame(options.ShapeOptions, result.ShapeOptions);
        Assert.NotSame(options.GraphicsOptions, result.GraphicsOptions);
        Assert.Equal(IntersectionRule.EvenOdd, result.ShapeOptions.IntersectionRule);
        Assert.Equal(PixelAlphaCompositionMode.SrcIn, result.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(PixelColorBlendingMode.Multiply, result.GraphicsOptions.ColorBlendingMode);
        Assert.Equal(options.Transform, result.Transform);
        Assert.Equal(IntersectionRule.NonZero, options.ShapeOptions.IntersectionRule);
        Assert.Equal(PixelAlphaCompositionMode.SrcOver, options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(PixelColorBlendingMode.Normal, options.GraphicsOptions.ColorBlendingMode);
    }

    [Fact]
    public void TextBuilder_GenerateGlyphs_ReturnsLayerMetadataForSimpleText()
    {
        TextOptions options = new(TestFontUtilities.GetFont(TestFonts.OpenSans, 18))
        {
            Origin = new Vector2(4, 7)
        };

        IReadOnlyList<GlyphPathCollection> glyphs = TextBuilder.GenerateGlyphs("Hi", options);

        Assert.Equal(2, glyphs.Count);

        foreach (GlyphPathCollection glyph in glyphs)
        {
            Assert.True(glyph.PathList.Count > 0);
            Assert.Equal(1, glyph.LayerCount);
            Assert.Equal(glyph.PathList.Count, glyph.Layers.Sum(layer => layer.Count));
            Assert.Equal(GlyphLayerKind.Glyph, glyph.Layers[0].Kind);
            Assert.Equal(glyph.Paths.Bounds, glyph.ToPathCollection().Bounds);
        }
    }

    [Fact]
    public void TextBuilder_PathOverload_TranslatesPathWhenOptionsHaveOrigin()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        const string text = "Path";

        TextOptions optionsWithOrigin = new(font)
        {
            Origin = new Vector2(17, 29)
        };

        TextOptions optionsWithoutOrigin = new(font);
        Path path = new([new PointF(0, 100), new PointF(600, 100)]);

        IPathCollection withOrigin = TextBuilder.GeneratePaths(text, path, optionsWithOrigin);
        IPathCollection translatedPaths = TextBuilder.GeneratePaths(text, path.Translate(optionsWithOrigin.Origin), optionsWithoutOrigin);
        IReadOnlyList<GlyphPathCollection> glyphs = TextBuilder.GenerateGlyphs(text, path, optionsWithOrigin);
        RectangleF glyphBounds = glyphs.Select(glyph => glyph.Bounds).Aggregate(RectangleF.Union);

        AssertRectEqual(translatedPaths.Bounds, withOrigin.Bounds);
        AssertRectEqual(withOrigin.Bounds, glyphBounds);
    }

    private static void AssertRectEqual(RectangleF expected, RectangleF actual, int precision = 3)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Width, actual.Width, precision);
        Assert.Equal(expected.Height, actual.Height, precision);
    }
}
