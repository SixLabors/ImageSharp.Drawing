// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    [GroupOutput("Drawing")]
    public class DrawPathTests
    {
        public static readonly TheoryData<string, byte, float> DrawPathData =
            new TheoryData<string, byte, float>
            {
                { "White", 255, 1.5f },
                { "Red", 255, 3 },
                { "HotPink", 255, 5 },
                { "HotPink", 150, 5 },
                { "White", 255, 15 },
            };

        [Theory]
        [WithSolidFilledImages(nameof(DrawPathData), 300, 600, "Blue", PixelTypes.Rgba32)]
        public void DrawPath<TPixel>(TestImageProvider<TPixel> provider, string colorName, byte alpha, float thickness)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var linearSegment = new LinearLineSegment(
                new Vector2(10, 10),
                new Vector2(200, 150),
                new Vector2(50, 300));
            var bezierSegment = new CubicBezierLineSegment(
                new Vector2(50, 300),
                new Vector2(500, 500),
                new Vector2(60, 10),
                new Vector2(10, 400));
            var ellipticArcSegment1 = new EllipticalArcLineSegment(80, 425, (float)Math.Sqrt(5525), 40, (float)(Math.Atan2(25, 70) * 180 / Math.PI), -90, -180, Matrix3x2.Identity);
            var ellipticArcSegment2 = new EllipticalArcLineSegment(150, 520, 140, 70, 0, 180, 360, Matrix3x2.Identity);

            var path = new Path(linearSegment, bezierSegment, ellipticArcSegment1, ellipticArcSegment2);

            Rgba32 rgba = TestUtils.GetColorByName(colorName);
            rgba.A = alpha;
            Color color = rgba;

            FormattableString testDetails = $"{colorName}_A{alpha}_T{thickness}";

            provider.RunValidatingProcessorTest(
                x => x.Draw(color, thickness, path),
                testDetails,
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }

        [Theory]
        [WithSolidFilledImages(256, 256, "Black", PixelTypes.Rgba32)]
        public void PathExtendingOffEdgeOfImageShouldNotBeCropped<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color color = Color.White;
            Pen pen = Pens.Solid(color, 5f);

            provider.RunValidatingProcessorTest(
                x =>
                    {
                        for (int i = 0; i < 300; i += 20)
                        {
                            var points = new PointF[] { new Vector2(100, 2), new Vector2(-10, i) };
                            x.DrawLines(pen, points);
                        }
                    },
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }

        [Theory]
        [WithSolidFilledImages(40, 40, "White", PixelTypes.Rgba32)]
        public void DrawPathClippedOnTop<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            PointF[] points =
            {
                new PointF(10f, -10f),
                new PointF(20f, 20f),
                new PointF(30f, -30f)
            };

            IPath path = new PathBuilder().AddLines(points).Build();

            provider.VerifyOperation(
                image => image.Mutate(x => x.Draw(Color.Black, 1, path)),
                appendSourceFileOrDescription: false,
                appendPixelTypeToFileName: false);
        }

        [Theory]
        [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32)]
        public void DrawPathArcTo<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            // TODO:
            // There's something wrong with EllipticalArcLineSegment.
            var pb = new PathBuilder();

            // This works using code derived from SVGSharpie
            pb.MoveTo(new Vector2(50, 50));
            pb.ArcTo(20, 50, -72, false, true, new Vector2(200, 200));
            IPath path = pb.Build();

            // This fails using the same derived properties.
            pb = new PathBuilder();
            pb.AddEllipticalArc(new(125, 125), 61.218582F, 153.046448F, -72, -38.1334763F, 180);
            IPath path2 = pb.Build();

            // Filling for the moment for visibility.
            provider.VerifyOperation(
                image => image.Mutate(x => x.Fill(Color.Black, path).Fill(Color.Red.WithAlpha(.5F), path2)),
                appendSourceFileOrDescription: false,
                appendPixelTypeToFileName: false);
        }
    }
}
