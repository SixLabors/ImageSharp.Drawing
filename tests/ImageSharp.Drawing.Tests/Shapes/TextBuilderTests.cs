// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class TextBuilderTests
{
    [Fact]
    public void TextBuilder_Bounds_AreCorrect()
    {
        Vector2 position = new(5, 5);
        TextOptions options = new(TestFontUtilities.GetFont(TestFonts.OpenSans, 16))
        {
            Origin = position
        };

        const string text = "The quick brown fox jumps over the lazy fox";

        IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, options);

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
}
