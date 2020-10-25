using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    [GroupOutput("Drawing")]
    public class DrawingRobustnessTests
    {
        
        [Theory]
        // [WithSolidFilledImages(3600, 2400, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 30, 30, false)]
        [WithSolidFilledImages(3600, 2400, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 30, 30, true)]
        // [WithSolidFilledImages(7200, 4800, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 60, 60, false)]
        // [WithSolidFilledImages(7200, 4800, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 60, 60, true)]
        public void LargeGeoJson(TestImageProvider<Rgba32> provider, string geoJsonFile, int aa, float sx, float sy, bool usePolygonScanner)
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(geoJsonFile));

            PointF[][] points = PolygonFactory.GetGeoJsonPoints(jsonContent, Matrix3x2.CreateScale(sx, sy));

            using Image<Rgba32> image = provider.GetImage();
            var options = new ShapeGraphicsOptions()
            {
                GraphicsOptions = new GraphicsOptions() {Antialias = aa > 0, AntialiasSubpixelDepth = aa},
                ShapeOptions = new ShapeOptions() { UsePolygonScanner = usePolygonScanner}
            };
            foreach (PointF[] loop in points)
            {
                image.Mutate(c => DrawLineExtensions.DrawLines(c, options, Color.White, 1.0f, loop));
            }

            string details = $"_{System.IO.Path.GetFileName(geoJsonFile)}_{sx}x{sy}_aa{aa}";
            if (usePolygonScanner)
            {
                details += "_Scanner";
            }

            image.DebugSave(provider,
                details,
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }

        [Theory]
        [WithSolidFilledImages(10000, 10000, "Black", PixelTypes.Rgba32, 16, 0, false)]
        [WithSolidFilledImages(10000, 10000, "Black", PixelTypes.Rgba32, 16, 0, true)]
        [WithSolidFilledImages(10000, 10000, "Black", PixelTypes.Rgba32, 16, 5000, false)]
        [WithSolidFilledImages(10000, 10000, "Black", PixelTypes.Rgba32, 16, 5000, true)]
        [WithSolidFilledImages(10000, 10000, "Black", PixelTypes.Rgba32, 16, 9000, true)]
        public void Mississippi(TestImageProvider<Rgba32> provider, int aa, int offset, bool usePolygonScanner)
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));
            
            FeatureCollection features = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

            var missisipiGeom = features.Features.Single(f => (string) f.Properties["NAME"] == "Mississippi").Geometry;

            var transform = Matrix3x2.CreateTranslation(-87, -54)
                            * Matrix3x2.CreateScale(60, 60)
                            * Matrix3x2.CreateTranslation(offset, offset);
            IReadOnlyList<PointF[]> points =PolygonFactory.GetGeoJsonPoints(missisipiGeom, transform);
            
            using Image<Rgba32> image = provider.GetImage();
            var options = new ShapeGraphicsOptions()
            {
                GraphicsOptions = new GraphicsOptions() {Antialias = aa > 0, AntialiasSubpixelDepth = aa},
                ShapeOptions = new ShapeOptions() { UsePolygonScanner = usePolygonScanner}
            };
            foreach (PointF[] loop in points)
            {
                image.Mutate(c => c.DrawLines(options, Color.White, 1.0f, loop));
            }

            string details = $"_aa{aa}_t{offset}";
            if (usePolygonScanner)
            {
                details += "_Scanner";
            }

            image.DebugSave(provider,
                details,
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5000)]
        [InlineData(9000)]
        public void Missisippi_Skia(int offset)
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));
            
            FeatureCollection features = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

            var missisipiGeom = features.Features.Single(f => (string) f.Properties["NAME"] == "Mississippi").Geometry;

            var transform = Matrix3x2.CreateTranslation(-87, -54)
                            * Matrix3x2.CreateScale(60, 60)
                            * Matrix3x2.CreateTranslation(offset, offset);
            IReadOnlyList<PointF[]> points =PolygonFactory.GetGeoJsonPoints(missisipiGeom, transform);
            
            
            SKPath path = new SKPath();

            foreach (PointF[] pts in points.Where(p => p.Length > 2))
            {
                path.MoveTo(pts[0].X, pts[0].Y);

                for (int i = 0; i < pts.Length; i++)
                {
                    path.LineTo(pts[i].X, pts[i].Y);
                }
                path.LineTo(pts[0].X, pts[0].Y);
            }
            
            SKImageInfo imageInfo = new SKImageInfo(10000, 10000);

            using SKPaint paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.White,
                StrokeWidth = 1f,
                IsAntialias = true,
            };
            
            using SKSurface surface = SKSurface.Create(imageInfo);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(0,0, 0));
            canvas.DrawPath(path, paint);

            string outDir = TestEnvironment.CreateOutputDirectory("Skia");
            string fn = System.IO.Path.Combine(outDir, $"Missisippi_Skia_{offset}.png");
            
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

            using FileStream fs = File.Create(fn);
            data.SaveTo(fs);
        }
    }
}