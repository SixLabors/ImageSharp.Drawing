// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SDPointF = System.Drawing.PointF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public class DrawBeziers
    {
        [Benchmark(Baseline = true, Description = "System.Drawing Draw Beziers")]
        public void DrawPathSystemDrawing()
        {
            using (var destination = new Bitmap(800, 800))
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.InterpolationMode = InterpolationMode.Default;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var pen = new System.Drawing.Pen(System.Drawing.Color.HotPink, 10))
                {
                    graphics.DrawBeziers(
                        pen,
                        new[] { new SDPointF(10, 500), new SDPointF(30, 10), new SDPointF(240, 30), new SDPointF(300, 500) });
                }

                using (var stream = new MemoryStream())
                {
                    destination.Save(stream, ImageFormat.Bmp);
                }
            }
        }

        [Benchmark(Description = "ImageSharp Draw Beziers")]
        public void DrawLinesCore()
        {
            using (var image = new Image<Rgba32>(800, 800))
            {
                image.Mutate(x => x.DrawBeziers(
                    Color.HotPink,
                    10,
                    new Vector2(10, 500),
                    new Vector2(30, 10),
                    new Vector2(240, 30),
                    new Vector2(300, 500)));

                using (var stream = new MemoryStream())
                {
                    image.SaveAsBmp(stream);
                }
            }
        }
    }
}
