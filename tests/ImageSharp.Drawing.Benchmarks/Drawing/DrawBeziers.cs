// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Pen = System.Drawing.Pen;
using SDPointF = System.Drawing.PointF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

public class DrawBeziers
{
    [Benchmark(Baseline = true, Description = "System.Drawing Draw Beziers")]
    public void DrawPathSystemDrawing()
    {
        using (Bitmap destination = new(800, 800))
        using (Graphics graphics = Graphics.FromImage(destination))
        {
            graphics.InterpolationMode = InterpolationMode.Default;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new(System.Drawing.Color.HotPink, 10))
            {
                graphics.DrawBeziers(
                    pen,
                    [new SDPointF(10, 500), new SDPointF(30, 10), new SDPointF(240, 30), new SDPointF(300, 500)]);
            }

            using (MemoryStream stream = new())
            {
                destination.Save(stream, ImageFormat.Bmp);
            }
        }
    }

    [Benchmark(Description = "ImageSharp Draw Beziers")]
    public void DrawLinesCore()
    {
        using (Image<Rgba32> image = new(800, 800))
        {
            image.Mutate(x => x.DrawBeziers(
                Color.HotPink,
                10,
                new Vector2(10, 500),
                new Vector2(30, 10),
                new Vector2(240, 30),
                new Vector2(300, 500)));

            using (MemoryStream stream = new())
            {
                image.SaveAsBmp(stream);
            }
        }
    }
}
