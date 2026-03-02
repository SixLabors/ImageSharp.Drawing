// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

/// <summary>
/// Using the brush as a source of pixels colors blends the brush color with source.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class DrawTextProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly DrawTextProcessor definition;

    public DrawTextProcessor(Configuration configuration, DrawTextProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
        => this.definition = definition;

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        using DrawingCanvas<TPixel> canvas = new(
            this.Configuration,
            new Buffer2DRegion<TPixel>(source.PixelBuffer, source.Bounds),
            this.definition.DrawingOptions);

        canvas.DrawText(
            this.definition.TextOptions,
            this.definition.Text,
            this.definition.Brush,
            this.definition.Pen);
    }
}
