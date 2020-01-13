// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public class DrawPolygon
    {
        [Benchmark(Baseline = true, Description = "System.Drawing Draw Polygon")]
        public void DrawPolygonSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.InterpolationMode = InterpolationMode.Default;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.HotPink, 10))
                {
                    graphics.DrawPolygon(
                        pen,
                        new[] { new PointF(10, 10), new PointF(550, 50), new PointF(200, 400) });
                }

                using (var stream = new MemoryStream())
                {
                    destination.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                }
            }
        }

        [Benchmark(Description = "ImageSharp Draw Polygon")]
        public void DrawPolygonCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.DrawPolygon(
                    Color.HotPink,
                    10,
                    new Vector2(10, 10),
                    new Vector2(550, 50),
                    new Vector2(200, 400)));

                using (var ms = new MemoryStream())
                {
                    image.SaveAsBmp(ms);
                }
            }
        }
    }
}
