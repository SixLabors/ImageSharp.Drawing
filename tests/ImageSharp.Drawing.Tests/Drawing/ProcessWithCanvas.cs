// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

public class ProcessWithCanvas : BaseImageOperationsExtensionTest
{
    private readonly DrawingOptions nonDefaultOptions = new();

    [Fact]
    public void CanvasActionDefaultOptions()
    {
        this.operations.Paint(canvas => canvas.Clear(Brushes.Solid(Color.Red)));

        PaintProcessor processor = this.Verify<PaintProcessor>();

        GraphicsOptions expectedOptions = this.graphicsOptions;
        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(expectedOptions.BlendPercentage, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.GraphicsOptions.ColorBlendingMode);
    }

    [Fact]
    public void CanvasActionWithOptions()
    {
        this.operations.Paint(
            this.nonDefaultOptions,
            canvas => canvas.Clear(Brushes.Solid(Color.Red)));

        PaintProcessor processor = this.Verify<PaintProcessor>();
        Assert.Equal(this.nonDefaultOptions, processor.Options);
    }
}
