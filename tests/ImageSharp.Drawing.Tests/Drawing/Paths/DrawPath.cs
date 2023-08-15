// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class DrawPath : BaseImageOperationsExtensionTest
{
    private readonly SolidPen pen = Pens.Solid(Color.HotPink, 2);
    private readonly IPath path = new EllipsePolygon(10, 10, 100);

    [Fact]
    public void Pen()
    {
        this.operations.Draw(new DrawingOptions(), this.pen, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        Assert.Equal(this.pen, processor.Pen);
    }

    [Fact]
    public void PenDefaultOptions()
    {
        this.operations.Draw(this.pen, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        Assert.Equal(this.pen, processor.Pen);
    }

    [Fact]
    public void BrushAndThickness()
    {
        this.operations.Draw(new DrawingOptions(), this.pen.StrokeFill, 10, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        Assert.Equal(this.pen.StrokeFill, processor.Pen.StrokeFill);
        var processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void BrushAndThicknessDefaultOptions()
    {
        this.operations.Draw(this.pen.StrokeFill, 10, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        Assert.Equal(this.pen.StrokeFill, processor.Pen.StrokeFill);
        var processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void ColorAndThickness()
    {
        this.operations.Draw(new DrawingOptions(), Color.Red, 10, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
        Assert.Equal(Color.Red, brush.Color);
        var processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void ColorAndThicknessDefaultOptions()
    {
        this.operations.Draw(Color.Red, 10, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
        Assert.Equal(Color.Red, brush.Color);
        var processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void JointAndEndCapStyle()
    {
        this.operations.Draw(new DrawingOptions(), this.pen.StrokeFill, 10, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        var processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(this.pen.JointStyle, processorPen.JointStyle);
        Assert.Equal(this.pen.EndCapStyle, processorPen.EndCapStyle);
    }

    [Fact]
    public void JointAndEndCapStyleDefaultOptions()
    {
        this.operations.Draw(this.pen.StrokeFill, 10, this.path);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Path);
        var processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(this.pen.JointStyle, processorPen.JointStyle);
        Assert.Equal(this.pen.EndCapStyle, processorPen.EndCapStyle);
    }
}
