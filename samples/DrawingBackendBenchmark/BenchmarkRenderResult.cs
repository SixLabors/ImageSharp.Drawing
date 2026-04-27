// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DrawingBackendBenchmark;

/// <summary>
/// One completed benchmark render, including timing, optional preview pixels, and backend diagnostics.
/// </summary>
internal sealed class BenchmarkRenderResult : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BenchmarkRenderResult"/> class.
    /// </summary>
    public BenchmarkRenderResult(double renderMilliseconds, Image<Bgra32>? preview, string? backendFailure = null)
    {
        this.RenderMilliseconds = renderMilliseconds;
        this.Preview = preview;
        this.BackendFailure = backendFailure;
    }

    /// <summary>
    /// Gets the elapsed render time for this iteration.
    /// </summary>
    public double RenderMilliseconds { get; }

    /// <summary>
    /// Gets the optional preview image captured for the UI.
    /// </summary>
    public Image<Bgra32>? Preview { get; }

    /// <summary>
    /// Gets the backend failure reason, when one was reported.
    /// </summary>
    public string? BackendFailure { get; }

    /// <inheritdoc />
    public void Dispose() => this.Preview?.Dispose();
}
