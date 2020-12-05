// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;
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
            var processor = new FillPathProcessor(
                new ShapeGraphicsOptions()
                {
                    GraphicsOptions = { Antialias = true }
                },
                Brushes.Solid(Color.Red),
                path);

            IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

            Assert.IsType<FillRegionProcessor<Rgba32>>(pixelProcessor);
        }

        [Fact]
        public void RectangleFloatAndAntialias()
        {
            var imageSize = new Rectangle(0, 0, 500, 500);
            var floatRect = new RectangleF(10.5f, 10.5f, 400.6f, 400.9f);
            var expectedRect = new Rectangle(10, 10, 400, 400);
            var path = new RectangularPolygon(floatRect);
            var processor = new FillPathProcessor(
                new ShapeGraphicsOptions()
                {
                    GraphicsOptions = { Antialias = true }
                },
                Brushes.Solid(Color.Red),
                path);

            IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

            Assert.IsType<FillRegionProcessor<Rgba32>>(pixelProcessor);
        }

        [Fact]
        public void IntRectangle()
        {
            var imageSize = new Rectangle(0, 0, 500, 500);
            var expectedRect = new Rectangle(10, 10, 400, 400);
            var path = new RectangularPolygon(expectedRect);
            var processor = new FillPathProcessor(
                new ShapeGraphicsOptions()
                {
                    GraphicsOptions = { Antialias = true }
                },
                Brushes.Solid(Color.Red),
                path);

            IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

            FillProcessor<Rgba32> fill = Assert.IsType<FillProcessor<Rgba32>>(pixelProcessor);
            Assert.Equal(expectedRect, fill.GetProtectedValue<Rectangle>("SourceRectangle"));
        }

        [Fact]
        public void FloatRectAntialiasingOff()
        {
            var imageSize = new Rectangle(0, 0, 500, 500);
            var floatRect = new RectangleF(10.5f, 10.5f, 400.6f, 400.9f);
            var expectedRect = new Rectangle(10, 10, 400, 400);
            var path = new RectangularPolygon(floatRect);
            var processor = new FillPathProcessor(
                new ShapeGraphicsOptions()
                {
                    GraphicsOptions = { Antialias = false }
                },
                Brushes.Solid(Color.Red),
                path);

            IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);
            FillProcessor<Rgba32> fill = Assert.IsType<FillProcessor<Rgba32>>(pixelProcessor);

            Assert.Equal(expectedRect, fill.GetProtectedValue<Rectangle>("SourceRectangle"));
        }
    }

    internal static class ReflectionHelpers
    {
        internal static T GetProtectedValue<T>(this object obj, string name)
            => (T)obj.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
            .Single(x => x.Name == name)
            .GetValue(obj);
    }
}
