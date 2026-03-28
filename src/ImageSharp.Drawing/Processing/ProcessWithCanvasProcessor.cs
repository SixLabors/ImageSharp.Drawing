// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Defines a processor that executes a canvas callback for each image frame.
/// </summary>
public sealed class ProcessWithCanvasProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessWithCanvasProcessor"/> class.
    /// </summary>
    /// <param name="options">The drawing options.</param>
    /// <param name="action">The per-frame canvas callback.</param>
    public ProcessWithCanvasProcessor(DrawingOptions options, CanvasAction action)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(action, nameof(action));

        this.Options = options;
        this.Action = action;
    }

    /// <summary>
    /// Gets the drawing options.
    /// </summary>
    public DrawingOptions Options { get; }

    internal CanvasAction Action { get; }

    /// <inheritdoc />
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(
        Configuration configuration,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
        => new ProcessWithCanvasProcessor<TPixel>(configuration, this, source, sourceRectangle);
}
