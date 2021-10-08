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
using SDBitmap = System.Drawing.Bitmap;
using SDPointF = System.Drawing.PointF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing
{
    public abstract class FillPolygon
    {
        private PointF[][] points;
        private Polygon[] polygons;
        private SDPointF[][] sdPoints;
        private List<SKPath> skPaths;

        private Image<Rgba32> image;
        private SDBitmap sdBitmap;
        private Graphics sdGraphics;
        private SKBitmap skBitmap;
        private SKCanvas skCanvas;

        protected abstract int Width { get; }

        protected abstract int Height { get; }

        protected virtual PointF[][] GetPoints(FeatureCollection features)
            => features.Features
            .SelectMany(f => PolygonFactory.GetGeoJsonPoints(f, Matrix3x2.CreateScale(60, 60)))
            .ToArray();

        [GlobalSetup]
        public void Setup()
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));

            FeatureCollection featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

            this.points = this.GetPoints(featureCollection);
            this.polygons = this.points.Select(pts => new Polygon(new LinearLineSegment(pts))).ToArray();

            this.sdPoints = this.points.Select(pts => pts.Select(p => new SDPointF(p.X, p.Y)).ToArray()).ToArray();

            this.skPaths = new List<SKPath>();
            foreach (PointF[] ptArr in this.points.Where(pts => pts.Length > 2))
            {
                var skPath = new SKPath();
                skPath.MoveTo(ptArr[0].X, ptArr[1].Y);
                for (int i = 1; i < ptArr.Length; i++)
                {
                    skPath.LineTo(ptArr[i].X, ptArr[i].Y);
                }

                skPath.LineTo(ptArr[0].X, ptArr[1].Y);
                this.skPaths.Add(skPath);
            }

            this.image = new Image<Rgba32>(this.Width, this.Height);
            this.sdBitmap = new SDBitmap(this.Width, this.Height);
            this.sdGraphics = Graphics.FromImage(this.sdBitmap);
            this.sdGraphics.InterpolationMode = InterpolationMode.Default;
            this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            this.skBitmap = new SKBitmap(this.Width, this.Height);
            this.skCanvas = new SKCanvas(this.skBitmap);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            this.image.Dispose();
            this.sdGraphics.Dispose();
            this.sdBitmap.Dispose();
            this.skCanvas.Dispose();
            this.skBitmap.Dispose();
            foreach (SKPath skPath in this.skPaths)
            {
                skPath.Dispose();
            }
        }

        [Benchmark]
        public void SystemDrawing()
        {
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

            foreach (SDPointF[] loop in this.sdPoints)
            {
                this.sdGraphics.FillPolygon(brush, loop);
            }
        }

        [Benchmark]
        public void ImageSharp()
            => this.image.Mutate(c =>
            {
                foreach (Polygon polygon in this.polygons)
                {
                    c.Fill(Color.White, polygon);
                }
            });

        [Benchmark(Baseline = true)]
        public void SkiaSharp()
        {
            foreach (SKPath path in this.skPaths)
            {
                // Emulate using different color for each polygon:
                using var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = SKColors.White,
                    IsAntialias = true,
                };
                this.skCanvas.DrawPath(path, paint);
            }
        }
    }

    public class FillPolygonAll : FillPolygon
    {
        protected override int Width => 7200;

        protected override int Height => 4800;
    }

    public class FillPolygonMedium : FillPolygon
    {
        protected override int Width => 1000;

        protected override int Height => 1000;

        protected override PointF[][] GetPoints(FeatureCollection features)
        {
            Feature state = features.Features.Single(f => (string)f.Properties["NAME"] == "Mississippi");

            Matrix3x2 transform = Matrix3x2.CreateTranslation(-87, -54)
                                  * Matrix3x2.CreateScale(60, 60);
            return PolygonFactory.GetGeoJsonPoints(state, transform).ToArray();
        }

        // ** 11/13/2020 @ Anton's PC ***
        // BenchmarkDotNet=v0.12.1, OS=Windows 10.0.18363.1198 (1909/November2018Update/19H2)
        // Intel Core i7-7700HQ CPU 2.80GHz (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
        //     .NET Core SDK=5.0.100-preview.6.20318.15
        // [Host]     : .NET Core 3.1.1 (CoreCLR 4.700.19.60701, CoreFX 4.700.19.60801), X64 RyuJIT
        // DefaultJob : .NET Core 3.1.1 (CoreCLR 4.700.19.60701, CoreFX 4.700.19.60801), X64 RyuJIT
        //
        //
        // |        Method |       Mean |    Error |    StdDev | Ratio | RatioSD |
        // |-------------- |-----------:|---------:|----------:|------:|--------:|
        // | SystemDrawing |   457.4 us |  9.07 us |  23.40 us |  2.15 |    0.10 |
        // |    ImageSharp | 3,079.5 us | 61.45 us | 138.71 us | 14.30 |    0.89 |
        // |     SkiaSharp |   217.7 us |  4.29 us |   6.55 us |  1.00 |    0.00 |
    }

    public class FillPolygonSmall : FillPolygon
    {
        protected override int Width => 1000;

        protected override int Height => 1000;

        protected override PointF[][] GetPoints(FeatureCollection features)
        {
            Feature state = features.Features.Single(f => (string)f.Properties["NAME"] == "Utah");

            Matrix3x2 transform = Matrix3x2.CreateTranslation(-60, -40)
                                  * Matrix3x2.CreateScale(60, 60);
            return PolygonFactory.GetGeoJsonPoints(state, transform).ToArray();
        }
    }
}
