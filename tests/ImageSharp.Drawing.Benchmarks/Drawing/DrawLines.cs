// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using SDPoint = System.Drawing.Point;
using SDPointF = System.Drawing.PointF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public class DrawLines
    {
        private PointF[][] points;
        
        private Image<Rgba32> image;
        
        private SDPointF[][] sdPoints;
        private System.Drawing.Bitmap sdBitmap;
        private Graphics sdGraphics;
        
        private SKPath skPath;
        private SKSurface skSurface;

        private const int Width = 7200;
        private const int Height = 4800;

        [GlobalSetup]
        public void Setup()
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));

            this.points = PolygonFactory.GetGeoJsonPoints(jsonContent, Matrix3x2.CreateScale(60, 60));
            this.sdPoints = this.points.Select(pts => pts.Select(p => new SDPointF(p.X, p.Y)).ToArray()).ToArray();
            
            this.skPath = new SKPath();
            foreach (PointF[] ptArr in this.points.Where(pts => pts.Length > 2))
            {
                skPath.MoveTo(ptArr[0].X, ptArr[1].Y);
                for (int i = 1; i < ptArr.Length; i++)
                {
                    skPath.LineTo(ptArr[i].X, ptArr[i].Y);
                }
                skPath.LineTo(ptArr[0].X, ptArr[1].Y);
            }
            
            this.image = new Image<Rgba32>(Width, Height);
            this.sdBitmap = new Bitmap(Width, Height);
            this.sdGraphics = Graphics.FromImage(this.sdBitmap);
            this.sdGraphics.InterpolationMode = InterpolationMode.Default;
            this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            this.skSurface = SKSurface.Create(new SKImageInfo(Width, Height));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            this.image.Dispose();
            this.sdGraphics.Dispose();
            this.sdBitmap.Dispose();
            this.skSurface.Dispose();
            this.skPath.Dispose();
        }
        
        [Benchmark]
        public void SystemDrawing()
        { 
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);

            foreach (SDPointF[] loop in this.sdPoints)
            {
                this.sdGraphics.DrawLines(pen, loop);
            }
        }

        [Benchmark]
        public void ImageSharp_Original()
        {
            ImageSharpImpl(false);
        }
        
        [Benchmark]
        public void ImageSharp_ActiveEdges()
        {
            ImageSharpImpl(true);
        }
        
        private void ImageSharpImpl(bool useActiveEdges)
        {
            var options = new ShapeGraphicsOptions()
            {
                ShapeOptions = new ShapeOptions() { UsePolygonScanner = useActiveEdges}
            };
            this.image.Mutate(c =>
            {
                foreach (PointF[] loop in this.points)
                {
                    c.DrawLines(options, Color.White, 2, loop);
                }
            });
        }

        [Benchmark(Baseline = true)]
        public void SkiaSharp()
        {
            using SKPaint paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.White,
                StrokeWidth = 2f,
                IsAntialias = true,
            };
            
            this.skSurface.Canvas.DrawPath(this.skPath, paint);
        }
    }
}
