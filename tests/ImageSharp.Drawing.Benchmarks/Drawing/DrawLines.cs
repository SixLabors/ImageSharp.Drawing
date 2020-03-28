// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SDPoint = System.Drawing.Point;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public class DrawLines
    {
        [Benchmark(Baseline = true, Description = "System.Drawing Draw Lines")]
        public void DrawPathSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.InterpolationMode = InterpolationMode.Default;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var pen = new System.Drawing.Pen(System.Drawing.Color.HotPink, 10))
                {
                    graphics.DrawLines(
                        pen,
                        new[] { new SDPoint(10, 10), new SDPoint(550, 50), new SDPoint(200, 400) });
                }

                using (var stream = new MemoryStream())
                {
                    destination.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                }
            }
        }

        [Benchmark(Description = "ImageSharp Draw Lines")]
        public void DrawLinesCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.DrawLines(
                    Color.HotPink,
                    10,
                    new Vector2(10, 10),
                    new Vector2(550, 50),
                    new Vector2(200, 400)));

                using (var stream = new MemoryStream())
                {
                    image.SaveAsBmp(stream);
                }
            }
        }
    }
}
