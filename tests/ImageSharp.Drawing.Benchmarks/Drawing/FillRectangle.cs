// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using SDRectangle = System.Drawing.Rectangle;
using SDSize = System.Drawing.Size;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public class FillRectangle
    {
        [Benchmark(Baseline = true, Description = "System.Drawing Fill Rectangle")]
        public SDSize FillRectangleSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.InterpolationMode = InterpolationMode.Default;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillRectangle(System.Drawing.Brushes.HotPink, new SDRectangle(10, 10, 190, 140));

                return destination.Size;
            }
        }

        [Benchmark(Description = "ImageSharp Fill Rectangle")]
        public Size FillRectangleCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.Fill(Color.HotPink, new Rectangle(10, 10, 190, 140)));

                return new Size(image.Width, image.Height);
            }
        }

        [Benchmark(Description = "ImageSharp Fill Rectangle - As Polygon")]
        public Size FillPolygonCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.FillPolygon(
                    Color.HotPink,
                    new Vector2(10, 10),
                    new Vector2(200, 10),
                    new Vector2(200, 150),
                    new Vector2(10, 150)));

                return new Size(image.Width, image.Height);
            }
        }
    }
}
