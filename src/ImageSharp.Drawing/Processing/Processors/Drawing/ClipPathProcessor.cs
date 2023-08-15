// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Allows the recursive application of processing operations against an image within a given region.
/// </summary>
public class ClipPathProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClipPathProcessor"/> class.
    /// </summary>
    /// <param name="options">The drawing options.</param>
    /// <param name="path">The <see cref="IPath"/> defining the region to operate within.</param>
    /// <param name="operation">The operation to perform on the source.</param>
    public ClipPathProcessor(DrawingOptions options, IPath path, Action<IImageProcessingContext> operation)
    {
        this.Options = options;
        this.Region = path;
        this.Operation = operation;
    }

    /// <summary>
    /// Gets the drawing options.
    /// </summary>
    public DrawingOptions Options { get; }

    /// <summary>
    /// Gets the <see cref="IPath"/> defining the region to operate within.
    /// </summary>
    public IPath Region { get; }

    /// <summary>
    /// Gets the operation to perform on the source.
    /// </summary>
    public Action<IImageProcessingContext> Operation { get; }

    /// <inheritdoc/>
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(
        Configuration configuration,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
        => new ClipPathProcessor<TPixel>(this, source, configuration, sourceRectangle);
}
