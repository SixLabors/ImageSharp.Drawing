// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Using the brush as a source of pixels colors blends the brush color with source.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class FillProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly FillProcessor definition;

    public FillProcessor(Configuration configuration, FillProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
        => this.definition = definition;

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        Rectangle interest = Rectangle.Intersect(this.SourceRectangle, source.Bounds);
        if (interest.Width == 0 || interest.Height == 0)
        {
            return;
        }

        using DrawingCanvas<TPixel> canvas = new(
            this.Configuration,
            new Buffer2DRegion<TPixel>(source.PixelBuffer, interest),
            this.definition.Options);

        canvas.Fill(this.definition.Brush);
    }
}
