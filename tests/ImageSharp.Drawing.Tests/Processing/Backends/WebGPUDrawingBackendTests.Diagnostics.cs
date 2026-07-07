// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public partial class WebGPUDrawingBackendTests
{
    // A blank or near-uniform frame (the black-screen failure mode) has only a handful of distinct
    // colours; a correctly rendered batch of thousands of random-coloured, blended lines has hundreds of
    // thousands. This threshold cleanly separates a real render from a dropped-coverage failure without
    // depending on how much background remains visible at high line density.
    private const int RenderedContentDistinctColorFloor = 2000;

    // A moderate (100K-line) stroke batch whose scratch buffers fit under the device's per-buffer ceiling
    // must render in a single pass. The bounding-box-diagonal segment estimate previously over-seeded the
    // scratch buffers so badly that even this scene spilled over the binding limit and chunked; both fill
    // and stroke estimates are now seeded from exact per-line tile spans, keeping the buffers realistic.
    [WebGPUFact]
    public void FillManyLines_ModerateScene_RendersSinglePass()
    {
        (int Distinct, bool Chunked, int Errors) result = RenderManyLines(600, 400, lineCount: 100_000, passes: 3);
        Assert.Equal(0, result.Errors);
        Assert.False(result.Chunked, "A 100K-line scene at 600x400 fits one binding and must not chunk.");
        Assert.True(result.Distinct >= RenderedContentDistinctColorFloor, $"Frame is blank: {result.Distinct} distinct colours.");
    }

    // Regression guard for the benchmark app's black screen: jumping straight to 100K lines at the app's
    // window size (no ramp-up growing the retained scratch first) previously produced a fully transparent
    // frame. The scene's ptcl buffer exceeds the device's 256 MiB maxBufferSize, so it cannot render in a
    // single buffer; the chunking ceiling now respects maxBufferSize (not just the larger binding size),
    // so the scene chunks and renders instead of failing buffer creation with an invalid-buffer error.
    [WebGPUFact]
    public void FillManyLines_AtWindowSize_RendersWithoutBlackScreen()
    {
        (int Distinct, bool Chunked, int Errors) result = RenderManyLines(1600, 1100, lineCount: 100_000, passes: 1);
        Assert.Equal(0, result.Errors);
        Assert.True(result.Distinct >= RenderedContentDistinctColorFloor, $"First flush is a black screen: {result.Distinct} distinct colours.");
    }

    // A correctly seeded scene renders fully on its first flush. An under-seeded scratch estimate (for
    // example counting only the stroke centerline instead of the emitted offset sides, joins and caps)
    // overflows the first flush, drops coverage, and only converges after later flushes grow the buffers.
    // So a single-pass render must be pixel-identical to a converged multi-pass render of the same scene.
    [WebGPUFact]
    public void FillManyLines_SinglePass_MatchesConvergedRender()
    {
        const int width = 600;
        const int height = 400;
        (PointF Start, PointF End, Color Color, float Width)[] lines = CreateRandomLines(width, height, 100_000);
        Brush background = Brushes.Solid(Color.ParseHex("#003366"));

        using Image<Rgba32> singlePass = RenderManyLinesToImage(width, height, background, lines, passes: 1);
        using Image<Rgba32> converged = RenderManyLinesToImage(width, height, background, lines, passes: 4);

        long differing = CountDifferingPixels(singlePass, converged);
        long total = (long)width * height;
        Assert.True(
            differing <= total / 500,
            $"Single-pass render differs from the converged render in {differing} of {total} pixels, indicating under-seeded scratch.");
    }

    private static Image<Rgba32> RenderManyLinesToImage(int width, int height, Brush background, (PointF Start, PointF End, Color Color, float Width)[] lines, int passes)
    {
        using WebGPURenderTarget target = new(WebGPUTextureFormat.Bgra8Unorm, width, height);
        for (int pass = 0; pass < passes; pass++)
        {
            DrawLines(target, background, lines);
        }

        return target.ReadbackImage().CloneAs<Rgba32>();
    }

    private static long CountDifferingPixels(Image<Rgba32> first, Image<Rgba32> second)
    {
        long differing = 0;
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
            {
                if (!first[x, y].Equals(second[x, y]))
                {
                    differing++;
                }
            }
        }

        return differing;
    }

    private static (int Distinct, bool Chunked, int Errors) RenderManyLines(int width, int height, int lineCount, int passes)
    {
        (PointF Start, PointF End, Color Color, float Width)[] lines = CreateRandomLines(width, height, lineCount);
        Brush background = Brushes.Solid(Color.ParseHex("#003366"));

        int gpuErrors = 0;
        WebGPUEnvironment.UncapturedError = (_, _) => Interlocked.Increment(ref gpuErrors);
        try
        {
            using WebGPURenderTarget target = new(WebGPUTextureFormat.Bgra8Unorm, width, height);
            for (int pass = 0; pass < passes; pass++)
            {
                DrawLines(target, background, lines);
            }

            using Image<Rgba32> readback = target.ReadbackImage().CloneAs<Rgba32>();
            return (CountDistinctColors(readback), target.Backend.DiagnosticLastFlushUsedChunking, gpuErrors);
        }
        finally
        {
            WebGPUEnvironment.UncapturedError = null;
        }
    }

    private static (PointF Start, PointF End, Color Color, float Width)[] CreateRandomLines(int width, int height, int lineCount)
    {
        Random rng = new(0);
        (PointF Start, PointF End, Color Color, float Width)[] lines = new (PointF, PointF, Color, float)[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            lines[i] = (
                new PointF((float)(rng.NextDouble() * width), (float)(rng.NextDouble() * height)),
                new PointF((float)(rng.NextDouble() * width), (float)(rng.NextDouble() * height)),
                Color.FromPixel(new Rgba32((byte)rng.Next(255), (byte)rng.Next(255), (byte)rng.Next(255), (byte)rng.Next(255))),
                rng.Next(1, 10));
        }

        return lines;
    }

    private static void DrawLines(WebGPURenderTarget target, Brush background, ReadOnlySpan<(PointF Start, PointF End, Color Color, float Width)> lines)
    {
        using DrawingCanvas canvas = target.CreateCanvas();
        canvas.Fill(background);
        foreach ((PointF start, PointF end, Color color, float width) in lines)
        {
            canvas.DrawLine(new SolidPen(color, width), start, end);
        }
    }

    private static int CountDistinctColors(Image<Rgba32> image)
    {
        HashSet<uint> colors = new();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    colors.Add(row[x].PackedValue);
                }
            }
        });

        return colors.Count;
    }
}
