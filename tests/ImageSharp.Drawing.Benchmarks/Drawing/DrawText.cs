// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SDRectangleF = System.Drawing.RectangleF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    [MemoryDiagnoser]
    public class DrawText
    {
        [Params(10, 100)]
        public int TextIterations { get; set; }

        public string TextPhrase { get; set; } = "Hello World";

        public string TextToRender => string.Join(" ", Enumerable.Repeat(this.TextPhrase, this.TextIterations));

        [Benchmark(Baseline = true, Description = "System.Drawing Draw Text")]
        public void DrawTextSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.InterpolationMode = InterpolationMode.Default;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var font = new Font("Arial", 12, GraphicsUnit.Point))
                {
                    graphics.DrawString(
                        this.TextToRender,
                        font,
                        System.Drawing.Brushes.HotPink,
                        new SDRectangleF(10, 10, 780, 780));
                }
            }
        }

        [Benchmark(Description = "ImageSharp Draw Text - Cached Glyphs")]
        public void DrawTextCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 12);
                image.Mutate(x => x.DrawText(
                    new TextGraphicsOptions { Antialias = true, WrapTextWidth = 780 },
                    this.TextToRender,
                    font,
                    Processing.Brushes.Solid(Color.HotPink),
                    new PointF(10, 10)));
            }
        }

        [Benchmark(Description = "ImageSharp Draw Text - Naive")]
        public void DrawTextCoreOld()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 12);
                image.Mutate(x => DrawTextOldVersion(
                    x,
                    new TextGraphicsOptions { Antialias = true, WrapTextWidth = 780 },
                    this.TextToRender,
                    font,
                    Processing.Brushes.Solid(Color.HotPink),
                    null,
                    new PointF(10, 10)));
            }

            IImageProcessingContext DrawTextOldVersion(
                IImageProcessingContext source,
                TextGraphicsOptions options,
                string text,
                Fonts.Font font,
                IBrush brush,
                IPen pen,
                PointF location)
            {
                const float dpiX = 72;
                const float dpiY = 72;

                var style = new Fonts.RendererOptions(font, dpiX, dpiY, location)
                {
                    ApplyKerning = options.ApplyKerning,
                    TabWidth = options.TabWidth,
                    WrappingWidth = options.WrapTextWidth,
                    HorizontalAlignment = options.HorizontalAlignment,
                    VerticalAlignment = options.VerticalAlignment
                };

                IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, style);

                var pathOptions = new ShapeGraphicsOptions((GraphicsOptions)options);
                if (brush != null)
                {
                    source.Fill(pathOptions, brush, glyphs);
                }

                if (pen != null)
                {
                    source.Draw(pathOptions, pen, glyphs);
                }

                return source;
            }
        }
    }
}
