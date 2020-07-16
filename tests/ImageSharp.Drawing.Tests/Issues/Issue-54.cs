using System;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    public class Issue_54
    {
        [Fact]
        public void CanDrawWithoutMemoryException()
        {
            int width = 768;
            int height = 438;

            // Creates a new image with empty pixel data. 
            using (var image = new Image<Rgba32>(width, height))
            {
                FontFamily family = SystemFonts.Find("verdana");
                Font font = family.CreateFont(48, FontStyle.Bold);

                // The options are optional
                TextGraphicsOptions options = new TextGraphicsOptions()
                {
                    TextOptions = new TextOptions()
                    {
                        ApplyKerning = true,
                        TabWidth = 8, // a tab renders as 8 spaces wide
                        WrapTextWidth = width, // greater than zero so we will word wrap at 100 pixels wide
                        HorizontalAlignment = HorizontalAlignment.Center // right align
                    }
                };

                IBrush brush = Brushes.Solid(Color.White);
                IPen pen = Pens.Solid(Color.White, 1);
                string text = "sample text";

                // Draw the text
                image.Mutate(x => x.DrawText(options, text, font, brush, pen, new PointF(0, 100)));
            } 
        }

        [Fact]
        public void PenMustHaveAWidthGraterThanZero()
        {
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                IPen pen = new Pen(Color.White, 0); 
            });

            Assert.Equal("Parameter \"width\" (System.Single) must be greater than 0, was 0 (Parameter 'width')", ex.Message);
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
