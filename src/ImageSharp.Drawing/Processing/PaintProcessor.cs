// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Defines the image processor used by <see cref="PaintExtensions.Paint(IImageProcessingContext, DrawingOptions, CanvasAction)"/>
/// to execute a canvas callback for each image frame.
/// </summary>
public sealed class PaintProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaintProcessor"/> class.
    /// </summary>
    /// <param name="options">The drawing options used when creating each frame canvas.</param>
    /// <param name="action">The per-frame painting callback.</param>
    public PaintProcessor(DrawingOptions options, CanvasAction action)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(action, nameof(action));

        this.Options = options;
        this.Action = action;
    }

    /// <summary>
    /// Gets the drawing options used when creating each frame canvas.
    /// </summary>
    public DrawingOptions Options { get; }

    /// <summary>
    /// Gets the per-frame painting callback.
    /// </summary>
    internal CanvasAction Action { get; }

    /// <inheritdoc />
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(
        Configuration configuration,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
        => new PaintProcessor<TPixel>(configuration, this, source, sourceRectangle);
}
