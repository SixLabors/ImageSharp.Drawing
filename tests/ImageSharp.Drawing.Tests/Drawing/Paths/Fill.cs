// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class Fill : BaseImageOperationsExtensionTest
{
    private readonly DrawingOptions nonDefaultOptions = new();
    private readonly Brush brush = new SolidBrush(Color.HotPink);

    [Fact]
    public void Brush()
    {
        this.operations.Fill(this.nonDefaultOptions, this.brush);

        FillProcessor processor = this.Verify<FillProcessor>();

        DrawingOptions expectedOptions = this.nonDefaultOptions;
        Assert.Equal(expectedOptions, processor.Options);
        Assert.Equal(expectedOptions.GraphicsOptions.BlendPercentage, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(expectedOptions.GraphicsOptions.AlphaCompositionMode, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(expectedOptions.GraphicsOptions.ColorBlendingMode, processor.Options.GraphicsOptions.ColorBlendingMode);

        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void BrushDefaultOptions()
    {
        this.operations.Fill(this.brush);

        FillProcessor processor = this.Verify<FillProcessor>();

        GraphicsOptions expectedOptions = this.graphicsOptions;
        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(expectedOptions.BlendPercentage, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.GraphicsOptions.ColorBlendingMode);

        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void ColorSet()
    {
        this.operations.Fill(this.nonDefaultOptions, Color.Red);

        FillProcessor processor = this.Verify<FillProcessor>();

        DrawingOptions expectedOptions = this.nonDefaultOptions;
        Assert.Equal(expectedOptions, processor.Options);

        Assert.Equal(expectedOptions.GraphicsOptions.BlendPercentage, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(expectedOptions.GraphicsOptions.AlphaCompositionMode, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(expectedOptions.GraphicsOptions.ColorBlendingMode, processor.Options.GraphicsOptions.ColorBlendingMode);
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }

    [Fact]
    public void ColorSetDefaultOptions()
    {
        this.operations.Fill(Color.Red);

        FillProcessor processor = this.Verify<FillProcessor>();

        GraphicsOptions expectedOptions = this.graphicsOptions;
        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(expectedOptions.BlendPercentage, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.GraphicsOptions.ColorBlendingMode);

        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }
}
