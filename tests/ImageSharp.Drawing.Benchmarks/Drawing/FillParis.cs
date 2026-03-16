// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
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

    //[Benchmark(Baseline = true)]
    //public void SkiaSharp()
    //{
    //    SKCanvas canvas = this.skSurface.Canvas;
    //    foreach ((SKPath path, SKPaint fillPaint, SKPaint strokePaint) in this.skElements)
    //    {
    //        if (fillPaint is not null)
    //        {
    //            canvas.DrawPath(path, fillPaint);
    //        }

    //        if (strokePaint is not null)
    //        {
    //            canvas.DrawPath(path, strokePaint);
    //        }
    //    }
    //}

    //[Benchmark]
    //public void SystemDrawing()
    //{
    //    foreach ((GraphicsPath path, SDSolidBrush fill, SDPen stroke) in this.sdElements)
    //    {
    //        if (fill is not null)
    //        {
    //            this.sdGraphics.FillPath(fill, path);
    //        }

    //        if (stroke is not null)
    //        {
    //            this.sdGraphics.DrawPath(stroke, path);
    //        }
    //    }
    //}

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

    //[Benchmark]
    //public void ImageSharpWebGPU()
    //{
    //    using DrawingCanvas<Rgba32> canvas = new(this.webGpuConfiguration, this.webGpuNativeFrame, new DrawingOptions());
    //    foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in this.isElements)
    //    {
    //        if (fill is not null)
    //        {
    //            canvas.Fill(fill, path);
    //        }

    //        if (stroke is not null)
    //        {
    //            canvas.Draw(stroke, path);
    //        }
    //    }

    //    canvas.Flush();
    //}

    internal static void VerifyOutput()
    {
        FillParis bench = new();
        bench.Setup();

        //bench.SkiaSharp();
        // bench.SystemDrawing();
        bench.ImageSharp();
        //bench.ImageSharpWebGPU();

        SvgBenchmarkHelper.VerifyOutput(
            "paris",
            Width,
            Height,
            bench.skSurface,
            bench.sdBitmap,
            bench.image,
            bench.webGpuNativeTextureHandle);

        bench.Cleanup();
    }

    internal static void Profile(int warmupCount = 8, int iterationCount = 5)
    {
        FillParis bench = new();
        bench.Setup();
        try
        {
            bench.ProfileCore(warmupCount, iterationCount);
        }
        finally
        {
            bench.Cleanup();
        }
    }

    private void ProfileCore(int warmupCount, int iterationCount)
    {
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(DefaultDrawingBackend.Instance);

        DrawingOptions drawingOptions = new();
        List<CompositionCommand> commandTemplates = CreateCommandTemplates(this.isElements, drawingOptions);

        Console.WriteLine($"Paris elements: {this.isElements.Count}");
        Console.WriteLine($"Paris fill commands: {commandTemplates.Count}");
        Console.WriteLine($"Warmups: {warmupCount}");
        Console.WriteLine($"Measured iterations: {iterationCount}");

        for (int i = 0; i < warmupCount; i++)
        {
            _ = ProfilePublicCanvas(configuration, drawingOptions);
            _ = ProfileInternalPipeline(configuration, drawingOptions, commandTemplates);
        }

        List<PublicCanvasProfile> publicProfiles = [];
        List<InternalPipelineProfile> internalProfiles = [];
        for (int i = 0; i < iterationCount; i++)
        {
            publicProfiles.Add(ProfilePublicCanvas(configuration, drawingOptions));
            internalProfiles.Add(ProfileInternalPipeline(configuration, drawingOptions, commandTemplates));
        }

        PublicCanvasProfile publicMean = PublicCanvasProfile.Average(publicProfiles);
        InternalPipelineProfile internalMean = InternalPipelineProfile.Average(internalProfiles);

        Console.WriteLine();
        Console.WriteLine("Public canvas (backend-selected flush path):");
        Console.WriteLine($"  Queue fills:    {publicMean.QueueMilliseconds,8:F2} ms");
        Console.WriteLine($"  Flush:          {publicMean.FlushMilliseconds,8:F2} ms");
        Console.WriteLine($"  Total:          {publicMean.TotalMilliseconds,8:F2} ms");

        Console.WriteLine();
        Console.WriteLine("Internal pipeline (direct flush-scene executor):");
        Console.WriteLine($"  Create commands:{internalMean.CreateCommandsMilliseconds,8:F2} ms");
        Console.WriteLine($"  Prepare:        {internalMean.PrepareMilliseconds,8:F2} ms");
        Console.WriteLine($"  Build scene:    {internalMean.BuildSceneMilliseconds,8:F2} ms");
        Console.WriteLine($"  Raster no-op:   {internalMean.RasterizeNoOpMilliseconds,8:F2} ms");
        Console.WriteLine($"  Create applic.: {internalMean.CreateApplicatorsMilliseconds,8:F2} ms");
        Console.WriteLine($"  Raster+compose: {internalMean.ComposeMilliseconds,8:F2} ms");
        Console.WriteLine($"  Dispose applic.:{internalMean.DisposeApplicatorsMilliseconds,8:F2} ms");
        Console.WriteLine($"  Total:          {internalMean.TotalMilliseconds,8:F2} ms");

        Console.WriteLine();
        Console.WriteLine("Scene stats:");
        Console.WriteLine($"  Items:          {internalMean.ItemCount:N0}");
        Console.WriteLine($"  Rows:           {internalMean.RowCount:N0}");
        Console.WriteLine($"  Total edges:    {internalMean.TotalEdgeCount:N0}");
        Console.WriteLine($"  Single-band item ratio:  {internalMean.SingleBandItemRatio:P2}");
        Console.WriteLine($"  Small-edge item ratio:   {internalMean.SmallEdgeItemRatio:P2}");
    }

    private PublicCanvasProfile ProfilePublicCanvas(
        Configuration configuration,
        DrawingOptions drawingOptions)
    {
        using Image<Rgba32> targetImage = new(Width, Height);
        using DrawingCanvas<Rgba32> canvas = new(
            configuration,
            new Buffer2DRegion<Rgba32>(targetImage.Frames.RootFrame.PixelBuffer),
            drawingOptions);

        Stopwatch sw = Stopwatch.StartNew();
        foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in this.isElements)
        {
            if (fill is not null)
            {
                canvas.Fill(fill, path);
            }

            if (stroke is not null)
            {
                // Paris benchmark is fill-only.
            }
        }

        double queueMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        canvas.Flush();
        double flushMs = sw.Elapsed.TotalMilliseconds;

        return new PublicCanvasProfile(queueMs, flushMs);
    }

    private InternalPipelineProfile ProfileInternalPipeline(
        Configuration configuration,
        DrawingOptions drawingOptions,
        List<CompositionCommand> commandTemplates)
    {
        using Image<Rgba32> targetImage = new(Width, Height);
        Buffer2DRegion<Rgba32> targetRegion = new(targetImage.Frames.RootFrame.PixelBuffer);
        MemoryCanvasFrame<Rgba32> frame = new(targetRegion);

        Stopwatch sw = Stopwatch.StartNew();
        List<CompositionCommand> commands = new(commandTemplates);
        double createCommandsMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        PrepareCommands(commands);
        double prepareMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        using FlushScene scene = FlushScene.Create(commands, frame.Bounds, configuration.MemoryAllocator);
        double buildSceneMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        scene.RasterizeNoOp(configuration.MemoryAllocator);
        double rasterizeNoOpMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        FlushScene.ExecutionProfile composeProfile = scene.Execute(configuration, targetRegion);
        double totalComposeMs = sw.Elapsed.TotalMilliseconds;

        return new InternalPipelineProfile(
            createCommandsMs,
            prepareMs,
            buildSceneMs,
            rasterizeNoOpMs,
            composeProfile.CreateApplicatorsMilliseconds,
            composeProfile.RasterizeAndComposeMilliseconds,
            composeProfile.DisposeApplicatorsMilliseconds,
            createCommandsMs + prepareMs + buildSceneMs + rasterizeNoOpMs + totalComposeMs,
            scene.ItemCount,
            scene.RowCount,
            scene.TotalEdgeCount,
            scene.ItemCount == 0 ? 0 : (double)scene.SingleBandItemCount / scene.ItemCount,
            scene.ItemCount == 0 ? 0 : (double)scene.SmallEdgeItemCount / scene.ItemCount);
    }

    private static List<CompositionCommand> CreateCommandTemplates(
        List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> elements,
        DrawingOptions drawingOptions)
    {
        List<CompositionCommand> commands = new(elements.Count);
        GraphicsOptions graphicsOptions = drawingOptions.GraphicsOptions;
        ShapeOptions shapeOptions = drawingOptions.ShapeOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in elements)
        {
            if (fill is null)
            {
                continue;
            }

            RectangleF bounds = path.Bounds;
            Rectangle interest = Rectangle.FromLTRB(
                (int)MathF.Floor(bounds.Left),
                (int)MathF.Floor(bounds.Top),
                (int)MathF.Ceiling(bounds.Right),
                (int)MathF.Ceiling(bounds.Bottom));

            RasterizerOptions rasterizerOptions = new(
                interest,
                shapeOptions.IntersectionRule,
                rasterizationMode,
                RasterizerSamplingOrigin.PixelBoundary,
                graphicsOptions.AntialiasThreshold);

            commands.Add(
                CompositionCommand.Create(
                    path,
                    fill,
                    graphicsOptions,
                    in rasterizerOptions,
                    shapeOptions,
                    Matrix4x4.Identity));
        }

        return commands;
    }

    private static void PrepareCommands(List<CompositionCommand> commands)
        => Parallel.ForEach(Partitioner.Create(0, commands.Count), range =>
        {
            Span<CompositionCommand> span = CollectionsMarshal.AsSpan(commands);
            for (int i = range.Item1; i < range.Item2; i++)
            {
                span[i].Prepare();
            }
        });

    private readonly record struct PublicCanvasProfile(
        double QueueMilliseconds,
        double FlushMilliseconds)
    {
        public double TotalMilliseconds => this.QueueMilliseconds + this.FlushMilliseconds;

        public static PublicCanvasProfile Average(List<PublicCanvasProfile> profiles)
        {
            double count = profiles.Count;
            return new PublicCanvasProfile(
                profiles.Sum(x => x.QueueMilliseconds) / count,
                profiles.Sum(x => x.FlushMilliseconds) / count);
        }
    }

    private readonly record struct InternalPipelineProfile(
        double CreateCommandsMilliseconds,
        double PrepareMilliseconds,
        double BuildSceneMilliseconds,
        double RasterizeNoOpMilliseconds,
        double CreateApplicatorsMilliseconds,
        double ComposeMilliseconds,
        double DisposeApplicatorsMilliseconds,
        double TotalMilliseconds,
        double ItemCount,
        double RowCount,
        double TotalEdgeCount,
        double SingleBandItemRatio,
        double SmallEdgeItemRatio)
    {
        public static InternalPipelineProfile Average(List<InternalPipelineProfile> profiles)
        {
            double count = profiles.Count;
            return new InternalPipelineProfile(
                profiles.Sum(x => x.CreateCommandsMilliseconds) / count,
                profiles.Sum(x => x.PrepareMilliseconds) / count,
                profiles.Sum(x => x.BuildSceneMilliseconds) / count,
                profiles.Sum(x => x.RasterizeNoOpMilliseconds) / count,
                profiles.Sum(x => x.CreateApplicatorsMilliseconds) / count,
                profiles.Sum(x => x.ComposeMilliseconds) / count,
                profiles.Sum(x => x.DisposeApplicatorsMilliseconds) / count,
                profiles.Sum(x => x.TotalMilliseconds) / count,
                profiles.Sum(x => x.ItemCount) / count,
                profiles.Sum(x => x.RowCount) / count,
                profiles.Sum(x => x.TotalEdgeCount) / count,
                profiles.Sum(x => x.SingleBandItemRatio) / count,
                profiles.Sum(x => x.SmallEdgeItemRatio) / count);
        }
    }
}
