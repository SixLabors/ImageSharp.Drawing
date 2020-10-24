// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    [GroupOutput("Drawing")]
    public class DrawLinesTests
    {
        [Theory]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, 2.5, true)]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 0.6f, 10, true)]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, 5, false)]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Bgr24, "Yellow", 1f, 10, true)]
        public void DrawLines_Simple<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
            var pen = new Pen(color, thickness);

            DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
        }

        [Theory]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "White", 1f, 5, false)]
        public void DrawLines_Dash<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
            Pen pen = Pens.Dash(color, thickness);

            DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
        }

        [Theory]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "LightGreen", 1f, 5, false)]
        public void DrawLines_Dot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
            Pen pen = Pens.Dot(color, thickness);

            DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
        }

        [Theory]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Yellow", 1f, 5, false)]
        public void DrawLines_DashDot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
            Pen pen = Pens.DashDot(color, thickness);

            DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
        }

        [Theory]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, "Black", 1f, 5, false)]
        public void DrawLines_DashDotDot<TPixel>(TestImageProvider<TPixel> provider, string colorName, float alpha, float thickness, bool antialias)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color color = TestUtils.GetColorByName(colorName).WithAlpha(alpha);
            Pen pen = Pens.DashDotDot(color, thickness);

            DrawLinesImpl(provider, colorName, alpha, thickness, antialias, pen);
        }


        private static void DrawLinesImpl<TPixel>(
            TestImageProvider<TPixel> provider,
            string colorName,
            float alpha,
            float thickness,
            bool antialias,
            Pen pen)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            PointF[] simplePath = { new Vector2(10, 10), new Vector2(200, 150), new Vector2(50, 300) };

            var options = new GraphicsOptions { Antialias = antialias };

            string aa = antialias ? "" : "_NoAntialias";
            FormattableString outputDetails = $"{colorName}_A({alpha})_T({thickness}){aa}";

            provider.RunValidatingProcessorTest(
                c => c.SetGraphicsOptions(options).DrawLines(pen, simplePath),
                outputDetails,
                appendSourceFileOrDescription: false);
        }

        [Theory]
        [WithSolidFilledImages(3600, 2400, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 30, 30, false)]
        [WithSolidFilledImages(3600, 2400, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 30, 30, true)]
        [WithSolidFilledImages(7200, 4800, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 60, 60, false)]
        [WithSolidFilledImages(7200, 4800, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 60, 60, true)]
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
                image.Mutate(c => c.DrawLines(options, Color.White, 1.0f, loop));
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
    }
}
