// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Defines a processor to fill <see cref="Image"/> pixels withing a given <see cref="IPath"/>
/// with the given <see cref="Processing.Brush"/> and blending defined by the given <see cref="DrawingOptions"/>.
/// </summary>
public class FillPathProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FillPathProcessor" /> class.
    /// </summary>
    /// <param name="options">The graphics options.</param>
    /// <param name="brush">The details how to fill the region of interest.</param>
    /// <param name="path">The logic path to be filled.</param>
    public FillPathProcessor(DrawingOptions options, Brush brush, IPath path)
        : this(options, brush, path, RasterizerSamplingOrigin.PixelBoundary)
    {
    }

    internal FillPathProcessor(
        DrawingOptions options,
        Brush brush,
        IPath path,
        RasterizerSamplingOrigin samplingOrigin)
    {
        this.Region = path;
        this.Brush = brush;
        this.Options = options;
        this.SamplingOrigin = samplingOrigin;
    }

    /// <summary>
    /// Gets the <see cref="Processing.Brush"/> used for filling the destination image.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the logic path that this processor applies to.
    /// </summary>
    public IPath Region { get; }

    /// <summary>
    /// Gets the <see cref="GraphicsOptions"/> defining how to blend the brush pixels over the image pixels.
    /// </summary>
    public DrawingOptions Options { get; }

    internal RasterizerSamplingOrigin SamplingOrigin { get; }

    /// <inheritdoc />
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
        => new FillPathProcessor<TPixel>(configuration, this, source, sourceRectangle);
}
