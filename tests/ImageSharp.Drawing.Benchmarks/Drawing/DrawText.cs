// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using SDPoint = System.Drawing.Point;
using SDPointF = System.Drawing.PointF;
using SDBitmap = System.Drawing.Bitmap;
using SDRectangleF = System.Drawing.RectangleF;
using SDFont = System.Drawing.Font;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    [MemoryDiagnoser]
    public class DrawText
    {
        public const int Width = 800;
        public const int Height = 800;

        [Params(1, 20)]
        public int TextIterations { get; set; }

        protected const string TextPhrase= "asdfghjkl123456789{}[]+$%?";

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
            this.sdBitmap = new Bitmap(Width, Height);
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
        {
            this.sdGraphics.DrawString(
                this.TextToRender,
                this.sdFont,
                System.Drawing.Brushes.HotPink,
                new SDRectangleF(10, 10, 780, 780));
        }

        [Benchmark]
        public void ImageSharp()
        {
            this.image.Mutate(x => x
                .SetGraphicsOptions(o => o.Antialias = true)
                .SetTextOptions(o => o.WrapTextWidth = 780)
                .DrawText(
                    this.TextToRender,
                    font,
                    Processing.Brushes.Solid(Color.HotPink),
                    new PointF(10, 10)));
        }

        [Benchmark(Baseline = true)]
        public void SkiaSharp()
        {
            using SKPaint paint = new SKPaint
            {
                Color = SKColors.HotPink,
                IsAntialias = true,
                TextSize = 16, // 12*1.3333
                Typeface = skTypeface
            };
            
            this.skCanvas.DrawText(TextToRender, 10, 10, paint);
        }
    }
}
