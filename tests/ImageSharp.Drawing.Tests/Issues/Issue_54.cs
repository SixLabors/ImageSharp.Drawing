// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    public class Issue_54
    {
        [WindowsFact]
        public void CanDrawWithoutMemoryException()
        {
            int width = 768;
            int height = 438;

            // Creates a new image with empty pixel data.
            using (var image = new Image<Rgba32>(width, height))
            {
                FontFamily family = SystemFonts.Get("verdana");
                Font font = family.CreateFont(48, FontStyle.Bold);

                // The options are optional
                RichTextOptions textOptions = new(font)
                {
                    TabWidth = 8, // a tab renders as 8 spaces wide
                    WrappingLength = width, // greater than zero so we will word wrap at 100 pixels wide
                    HorizontalAlignment = HorizontalAlignment.Right, // right align,
                    Origin = new PointF(0, 100)
                };

                Brush brush = Brushes.Solid(Color.White);
                Pen pen = Pens.Solid(Color.White, 1);
                string text = "sample text";

                // Draw the text
                image.Mutate(x => x.DrawText(textOptions, text, brush, pen));
            }
        }

        [Fact]
        public void PenMustHaveAWidthGreaterThanZero()
        {
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                Pen pen = new SolidPen(Color.White, 0);
            });

            Assert.StartsWith("Parameter \"strokeWidth\" (System.Single) must be greater than 0, was 0", ex.Message);
        }

        [Fact]
        public void ComplexPolygoWithZeroPathsCausesBoundsToBeNonSensicalValue()
        {
            var polygon = new ComplexPolygon(Array.Empty<IPath>());

            Assert.NotEqual(float.NegativeInfinity, polygon.Bounds.Width);
            Assert.NotEqual(float.PositiveInfinity, polygon.Bounds.Width);
            Assert.NotEqual(float.NaN, polygon.Bounds.Width);
        }
    }
}
