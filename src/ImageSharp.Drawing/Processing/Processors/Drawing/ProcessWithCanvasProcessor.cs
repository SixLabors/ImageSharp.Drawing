// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Defines a processor that executes a canvas callback for each image frame.
/// </summary>
public sealed class ProcessWithCanvasProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessWithCanvasProcessor"/> class.
    /// </summary>
    /// <param name="options">The drawing options.</param>
    /// <param name="pixelType">The pixel type expected by <paramref name="action"/>.</param>
    /// <param name="action">The per-frame canvas callback.</param>
    public ProcessWithCanvasProcessor(DrawingOptions options, Type pixelType, Delegate action)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(pixelType, nameof(pixelType));
        Guard.NotNull(action, nameof(action));

        this.Options = options;
        this.PixelType = pixelType;
        this.Action = action;
    }

    /// <summary>
    /// Gets the drawing options.
    /// </summary>
    public DrawingOptions Options { get; }

    internal Type PixelType { get; }

    internal Delegate Action { get; }

    /// <inheritdoc />
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(
        Configuration configuration,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (typeof(TPixel) != this.PixelType)
        {
            throw new InvalidOperationException(
                $"ProcessWithCanvas expects pixel type '{this.PixelType.Name}' but the image uses '{typeof(TPixel).Name}'.");
        }

        return new ProcessWithCanvasProcessor<TPixel>(configuration, this, source, sourceRectangle);
    }
}
