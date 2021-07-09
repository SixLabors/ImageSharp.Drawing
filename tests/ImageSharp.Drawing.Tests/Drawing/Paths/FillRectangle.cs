// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class FillRectangle : BaseImageOperationsExtensionTest
    {
        private readonly IBrush brush = Brushes.Solid(Color.HotPink);
        private RectangleF rectangle = new RectangleF(10, 10, 20, 20);

        private RectangularPolygon RectanglePolygon => new RectangularPolygon(this.rectangle);

        [Fact]
        public void Brush()
        {
            this.operations.Fill(new DrawingOptions(), this.brush, this.rectangle);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Path));
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Fill(this.brush, this.rectangle);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Path));
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Fill(new DrawingOptions(), Color.Red, this.rectangle);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Path));
            Assert.NotEqual(this.brush, processor.Brush);
            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }

        [Fact]
        public void ColorAndThicknessDefaultOptions()
        {
            this.operations.Fill(Color.Red, this.rectangle);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Path));
            Assert.NotEqual(this.brush, processor.Brush);
            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }
    }
}
