// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    public class DrawingProfilingBenchmarks : IDisposable
    {
        private Image<Rgba32> image;
        private Polygon[] polygons;

        public DrawingProfilingBenchmarks()
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));

            FeatureCollection featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

            PointF[][] points = GetPoints(featureCollection);
            this.polygons = points.Select(pts => new Polygon(new LinearLineSegment(pts))).ToArray();
            
            this.image = new Image<Rgba32>(1000, 1000);
            
            static PointF[][] GetPoints(FeatureCollection features)
            {
                Feature state = features.Features.Single(f => (string) f.Properties["NAME"] == "Mississippi");

                Matrix3x2 transform = Matrix3x2.CreateTranslation(-87, -54)
                                      * Matrix3x2.CreateScale(60, 60);
                return PolygonFactory.GetGeoJsonPoints(state, transform).ToArray();
            }
        }

        [Theory(Skip = "For local profiling only")]
        [InlineData(IntersectionRule.OddEven)]
        [InlineData(IntersectionRule.Nonzero)]
        public void FillPolygon(IntersectionRule intersectionRule)
        {
            const int Times = 100;

            for (int i = 0; i < Times; i++)
            {
                this.image.Mutate(c =>
                {
                    c.SetShapeOptions(new ShapeOptions()
                    {
                        IntersectionRule = intersectionRule
                    });
                    foreach (Polygon polygon in this.polygons)
                    {
                        c.Fill(Color.White, polygon);
                    }
                });
            }
        }

        [Theory(Skip = "For local profiling only")]
        [InlineData(1)]
        [InlineData(10)]
        public void DrawText(int textIterations)
        {
            const int Times = 20;
            const string TextPhrase= "asdfghjkl123456789{}[]+$%?";
            string textToRender = string.Join("/n", Enumerable.Repeat(TextPhrase, textIterations));
            
            Font font = SystemFonts.CreateFont("Arial", 12);

            for (int i = 0; i < Times; i++)
            {
                this.image.Mutate(x => x
                    .SetGraphicsOptions(o => o.Antialias = true)
                    .SetTextOptions(o => o.WrapTextWidth = 780)
                    .DrawText(
                        textToRender,
                        font,
                        Brushes.Solid(Color.HotPink),
                        new PointF(10, 10)));
            }
        }

        public void Dispose()
        {
            this.image.Dispose();
        }
    }
}