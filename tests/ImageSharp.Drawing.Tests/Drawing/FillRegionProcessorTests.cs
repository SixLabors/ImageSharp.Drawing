// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using Moq;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    public class FillRegionProcessorTests
    {
        [Fact]
        public void FillOffCanvas()
        {
            var bounds = new Rectangle(-100, -10, 10, 10);
            var brush = new Mock<IBrush>();
            var options = new GraphicsOptions { Antialias = true };
            var processor = new FillRegionProcessor(new DrawingOptions() { GraphicsOptions = options }, brush.Object, new MockRegion1());
            var img = new Image<Rgba32>(10, 10);
            processor.Execute(img.GetConfiguration(), img, bounds);
        }

        [Fact]
        public void DrawOffCanvas()
        {
            using (var img = new Image<Rgba32>(10, 10))
            {
                img.Mutate(x => x.DrawLines(
                    new Pen(Color.Black, 10),
                    new Vector2(-10, 5),
                    new Vector2(20, 5)));
            }
        }

        [Fact]
        public void DoesNotThrowForIssue928()
        {
            var rectText = new RectangleF(0, 0, 2000, 2000);
            using (var img = new Image<Rgba32>((int)rectText.Width, (int)rectText.Height))
            {
                img.Mutate(x => x.Fill(Color.Transparent));

                img.Mutate(
                    ctx => ctx.DrawLines(
                        Color.Red,
                        0.984252f,
                        new PointF(104.762581f, 1074.99365f),
                        new PointF(104.758667f, 1075.01721f),
                        new PointF(104.757675f, 1075.04114f),
                        new PointF(104.759628f, 1075.065f),
                        new PointF(104.764488f, 1075.08838f),
                        new PointF(104.772186f, 1075.111f),
                        new PointF(104.782608f, 1075.13245f),
                        new PointF(104.782608f, 1075.13245f)));
            }
        }

        [Fact]
        public void DoesNotThrowFillingTriangle()
        {
            using (var image = new Image<Rgba32>(28, 28))
            {
                var path = new Polygon(
                    new LinearLineSegment(new PointF(17.11f, 13.99659f), new PointF(14.01433f, 27.06201f)),
                    new LinearLineSegment(new PointF(14.01433f, 27.06201f), new PointF(13.79267f, 14.00023f)),
                    new LinearLineSegment(new PointF(13.79267f, 14.00023f), new PointF(17.11f, 13.99659f)));

                image.Mutate(ctx => ctx.Fill(Color.White, path));
            }
        }

        // Mocking the region throws an error in netcore2.0
        private class MockRegion1 : Region
        {
            public override Rectangle Bounds => new Rectangle(-100, -10, 10, 10);

            internal override IPath Shape => throw new NotImplementedException();
        }

        private class MockRegion2 : Region
        {
            public MockRegion2(Rectangle bounds)
                => this.Bounds = bounds;

            public override Rectangle Bounds { get; }

            internal override IPath Shape => throw new NotImplementedException();
        }
    }
}
