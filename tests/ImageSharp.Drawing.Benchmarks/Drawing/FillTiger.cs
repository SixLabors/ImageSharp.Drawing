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

    private WebGPURenderTarget<Rgba32> webGpuTarget;

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

        this.webGpuTarget = new WebGPURenderTarget<Rgba32>(width, height);
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
        => this.image.Mutate(c => c.Paint(canvas =>
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
    public void ImageSharp_SingleThreaded()
    {
        Configuration configuration = this.image.Configuration.Clone();
        configuration.MaxDegreeOfParallelism = 1;
        this.image.Mutate(configuration, c => c.Paint(canvas =>
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
    }

    [Benchmark]
    public void ImageSharpWebGPU()
    {
        using DrawingCanvas<Rgba32> canvas = this.webGpuTarget.CreateCanvas();
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

    public static void VerifyOutput(int dimensions = 1000)
    {
        FillTiger bench = new() { Dimensions = dimensions };
        bench.Setup();

        bench.SkiaSharp();
        bench.SystemDrawing();
        bench.ImageSharp();
        bench.ImageSharpWebGPU();

        PrintWebGpuDiagnostics(bench);

        SvgBenchmarkHelper.VerifyOutput(
            $"tiger-{dimensions}",
            bench.Dimensions,
            bench.Dimensions,
            bench.skSurface,
            bench.sdBitmap,
            bench.image,
            bench.webGpuTarget.TextureHandle);

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
                if (fill is not null) { canvas.Fill(fill, path); }
                if (stroke is not null) { canvas.Draw(stroke, path); }
            }
        }));

        WebGPURenderTarget<Rgba32> webGpuTarget = new(width, height);
        using (DrawingCanvas<Rgba32> canvas = webGpuTarget.CreateCanvas())
        {
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in baked)
            {
                if (fill is not null) { canvas.Fill(fill, path); }
                if (stroke is not null) { canvas.Draw(stroke, path); }
            }

            canvas.Flush();
        }

        FillTiger printer = new() { Dimensions = width };
        printer.webGpuTarget = webGpuTarget;
        Console.WriteLine();
        Console.WriteLine($"=== Transform-baked verify {width}x{height} zoom={zoom} pan=({panX},{panY}) ===");
        PrintWebGpuDiagnostics(printer);

        string tag = $"tiger-baked-{width}x{height}-z{zoom:0.##}-p{panX:0.##}_{panY:0.##}";
        string dir = System.IO.Path.Combine(AppContext.BaseDirectory, tag + "-verify");
        System.IO.Directory.CreateDirectory(dir);
        image.Save(System.IO.Path.Combine(dir, tag + "-imagesharp.png"));

        if (WebGPUTextureTransfer.TryReadTexture(webGpuTarget.TextureHandle, width, height, out Image<Rgba32> gpuImage, out string readError))
        {
            gpuImage.SaveAsPng(System.IO.Path.Combine(dir, tag + "-webgpu.png"));
            gpuImage.Dispose();
        }
        else
        {
            Console.WriteLine($"WebGPU readback failed: {readError}");
        }

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
            transform4.M11, transform4.M21, transform4.M41,
            transform4.M12, transform4.M22, transform4.M42,
            0f, 0f, 1f);
        System.Drawing.Drawing2D.Matrix sdTransform = new(
            transform4.M11, transform4.M12,
            transform4.M21, transform4.M22,
            transform4.M41, transform4.M42);

        SKSurface sk = SKSurface.Create(new SKImageInfo(width, height));
        sk.Canvas.SetMatrix(skTransform);
        foreach ((SKPath path, SKPaint fillPaint, SKPaint strokePaint) in skElements)
        {
            if (fillPaint is not null) { sk.Canvas.DrawPath(path, fillPaint); }
            if (strokePaint is not null) { sk.Canvas.DrawPath(path, strokePaint); }
        }

        Bitmap sdBitmap = new(width, height);
        Graphics sdGraphics = Graphics.FromImage(sdBitmap);
        sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        sdGraphics.Transform = sdTransform;
        foreach ((GraphicsPath path, SDSolidBrush fill, SDPen stroke) in sdElements)
        {
            if (fill is not null) { sdGraphics.FillPath(fill, path); }
            if (stroke is not null) { sdGraphics.DrawPath(stroke, path); }
        }

        Image<Rgba32> image = new(width, height);
        image.Mutate(c => c.Paint(canvas =>
        {
            canvas.Save(new DrawingOptions { Transform = transform4 });
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in isElements)
            {
                if (fill is not null) { canvas.Fill(fill, path); }
                if (stroke is not null) { canvas.Draw(stroke, path); }
            }

            canvas.Restore();
        }));

        WebGPURenderTarget<Rgba32> webGpuTarget = new(width, height);
        using (DrawingCanvas<Rgba32> canvas = webGpuTarget.CreateCanvas())
        {
            canvas.Save(new DrawingOptions { Transform = transform4 });
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in isElements)
            {
                if (fill is not null) { canvas.Fill(fill, path); }
                if (stroke is not null) { canvas.Draw(stroke, path); }
            }

            canvas.Restore();
            canvas.Flush();
        }

        FillTiger printer = new() { Dimensions = width };
        printer.webGpuTarget = webGpuTarget;
        Console.WriteLine();
        Console.WriteLine($"=== Transform verify {width}x{height} zoom={zoom} pan=({panX},{panY}) ===");
        PrintWebGpuDiagnostics(printer);

        string tag = $"tiger-xform-{width}x{height}-z{zoom:0.##}-p{panX:0.##}_{panY:0.##}";
        SvgBenchmarkHelper.VerifyOutput(tag, width, height, sk, sdBitmap, image, webGpuTarget.TextureHandle);

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
        WebGPUDrawingBackend backend = bench.webGpuTarget.Graphics.Backend;

        Console.WriteLine();
        Console.WriteLine($"=== WebGPU flush diagnostics (dim={bench.Dimensions}) ===");
        Console.WriteLine($"  UsedGPU            : {backend.TestingLastFlushUsedGPU}");
        Console.WriteLine($"  UsedChunking       : {backend.TestingLastFlushUsedChunking} (binding failure: {backend.TestingLastChunkingBindingFailure})");
        Console.WriteLine($"  AttemptCount       : {backend.TestingLastAttemptCount}");
        Console.WriteLine($"  ExhaustedRetries   : {backend.TestingLastExhaustedRetryBudget}");
        Console.WriteLine($"  SceneFailure       : {backend.TestingLastGPUInitializationFailure ?? "<none>"}");
        Console.WriteLine($"  EncodedPathTagBytes: {backend.TestingLastEncodedScenePathTagByteCount}");
        Console.WriteLine($"  CoarseDispatch     : {backend.TestingLastCoarseDispatch.X} x {backend.TestingLastCoarseDispatch.Y}");
        Console.WriteLine($"  FineDispatch       : {backend.TestingLastFineDispatch.X} x {backend.TestingLastFineDispatch.Y}");
        Console.WriteLine($"  ChunkWindow        : start={backend.TestingLastChunkWindow.Start} height={backend.TestingLastChunkWindow.Height} bufferHeight={backend.TestingLastChunkWindow.BufferHeight}");
        Console.WriteLine();

        for (int i = 0; i < backend.TestingLastAttemptCount; i++)
        {
            WebGPUSceneBumpSizes sizes = backend.TestingLastAttemptBumpSizes[i];
            GpuSceneBumpAllocators alloc = backend.TestingLastAttemptBumpAllocators[i];
            bool requiresGrowth = backend.TestingLastAttemptRequiresGrowth[i];

            Console.WriteLine($"  Attempt #{i} requiresGrowth={requiresGrowth} failed=0x{alloc.Failed:x}");
            Console.WriteLine($"    buffer     {"budget",12} {"actual",12}  overflow?");
            Print("Lines     ", alloc.Lines,      sizes.Lines);
            Print("Binning   ", alloc.Binning,    sizes.Binning);
            Print("PathRows  ", alloc.PathRows,   sizes.PathRows);
            Print("Tile      ", alloc.Tile,       sizes.PathTiles);
            Print("SegCounts ", alloc.SegCounts,  sizes.SegCounts);
            Print("Segments  ", alloc.Segments,   sizes.Segments);
            Print("BlendSpill", alloc.BlendSpill, sizes.BlendSpill);
            Print("Ptcl      ", alloc.Ptcl,       sizes.Ptcl);
        }

        Console.WriteLine("=== end WebGPU diagnostics ===");
        Console.WriteLine();

        static void Print(string name, uint actual, uint budget)
        {
            string marker = actual > budget ? "  OVERFLOW" : string.Empty;
            Console.WriteLine($"    {name}  {budget,12:N0} {actual,12:N0}{marker}");
        }
    }
}
