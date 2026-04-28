// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

[MemoryDiagnoser]
[WarmupCount(5)]
[IterationCount(5)]
public class DrawTextRepeatedGlyphs
{
    public const int Width = 1200;
    public const int Height = 280;

    private readonly DrawingOptions drawingOptions = new()
    {
        GraphicsOptions = new GraphicsOptions
        {
            Antialias = true
        }
    };

    private readonly Brush brush = Brushes.Solid(Color.HotPink);

    private Image<Rgba32> defaultImage;
    private WebGPURenderTarget<Rgba32> webGpuTarget;
    private RichTextOptions textOptions;
    private string text;

    [Params(200, 1000)]
    public int GlyphCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Tiled rasterization benefits from a warmed worker pool. Doing this once in setup
        // reduces first-iteration noise without affecting per-method correctness.
        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        int desiredWorkerThreads = Math.Max(minWorkerThreads, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(desiredWorkerThreads, minCompletionPortThreads);
        Parallel.For(0, desiredWorkerThreads, static _ => { });

        Font font = SystemFonts.CreateFont("Arial", 48);
        this.textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(8, 8),
            WrappingLength = Width - 16
        };

        this.defaultImage = new Image<Rgba32>(Width, Height);
        this.webGpuTarget = new WebGPURenderTarget<Rgba32>(Width, Height);

        this.text = new string('A', this.GlyphCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.defaultImage.Dispose();
        this.webGpuTarget.Dispose();
    }

    [Benchmark(Baseline = true, Description = "DrawingCanvas Default Backend")]
    public void DrawingCanvasDefaultBackend()
    {
        this.defaultImage.Mutate(c => c.Paint(
            this.drawingOptions,
            canvas => canvas.DrawText(this.textOptions, this.text, this.brush, null)));
    }

    [Benchmark(Description = "DrawingCanvas WebGPU Backend (NativeSurface)")]
    public void DrawingCanvasWebGPUBackendNativeSurface()
    {
        using DrawingCanvas canvas = this.webGpuTarget.CreateCanvas(this.drawingOptions);
        canvas.DrawText(this.textOptions, this.text, this.brush, null);
        canvas.Flush();
    }
}
