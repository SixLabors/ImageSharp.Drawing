// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

[MemoryDiagnoser]
public class DrawTextDecorated
{
    public const int Width = 800;
    public const int Height = 800;

    private const string TextPhrase = "asdfghjkl123456789{}[]+$%?";

    private Image<Rgba32> image;
    private Font font;
    private IPath curve;
    private string text;
    private List<RichTextRun> textRuns;

    [GlobalSetup]
    public void Setup()
    {
        this.image = new Image<Rgba32>(Width, Height);
        this.font = SystemFonts.CreateFont("Arial", 12);
        this.text = string.Join(" ", Enumerable.Repeat(TextPhrase, 20));

        // All three decoration lanes are active so the benchmark is dominated by decoration
        // emission rather than glyph fills.
        this.textRuns =
        [
            new RichTextRun
            {
                Start = 0,
                End = CodePoint.GetCodePointCount(this.text.AsSpan()),
                TextDecorations = TextDecorations.Underline | TextDecorations.Overline | TextDecorations.Strikeout
            }
        ];

        _ = Path.TryParseSvgPath("M80,400 C80,80 400,80 400,400 C400,720 720,720 720,400", out this.curve);
    }

    [GlobalCleanup]
    public void Cleanup() => this.image.Dispose();

    [Benchmark(Baseline = true)]
    public void Linear()
    {
        RichTextOptions textOptions = new(this.font)
        {
            WrappingLength = 780,
            Origin = new PointF(10, 10),
            TextRuns = this.textRuns
        };

        this.image.Mutate(x => x.Paint(
            canvas => canvas.DrawText(textOptions, this.text, Brushes.Solid(Color.HotPink), pen: null)));
    }

    [Benchmark]
    public void OnPath()
    {
        RichTextOptions textOptions = new(this.font)
        {
            WrappingLength = this.curve.ComputeLength(),
            TextRuns = this.textRuns
        };

        this.image.Mutate(x => x.Paint(
            canvas => canvas.DrawText(textOptions, this.text, this.curve, Brushes.Solid(Color.HotPink), pen: null)));
    }
}
