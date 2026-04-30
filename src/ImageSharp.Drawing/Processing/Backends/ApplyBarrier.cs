// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Processor barrier recorded in a drawing backend timeline.
/// </summary>
internal sealed class ApplyBarrier
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyBarrier"/> class.
    /// </summary>
    /// <param name="path">The closed path defining the processed region.</param>
    /// <param name="options">The drawing options captured when the barrier was recorded.</param>
    /// <param name="clipPaths">The active clip paths captured when the barrier was recorded.</param>
    /// <param name="canvasBounds">The canvas-local bounds captured when the barrier was recorded.</param>
    /// <param name="targetBounds">The absolute target bounds captured when the barrier was recorded.</param>
    /// <param name="destinationOffset">The absolute destination offset captured when the barrier was recorded.</param>
    /// <param name="isInsideLayer">Indicates whether the barrier was recorded inside a layer.</param>
    /// <param name="operation">The processor operation to run against the replay-time snapshot.</param>
    internal ApplyBarrier(
        IPath path,
        DrawingOptions options,
        IReadOnlyList<IPath> clipPaths,
        Rectangle canvasBounds,
        Rectangle targetBounds,
        Point destinationOffset,
        bool isInsideLayer,
        Action<IImageProcessingContext> operation)
    {
        this.Path = path;
        this.Options = options;
        this.ClipPaths = clipPaths;
        this.CanvasBounds = canvasBounds;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
        this.IsInsideLayer = isInsideLayer;
        this.Operation = operation;
    }

    /// <summary>
    /// Gets the closed path defining the processed region.
    /// </summary>
    public IPath Path { get; }

    /// <summary>
    /// Gets the drawing options captured when the barrier was recorded.
    /// </summary>
    public DrawingOptions Options { get; }

    /// <summary>
    /// Gets the active clip paths captured when the barrier was recorded.
    /// </summary>
    public IReadOnlyList<IPath> ClipPaths { get; }

    /// <summary>
    /// Gets the canvas-local bounds captured when the barrier was recorded.
    /// </summary>
    public Rectangle CanvasBounds { get; }

    /// <summary>
    /// Gets the absolute target bounds captured when the barrier was recorded.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the absolute destination offset captured when the barrier was recorded.
    /// </summary>
    public Point DestinationOffset { get; }

    /// <summary>
    /// Gets a value indicating whether the barrier was recorded inside a layer.
    /// </summary>
    public bool IsInsideLayer { get; }

    /// <summary>
    /// Gets the processor operation to run against the replay-time snapshot.
    /// </summary>
    public Action<IImageProcessingContext> Operation { get; }

    /// <summary>
    /// Creates the transient image-brush draw command that writes this barrier's processed snapshot back to the target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="backend">The backend used to read the replay-time target pixels.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="ownedResource">The image resource that must stay alive while the returned command batch is rendered.</param>
    /// <returns>The transient write-back command batch, or <see langword="null"/> when the barrier has no target coverage.</returns>
    public DrawingCommandBatch? CreateWriteBackBatch<TPixel>(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> target,
        out IDisposable? ownedResource)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RectangleF rawBounds = RectangleF.Transform(this.Path.Bounds, this.Options.Transform);
        Rectangle sourceRect = ToConservativeBounds(rawBounds);
        sourceRect = Rectangle.Intersect(this.CanvasBounds, sourceRect);

        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            ownedResource = null;
            return null;
        }

        Image<TPixel> sourceImage = new(configuration, sourceRect.Width, sourceRect.Height);
        try
        {
            backend.ReadRegion(
                configuration,
                target,
                sourceRect,
                new Buffer2DRegion<TPixel>(sourceImage.Frames.RootFrame.PixelBuffer));

            sourceImage.Mutate(this.Operation);

            Point brushOffset = new(
                sourceRect.X - (int)MathF.Floor(rawBounds.Left),
                sourceRect.Y - (int)MathF.Floor(rawBounds.Top));

            ImageBrush<TPixel> brush = new(sourceImage, sourceImage.Bounds, brushOffset);
            GraphicsOptions graphicsOptions = this.Options.GraphicsOptions;
            RasterizationMode rasterizationMode = graphicsOptions.Antialias
                ? RasterizationMode.Antialiased
                : RasterizationMode.Aliased;

            RectangleF pathBounds = this.Path.Bounds;
            Rectangle interest = Rectangle.FromLTRB(
                (int)MathF.Floor(pathBounds.Left),
                (int)MathF.Floor(pathBounds.Top),
                (int)MathF.Ceiling(pathBounds.Right),
                (int)MathF.Ceiling(pathBounds.Bottom));

            RasterizerOptions rasterizerOptions = new(
                interest,
                this.Options.ShapeOptions.IntersectionRule,
                rasterizationMode,
                RasterizerSamplingOrigin.PixelBoundary,
                graphicsOptions.AntialiasThreshold);

            CompositionCommand command = CompositionCommand.Create(
                this.Path,
                brush,
                this.Options,
                in rasterizerOptions,
                this.TargetBounds,
                this.DestinationOffset,
                this.ClipPaths,
                this.IsInsideLayer);

            ownedResource = sourceImage;
            CompositionSceneCommand[] commands = [new PathCompositionSceneCommand(command)];
            return new DrawingCommandBatch(commands, hasLayers: false);
        }
        catch
        {
            sourceImage.Dispose();
            throw;
        }
    }

    private static Rectangle ToConservativeBounds(RectangleF bounds)
        => Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));
}
