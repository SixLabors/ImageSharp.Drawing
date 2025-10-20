// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SDRectangle = System.Drawing.Rectangle;
using SDSize = System.Drawing.Size;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

public class FillRectangle
{
    [Benchmark(Baseline = true, Description = "System.Drawing Fill Rectangle")]
    public SDSize FillRectangleSystemDrawing()
    {
        using (Bitmap destination = new(800, 800))
        using (Graphics graphics = Graphics.FromImage(destination))
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
        using (Image<Rgba32> image = new(800, 800))
        {
            image.Mutate(x => x.Fill(Color.HotPink, new Rectangle(10, 10, 190, 140)));

            return new Size(image.Width, image.Height);
        }
    }

    [Benchmark(Description = "ImageSharp Fill Rectangle - As Polygon")]
    public Size FillPolygonCore()
    {
        using (Image<Rgba32> image = new(800, 800))
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
