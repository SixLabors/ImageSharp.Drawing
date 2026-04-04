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
        this.operations.ProcessWithCanvas(canvas => canvas.Clear(Brushes.Solid(Color.Red)));

        ProcessWithCanvasProcessor processor = this.Verify<ProcessWithCanvasProcessor>();

        GraphicsOptions expectedOptions = this.graphicsOptions;
        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(expectedOptions.BlendPercentage, processor.Options.GraphicsOptions.BlendPercentage);
        Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.GraphicsOptions.AlphaCompositionMode);
        Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.GraphicsOptions.ColorBlendingMode);
    }

    [Fact]
    public void CanvasActionWithOptions()
    {
        this.operations.ProcessWithCanvas(
            this.nonDefaultOptions,
            canvas => canvas.Clear(Brushes.Solid(Color.Red)));

        ProcessWithCanvasProcessor processor = this.Verify<ProcessWithCanvasProcessor>();
        Assert.Equal(this.nonDefaultOptions, processor.Options);
    }
}
