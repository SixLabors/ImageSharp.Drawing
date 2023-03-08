// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Brush = SixLabors.ImageSharp.Drawing.Processing.Brush;
using Pen = SixLabors.ImageSharp.Drawing.Processing.Pen;
using SDRectangleF = System.Drawing.RectangleF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing
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
                using (var font = new System.Drawing.Font("Arial", 12, GraphicsUnit.Point))
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
            using var image = new Image<Rgba32>(800, 800);
            Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 12);
            TextDrawingOptions textOptions = new(font)
            {
                WrappingLength = 780,
                Origin = new PointF(10, 10)
            };

            image.Mutate(x => x.DrawText(
                textOptions,
                this.TextToRender,
                Processing.Pens.Solid(Color.HotPink, 10)));
        }

        [Benchmark(Description = "ImageSharp Draw Text Outline - Naive")]
        public void DrawTextCoreOld()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 12);
                TextDrawingOptions textOptions = new(font)
                {
                    WrappingLength = 780,
                    Origin = new PointF(10, 10)
                };

                image.Mutate(
                    x => DrawTextOldVersion(
                        x,
                        new DrawingOptions { GraphicsOptions = { Antialias = true } },
                        textOptions,
                        this.TextToRender,
                        null,
                        Processing.Pens.Solid(Color.HotPink, 10)));
            }

            static IImageProcessingContext DrawTextOldVersion(
                IImageProcessingContext source,
                DrawingOptions options,
                TextDrawingOptions textOptions,
                string text,
                Brush brush,
                Pen pen)
            {
                IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, textOptions);

                var pathOptions = new DrawingOptions() { GraphicsOptions = options.GraphicsOptions };
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

        // 11/12/2020
        // BenchmarkDotNet=v0.12.1, OS=Windows 10.0.18363.1198 (1909/November2018Update/19H2)
        // Intel Core i7-7700HQ CPU 2.80GHz (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
        //     .NET Core SDK=5.0.100-preview.6.20318.15
        // [Host]     : .NET Core 3.1.1 (CoreCLR 4.700.19.60701, CoreFX 4.700.19.60801), X64 RyuJIT
        // DefaultJob : .NET Core 3.1.1 (CoreCLR 4.700.19.60701, CoreFX 4.700.19.60801), X64 RyuJIT
        //
        //
        // |        Method | TextIterations |         Mean |      Error |     StdDev |  Ratio | RatioSD |     Gen 0 |    Gen 1 | Gen 2 | Allocated |
        // |-------------- |--------------- |-------------:|-----------:|-----------:|-------:|--------:|----------:|---------:|------:|----------:|
        // | SystemDrawing |              1 |     55.03 us |   0.199 us |   0.186 us |   5.43 |    0.03 |         - |        - |     - |      40 B |
        // |    ImageSharp |              1 |  2,161.92 us |   4.203 us |   3.510 us | 213.14 |    0.52 |  253.9063 |        - |     - |  804452 B |
        // |     SkiaSharp |              1 |     10.14 us |   0.040 us |   0.031 us |   1.00 |    0.00 |    0.5341 |        - |     - |    1680 B |
        // |               |                |              |            |            |        |         |           |          |       |           |
        // | SystemDrawing |             20 |  1,450.12 us |   3.583 us |   3.176 us |  27.36 |    0.11 |         - |        - |     - |    3696 B |
        // |    ImageSharp |             20 | 28,559.17 us | 244.615 us | 216.844 us | 538.85 |    3.98 | 2312.5000 | 781.2500 |     - | 9509056 B |
        // |     SkiaSharp |             20 |     53.00 us |   0.166 us |   0.147 us |   1.00 |    0.00 |    1.6479 |        - |     - |    5336 B |
    }
}
