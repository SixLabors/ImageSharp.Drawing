// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.Fonts;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public class TextBuilderTests
    {
        [Fact]
        public void TextBuilder_Bounds_AreCorrect()
        {
            Vector2 position = new(5, 5);
            var options = new TextOptions(TestFontUtilities.GetFont(TestFonts.OpenSans, 16))
            {
                Origin = position
            };

            const string text = "Hello World";

            IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, options);

            RectangleF builderBounds = glyphs.Bounds;
            FontRectangle directMeasured = TextMeasurer.MeasureBounds(text, options);
            var measuredBounds = new FontRectangle(new(0, 0), directMeasured.Size + directMeasured.Location);

            Assert.Equal(measuredBounds.X, builderBounds.X);
            Assert.Equal(measuredBounds.Y, builderBounds.Y);
            Assert.Equal(measuredBounds.Width, builderBounds.Width);
            Assert.Equal(measuredBounds.Height, builderBounds.Height);
        }
    }
}
