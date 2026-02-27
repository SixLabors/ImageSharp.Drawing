// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

[MemoryDiagnoser]
[WarmupCount(5)]
[IterationCount(15)]
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

    private readonly GraphicsOptions clearOptions = new()
    {
        Antialias = false,
        AlphaCompositionMode = PixelAlphaCompositionMode.Src,
        ColorBlendingMode = PixelColorBlendingMode.Normal,
        BlendPercentage = 1F
    };

    private readonly Brush brush = Brushes.Solid(Color.HotPink);
    private readonly Brush clearBrush = Brushes.Solid(Color.Transparent);

    private Configuration defaultConfiguration;
    private Image<Rgba32> defaultImage;
    private Image<Rgba32> webGpuCpuImage;
    private WebGPUDrawingBackend webGpuBackend;
    private Configuration webGpuConfiguration;
    private NativeSurfaceOnlyFrame<Rgba32> webGpuNativeFrame;
    private nint webGpuNativeTextureHandle;
    private nint webGpuNativeTextureViewHandle;
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

        this.defaultConfiguration = Configuration.Default;
        this.defaultImage = new Image<Rgba32>(Width, Height);
        this.webGpuBackend = new WebGPUDrawingBackend();
        this.webGpuConfiguration = Configuration.Default.Clone();
        this.webGpuConfiguration.SetDrawingBackend(this.webGpuBackend);
        this.webGpuCpuImage = new Image<Rgba32>(this.webGpuConfiguration, Width, Height);

        if (!WebGPUTestNativeSurfaceAllocator.TryCreate<Rgba32>(
                this.webGpuBackend,
                Width,
                Height,
                isSrgb: false,
                isPremultipliedAlpha: false,
                out NativeSurface nativeSurface,
                out this.webGpuNativeTextureHandle,
                out this.webGpuNativeTextureViewHandle,
                out string nativeSurfaceError))
        {
            throw new InvalidOperationException(
                $"Unable to create benchmark native WebGPU target. GPUReady={this.webGpuBackend.TestingIsGPUReady}, Error='{(nativeSurfaceError.Length > 0 ? nativeSurfaceError : this.webGpuBackend.TestingLastGPUInitializationFailure ?? "<none>")}'.");
        }

        this.webGpuNativeFrame = new NativeSurfaceOnlyFrame<Rgba32>(
            new Rectangle(0, 0, Width, Height),
            nativeSurface);

        this.text = new string('A', this.GlyphCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.defaultImage.Dispose();
        this.webGpuCpuImage.Dispose();
        WebGPUTestNativeSurfaceAllocator.Release(
            this.webGpuNativeTextureHandle,
            this.webGpuNativeTextureViewHandle);
        this.webGpuNativeTextureHandle = 0;
        this.webGpuNativeTextureViewHandle = 0;
        this.webGpuBackend.Dispose();
    }

    [Benchmark(Baseline = true, Description = "DrawingCanvas Default Backend")]
    public void DrawingCanvasDefaultBackend()
    {
        CpuRegionOnlyFrame<Rgba32> frame = new(GetFrameRegion(this.defaultImage));
        // this.ClearWithDrawingCanvas(this.defaultConfiguration, frame);
        using DrawingCanvas<Rgba32> canvas = new(this.defaultConfiguration, frame);
        canvas.DrawText(this.textOptions, this.text, this.drawingOptions, this.brush, pen: null);
        canvas.Flush();
    }

    [Benchmark(Description = "DrawingCanvas WebGPU Backend (CPURegion)")]
    public void DrawingCanvasWebGPUBackendCpuRegion()
    {
        CpuRegionOnlyFrame<Rgba32> frame = new(GetFrameRegion(this.webGpuCpuImage));
        // this.ClearWithDrawingCanvas(this.webGpuConfiguration, frame);
        using DrawingCanvas<Rgba32> canvas = new(this.webGpuConfiguration, frame);
        canvas.DrawText(this.textOptions, this.text, this.drawingOptions, this.brush, pen: null);
        canvas.Flush();
    }

    [Benchmark(Description = "DrawingCanvas WebGPU Backend (NativeSurface)")]
    public void DrawingCanvasWebGPUBackendNativeSurface()
    {
        // this.ClearWithDrawingCanvas(this.webGpuConfiguration, this.webGpuNativeFrame);
        using DrawingCanvas<Rgba32> canvas = new(this.webGpuConfiguration, this.webGpuNativeFrame);
        canvas.DrawText(this.textOptions, this.text, this.drawingOptions, this.brush, pen: null);
        canvas.Flush();
    }

    private void ClearWithDrawingCanvas(Configuration configuration, ICanvasFrame<Rgba32> target)
    {
        using DrawingCanvas<Rgba32> canvas = new(configuration, target);
        canvas.Fill(this.clearBrush, this.clearOptions);
        canvas.Flush();
    }

    private static Buffer2DRegion<Rgba32> GetFrameRegion(Image<Rgba32> image)
        => new(image.Frames.RootFrame.PixelBuffer, new Rectangle(0, 0, image.Width, image.Height));

    private sealed class CpuRegionOnlyFrame<TPixel> : ICanvasFrame<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Buffer2DRegion<TPixel> region;

        public CpuRegionOnlyFrame(Buffer2DRegion<TPixel> region) => this.region = region;

        public Rectangle Bounds => this.region.Rectangle;

        public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
        {
            region = this.region;
            return true;
        }

        public bool TryGetNativeSurface(out NativeSurface surface)
        {
            surface = default;
            return false;
        }
    }

    private sealed class NativeSurfaceOnlyFrame<TPixel> : ICanvasFrame<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly NativeSurface surface;

        public NativeSurfaceOnlyFrame(Rectangle bounds, NativeSurface surface)
        {
            this.Bounds = bounds;
            this.surface = surface;
        }

        public Rectangle Bounds { get; }

        public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
        {
            region = default;
            return false;
        }

        public bool TryGetNativeSurface(out NativeSurface surface)
        {
            surface = this.surface;
            return true;
        }
    }
}
