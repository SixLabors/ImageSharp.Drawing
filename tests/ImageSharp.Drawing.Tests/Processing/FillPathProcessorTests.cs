using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing
{
    public class FillPathProcessorTests
    {
        [Fact]
        public void OtherShape()
        {
            var imageSize = new Rectangle(0, 0, 500, 500);
            var path = new EllipsePolygon(1, 1, 23);
            var processor = new FillPathProcessor(new ImageSharp.Drawing.Processing.ShapeGraphicsOptions()
            {
                GraphicsOptions = {
                    Antialias = true
                }
            }, Brushes.Solid(Color.Red), path);
            var pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

            Assert.IsType<FillRegionProcessor<Rgba32>>(pixelProcessor);
        }

        [Fact]
        public void RectangleFloatAndAntialias()
        {
            var imageSize = new Rectangle(0, 0, 500, 500);
            var floatRect = new RectangleF(10.5f, 10.5f, 400.6f, 400.9f);
            var expectedRect = new Rectangle(10, 10, 400, 400);
            var path = new RectangularPolygon(floatRect);
            var processor = new FillPathProcessor(new ImageSharp.Drawing.Processing.ShapeGraphicsOptions()
            {
                GraphicsOptions = {
                    Antialias = true
                }
            }, Brushes.Solid(Color.Red), path);
            var pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

            Assert.IsType<FillRegionProcessor<Rgba32>>(pixelProcessor);
        }

        [Fact]
        public void IntRectangle()
        {
            var imageSize = new Rectangle(0, 0, 500, 500);
            var expectedRect = new Rectangle(10, 10, 400, 400);
            var path = new RectangularPolygon(expectedRect);
            var processor = new FillPathProcessor(new ImageSharp.Drawing.Processing.ShapeGraphicsOptions()
            {
                GraphicsOptions = {
                    Antialias = true
                }
            }, Brushes.Solid(Color.Red), path);
            var pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

            var fill = Assert.IsType<FillProcessor<Rgba32>>(pixelProcessor);
            Assert.Equal(expectedRect, fill.GetProtectedValue<Rectangle>("SourceRectangle"));
        }

        [Fact]
        public void FloatRectAntialiasingOff()
        {
            var imageSize = new Rectangle(0, 0, 500, 500);
            var floatRect = new RectangleF(10.5f, 10.5f, 400.6f, 400.9f);
            var expectedRect = new Rectangle(10, 10, 400, 400);
            var path = new RectangularPolygon(floatRect);
            var processor = new FillPathProcessor(new ImageSharp.Drawing.Processing.ShapeGraphicsOptions()
            {
                GraphicsOptions = {
                    Antialias = false
                }
            }, Brushes.Solid(Color.Red), path);
            var pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

            var fill = Assert.IsType<FillProcessor<Rgba32>>(pixelProcessor);
            Assert.Equal(expectedRect, fill.GetProtectedValue<Rectangle>("SourceRectangle"));
        }
    }

    internal static class ReflectionHelpers
    {
        internal static T GetProtectedValue<T>(this object obj, string name)
        {
            return (T)obj.GetType()
                    .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy)
                    .Single(x => x.Name == name)
                    .GetValue(obj);
        }
    }
}
