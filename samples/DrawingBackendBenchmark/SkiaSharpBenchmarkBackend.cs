// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace DrawingBackendBenchmark;

internal sealed class SkiaSharpBenchmarkBackend : IBenchmarkBackend, IDisposable
{
    private static readonly SKColor BackgroundColor = SKColor.Parse("#003366");

    private readonly GRContext? context;
    private SKSurface? surface;
    private int cachedWidth;
    private int cachedHeight;

    public SkiaSharpBenchmarkBackend(GRContext? context = null) => this.context = context;

    public bool IsGpu => this.context is not null;

    /// <summary>
    /// Renders the benchmark scene through Skia and optionally captures a readback preview.
    /// </summary>
    /// <remarks>
    /// The returned duration measures submission through <c>Flush()</c>. Preview readback happens afterward, so the
    /// reported GPU timing is a submission timing rather than a fully synchronized render-complete timing.
    /// </remarks>
    public BenchmarkRenderResult Render(ReadOnlySpan<VisualLine> lines, int width, int height, bool capturePreview)
    {
        this.EnsureSurface(width, height);

        SKCanvas canvas = this.surface!.Canvas;
        using SKPaint paint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            StrokeCap = SKStrokeCap.Square,
        };

        Stopwatch stopwatch = Stopwatch.StartNew();

        canvas.Clear(BackgroundColor);

        foreach (VisualLine line in lines)
        {
            paint.Color = line.SkiaColor;
            paint.StrokeWidth = line.Width;
            canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, paint);
        }

        canvas.Flush();
        this.context?.Flush();

        stopwatch.Stop();

        Image<Bgra32>? preview = null;
        if (capturePreview)
        {
            preview = new Image<Bgra32>(width, height);
            if (preview.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            {
                SKImageInfo readbackInfo = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using System.Buffers.MemoryHandle pin = memory.Pin();
                unsafe
                {
                    this.surface.ReadPixels(readbackInfo, (nint)pin.Pointer, width * 4, 0, 0);
                }
            }
        }

        return new BenchmarkRenderResult(stopwatch.Elapsed.TotalMilliseconds, preview, usedGpu: this.context != null);
    }

    public override string ToString() => this.context != null ? "SkiaSharp (GPU)" : "SkiaSharp (CPU)";

    public void Dispose() => this.surface?.Dispose();

    private void EnsureSurface(int width, int height)
    {
        if (this.surface != null && this.cachedWidth == width && this.cachedHeight == height)
        {
            return;
        }

        this.surface?.Dispose();

        SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        this.surface = this.context != null
            ? SKSurface.Create(this.context, false, info)
            : SKSurface.Create(info);
        this.cachedWidth = width;
        this.cachedHeight = height;
    }
}
