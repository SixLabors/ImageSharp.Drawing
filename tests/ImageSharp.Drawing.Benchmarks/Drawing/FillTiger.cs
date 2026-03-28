// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using SDColor = System.Drawing.Color;
using SDPen = System.Drawing.Pen;
using SDSolidBrush = System.Drawing.SolidBrush;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

/// <summary>
/// Benchmarks rendering the Ghostscript Tiger SVG (~240 path elements with fills and strokes)
/// across SkiaSharp, System.Drawing, ImageSharp (CPU), and ImageSharp (WebGPU).
/// </summary>
public class FillTiger
{
    private const float Scale = 4f;
    private const int Width = 800;
    private const int Height = 800;

    private static readonly string SvgFilePath =
        TestFile.GetInputFileFullPath(TestImages.Svg.GhostscriptTiger);

    private SKSurface skSurface;
    private List<(SKPath Path, SKPaint FillPaint, SKPaint StrokePaint)> skElements;

    private Bitmap sdBitmap;
    private Graphics sdGraphics;
    private List<(GraphicsPath Path, SDSolidBrush Fill, SDPen Stroke)> sdElements;

    private Image<Rgba32> image;
    private List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> isElements;

    private WebGPUDrawingBackend webGpuBackend;
    private Configuration webGpuConfiguration;
    private NativeCanvasFrame<Rgba32> webGpuNativeFrame;
    private nint webGpuNativeTextureHandle;
    private nint webGpuNativeTextureViewHandle;

    [GlobalSetup]
    public void Setup()
    {
        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        int desiredWorkerThreads = Math.Max(minWorkerThreads, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(desiredWorkerThreads, minCompletionPortThreads);
        Parallel.For(0, desiredWorkerThreads, static _ => { });

        List<SvgBenchmarkHelper.SvgElement> elements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);

        this.skSurface = SKSurface.Create(new SKImageInfo(Width, Height));
        this.skElements = SvgBenchmarkHelper.BuildSkiaElements(elements, Scale);

        this.sdBitmap = new Bitmap(Width, Height);
        this.sdGraphics = Graphics.FromImage(this.sdBitmap);
        this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        this.sdElements = SvgBenchmarkHelper.BuildSystemDrawingElements(elements, Scale);

        this.image = new Image<Rgba32>(Width, Height);
        this.isElements = SvgBenchmarkHelper.BuildImageSharpElements(elements, Scale);

        this.webGpuBackend = new WebGPUDrawingBackend();
        this.webGpuConfiguration = Configuration.Default.Clone();
        this.webGpuConfiguration.SetDrawingBackend(this.webGpuBackend);

        if (!WebGPUTestNativeSurfaceAllocator.TryCreate<Rgba32>(
                Width,
                Height,
                out NativeSurface nativeSurface,
                out this.webGpuNativeTextureHandle,
                out this.webGpuNativeTextureViewHandle,
                out string nativeSurfaceError))
        {
            throw new InvalidOperationException(
                $"Unable to create benchmark native WebGPU target. Error='{nativeSurfaceError}'.");
        }

        this.webGpuNativeFrame = new NativeCanvasFrame<Rgba32>(
            new Rectangle(0, 0, Width, Height),
            nativeSurface);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        this.sdGraphics.Clear(SDColor.Transparent);
        this.skSurface.Canvas.Clear(SKColors.Transparent);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach ((SKPath path, SKPaint fill, SKPaint stroke) in this.skElements)
        {
            path.Dispose();
            fill?.Dispose();
            stroke?.Dispose();
        }

        this.skSurface.Dispose();

        foreach ((GraphicsPath path, SDSolidBrush fill, SDPen stroke) in this.sdElements)
        {
            path.Dispose();
            fill?.Dispose();
            stroke?.Dispose();
        }

        this.sdGraphics.Dispose();
        this.sdBitmap.Dispose();

        this.image.Dispose();

        WebGPUTestNativeSurfaceAllocator.Release(
            this.webGpuNativeTextureHandle,
            this.webGpuNativeTextureViewHandle);
        this.webGpuNativeTextureHandle = 0;
        this.webGpuNativeTextureViewHandle = 0;
        this.webGpuBackend.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void SkiaSharp()
    {
        SKCanvas canvas = this.skSurface.Canvas;
        foreach ((SKPath path, SKPaint fillPaint, SKPaint strokePaint) in this.skElements)
        {
            if (fillPaint is not null)
            {
                canvas.DrawPath(path, fillPaint);
            }

            if (strokePaint is not null)
            {
                canvas.DrawPath(path, strokePaint);
            }
        }
    }

    [Benchmark]
    public void SystemDrawing()
    {
        foreach ((GraphicsPath path, SDSolidBrush fill, SDPen stroke) in this.sdElements)
        {
            if (fill is not null)
            {
                this.sdGraphics.FillPath(fill, path);
            }

            if (stroke is not null)
            {
                this.sdGraphics.DrawPath(stroke, path);
            }
        }
    }

    [Benchmark]
    public void ImageSharp()
        => this.image.Mutate(c => c.ProcessWithCanvas(canvas =>
        {
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in this.isElements)
            {
                if (fill is not null)
                {
                    canvas.Fill(fill, path);
                }

                if (stroke is not null)
                {
                    canvas.Draw(stroke, path);
                }
            }
        }));

    [Benchmark]
    public void ImageSharpWebGPU()
    {
        using DrawingCanvas<Rgba32> canvas = new(this.webGpuConfiguration, this.webGpuNativeFrame, new DrawingOptions());
        foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in this.isElements)
        {
            if (fill is not null)
            {
                canvas.Fill(fill, path);
            }

            if (stroke is not null)
            {
                canvas.Draw(stroke, path);
            }
        }

        canvas.Flush();
    }

    public static void VerifyOutput()
    {
        FillTiger bench = new();
        bench.Setup();

        bench.SkiaSharp();
        bench.SystemDrawing();
        bench.ImageSharp();
        bench.ImageSharpWebGPU();

        //SvgBenchmarkHelper.VerifyOutput(
        //    "tiger",
        //    Width,
        //    Height,
        //    bench.skSurface,
        //    bench.sdBitmap,
        //    bench.image,
        //    bench.webGpuNativeTextureHandle);

        bench.Cleanup();
    }
}
