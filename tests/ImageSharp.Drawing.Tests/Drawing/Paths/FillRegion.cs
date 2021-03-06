// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class FillRegion : BaseImageOperationsExtensionTest
    {
        private readonly IBrush brush = new SolidBrush(Color.HotPink);
        private readonly Region region = new ShapeRegion(new RectangularPolygon(10, 10, 10, 10));

        [Fact]
        public void Brush()
        {
            this.operations.Fill(new DrawingOptions(), this.brush, this.region);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.Equal(this.region, processor.Region);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Fill(this.brush, this.region);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.Equal(this.region, processor.Region);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Fill(new DrawingOptions(), Color.Red, this.region);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.Equal(this.region, processor.Region);
            SolidBrush solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }

        [Fact]
        public void ColorSetDefaultOptions()
        {
            this.operations.Fill(Color.Red, this.region);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.Equal(this.region, processor.Region);
            SolidBrush solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }
    }
}
