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
internal sealed class WebGpuBenchmarkBackend : IBenchmarkBackend
{
    private WebGPURenderTarget<Bgra32>? renderTarget;

    private WebGpuBenchmarkBackend()
    {
    }

    public static bool TryCreate([NotNullWhen(true)] out WebGpuBenchmarkBackend? result, [NotNullWhen(false)] out string? error)
    {
        WebGPUEnvironmentError probeError = WebGPUEnvironment.ProbeComputePipelineSupport();
        if (probeError != WebGPUEnvironmentError.Success)
        {
            result = null;
            error = $"WebGPU unavailable: {probeError}.";
            return false;
        }

        result = new WebGpuBenchmarkBackend();
        error = null;
        return true;
    }

    /// <summary>
    /// Renders the benchmark scene through the WebGPU backend and optionally captures a readback preview.
    /// </summary>
    public BenchmarkRenderResult Render(ReadOnlySpan<VisualLine> lines, int width, int height, bool capturePreview)
    {
        WebGPURenderTarget<Bgra32> renderTarget = this.EnsureRenderTarget(width, height);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using (DrawingCanvas<Bgra32> canvas = renderTarget.CreateCanvas(new DrawingOptions()))
        {
            VisualLine.RenderLinesToCanvas(canvas, lines);
            canvas.Flush();
        }

        stopwatch.Stop();

        Image<Bgra32>? preview = null;
        string? readbackError = null;
        if (capturePreview)
        {
            try
            {
                preview = renderTarget.Readback();
            }
            catch (Exception ex)
            {
                preview = null;
                readbackError = ex.Message;
            }
        }

        return new BenchmarkRenderResult(
            stopwatch.Elapsed.TotalMilliseconds,
            preview,
            renderTarget.Graphics.Backend.DiagnosticLastFlushUsedGPU,
            readbackError ?? renderTarget.Graphics.Backend.DiagnosticLastSceneFailure);
    }

    /// <summary>
    /// Gets the name of this backend.
    /// </summary>
    public override string ToString() => "WebGPU";

    /// <inheritdoc />
    public void Dispose()
    {
        this.renderTarget?.Dispose();
        this.renderTarget = null;
    }

    private WebGPURenderTarget<Bgra32> EnsureRenderTarget(int width, int height)
    {
        if (this.renderTarget is not null)
        {
            return this.renderTarget;
        }

        this.renderTarget = new WebGPURenderTarget<Bgra32>(width, height);
        return this.renderTarget;
    }
}
