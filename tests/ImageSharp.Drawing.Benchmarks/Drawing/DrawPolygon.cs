// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

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

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public abstract class DrawPolygon
    {
        private PointF[][] points;

        private Image<Rgba32> image;

        private SDPointF[][] sdPoints;
        private System.Drawing.Bitmap sdBitmap;
        private Graphics sdGraphics;

        private SKPath skPath;
        private SKSurface skSurface;

        protected abstract int Width { get; }
        protected abstract int Height { get; }
        protected abstract float Thickness { get; }

        protected virtual PointF[][] GetPoints(FeatureCollection features) =>
            features.Features.SelectMany(f => PolygonFactory.GetGeoJsonPoints(f,  Matrix3x2.CreateScale(60, 60))).ToArray();

        [GlobalSetup]
        public void Setup()
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));

            FeatureCollection featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);



            this.points = this.GetPoints(featureCollection);
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
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, this.Thickness);

            foreach (SDPointF[] loop in this.sdPoints)
            {
                this.sdGraphics.DrawPolygon(pen, loop);
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
                    c.DrawPolygon(options, Color.White, this.Thickness, loop);
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
                StrokeWidth = this.Thickness,
                IsAntialias = true,
            };

            this.skSurface.Canvas.DrawPath(this.skPath, paint);
        }
    }

    public class DrawPolygonAll : DrawPolygon
    {
        protected override int Width => 7200;
        protected override int Height => 4800;
        protected override float Thickness => 2f;
    }

    public class DrawPolygonMediumThin : DrawPolygon
    {
        protected override int Width => 1000;
        protected override int Height => 1000;
        protected override float Thickness => 1f;

        protected override PointF[][] GetPoints(FeatureCollection features)
        {
            Feature state = features.Features.Single(f => (string) f.Properties["NAME"] == "Mississippi");

            Matrix3x2 transform = Matrix3x2.CreateTranslation(-87, -54)
                                  * Matrix3x2.CreateScale(60, 60);
            return PolygonFactory.GetGeoJsonPoints(state, transform).ToArray();
        }
    }

    public class DrawPolygonMediumThick : DrawPolygonMediumThin
    {
        protected override float Thickness => 10f;
    }
}
