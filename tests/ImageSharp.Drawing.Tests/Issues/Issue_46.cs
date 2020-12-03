// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    public class Issue_46
    {
        [Fact]
        public void CanRenderCustomFont()
        {
            Font font = CreateFont("icomoon-events.ttf", 175);

            var options = new RendererOptions(font)
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            const int ImageSize = 300;

            var image = new Image<Rgba32>(ImageSize, ImageSize);

            string iconText = char.ConvertFromUtf32(int.Parse("e926", NumberStyles.HexNumber));

            FontRectangle rect = TextMeasurer.Measure(iconText, options);

            float textX = ((ImageSize - rect.Width) * 0.5F) + rect.Left;
            float textY = ((ImageSize - rect.Height) * 0.5F) + (rect.Top * 0.25F);

            image.Mutate(x => x.DrawText(iconText, font, Color.Black, new PointF(textX, textY)));
            image.Save(TestFontUtilities.GetPath("e96.png"));
        }

        private static Font CreateFont(string fontName, int size)
        {
            var fontCollection = new FontCollection();
            string fontPath = TestFontUtilities.GetPath(fontName);
            return fontCollection.Install(fontPath).CreateFont(size);
        }
    }
}
