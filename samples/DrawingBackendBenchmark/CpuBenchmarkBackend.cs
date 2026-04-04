// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace DrawingBackendBenchmark;

/// <summary>
/// CPU implementation of the benchmark scene used as the baseline backend.
/// </summary>
internal sealed class CpuBenchmarkBackend : IBenchmarkBackend
{
    private readonly Configuration configuration;
    private Image<Bgra32>? image;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuBenchmarkBackend"/> class.
    /// </summary>
    public CpuBenchmarkBackend() => this.configuration = Configuration.Default.Clone();

    /// <summary>
    /// Renders the benchmark scene through the CPU backend.
    /// </summary>
    public BenchmarkRenderResult Render(ReadOnlySpan<VisualLine> lines, int width, int height, bool capturePreview)
    {
        Image<Bgra32> image = this.EnsureImage(width, height);
        Buffer2DRegion<Bgra32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using (DrawingCanvas<Bgra32> canvas = new(this.configuration, region, new DrawingOptions()))
        {
            VisualLine.RenderLinesToCanvas(canvas, lines);
            canvas.Flush();
        }

        stopwatch.Stop();

        Image<Bgra32>? preview = capturePreview ? image.Clone() : null;
        return new BenchmarkRenderResult(stopwatch.Elapsed.TotalMilliseconds, preview);
    }

    /// <summary>
    /// Gets the name of this backend.
    /// </summary>
    public override string ToString() => "CPU";

    /// <inheritdoc />
    public void Dispose()
    {
        this.image?.Dispose();
        this.image = null;
    }

    /// <summary>
    /// Gets the cached CPU render target image for the benchmark backend.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The cached image.</returns>
    private Image<Bgra32> EnsureImage(int width, int height)
    {
        if (this.image is not null)
        {
            return this.image;
        }

        this.image = new Image<Bgra32>(width, height);
        return this.image;
    }
}
