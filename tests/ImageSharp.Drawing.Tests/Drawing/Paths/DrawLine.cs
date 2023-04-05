// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class DrawLine : BaseImageOperationsExtensionTest
    {
        private readonly SolidPen pen = Pens.Solid(Color.HotPink, 2);
        private readonly PointF[] points = new PointF[]
        {
            new PointF(10, 10),
            new PointF(20, 20),
            new PointF(20, 50),
            new PointF(50, 10)
        };

        private void VerifyPoints(PointF[] expectedPoints, IPath path)
        {
            ISimplePath simplePath = Assert.Single(path.Flatten());
            Assert.False(simplePath.IsClosed);
            Assert.Equal(expectedPoints, simplePath.Points.ToArray());
        }

        [Fact]
        public void Pen()
        {
            this.operations.DrawLine(new DrawingOptions(), this.pen, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            Assert.Equal(this.pen, processor.Pen);
        }

        [Fact]
        public void PenDefaultOptions()
        {
            this.operations.DrawLine(this.pen, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            Assert.Equal(this.pen, processor.Pen);
        }

        [Fact]
        public void BrushAndThickness()
        {
            this.operations.DrawLine(new DrawingOptions(), this.pen.StrokeFill, 10, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            var processorPen = Assert.IsType<SolidPen>(processor.Pen);
            Assert.Equal(this.pen.StrokeFill, processorPen.StrokeFill);
            Assert.Equal(10, processorPen.StrokeWidth);
        }

        [Fact]
        public void BrushAndThicknessDefaultOptions()
        {
            this.operations.DrawLine(this.pen.StrokeFill, 10, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            var processorPen = Assert.IsType<SolidPen>(processor.Pen);
            Assert.Equal(this.pen.StrokeFill, processorPen.StrokeFill);
            Assert.Equal(10, processorPen.StrokeWidth);
        }

        [Fact]
        public void ColorAndThickness()
        {
            this.operations.DrawLine(new DrawingOptions(), Color.Red, 10, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
            var processorPen = Assert.IsType<SolidPen>(processor.Pen);
            Assert.Equal(Color.Red, brush.Color);
            Assert.Equal(10, processorPen.StrokeWidth);
        }

        [Fact]
        public void ColorAndThicknessDefaultOptions()
        {
            this.operations.DrawLine(Color.Red, 10, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
            Assert.Equal(Color.Red, brush.Color);
            var processorPen = Assert.IsType<SolidPen>(processor.Pen);
            Assert.Equal(10, processorPen.StrokeWidth);
        }

        [Fact]
        public void JointAndEndCapStyle()
        {
            this.operations.DrawLine(new DrawingOptions(), this.pen, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            var processorPen = Assert.IsType<SolidPen>(processor.Pen);
            Assert.Equal(this.pen.JointStyle, processorPen.JointStyle);
            Assert.Equal(this.pen.EndCapStyle, processorPen.EndCapStyle);
        }

        [Fact]
        public void JointAndEndCapStyleDefaultOptions()
        {
            this.operations.DrawLine(this.pen, this.points);

            DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

            Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
            this.VerifyPoints(this.points, processor.Path);
            var processorPen = Assert.IsType<SolidPen>(processor.Pen);
            Assert.Equal(this.pen.JointStyle, processorPen.JointStyle);
            Assert.Equal(this.pen.EndCapStyle, processorPen.EndCapStyle);
        }
    }
}
