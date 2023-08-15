// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class DrawPathCollection : BaseImageOperationsExtensionTest
{
    private readonly GraphicsOptions nonDefault = new GraphicsOptions { Antialias = false };
    private readonly Color color = Color.HotPink;
    private readonly SolidPen pen = Pens.Solid(Color.HotPink, 1);
    private readonly IPath path1 = new Path(new LinearLineSegment(
        new PointF[]
        {
            new Vector2(10, 10),
            new Vector2(20, 10),
            new Vector2(20, 10),
            new Vector2(30, 10),
        }));

    private readonly IPath path2 = new Path(new LinearLineSegment(
        new PointF[]
        {
            new Vector2(10, 10),
            new Vector2(20, 10),
            new Vector2(20, 10),
            new Vector2(30, 10),
        }));

    private readonly IPathCollection pathCollection;

    public DrawPathCollection()
        => this.pathCollection = new PathCollection(this.path1, this.path2);

    [Fact]
    public void Pen()
    {
        this.operations.Draw(new DrawingOptions(), this.pen, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.NotEqual(this.shapeOptions, p.Options.ShapeOptions);
            Assert.Equal(this.pen, p.Pen);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }

    [Fact]
    public void PenDefaultOptions()
    {
        this.operations.Draw(this.pen, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.Equal(this.shapeOptions, p.Options.ShapeOptions);
            Assert.Equal(this.pen, p.Pen);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }

    [Fact]
    public void BrushAndThickness()
    {
        this.operations.Draw(new DrawingOptions(), this.pen.StrokeFill, 10, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.NotEqual(this.shapeOptions, p.Options.ShapeOptions);
            Assert.Equal(this.pen.StrokeFill, p.Pen.StrokeFill);
            var pPen = Assert.IsType<SolidPen>(p.Pen);
            Assert.Equal(10, pPen.StrokeWidth);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }

    [Fact]
    public void BrushAndThicknessDefaultOptions()
    {
        this.operations.Draw(this.pen.StrokeFill, 10, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.Equal(this.shapeOptions, p.Options.ShapeOptions);
            Assert.Equal(this.pen.StrokeFill, p.Pen.StrokeFill);
            var pPen = Assert.IsType<SolidPen>(p.Pen);
            Assert.Equal(10, pPen.StrokeWidth);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }

    [Fact]
    public void ColorAndThickness()
    {
        this.operations.Draw(new DrawingOptions(), Color.Pink, 10, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.NotEqual(this.shapeOptions, p.Options.ShapeOptions);
            SolidBrush brush = Assert.IsType<SolidBrush>(p.Pen.StrokeFill);
            Assert.Equal(Color.Pink, brush.Color);
            var pPen = Assert.IsType<SolidPen>(p.Pen);
            Assert.Equal(10, pPen.StrokeWidth);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }

    [Fact]
    public void ColorAndThicknessDefaultOptions()
    {
        this.operations.Draw(Color.Pink, 10, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.Equal(this.shapeOptions, p.Options.ShapeOptions);
            SolidBrush brush = Assert.IsType<SolidBrush>(p.Pen.StrokeFill);
            Assert.Equal(Color.Pink, brush.Color);
            var pPen = Assert.IsType<SolidPen>(p.Pen);
            Assert.Equal(10, pPen.StrokeWidth);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }

    [Fact]
    public void JointAndEndCapStyle()
    {
        this.operations.Draw(new DrawingOptions(), this.pen.StrokeFill, 10, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.NotEqual(this.shapeOptions, p.Options.ShapeOptions);
            var pPen = Assert.IsType<SolidPen>(p.Pen);
            Assert.Equal(this.pen.JointStyle, pPen.JointStyle);
            Assert.Equal(this.pen.EndCapStyle, pPen.EndCapStyle);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }

    [Fact]
    public void JointAndEndCapStyleDefaultOptions()
    {
        this.operations.Draw(this.pen.StrokeFill, 10, this.pathCollection);
        IEnumerable<DrawPathProcessor> processors = this.VerifyAll<DrawPathProcessor>();

        Assert.All(processors, p =>
        {
            Assert.Equal(this.shapeOptions, p.Options.ShapeOptions);
            var pPen = Assert.IsType<SolidPen>(p.Pen);
            Assert.Equal(this.pen.JointStyle, pPen.JointStyle);
            Assert.Equal(this.pen.EndCapStyle, pPen.EndCapStyle);
        });

        Assert.Collection(
            processors,
            p => Assert.Equal(this.path1, p.Path),
            p => Assert.Equal(this.path2, p.Path));
    }
}
