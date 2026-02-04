// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using CoreBrushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using SDRectangle = System.Drawing.Rectangle;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

public class FillWithPattern
{
    [Benchmark(Baseline = true, Description = "System.Drawing Fill with Pattern")]
    public void DrawPatternPolygonSystemDrawing()
    {
        using (Bitmap destination = new(800, 800))
        using (Graphics graphics = Graphics.FromImage(destination))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (HatchBrush brush = new(HatchStyle.BackwardDiagonal, System.Drawing.Color.HotPink))
            {
                graphics.FillRectangle(brush, new SDRectangle(0, 0, 800, 800)); // can't find a way to flood fill with a brush
            }

            using (MemoryStream stream = new())
            {
                destination.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
            }
        }
    }

    [Benchmark(Description = "ImageSharp Fill with Pattern")]
    public void DrawPatternPolygon3Core()
    {
        using (Image<Rgba32> image = new(800, 800))
        {
            image.Mutate(x => x.Fill(CoreBrushes.BackwardDiagonal(Color.HotPink)));

            using (MemoryStream stream = new())
            {
                image.SaveAsBmp(stream);
            }
        }
    }
}
