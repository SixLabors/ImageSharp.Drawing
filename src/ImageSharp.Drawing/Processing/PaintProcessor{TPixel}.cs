// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Executes the <see cref="PaintProcessor"/> callback for a specific pixel type by creating a
/// <see cref="DrawingCanvas"/> over each frame.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal sealed class PaintProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly PaintProcessor definition;
    private readonly CanvasAction action;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaintProcessor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The processing configuration.</param>
    /// <param name="definition">The non-generic processor definition that owns the drawing options and callback.</param>
    /// <param name="source">The source image.</param>
    /// <param name="sourceRectangle">The source bounds passed through the processing pipeline.</param>
    public PaintProcessor(
        Configuration configuration,
        PaintProcessor definition,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
    {
        this.definition = definition;
        this.action = definition.Action;
    }

    /// <inheritdoc />
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        using DrawingCanvas canvas = source.CreateCanvas(this.Configuration, this.definition.Options);
        this.action(canvas);
    }
}
