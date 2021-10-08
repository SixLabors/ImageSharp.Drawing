// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using CoreBrushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using SDRectangle = System.Drawing.Rectangle;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing
{
    public class FillWithPattern
    {
        [Benchmark(Baseline = true, Description = "System.Drawing Fill with Pattern")]
        public void DrawPatternPolygonSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var brush = new HatchBrush(HatchStyle.BackwardDiagonal, System.Drawing.Color.HotPink))
                {
                    graphics.FillRectangle(brush, new SDRectangle(0, 0, 800, 800)); // can't find a way to flood fill with a brush
                }

                using (var stream = new MemoryStream())
                {
                    destination.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                }
            }
        }

        [Benchmark(Description = "ImageSharp Fill with Pattern")]
        public void DrawPatternPolygon3Core()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.Fill(CoreBrushes.BackwardDiagonal(Color.HotPink)));

                using (var stream = new MemoryStream())
                {
                    image.SaveAsBmp(stream);
                }
            }
        }
    }
}
