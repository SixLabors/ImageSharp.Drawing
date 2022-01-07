// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    public class Issue_175
    {
        [Fact]
        public void CanRotateFilledFont()
        {
            if (!TestEnvironment.IsWindows)
            {
                return;
            }

            using (var image = new Image<Rgba32>(300, 200))
            {
                string text = "QuickTYZ";
                int rotationAngle = 90;
                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 40, Fonts.FontStyle.Regular);

                AffineTransformBuilder builder = new AffineTransformBuilder()
                        .AppendRotationDegrees(rotationAngle)
                        .AppendTranslation(new PointF(0, 0));
                var drawingOptions = new DrawingOptions
                {
                    Transform = builder.BuildMatrix(image.Bounds())
                };

                image.Mutate(c => c.DrawText(drawingOptions, text, font, Brushes.Solid(Color.Red), new PointF(0, 0)));

                // ensure the font renders the same as the test image
                IEnumerable<ImageSimilarityReport> reports = ExactImageComparer.Instance.CompareImages(Image.Load<Rgba32>(TestFile.GetInputFileFullPath(TestImages.Png.Issue175Filled)), image);
                Assert.False(reports.Any());

                // image.SaveAsPng(@"./issue175_filled.png");
            }
        }

        [Fact]
        public void CanRotateOutlineFont()
        {
            if (!TestEnvironment.IsWindows)
            {
                return;
            }

            using (var image = new Image<Rgba32>(300, 200))
            {
                string text = "QuickTYZ";
                int rotationAngle = 90;
                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 40, Fonts.FontStyle.Regular);

                AffineTransformBuilder builder = new AffineTransformBuilder()
                        .AppendRotationDegrees(rotationAngle)
                        .AppendTranslation(new PointF(0, 0));
                var drawingOptions = new DrawingOptions
                {
                    Transform = builder.BuildMatrix(image.Bounds())
                };
                image.Mutate(c => c.DrawText(drawingOptions, text, font, Pens.Solid(Color.Blue, 1), new PointF(0, 0)));

                // ensure the font renders the same as the test image
                IEnumerable<ImageSimilarityReport> reports = ExactImageComparer.Instance.CompareImages(Image.Load<Rgba32>(TestFile.GetInputFileFullPath(TestImages.Png.Issue175Outlined)), image);
                Assert.False(reports.Any());

                // image.SaveAsPng(@"./issue175_outlined.png");
            }
        }
    }
}
