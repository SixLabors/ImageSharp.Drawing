// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

[ShortRunJob]
public class EllipseStressTest
{
    private Image<Rgba32> image;
    private readonly int width = 2560;
    private readonly int height = 1369;
    private readonly Random random = new();

    [GlobalSetup]
    public void Setup() => this.image = new(this.width, this.height, Color.White.ToPixel<Rgba32>());

    [Benchmark]
    public void DrawImageSharp()
    {
        for (int i = 0; i < 20_000; i++)
        {
            Color brushColor = Color.FromPixel(new Rgba32((byte)this.Rand(255), (byte)this.Rand(255), (byte)this.Rand(255), (byte)this.Rand(255)));
            Color penColor = Color.FromPixel(new Rgba32((byte)this.Rand(255), (byte)this.Rand(255), (byte)this.Rand(255), (byte)this.Rand(255)));

            float r = this.Rand(20f) + 1f;
            float x = this.Rand(this.width);
            float y = this.Rand(this.height);
            EllipsePolygon ellipse = new(new PointF(x, y), r);
            this.image.Mutate(
                m =>
                m.Fill(Brushes.Solid(brushColor), ellipse)
                .Draw(Pens.Solid(penColor, this.Rand(5)), ellipse));
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.image.SaveAsPng(TestEnvironment.GetFullPath("artifacts\\ellipse-stress.png"));
        this.image.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Rand(float x)
        => ((float)(((this.random.Next() << 15) | this.random.Next()) & 0x3FFFFFFF) % 1000000) * x / 1000000f;
}
