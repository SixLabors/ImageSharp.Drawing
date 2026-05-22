// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

/// <summary>
/// Measures the per-canvas shared glyph-outline cache (opt-in via
/// <see cref="GlyphCacheDefaultsExtensions.SetSharedGlyphCache"/>) against the default
/// per-call cache.
/// <para>
/// Unlike <see cref="DrawTextRepeatedGlyphs"/> (a single <c>DrawText</c> call, where the
/// per-call cache already captures repeats), this draws <see cref="RunCount"/> separate
/// <c>DrawText</c> calls that reuse a common alphabet across runs - the cross-call reuse the
/// shared cache is designed to exploit. The baseline rebuilds each run's glyph outlines from
/// scratch; the shared variant reuses outlines built by earlier runs on the same canvas.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class DrawTextSharedGlyphCache
{
    public const int Width = 700;

    // Vertical advance between runs. The canvas height is sized in Setup to fit every run so all
    // text is actually rasterized (not culled off-canvas), keeping the on/off comparison fair.
    private const float LineHeight = 20F;

    private readonly Brush brush = Brushes.Solid(Color.Black);

    private readonly DrawingOptions drawingOptions = new()
    {
        GraphicsOptions = new GraphicsOptions { Antialias = true }
    };

    private Image<Rgba32> privateCacheImage;
    private Image<Rgba32> sharedCacheImage;
    private Font font;
    private string[] runs;

    [Params(50, 250, 1000)]
    public int RunCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.font = SystemFonts.CreateFont("Arial", 16);

        // Short runs drawn as separate DrawText calls. They share a common word pool so glyph
        // outlines overlap heavily across runs, which is exactly what a per-canvas cache can reuse.
        string[] words = ["The", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "Sphinx", "black", "quartz", "judge", "vow"];
        this.runs = new string[this.RunCount];
        for (int i = 0; i < this.RunCount; i++)
        {
            this.runs[i] = string.Join(
                ' ',
                words[i % words.Length],
                words[(i + 3) % words.Length],
                words[(i + 7) % words.Length],
                words[(i + 11) % words.Length]);
        }

        // Two isolated configurations differing only in the opt-in flag. Cloning Configuration.Default
        // gives an independent Properties bag (the same pattern used to scope SetDrawingBackend), so
        // setting the flag on one clone does not affect the other or the global default.
        Configuration privateConfig = Configuration.Default.Clone();
        privateConfig.SetSharedGlyphCache(false);

        Configuration sharedConfig = Configuration.Default.Clone();
        sharedConfig.SetSharedGlyphCache(true);

        // Tall enough to lay every run on its own line within the canvas bounds.
        int height = 16 + (int)(this.RunCount * LineHeight);
        this.privateCacheImage = new Image<Rgba32>(privateConfig, Width, height);
        this.sharedCacheImage = new Image<Rgba32>(sharedConfig, Width, height);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.privateCacheImage.Dispose();
        this.sharedCacheImage.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Per-call glyph cache (default)")]
    public void PrivateGlyphCache()
        => this.privateCacheImage.Mutate(c => c.Paint(this.drawingOptions, this.DrawRuns));

    [Benchmark(Description = "Shared per-canvas glyph cache")]
    public void SharedGlyphCache()
        => this.sharedCacheImage.Mutate(c => c.Paint(this.drawingOptions, this.DrawRuns));

    private void DrawRuns(DrawingCanvas canvas)
    {
        float y = 8;
        foreach (string run in this.runs)
        {
            RichTextOptions options = new(this.font) { Origin = new PointF(8, y) };
            canvas.DrawText(options, run, this.brush, null);
            y += LineHeight;
        }
    }
}
