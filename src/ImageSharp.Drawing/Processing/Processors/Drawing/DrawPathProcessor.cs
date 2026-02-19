// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Defines a processor to fill <see cref="Image"/> pixels withing a given <see cref="IPath"/>
/// with the given <see cref="Brush"/> and blending defined by the given <see cref="DrawingOptions"/>.
/// </summary>
public class DrawPathProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawPathProcessor" /> class.
    /// </summary>
    /// <param name="options">The graphics options.</param>
    /// <param name="pen">The details how to outline the region of interest.</param>
    /// <param name="path">The path to be filled.</param>
    public DrawPathProcessor(DrawingOptions options, Pen pen, IPath path)
    {
        this.Path = path;
        this.Pen = pen;
        this.Options = options;
    }

    /// <summary>
    /// Gets the <see cref="Brush"/> used for filling the destination image.
    /// </summary>
    public Pen Pen { get; }

    /// <summary>
    /// Gets the path that this processor applies to.
    /// </summary>
    public IPath Path { get; }

    /// <summary>
    /// Gets the <see cref="DrawingOptions"/> defining how to blend the brush pixels over the image pixels.
    /// </summary>
    public DrawingOptions Options { get; }

    /// <inheritdoc />
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Offset drawlines to align drawing outlines to pixel centers.
        // The global transform is applied in the FillPathProcessor.
        IPath outline = this.Pen.GeneratePath(this.Path.Transform(Matrix3x2.CreateTranslation(0.5F, 0.5F)));

        DrawingOptions effectiveOptions = this.Options;

        // Non-normalized stroked output can contain overlaps/self-intersections.
        // Rasterizing these contours with non-zero winding matches the intended stroke semantics.
        if (!this.Pen.StrokeOptions.NormalizeOutput &&
            this.Options.ShapeOptions.IntersectionRule != IntersectionRule.NonZero)
        {
            ShapeOptions shapeOptions = this.Options.ShapeOptions.DeepClone();
            shapeOptions.IntersectionRule = IntersectionRule.NonZero;

            effectiveOptions = new DrawingOptions(this.Options.GraphicsOptions, shapeOptions, this.Options.Transform);
        }

        return new FillPathProcessor(effectiveOptions, this.Pen.StrokeFill, outline)
            .CreatePixelSpecificProcessor(configuration, source, sourceRectangle);
    }
}
