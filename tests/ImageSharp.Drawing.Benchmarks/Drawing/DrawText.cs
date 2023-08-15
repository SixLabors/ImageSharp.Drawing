// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using SDBitmap = System.Drawing.Bitmap;
using SDFont = System.Drawing.Font;
using SDRectangleF = System.Drawing.RectangleF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

[MemoryDiagnoser]
public class DrawText
{
    public const int Width = 800;
    public const int Height = 800;

    [Params(1, 20)]
    public int TextIterations { get; set; }

    protected const string TextPhrase = "asdfghjkl123456789{}[]+$%?";

    public string TextToRender => string.Join(" ", Enumerable.Repeat(TextPhrase, this.TextIterations));

    private Image<Rgba32> image;
    private SDBitmap sdBitmap;
    private Graphics sdGraphics;
    private SKBitmap skBitmap;
    private SKCanvas skCanvas;

    private SDFont sdFont;
    private Fonts.Font font;
    private SKTypeface skTypeface;

    [GlobalSetup]
    public void Setup()
    {
        this.image = new Image<Rgba32>(Width, Height);
        this.sdBitmap = new SDBitmap(Width, Height);
        this.sdGraphics = Graphics.FromImage(this.sdBitmap);
        this.sdGraphics.InterpolationMode = InterpolationMode.Default;
        this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        this.sdGraphics.InterpolationMode = InterpolationMode.Default;
        this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        this.skBitmap = new SKBitmap(Width, Height);
        this.skCanvas = new SKCanvas(this.skBitmap);

        this.sdFont = new SDFont("Arial", 12, GraphicsUnit.Point);
        this.font = Fonts.SystemFonts.CreateFont("Arial", 12);
        this.skTypeface = SKTypeface.FromFamilyName("Arial");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.image.Dispose();
        this.sdGraphics.Dispose();
        this.sdBitmap.Dispose();
        this.skCanvas.Dispose();
        this.skBitmap.Dispose();
        this.sdFont.Dispose();
        this.skTypeface.Dispose();
    }

    [Benchmark]
    public void SystemDrawing()
        => this.sdGraphics.DrawString(
            this.TextToRender,
            this.sdFont,
            System.Drawing.Brushes.HotPink,
            new SDRectangleF(10, 10, 780, 780));

    [Benchmark]
    public void ImageSharp()
    {
        RichTextOptions textOptions = new(this.font)
        {
            WrappingLength = 780,
            Origin = new PointF(10, 10)
        };

        this.image.Mutate(x => x.DrawText(textOptions, this.TextToRender, Processing.Brushes.Solid(Color.HotPink)));
    }

    [Benchmark(Baseline = true)]
    public void SkiaSharp()
    {
        using var paint = new SKPaint
        {
            Color = SKColors.HotPink,
            IsAntialias = true,
            TextSize = 16, // 12*1.3333
            Typeface = this.skTypeface
        };

        this.skCanvas.DrawText(this.TextToRender, 10, 10, paint);
    }
}
