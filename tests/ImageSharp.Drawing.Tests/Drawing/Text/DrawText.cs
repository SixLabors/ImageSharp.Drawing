// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Text;

public class DrawText : BaseImageOperationsExtensionTest
{
    private readonly FontCollection fontCollection;
    private readonly RichTextOptions textOptions;
    private readonly DrawingOptions otherDrawingOptions = new()
    {
        GraphicsOptions = new GraphicsOptions()
    };

    private readonly Font font;

    public DrawText()
    {
        this.fontCollection = new FontCollection();
        this.font = this.fontCollection.Add(TestFontUtilities.GetPath("SixLaborsSampleAB.woff")).CreateFont(12);
        this.textOptions = new RichTextOptions(this.font) { WrappingLength = 99 };
    }

    [Fact]
    public void FillsForEachACharacterWhenBrushSetAndNotPen()
    {
        this.operations.DrawText(
            this.otherDrawingOptions,
            "123",
            this.font,
            Brushes.Solid(Color.Red),
            null,
            Vector2.Zero);

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
        Assert.NotEqual(this.textOptions, processor.TextOptions);
        Assert.NotEqual(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void FillsForEachACharacterWhenBrushSetAndNotPenDefaultOptions()
    {
        this.operations.DrawText(this.textOptions, "123", Brushes.Solid(Color.Red));

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
        Assert.Equal(this.textOptions, processor.TextOptions);
        Assert.Equal(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void FillsForEachACharacterWhenBrushSet()
    {
        this.operations.DrawText(this.otherDrawingOptions, "123", this.font, Brushes.Solid(Color.Red), Vector2.Zero);

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
        Assert.NotEqual(this.textOptions, processor.TextOptions);
        Assert.NotEqual(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void FillsForEachACharacterWhenBrushSetDefaultOptions()
    {
        this.operations.DrawText(this.textOptions, "123", Brushes.Solid(Color.Red));

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
        Assert.Equal(this.textOptions, processor.TextOptions);
        Assert.Equal(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void FillsForEachACharacterWhenColorSet()
    {
        this.operations.DrawText(this.otherDrawingOptions, "123", this.font, Color.Red, Vector2.Zero);

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
        Assert.NotEqual(this.textOptions, processor.TextOptions);
        Assert.NotEqual(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void FillsForEachACharacterWhenColorSetDefaultOptions()
    {
        this.operations.DrawText(this.textOptions, "123", Color.Red);

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
        Assert.Equal(this.textOptions, processor.TextOptions);
        Assert.Equal(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void DrawForEachACharacterWhenPenSetAndNotBrush()
    {
        this.operations.DrawText(
            this.otherDrawingOptions,
            "123",
            this.font,
            null,
            Pens.Dash(Color.Red, 1),
            Vector2.Zero);

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
        Assert.NotEqual(this.textOptions, processor.TextOptions);
        Assert.NotEqual(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void DrawForEachACharacterWhenPenSetAndNotBrushDefaultOptions()
    {
        this.operations.DrawText(this.textOptions, "123", Pens.Dash(Color.Red, 1));

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
        Assert.Equal(this.textOptions, processor.TextOptions);
        Assert.Equal(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void DrawForEachACharacterWhenPenSet()
    {
        this.operations.DrawText(this.otherDrawingOptions, "123", this.font, Pens.Dash(Color.Red, 1), Vector2.Zero);

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
        Assert.NotEqual(this.textOptions, processor.TextOptions);
        Assert.NotEqual(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void DrawForEachACharacterWhenPenSetDefaultOptions()
    {
        this.operations.DrawText(this.textOptions, "123", Pens.Dash(Color.Red, 1));

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

        Assert.Equal("123", processor.Text);
        Assert.Equal(this.font, processor.TextOptions.Font);
        SolidBrush penBrush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
        Assert.Equal(Color.Red, penBrush.Color);
        PatternPen processorPen = Assert.IsType<PatternPen>(processor.Pen);
        Assert.Equal(1, processorPen.StrokeWidth);
        Assert.Equal(PointF.Empty, processor.Location);
        Assert.Equal(this.textOptions, processor.TextOptions);
        Assert.Equal(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }

    [Fact]
    public void DrawForEachACharacterWhenPenSetAndFillFroEachWhenBrushSet()
    {
        this.operations.DrawText(
            this.otherDrawingOptions,
            "123",
            this.font,
            Brushes.Solid(Color.Red),
            Pens.Dash(Color.Red, 1),
            Vector2.Zero);

        DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

        Assert.Equal("123", processor.Text);
        Assert.Equal(this.font, processor.TextOptions.Font);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
        Assert.Equal(PointF.Empty, processor.Location);
        SolidBrush penBrush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
        Assert.Equal(Color.Red, penBrush.Color);
        PatternPen processorPen = Assert.IsType<PatternPen>(processor.Pen);
        Assert.Equal(1, processorPen.StrokeWidth);
        Assert.NotEqual(this.textOptions, processor.TextOptions);
        Assert.NotEqual(this.graphicsOptions, processor.DrawingOptions.GraphicsOptions);
    }
}
