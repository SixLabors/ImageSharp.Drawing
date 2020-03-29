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
    public class FillPolygon
    {
        private readonly Polygon shape;

        public FillPolygon()
        {
            this.shape = new Polygon(new LinearLineSegment(
                new Vector2(10, 10),
                new Vector2(550, 50),
                new Vector2(200, 400)));
        }

        [Benchmark(Baseline = true, Description = "System.Drawing Fill Polygon")]
        public void DrawSolidPolygonSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))

            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillPolygon(
                    System.Drawing.Brushes.HotPink,
                    new[] { new SDPoint(10, 10), new SDPoint(550, 50), new SDPoint(200, 400) });

                using (var stream = new MemoryStream())
                {
                    destination.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                }
            }
        }

        [Benchmark(Description = "ImageSharp Fill Polygon")]
        public void DrawSolidPolygonCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.FillPolygon(
                    Color.HotPink,
                    new Vector2(10, 10),
                    new Vector2(550, 50),
                    new Vector2(200, 400)));

                using (var stream = new MemoryStream())
                {
                    image.SaveAsBmp(stream);
                }
            }
        }

        [Benchmark(Description = "ImageSharp Fill Polygon - Cached shape")]
        public void DrawSolidPolygonCoreCached()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.Fill(
                    Color.HotPink,
                    this.shape));

                using (var stream = new MemoryStream())
                {
                    image.SaveAsBmp(stream);
                }
            }
        }
    }
}
