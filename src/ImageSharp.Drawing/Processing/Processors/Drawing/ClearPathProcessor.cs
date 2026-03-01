// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Defines a processor to clear <see cref="Image"/> pixels within a given <see cref="IPath"/>
/// with the given <see cref="Processing.Brush"/> using clear composition semantics defined by <see cref="DrawingOptions"/>.
/// </summary>
public class ClearPathProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClearPathProcessor" /> class.
    /// </summary>
    /// <param name="options">The drawing options.</param>
    /// <param name="brush">The details how to clear the region of interest.</param>
    /// <param name="path">The logic path to be cleared.</param>
    public ClearPathProcessor(DrawingOptions options, Brush brush, IPath path)
    {
        this.Region = path;
        this.Brush = brush;
        this.Options = options;
    }

    /// <summary>
    /// Gets the <see cref="Processing.Brush"/> used for clearing the destination image.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the logic path that this processor applies to.
    /// </summary>
    public IPath Region { get; }

    /// <summary>
    /// Gets the <see cref="DrawingOptions"/> defining clear composition behavior.
    /// </summary>
    public DrawingOptions Options { get; }

    /// <inheritdoc />
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
        => new ClearPathProcessor<TPixel>(configuration, this, source, sourceRectangle);
}
