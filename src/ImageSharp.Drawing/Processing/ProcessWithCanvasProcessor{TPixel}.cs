// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Executes a per-frame canvas callback for a specific pixel type.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal sealed class ProcessWithCanvasProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ProcessWithCanvasProcessor definition;
    private readonly CanvasAction action;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessWithCanvasProcessor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The processing configuration.</param>
    /// <param name="definition">The processor definition.</param>
    /// <param name="source">The source image.</param>
    /// <param name="sourceRectangle">The source bounds.</param>
    public ProcessWithCanvasProcessor(
        Configuration configuration,
        ProcessWithCanvasProcessor definition,
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
        using DrawingCanvas<TPixel> canvas = source.CreateCanvas(this.definition.Options);
        this.action(canvas);
    }
}
