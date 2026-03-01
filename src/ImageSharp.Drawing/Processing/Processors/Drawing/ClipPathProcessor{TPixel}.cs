// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Applies a processing operation to a clipped path region by constraining the operation's input domain
/// to the bounds of the path, then using the processed result as an image brush to fill the path.
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
        // Bounds in drawing are floating point. We must conservatively cover the entire shape bounds.
        RectangleF boundsF = this.definition.Region.Bounds;

        int left = (int)MathF.Floor(boundsF.Left);
        int top = (int)MathF.Floor(boundsF.Top);
        int right = (int)MathF.Ceiling(boundsF.Right);
        int bottom = (int)MathF.Ceiling(boundsF.Bottom);

        Rectangle crop = Rectangle.FromLTRB(left, top, right, bottom);

        // Constrain the operation to the intersection of the requested bounds and source region.
        Rectangle clipped = Rectangle.Intersect(this.sourceRectangle, crop);

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        Action<IImageProcessingContext> operation = this.definition.Operation;

        // Run the operation on the clipped context so only pixels inside the clip are affected,
        // matching the expected semantics of clipping in other graphics APIs.
        using Image<TPixel> clone = this.source.Clone(ctx => operation(ctx.Crop(clipped)));

        // Use the clone as a brush source so only the clipped result contributes to the fill,
        // keeping the effect confined to the clipped region.
        Point brushOffset = new(
            clipped.X - (int)MathF.Floor(boundsF.Left),
            clipped.Y - (int)MathF.Floor(boundsF.Top));

        ImageBrush brush = new(clone, clone.Bounds, brushOffset);

        // Fill the shape using the image brush.
        FillPathProcessor processor = new(this.definition.Options, brush, this.definition.Region);
        using IImageProcessor<TPixel> pixelProcessor = processor.CreatePixelSpecificProcessor(this.configuration, this.source, this.sourceRectangle);
        pixelProcessor.Execute();
    }
}
