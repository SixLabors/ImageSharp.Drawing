// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;

namespace DrawingBackendBenchmark;

/// <summary>
/// Small offscreen WebGPU host used by the sample so the benchmark can drive the real backend without manual WebGPU bootstrap code.
/// </summary>
internal sealed class WebGpuBenchmarkBackend : IBenchmarkBackend, IDisposable
{
    private RenderResources? resources;

    private WebGpuBenchmarkBackend()
    {
    }

    public static bool TryCreate([NotNullWhen(true)] out WebGpuBenchmarkBackend? result, [NotNullWhen(false)] out string? error)
    {
        using WebGPUDrawingBackend backend = new();
        if (!backend.IsSupported)
        {
            result = null;
            error = "WebGPU unsupported";
            return false;
        }

        try
        {
            using WebGPURenderTarget<Bgra32> probe = new(1, 1);
            result = new WebGpuBenchmarkBackend();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Renders the benchmark scene through the WebGPU backend and optionally captures a readback preview.
    /// </summary>
    public BenchmarkRenderResult Render(ReadOnlySpan<VisualLine> lines, int width, int height, bool capturePreview)
    {
        RenderResources resources = this.EnsureResources(width, height);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using (DrawingCanvas<Bgra32> canvas = resources.RenderTarget.CreateHybridCanvas(resources.CpuImage, new DrawingOptions()))
        {
            VisualLine.RenderLinesToCanvas(canvas, lines);
            canvas.Flush();
        }

        stopwatch.Stop();

        Image<Bgra32>? preview = capturePreview ? resources.RenderTarget.Readback() : null;
        return new BenchmarkRenderResult(
            stopwatch.Elapsed.TotalMilliseconds,
            preview,
            resources.RenderTarget.Graphics.Backend.DiagnosticLastFlushUsedGPU,
            resources.RenderTarget.Graphics.Backend.DiagnosticLastSceneFailure);
    }

    /// <summary>
    /// Gets the name of this backend.
    /// </summary>
    public override string ToString() => "WebGPU";

    /// <inheritdoc />
    public void Dispose()
    {
        this.resources?.Dispose();
        this.resources = null;
    }

    private RenderResources EnsureResources(int width, int height)
    {
        if (this.resources is RenderResources resources && resources.Width == width && resources.Height == height)
        {
            return resources;
        }

        this.resources?.Dispose();
        this.resources = new RenderResources(new WebGPURenderTarget<Bgra32>(width, height), new Image<Bgra32>(width, height));
        return this.resources;
    }

    private sealed class RenderResources : IDisposable
    {
        public RenderResources(WebGPURenderTarget<Bgra32> renderTarget, Image<Bgra32> cpuImage)
        {
            this.RenderTarget = renderTarget;
            this.CpuImage = cpuImage;
        }

        public WebGPURenderTarget<Bgra32> RenderTarget { get; }

        public Image<Bgra32> CpuImage { get; }

        public int Width => this.CpuImage.Width;

        public int Height => this.CpuImage.Height;

        public void Dispose()
        {
            this.RenderTarget.Dispose();
            this.CpuImage.Dispose();
        }
    }
}
