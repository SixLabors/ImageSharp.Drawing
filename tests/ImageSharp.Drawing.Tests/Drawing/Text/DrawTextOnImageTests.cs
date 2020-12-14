// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Text
{
    [GroupOutput("Drawing/Text")]
    public class DrawTextOnImageTests
    {
        private const string AB = "AB\nAB";

        private const string TestText = "Sphinx of black quartz, judge my vow\n0123456789";

        private static readonly ImageComparer TextDrawingComparer = TestEnvironment.IsFramework
            ? ImageComparer.TolerantPercentage(1e-3f) // Relax comparison on .NET Framework
            : ImageComparer.TolerantPercentage(1e-5f);

        private static readonly ImageComparer OutlinedTextDrawingComparer = ImageComparer.TolerantPercentage(5e-4f);

        public DrawTextOnImageTests(ITestOutputHelper output)
            => this.Output = output;

        private ITestOutputHelper Output { get; }

        [Theory]
        [WithSolidFilledImages(1276, 336, "White", PixelTypes.Rgba32, true)]
        [WithSolidFilledImages(1276, 336, "White", PixelTypes.Rgba32, false)]
        public void EmojiFontRendering<TPixel>(TestImageProvider<TPixel> provider, bool enableColorFonts)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont("OpenSans-Regular.ttf", 70);
            FontFamily emjoiFontFamily = CreateFont("TwemojiMozilla.ttf", 36).Family;

            Color color = Color.Black;
            string text = "A short piece of text 😀 with an emoji";

            var textGraphicOptions = new TextGraphicsOptions
            {
                TextOptions =
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FallbackFonts = { emjoiFontFamily },
                    RenderColorFonts = enableColorFonts
                }
            };

            provider.VerifyOperation(
              TextDrawingComparer,
              img =>
              {
                  var center = new PointF(img.Width / 2, img.Height / 2);
                  img.Mutate(i => i.DrawText(textGraphicOptions, text, font, color, center));
              },
              $"ColorFontsEnabled-{enableColorFonts}");
        }

        [Theory]
        [WithSolidFilledImages(276, 336, "White", PixelTypes.Rgba32)]
        public void DoesntThrowExceptionWhenOverlappingRightEdge_Issue688<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont("OpenSans-Regular.ttf", 36);
            Color color = Color.Black;
            string text = "A short piece of text";

            using (Image<TPixel> img = provider.GetImage())
            {
                // measure the text size
                FontRectangle size = TextMeasurer.Measure(text, new RendererOptions(font));

                // find out how much we need to scale the text to fill the space (up or down)
                float scalingFactor = Math.Min(img.Width / size.Width, img.Height / size.Height);

                // create a new font
                var scaledFont = new Font(font, scalingFactor * font.Size);

                var center = new PointF(img.Width / 2, img.Height / 2);
                var textGraphicOptions = new TextGraphicsOptions
                {
                    TextOptions =
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                img.Mutate(i => i.DrawText(textGraphicOptions, text, scaledFont, color, center));
            }
        }

        [Theory]
        [WithSolidFilledImages(1500, 500, "White", PixelTypes.Rgba32)]
        public void DoesntThrowExceptionWhenOverlappingRightEdge_Issue688_2<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using (Image<TPixel> img = provider.GetImage())
            {
                Font font = CreateFont("OpenSans-Regular.ttf", 39);
                string text = new string('a', 10000);

                Rgba32 color = Color.Black;
                var point = new PointF(100, 100);

                img.Mutate(ctx => ctx.DrawText(text, font, color, point));
            }
        }

        [Theory]
        [WithSolidFilledImages(200, 200, "White", PixelTypes.Rgba32)]
        public void OpenSansJWithNoneZeroShouldntExtendPastGlyphe<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using (Image<TPixel> img = provider.GetImage())
            {
                Font font = CreateFont("OpenSans-Regular.ttf", 50);
                Color color = Color.Black;

                img.Mutate(ctx => ctx.DrawText(TestText, font, Color.Black, new PointF(-50, 2)));

                Assert.Equal(Color.White.ToPixel<TPixel>(), img[173, 2]);
            }
        }

        [Theory]
        [WithSolidFilledImages(20, 50, "White", PixelTypes.Rgba32, 50, 0, 0, "OpenSans-Regular.ttf", "i")]
        [WithSolidFilledImages(200, 150, "White", PixelTypes.Rgba32, 50, 0, 0, "SixLaborsSampleAB.woff", AB)]
        [WithSolidFilledImages(900, 150, "White", PixelTypes.Rgba32, 50, 0, 0, "OpenSans-Regular.ttf", TestText)]
        [WithSolidFilledImages(400, 45, "White", PixelTypes.Rgba32, 20, 0, 0, "OpenSans-Regular.ttf", TestText)]
        [WithSolidFilledImages(1100, 200, "White", PixelTypes.Rgba32, 50, 150, 50, "OpenSans-Regular.ttf", TestText)]
        public void FontShapesAreRenderedCorrectly<TPixel>(
            TestImageProvider<TPixel> provider,
            int fontSize,
            int x,
            int y,
            string fontName,
            string text)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont(fontName, fontSize);

            provider.RunValidatingProcessorTest(
                c => c.DrawText(text, font, Color.Black, new PointF(x, y)),
                $"{fontName}-{fontSize}-{ToTestOutputDisplayText(text)}-({x},{y})",
                TextDrawingComparer,
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: true);
        }

        /// <summary>
        /// Based on:
        /// https://github.com/SixLabors/ImageSharp/issues/572
        /// </summary>
        [Theory]
        [WithSolidFilledImages(2480, 3508, "White", PixelTypes.Rgba32)]
        public void FontShapesAreRenderedCorrectly_LargeText<TPixel>(
            TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont("OpenSans-Regular.ttf", 36);

            var sb = new StringBuilder();
            string str = Repeat(" ", 78) + "THISISTESTWORDSTHISISTESTWORDSTHISISTESTWORDSTHISISTESTWORDSTHISISTESTWORDS";
            sb.Append(str);

            string newLines = Repeat(Environment.NewLine, 80);
            sb.Append(newLines);

            for (int i = 0; i < 10; i++)
            {
                sb.AppendLine(str);
            }

            var textOptions = new TextGraphicsOptions
            {
                TextOptions =
                {
                    ApplyKerning = true,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                }
            };

            Color color = Color.Black;

            // Strict comparer, because the image is sparse:
            var comparer = ImageComparer.TolerantPercentage(1e-6f);

            provider.VerifyOperation(
                comparer,
                img => img.Mutate(c => c.DrawText(textOptions, sb.ToString(), font, color, new PointF(10, 1))),
                false,
                true);
        }

        [Theory]
        [WithSolidFilledImages(400, 600, "White", PixelTypes.Rgba32, 1, 5)]
        [WithSolidFilledImages(400, 600, "White", PixelTypes.Rgba32, 1.5, 3)]
        [WithSolidFilledImages(400, 600, "White", PixelTypes.Rgba32, 2, 2)]
        public void FontShapesAreRenderedCorrectly_WithLineSpacing<TPixel>(
            TestImageProvider<TPixel> provider,
            float lineSpacing,
            int lineCount)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont("OpenSans-Regular.ttf", 16);

            var sb = new StringBuilder();
            string str = "Lorem ipsum dolor sit amet, consectetuer adipiscing elit. Maecenas porttitor congue massa. Fusce posuere, magna sed pulvinar ultricies, purus lectus malesuada libero, sit amet commodo magna eros quis urna.";

            for (int i = 0; i < lineCount; i++)
            {
                sb.AppendLine(str);
            }

            var textOptions = new TextGraphicsOptions
            {
                TextOptions =
                {
                    ApplyKerning = true,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    LineSpacing = lineSpacing,
                    WrapTextWidth = 300
                }
            };

            Color color = Color.Black;

            // Strict comparer, because the image is sparse:
            var comparer = ImageComparer.TolerantPercentage(1e-6f);

            provider.VerifyOperation(
                comparer,
                img => img.Mutate(c => c.DrawText(textOptions, sb.ToString(), font, color, new PointF(10, 1))),
                $"linespacing_{lineSpacing}_linecount_{lineCount}",
                false,
                false);
        }

        [Theory]
        [WithSolidFilledImages(200, 150, "White", PixelTypes.Rgba32, 50, 0, 0, "SixLaborsSampleAB.woff", AB)]
        [WithSolidFilledImages(900, 150, "White", PixelTypes.Rgba32, 50, 0, 0, "OpenSans-Regular.ttf", TestText)]
        [WithSolidFilledImages(1100, 200, "White", PixelTypes.Rgba32, 50, 150, 50, "OpenSans-Regular.ttf", TestText)]
        public void FontShapesAreRenderedCorrectlyWithAPen<TPixel>(
            TestImageProvider<TPixel> provider,
            int fontSize,
            int x,
            int y,
            string fontName,
            string text)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont(fontName, fontSize);
            Color color = Color.Black;

            provider.VerifyOperation(
                OutlinedTextDrawingComparer,
                img => img.Mutate(c => c.DrawText(text, new Font(font, fontSize), null, Pens.Solid(color, 1), new PointF(x, y))),
                $"pen_{fontName}-{fontSize}-{ToTestOutputDisplayText(text)}-({x},{y})",
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: true);
        }

        [Theory]
        [WithSolidFilledImages(200, 150, "White", PixelTypes.Rgba32, 50, 0, 0, "SixLaborsSampleAB.woff", AB)]
        [WithSolidFilledImages(900, 150, "White", PixelTypes.Rgba32, 50, 0, 0, "OpenSans-Regular.ttf", TestText)]
        [WithSolidFilledImages(1100, 200, "White", PixelTypes.Rgba32, 50, 150, 50, "OpenSans-Regular.ttf", TestText)]
        public void FontShapesAreRenderedCorrectlyWithAPenPatterned<TPixel>(
            TestImageProvider<TPixel> provider,
            int fontSize,
            int x,
            int y,
            string fontName,
            string text)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont(fontName, fontSize);
            Color color = Color.Black;

            provider.VerifyOperation(
                OutlinedTextDrawingComparer,
                img => img.Mutate(c => c.DrawText(text, new Font(font, fontSize), null, Pens.DashDot(color, 3), new PointF(x, y))),
                $"pen_{fontName}-{fontSize}-{ToTestOutputDisplayText(text)}-({x},{y})",
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: true);
        }

        [Theory]
        [WithSolidFilledImages(1000, 1500, "White", PixelTypes.Rgba32, "OpenSans-Regular.ttf")]
        public void TextPositioningIsRobust<TPixel>(TestImageProvider<TPixel> provider, string fontName)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font = CreateFont(fontName, 30);

            string text = Repeat(
                "Beware the Jabberwock, my son!  The jaws that bite, the claws that catch!  Beware the Jubjub bird, and shun The frumious Bandersnatch!\n",
                20);
            var textOptions = new TextGraphicsOptions
            {
                TextOptions =
                {
                    WrapTextWidth = 1000
                }
            };

            string details = fontName.Replace(" ", string.Empty);

            // Based on the reported 0.1755% difference with AccuracyMultiple = 8
            // We should avoid quality regressions leading to higher difference!
            var comparer = ImageComparer.TolerantPercentage(0.2f);

            provider.RunValidatingProcessorTest(
                x => x.DrawText(textOptions, text, font, Color.Black, new PointF(10, 50)),
                details,
                comparer,
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }

        private static string Repeat(string str, int times) => string.Concat(Enumerable.Repeat(str, times));

        private static string ToTestOutputDisplayText(string text)
        {
            string fnDisplayText = text.Replace("\n", string.Empty);
            fnDisplayText = fnDisplayText.Substring(0, Math.Min(fnDisplayText.Length, 4));
            return fnDisplayText;
        }

        private static Font CreateFont(string fontName, int size)
        {
            var fontCollection = new FontCollection();
            string fontPath = TestFontUtilities.GetPath(fontName);
            Font font = fontCollection.Install(fontPath).CreateFont(size);
            return font;
        }
    }
}
