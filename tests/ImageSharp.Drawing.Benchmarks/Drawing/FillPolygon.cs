// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SDPointF = System.Drawing.PointF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public class FillPolygon
    {
        private IPath shape;
        private SDPointF[] shapeVertices;

        public const int Size = 4000;

        [GlobalSetup]
        public void Setup()
        {
            this.shape = new EllipsePolygon(Size / 2, Size / 2, Size / 4);
            this.shapeVertices = this.shape.Flatten().Single().Points.ToArray().Select(p => new SDPointF(p.X, p.Y))
                .ToArray();
        }

        [Benchmark(Baseline = true)]
        public void SystemDrawing()
        {
            using (var destination = new Bitmap(Size, Size))

            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillPolygon(
                    System.Drawing.Brushes.HotPink,
                    this.shapeVertices);

                using (var stream = new MemoryStream())
                {
                    destination.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                }
            }
        }

        [Benchmark]
        public void ImageSharp()
        {
            using (var image = new Image<Rgba32>(Size, Size))
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
