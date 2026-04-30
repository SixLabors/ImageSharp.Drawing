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
/// Benchmarks rendering a 30k-path Paris map SVG (fill-only, with per-path transforms and opacity)
/// across SkiaSharp, System.Drawing, ImageSharp (CPU), and ImageSharp (WebGPU).
/// </summary>
public class FillParis
{
    // The SVG is ~1096x1060 with a Y-flip group transform.
    private const float Scale = 1f;
    private const int Width = 1096;
    private const int Height = 1060;

    private static readonly string SvgFilePath =
        TestFile.GetInputFileFullPath(TestImages.Svg.Paris30k);

    private SKSurface skSurface;
    private List<(SKPath Path, SKPaint FillPaint, SKPaint StrokePaint)> skElements;

    private Bitmap sdBitmap;
    private Graphics sdGraphics;
    private List<(GraphicsPath Path, SDSolidBrush Fill, SDPen Stroke)> sdElements;

    private Image<Rgba32> image;
    private List<SvgBenchmarkHelper.SvgElement> parsedElements;
    private List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> isElements;
    private DrawingBackendScene imageSharpRetainedScene;

    private WebGPURenderTarget webGpuTarget;
    private DrawingBackendScene webGpuRetainedScene;

    [GlobalSetup]
    public void Setup()
    {
        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        int desiredWorkerThreads = Math.Max(minWorkerThreads, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(desiredWorkerThreads, minCompletionPortThreads);
        Parallel.For(0, desiredWorkerThreads, static _ => { });

        this.parsedElements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);

        this.skSurface = SKSurface.Create(new SKImageInfo(Width, Height));
        this.skElements = SvgBenchmarkHelper.BuildSkiaElements(this.parsedElements, Scale);

        this.sdBitmap = new Bitmap(Width, Height);
        this.sdGraphics = Graphics.FromImage(this.sdBitmap);
        this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        this.sdElements = SvgBenchmarkHelper.BuildSystemDrawingElements(this.parsedElements, Scale);

        this.image = new Image<Rgba32>(Width, Height);
        this.isElements = SvgBenchmarkHelper.BuildImageSharpElements(this.parsedElements, Scale);
        this.imageSharpRetainedScene = CreateImageSharpRetainedScene(this.image, this.isElements);

        this.webGpuTarget = new WebGPURenderTarget(Width, Height);
        this.webGpuRetainedScene = CreateWebGpuRetainedScene(this.webGpuTarget, this.isElements);
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

        this.imageSharpRetainedScene.Dispose();
        this.image.Dispose();

        this.webGpuRetainedScene.Dispose();
        this.webGpuTarget.Dispose();
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
        => this.image.Mutate(c => c.Paint(canvas => DrawImageSharpElements(canvas, this.isElements)));

    [Benchmark]
    public void ImageSharpRetainedScene()
        => this.image.Mutate(c => c.Paint(canvas => canvas.RenderScene(this.imageSharpRetainedScene)));

    [Benchmark]
    public void ImageSharpWebGPU()
    {
        using DrawingCanvas canvas = this.webGpuTarget.CreateCanvas();
        DrawImageSharpElements(canvas, this.isElements);

        canvas.Flush();
    }

    [Benchmark]
    public void ImageSharpWebGPURetainedScene()
    {
        using DrawingCanvas canvas = this.webGpuTarget.CreateCanvas();
        canvas.RenderScene(this.webGpuRetainedScene);
    }

    internal static void VerifyOutput()
    {
        FillParis bench = new();
        bench.Setup();

        bench.SkiaSharp();
        bench.SystemDrawing();
        bench.ImageSharp();
        bench.ImageSharpWebGPU();

        FillParis retainedBench = new();
        retainedBench.Setup();
        retainedBench.ImageSharpRetainedScene();
        retainedBench.ImageSharpWebGPURetainedScene();

        SvgBenchmarkHelper.VerifyOutput(
            "paris",
            Width,
            Height,
            bench.skSurface,
            bench.sdBitmap,
            bench.image,
            bench.webGpuTarget);

        SvgBenchmarkHelper.VerifyOutput(
            "paris-retained",
            Width,
            Height,
            bench.skSurface,
            bench.sdBitmap,
            retainedBench.image,
            retainedBench.webGpuTarget);

        retainedBench.Cleanup();
        bench.Cleanup();
    }

    internal static void ProfileCpu(int iterations)
    {
        FillParis bench = new();
        bench.Setup();
        for (int i = 0; i < iterations; i++)
        {
            bench.ImageSharp();
        }

        bench.Cleanup();
    }

    internal static void ProfileWebGpu(int iterations)
    {
        FillParis bench = new();
        bench.Setup();
        for (int i = 0; i < iterations; i++)
        {
            bench.ImageSharpWebGPU();
        }

        bench.Cleanup();
    }

    private static DrawingBackendScene CreateImageSharpRetainedScene(
        Image<Rgba32> image,
        List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> elements)
    {
        using DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(image.Configuration, new DrawingOptions());
        DrawImageSharpElements(canvas, elements);

        return canvas.CreateScene();
    }

    private static DrawingBackendScene CreateWebGpuRetainedScene(
        WebGPURenderTarget target,
        List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> elements)
    {
        using DrawingCanvas canvas = target.CreateCanvas();
        DrawImageSharpElements(canvas, elements);

        return canvas.CreateScene();
    }

    private static void DrawImageSharpElements(
        DrawingCanvas canvas,
        List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> elements)
    {
        foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in elements)
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
    }
}
