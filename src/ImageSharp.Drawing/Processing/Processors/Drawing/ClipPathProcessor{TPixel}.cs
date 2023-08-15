// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// The main workhorse class. This has access to the pixel buffer but
/// in an abstract/generic way.
/// </summary>
/// <typeparam name="TPixel">The type of pixel.</typeparam>
internal class ClipPathProcessor<TPixel> : IImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ClipPathProcessor definition;
    private readonly Image<TPixel> source;
    private readonly Configuration configuration;
    private readonly Rectangle sourceRectangle;

    public ClipPathProcessor(ClipPathProcessor definition, Image<TPixel> source, Configuration configuration, Rectangle sourceRectangle)
    {
        this.definition = definition;
        this.source = source;
        this.configuration = configuration;
        this.sourceRectangle = sourceRectangle;
    }

    public void Dispose()
    {
    }

    public void Execute()
    {
        // Clone out our source image so we can apply various effects to it without mutating
        // the original yet.
        using Image<TPixel> clone = this.source.Clone(this.definition.Operation);

        // Use an image brush to apply cloned image as the source for filling the shape.
        // We pass explicit bounds to avoid the need to crop the clone;
        RectangleF bounds = this.definition.Region.Bounds;
        var brush = new ImageBrush(clone, bounds);

        // Grab hold of an image processor that can fill paths with a brush to allow it to do the hard pixel pushing for us
        var processor = new FillPathProcessor(this.definition.Options, brush, this.definition.Region);
        using IImageProcessor<TPixel> p = processor.CreatePixelSpecificProcessor(this.configuration, this.source, this.sourceRectangle);

        // Fill the shape using the image brush
        p.Execute();
    }
}
