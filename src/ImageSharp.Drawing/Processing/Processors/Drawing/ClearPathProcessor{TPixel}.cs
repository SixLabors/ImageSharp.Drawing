// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Uses a brush and a shape to clear the shape with clear composition semantics.
/// </summary>
/// <typeparam name="TPixel">The type of the color.</typeparam>
/// <seealso cref="ImageProcessor{TPixel}" />
internal class ClearPathProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ClearPathProcessor definition;
    private readonly IPath path;
    private readonly Rectangle bounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClearPathProcessor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The processing configuration.</param>
    /// <param name="definition">The processor definition.</param>
    /// <param name="source">The source image.</param>
    /// <param name="sourceRectangle">The source bounds.</param>
    public ClearPathProcessor(
        Configuration configuration,
        ClearPathProcessor definition,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
    {
        IPath path = definition.Region;
        int left = (int)MathF.Floor(path.Bounds.Left);
        int top = (int)MathF.Floor(path.Bounds.Top);
        int right = (int)MathF.Ceiling(path.Bounds.Right);
        int bottom = (int)MathF.Ceiling(path.Bounds.Bottom);

        this.bounds = Rectangle.FromLTRB(left, top, right, bottom);
        this.path = path;
        this.definition = definition;
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        Configuration configuration = this.Configuration;
        Brush brush = this.definition.Brush;

        Rectangle interest = Rectangle.Intersect(this.bounds, source.Bounds);
        if (interest.Equals(Rectangle.Empty))
        {
            return;
        }

        using DrawingCanvas<TPixel> canvas = new(
            configuration,
            new Buffer2DRegion<TPixel>(source.PixelBuffer, source.Bounds));

        canvas.ClearPath(this.path, brush, this.definition.Options);
    }
}
