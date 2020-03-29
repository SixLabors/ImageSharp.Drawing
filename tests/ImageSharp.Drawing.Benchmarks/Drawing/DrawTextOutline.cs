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
    public class DrawTextOutline
    {
        [Params(10, 100)]
        public int TextIterations { get; set; }

        public string TextPhrase { get; set; } = "Hello World";

        public string TextToRender => string.Join(" ", Enumerable.Repeat(this.TextPhrase, this.TextIterations));

        [Benchmark(Baseline = true, Description = "System.Drawing Draw Text Outline")]
        public void DrawTextSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.InterpolationMode = InterpolationMode.Default;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.HotPink, 10))
                using (var font = new Font("Arial", 12, GraphicsUnit.Point))
                using (var gp = new GraphicsPath())
                {
                    gp.AddString(
                        this.TextToRender,
                        font.FontFamily,
                        (int)font.Style,
                        font.Size,
                        new SDRectangleF(10, 10, 780, 780),
                        new StringFormat());

                    graphics.DrawPath(pen, gp);
                }
            }
        }

        [Benchmark(Description = "ImageSharp Draw Text Outline - Cached Glyphs")]
        public void DrawTextCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 12);

                image.Mutate(x => x.DrawText(
                    new TextGraphicsOptions { Antialias = true, WrapTextWidth = 780 },
                    this.TextToRender,
                    font,
                    Processing.Pens.Solid(Color.HotPink, 10),
                    new PointF(10, 10)));
            }
        }

        [Benchmark(Description = "ImageSharp Draw Text Outline - Naive")]
        public void DrawTextCoreOld()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 12);
                image.Mutate(
                    x => DrawTextOldVersion(
                        x,
                        new TextGraphicsOptions { Antialias = true, WrapTextWidth = 780 },
                        this.TextToRender,
                        font,
                        null,
                        Processing.Pens.Solid(Color.HotPink, 10),
                        new PointF(10, 10)));
            }

            IImageProcessingContext DrawTextOldVersion(
                IImageProcessingContext source,
                TextGraphicsOptions options,
                string text,
                SixLabors.Fonts.Font font,
                IBrush brush,
                IPen pen,
                PointF location)
            {
                var style = new SixLabors.Fonts.RendererOptions(font, options.DpiX, options.DpiY, location)
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
