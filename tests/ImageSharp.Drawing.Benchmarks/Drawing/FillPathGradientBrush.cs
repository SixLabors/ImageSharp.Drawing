// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

public class FillPathGradientBrush
{
    private Image<Rgba32> image;

    [GlobalSetup]
    public void Setup() => this.image = new Image<Rgba32>(100, 100);

    [GlobalCleanup]
    public void Cleanup() => this.image.Dispose();

    [Benchmark]
    public void FillGradientBrush_ImageSharp()
    {
        var star = new Star(50, 50, 5, 20, 45);
        PointF[] points = star.Points.ToArray();
        Color[] colors =
        {
            Color.Red, Color.Yellow, Color.Green, Color.Blue, Color.Purple,
            Color.Red, Color.Yellow, Color.Green, Color.Blue, Color.Purple
        };

        var brush = new PathGradientBrush(points, colors, Color.White);

        this.image.Mutate(x => x.Fill(brush));
    }
}
