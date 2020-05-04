// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
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
                image.Mutate(x => x
                    .SetGraphicsOptions(o => o.Antialias = true)
                    .SetTextOptions(o => o.WrapTextWidth = 780)
                    .DrawText(
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
                    new TextGraphicsOptions { GraphicsOptions = { Antialias = true }, TextOptions = { WrapTextWidth = 780 } },
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
                    ApplyKerning = options.TextOptions.ApplyKerning,
                    TabWidth = options.TextOptions.TabWidth,
                    WrappingWidth = options.TextOptions.WrapTextWidth,
                    HorizontalAlignment = options.TextOptions.HorizontalAlignment,
                    VerticalAlignment = options.TextOptions.VerticalAlignment
                };

                IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, style);

                var pathOptions = new ShapeGraphicsOptions() { GraphicsOptions = options.GraphicsOptions };
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
