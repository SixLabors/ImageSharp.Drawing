// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class Clear : BaseImageOperationsExtensionTest
{
    private readonly DrawingOptions nonDefaultOptions = new()
    {
        GraphicsOptions =
        {
            AlphaCompositionMode = PixelFormats.PixelAlphaCompositionMode.Clear,
            BlendPercentage = 0.5f,
            ColorBlendingMode = PixelFormats.PixelColorBlendingMode.Darken,
            AntialiasSubpixelDepth = 99
        }
    };

    private readonly Brush brush = new SolidBrush(Color.HotPink);

    [Fact]
    public void Brush()
    {
        this.operations.Clear(this.nonDefaultOptions, this.brush);

        FillProcessor processor = this.Verify<FillProcessor>();

        DrawingOptions expectedOptions = this.nonDefaultOptions;
        Assert.Equal(expectedOptions.ShapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);

        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void BrushDefaultOptions()
    {
        this.operations.Clear(this.brush);

        FillProcessor processor = this.Verify<FillProcessor>();

        ShapeOptions expectedOptions = this.shapeOptions;
        Assert.Equal(expectedOptions, processor.Options.ShapeOptions);
        Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);

        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void ColorSet()
    {
        this.operations.Clear(this.nonDefaultOptions, Color.Red);

        FillProcessor processor = this.Verify<FillProcessor>();

        ShapeOptions expectedOptions = this.shapeOptions;
        Assert.NotEqual(expectedOptions, processor.Options.ShapeOptions);

        Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }

    [Fact]
    public void ColorSetDefaultOptions()
    {
        this.operations.Clear(Color.Red);

        FillProcessor processor = this.Verify<FillProcessor>();

        ShapeOptions expectedOptions = this.shapeOptions;
        Assert.Equal(expectedOptions, processor.Options.ShapeOptions);
        Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);

        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }
}
