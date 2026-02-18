// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Uses a brush and a shape to fill the shape with contents of the brush.
/// </summary>
/// <typeparam name="TPixel">The type of the color.</typeparam>
/// <seealso cref="ImageProcessor{TPixel}" />
internal class FillPathProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly FillPathProcessor definition;
    private readonly IPath path;
    private readonly Rectangle bounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="FillPathProcessor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The processing configuration.</param>
    /// <param name="definition">The processor definition.</param>
    /// <param name="source">The source image.</param>
    /// <param name="sourceRectangle">The source bounds.</param>
    public FillPathProcessor(
        Configuration configuration,
        FillPathProcessor definition,
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
        this.path = path.AsClosedPath();
        this.definition = definition;
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        Configuration configuration = this.Configuration;
        ShapeOptions shapeOptions = this.definition.Options.ShapeOptions;
        GraphicsOptions graphicsOptions = this.definition.Options.GraphicsOptions;
        Brush brush = this.definition.Brush;

        // Align start/end positions.
        Rectangle interest = Rectangle.Intersect(this.bounds, source.Bounds);
        if (interest.Equals(Rectangle.Empty))
        {
            return; // No effect inside image;
        }

        MemoryAllocator allocator = this.Configuration.MemoryAllocator;
        IDrawingBackend drawingBackend = configuration.GetDrawingBackend();
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;
        RasterizerOptions rasterizerOptions = new(
            interest,
            shapeOptions.IntersectionRule,
            rasterizationMode,
            RasterizerSamplingOrigin.PixelBoundary);

        // The backend owns rasterization/compositing details. Processors only submit
        // operation-level data (path, brush, options, bounds).
        drawingBackend.FillPath(
            configuration,
            source,
            this.path,
            brush,
            graphicsOptions,
            rasterizerOptions,
            this.bounds,
            allocator);
    }
}
