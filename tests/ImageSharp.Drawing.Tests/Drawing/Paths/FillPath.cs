// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class FillPath : BaseImageOperationsExtensionTest
    {
        IBrush brush = Brushes.Solid(Color.HotPink);
        IPath path = new Star(1, 10, 5, 23, 56);

        [Fact]
        public void Brush()
        {
            this.operations.Fill(new ShapeGraphicsOptions(), this.brush, this.path);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options);
            Assert.Equal(this.path, processor.Shape);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Fill(this.brush, this.path);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options);
            Assert.Equal(this.path, processor.Shape);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Fill(new ShapeGraphicsOptions(), Color.Red, this.path);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options);
            Assert.Equal(this.path, processor.Shape);
            Assert.NotEqual(this.brush, processor.Brush);
            var brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }

        [Fact]
        public void ColorAndThicknessDefaultOptions()
        {
            this.operations.Fill(Color.Red, this.path);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options);
            Assert.Equal(this.path, processor.Shape);
            Assert.NotEqual(this.brush, processor.Brush);
            var brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }
    }
}
