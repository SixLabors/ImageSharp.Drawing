// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Uses a pen and path to draw an outlined path through <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class DrawPathProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly DrawPathProcessor definition;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawPathProcessor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The processing configuration.</param>
    /// <param name="definition">The processor definition.</param>
    /// <param name="source">The source image.</param>
    /// <param name="sourceRectangle">The source bounds.</param>
    public DrawPathProcessor(
        Configuration configuration,
        DrawPathProcessor definition,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
        => this.definition = definition;

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        using DrawingCanvas<TPixel> canvas = new(
            this.Configuration,
            new Buffer2DRegion<TPixel>(source.PixelBuffer, source.Bounds));

        canvas.DrawPath(this.definition.Path, this.definition.Pen, this.definition.Options);
    }
}
