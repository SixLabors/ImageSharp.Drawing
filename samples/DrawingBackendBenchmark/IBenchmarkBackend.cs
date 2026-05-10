// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace DrawingBackendBenchmark;

/// <summary>
/// Base interface for benchmark backends exposing their shared render method.
/// </summary>
internal interface IBenchmarkBackend : IDisposable
{
    /// <summary>
    /// Renders the benchmark scene and returns timing and optional preview data.
    /// </summary>
    /// <param name="lines">The visual lines to render.</param>
    /// <param name="width">The width of the render target.</param>
    /// <param name="height">The height of the render target.</param>
    /// <param name="capturePreview">Whether to capture a preview image of the final frame.</param>
    /// <returns>The benchmark render result including timing and diagnostics.</returns>
    public BenchmarkRenderResult Render(ReadOnlySpan<VisualLine> lines, int width, int height, bool capturePreview);
}
