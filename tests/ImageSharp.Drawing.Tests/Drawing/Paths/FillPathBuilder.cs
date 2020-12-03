// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class FillPathBuilder : BaseImageOperationsExtensionTest
    {
        private readonly IBrush brush = Brushes.Solid(Color.HotPink);
        private readonly IPath path = null;
        private readonly Action<PathBuilder> builder = pb =>
        {
            pb.StartFigure();
            pb.AddLine(10, 10, 20, 20);
            pb.AddLine(60, 450, 120, 340);
            pb.AddLine(120, 340, 10, 10);
            pb.CloseAllFigures();
        };

        public FillPathBuilder()
        {
            var pb = new PathBuilder();
            this.builder(pb);
            this.path = pb.Build();
        }

        private void VerifyPoints(IPath expectedPath, IPath path)
        {
            ISimplePath simplePathExpected = Assert.Single(expectedPath.Flatten());
            PointF[] expectedPoints = simplePathExpected.Points.ToArray();

            ISimplePath simplePath = Assert.Single(path.Flatten());
            Assert.True(simplePath.IsClosed);
            Assert.Equal(expectedPoints, simplePath.Points.ToArray());
        }

        [Fact]
        public void Brush()
        {
            this.operations.Fill(new ShapeGraphicsOptions(), this.brush, this.builder);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.path, processor.Shape);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Fill(this.brush, this.builder);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.path, processor.Shape);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Fill(new ShapeGraphicsOptions(), Color.Red, this.builder);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.path, processor.Shape);
            Assert.NotEqual(this.brush, processor.Brush);
            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }

        [Fact]
        public void ColorAndThicknessDefaultOptions()
        {
            this.operations.Fill(Color.Red, this.builder);

            FillPathProcessor processor = this.Verify<FillPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.path, processor.Shape);
            Assert.NotEqual(this.brush, processor.Brush);
            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }
    }
}
