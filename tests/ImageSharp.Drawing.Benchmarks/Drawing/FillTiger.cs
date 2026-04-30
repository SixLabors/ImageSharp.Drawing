// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
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
/// Benchmarks rendering the Ghostscript Tiger SVG (138 parsed path elements yielding
/// 182 draw commands: 86 fill-only, 44 fill-and-stroke, and 8 stroke-only)
/// across SkiaSharp, System.Drawing, ImageSharp (CPU), and ImageSharp (WebGPU).
/// </summary>
public class FillTiger
{
    private static readonly string SvgFilePath =
        TestFile.GetInputFileFullPath(TestImages.Svg.GhostscriptTiger);

    private SKSurface skSurface;
    private List<(SKPath Path, SKPaint FillPaint, SKPaint StrokePaint)> skElements;

    private Bitmap sdBitmap;
    private Graphics sdGraphics;
    private List<(GraphicsPath Path, SDSolidBrush Fill, SDPen Stroke)> sdElements;

    private Image<Rgba32> image;
    private List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> isElements;
    private DrawingBackendScene imageSharpRetainedScene;

    private WebGPURenderTarget webGpuTarget;
    private DrawingBackendScene webGpuRetainedScene;

    [Params(1000, 100)]
    public int Dimensions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        int width = this.Dimensions;
        int height = this.Dimensions;
        float scale = this.Dimensions / 200f;

        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        int desiredWorkerThreads = Math.Max(minWorkerThreads, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(desiredWorkerThreads, minCompletionPortThreads);
        Parallel.For(0, desiredWorkerThreads, static _ => { });

        List<SvgBenchmarkHelper.SvgElement> elements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);

        this.skSurface = SKSurface.Create(new SKImageInfo(width, height));
        this.skElements = SvgBenchmarkHelper.BuildSkiaElements(elements, scale);

        this.sdBitmap = new Bitmap(width, height);
        this.sdGraphics = Graphics.FromImage(this.sdBitmap);
        this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        this.sdElements = SvgBenchmarkHelper.BuildSystemDrawingElements(elements, scale);

        this.image = new Image<Rgba32>(width, height);
        this.isElements = SvgBenchmarkHelper.BuildImageSharpElements(elements, scale);
        this.imageSharpRetainedScene = CreateImageSharpRetainedScene(this.image, this.isElements);

        this.webGpuTarget = new WebGPURenderTarget(width, height);
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
    public void ImageSharp_SingleThreaded()
    {
        Configuration configuration = this.image.Configuration.Clone();
        configuration.MaxDegreeOfParallelism = 1;
        this.image.Mutate(configuration, c => c.Paint(canvas => DrawImageSharpElements(canvas, this.isElements)));
    }

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

    public static void VerifyOutput(int dimensions = 1000)
    {
        FillTiger bench = new() { Dimensions = dimensions };
        bench.Setup();

        bench.SkiaSharp();
        bench.SystemDrawing();
        bench.ImageSharp();
        bench.ImageSharpWebGPU();

        FillTiger retainedBench = new() { Dimensions = dimensions };
        retainedBench.Setup();
        retainedBench.ImageSharpRetainedScene();
        retainedBench.ImageSharpWebGPURetainedScene();

        PrintWebGpuDiagnostics(bench);

        SvgBenchmarkHelper.VerifyOutput(
            $"tiger-{dimensions}",
            bench.Dimensions,
            bench.Dimensions,
            bench.skSurface,
            bench.sdBitmap,
            bench.image,
            bench.webGpuTarget);

        SvgBenchmarkHelper.VerifyOutput(
            $"tiger-{dimensions}-retained",
            bench.Dimensions,
            bench.Dimensions,
            bench.skSurface,
            bench.sdBitmap,
            retainedBench.image,
            retainedBench.webGpuTarget);

        retainedBench.Cleanup();
        bench.Cleanup();
    }

    /// <summary>
    /// Like <see cref="VerifyTransform"/> but pre-bakes the pan/zoom matrix into each
    /// <see cref="IPath"/> before drawing, so the WebGPU scene sees identity transform + transformed
    /// geometry. Comparing the two outputs isolates bugs in scene-encoded transforms from bugs in
    /// rasterizing transformed geometry.
    /// </summary>
    public static void VerifyTransformBaked(int width, int height, float zoom, float panX, float panY)
    {
        List<SvgBenchmarkHelper.SvgElement> elements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);
        List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> isElements = SvgBenchmarkHelper.BuildImageSharpElements(elements, scale: 1f);

        Matrix4x4 transform4 =
            Matrix4x4.CreateTranslation(-panX, -panY, 0f) *
            Matrix4x4.CreateScale(zoom, zoom, 1f) *
            Matrix4x4.CreateTranslation(width * 0.5f, height * 0.5f, 0f);
        List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> baked = new(isElements.Count);
        foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in isElements)
        {
            baked.Add((path.Transform(transform4), fill, stroke));
        }

        Image<Rgba32> image = new(width, height);
        image.Mutate(c => c.Paint(canvas =>
        {
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in baked)
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

        WebGPURenderTarget webGpuTarget = new(width, height);
        using (DrawingCanvas canvas = webGpuTarget.CreateCanvas())
        {
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in baked)
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

        FillTiger printer = new()
        {
            Dimensions = width,
            webGpuTarget = webGpuTarget
        };

        Console.WriteLine();
        Console.WriteLine($"=== Transform-baked verify {width}x{height} zoom={zoom} pan=({panX},{panY}) ===");
        PrintWebGpuDiagnostics(printer);

        string tag = $"tiger-baked-{width}x{height}-z{zoom:0.##}-p{panX:0.##}_{panY:0.##}";
        string dir = System.IO.Path.Combine(AppContext.BaseDirectory, tag + "-verify");
        Directory.CreateDirectory(dir);
        image.Save(System.IO.Path.Combine(dir, tag + "-imagesharp.png"));

        using Image<Rgba32> gpuImage = webGpuTarget.Readback<Rgba32>();
        gpuImage.SaveAsPng(System.IO.Path.Combine(dir, tag + "-webgpu.png"));

        Console.WriteLine($"Output saved to: {dir}");

        image.Dispose();
        webGpuTarget.Dispose();
    }

    /// <summary>
    /// Reproduces the Tiger Viewer demo's pan/zoom transform on all four backends and saves PNGs.
    /// Use when investigating transform-dependent rendering artifacts seen in the demo window.
    /// </summary>
    public static void VerifyTransform(int width, int height, float zoom, float panX, float panY)
    {
        List<SvgBenchmarkHelper.SvgElement> elements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);
        List<(SKPath Path, SKPaint FillPaint, SKPaint StrokePaint)> skElements = SvgBenchmarkHelper.BuildSkiaElements(elements, scale: 1f);
        List<(GraphicsPath Path, SDSolidBrush Fill, SDPen Stroke)> sdElements = SvgBenchmarkHelper.BuildSystemDrawingElements(elements, scale: 1f);
        List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> isElements = SvgBenchmarkHelper.BuildImageSharpElements(elements, scale: 1f);

        Matrix4x4 transform4 =
            Matrix4x4.CreateTranslation(-panX, -panY, 0f) *
            Matrix4x4.CreateScale(zoom, zoom, 1f) *
            Matrix4x4.CreateTranslation(width * 0.5f, height * 0.5f, 0f);

        SKMatrix skTransform = new(
            transform4.M11,
            transform4.M21,
            transform4.M41,
            transform4.M12,
            transform4.M22,
            transform4.M42,
            0f,
            0f,
            1f);

        Matrix sdTransform = new(
            transform4.M11,
            transform4.M12,
            transform4.M21,
            transform4.M22,
            transform4.M41,
            transform4.M42);

        SKSurface sk = SKSurface.Create(new SKImageInfo(width, height));
        sk.Canvas.SetMatrix(skTransform);
        foreach ((SKPath path, SKPaint fillPaint, SKPaint strokePaint) in skElements)
        {
            if (fillPaint is not null)
            {
                sk.Canvas.DrawPath(path, fillPaint);
            }

            if (strokePaint is not null)
            {
                sk.Canvas.DrawPath(path, strokePaint);
            }
        }

        Bitmap sdBitmap = new(width, height);
        Graphics sdGraphics = Graphics.FromImage(sdBitmap);
        sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        sdGraphics.Transform = sdTransform;
        foreach ((GraphicsPath path, SDSolidBrush fill, SDPen stroke) in sdElements)
        {
            if (fill is not null)
            {
                sdGraphics.FillPath(fill, path);
            }

            if (stroke is not null)
            {
                sdGraphics.DrawPath(stroke, path);
            }
        }

        Image<Rgba32> image = new(width, height);
        image.Mutate(c => c.Paint(canvas =>
        {
            canvas.Save(new DrawingOptions { Transform = transform4 });
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in isElements)
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

            canvas.Restore();
        }));

        WebGPURenderTarget webGpuTarget = new(width, height);
        using (DrawingCanvas canvas = webGpuTarget.CreateCanvas())
        {
            canvas.Save(new DrawingOptions { Transform = transform4 });
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in isElements)
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

            canvas.Restore();
            canvas.Flush();
        }

        FillTiger printer = new()
        {
            Dimensions = width,
            webGpuTarget = webGpuTarget
        };
        Console.WriteLine();
        Console.WriteLine($"=== Transform verify {width}x{height} zoom={zoom} pan=({panX},{panY}) ===");
        PrintWebGpuDiagnostics(printer);

        string tag = $"tiger-xform-{width}x{height}-z{zoom:0.##}-p{panX:0.##}_{panY:0.##}";
        SvgBenchmarkHelper.VerifyOutput(tag, width, height, sk, sdBitmap, image, webGpuTarget);

        foreach ((SKPath path, SKPaint fill, SKPaint stroke) in skElements)
        {
            path.Dispose();
            fill?.Dispose();
            stroke?.Dispose();
        }

        sk.Dispose();
        foreach ((GraphicsPath path, SDSolidBrush fill, SDPen stroke) in sdElements)
        {
            path.Dispose();
            fill?.Dispose();
            stroke?.Dispose();
        }

        sdGraphics.Dispose();
        sdBitmap.Dispose();
        image.Dispose();
        webGpuTarget.Dispose();
    }

    private static void PrintWebGpuDiagnostics(FillTiger bench)
    {
        WebGPUDrawingBackend backend = bench.webGpuTarget.Backend;

        Console.WriteLine();
        Console.WriteLine($"=== WebGPU flush diagnostics (dim={bench.Dimensions}) ===");
        Console.WriteLine($"  UsedChunking : {backend.DiagnosticLastFlushUsedChunking} (binding failure: {backend.DiagnosticLastChunkingBindingFailure})");
        Console.WriteLine("=== end WebGPU diagnostics ===");
        Console.WriteLine();
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
