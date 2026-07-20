// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
    /// <param name="canvasBounds">The canvas-local bounds captured when the barrier was recorded.</param>
    /// <param name="targetBounds">The absolute target bounds captured when the barrier was recorded.</param>
    /// <param name="destinationOffset">The absolute destination offset captured when the barrier was recorded.</param>
    /// <param name="ownerLayer">The layer that owned this barrier when it was recorded.</param>
    /// <param name="operation">The processor operation to run against the replay-time snapshot.</param>
    /// <param name="effect">The layer effect represented by the operation, or <see langword="null"/> for a direct Apply operation.</param>
    /// <param name="writeBackOptions">
    /// The graphics options used to composite the processed pixels back onto the target, or
    /// <see langword="null"/> to replace the region outright.
    /// </param>
    /// <param name="writeBackOffset">The offset at which the processed pixels are written back.</param>
    public ApplyBarrier(
        IPath path,
        DrawingOptions options,
        Rectangle canvasBounds,
        Rectangle targetBounds,
        Point destinationOffset,
        DrawingCanvasLayer? ownerLayer,
        Action<IImageProcessingContext> operation,
        LayerEffect? effect,
        GraphicsOptions? writeBackOptions,
        Point writeBackOffset)
    {
        this.Path = path;
        this.OutputBounds = path.Bounds;

        this.Options = options;
        this.CanvasBounds = canvasBounds;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
        this.OwnerLayer = ownerLayer;
        this.Operation = operation;
        this.Effect = effect;
        this.WriteBackOptions = writeBackOptions;
        this.WriteBackOffset = writeBackOffset;
    }

    /// <summary>
    /// Gets the closed path defining the processed region.
    /// </summary>
    public IPath Path { get; }

    /// <summary>
    /// Gets the local bounds within which the processed output is written.
    /// </summary>
    public RectangleF OutputBounds { get; }

    /// <summary>
    /// Gets the drawing options captured when the barrier was recorded.
    /// </summary>
    public DrawingOptions Options { get; }

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
    public bool IsInsideLayer => this.OwnerLayer is not null;

    /// <summary>
    /// Gets the layer that owned this barrier when it was recorded.
    /// </summary>
    public DrawingCanvasLayer? OwnerLayer { get; }

    /// <summary>
    /// Gets the processor operation to run against the replay-time snapshot.
    /// </summary>
    public Action<IImageProcessingContext> Operation { get; }

    /// <summary>
    /// Gets the layer effect represented by the operation, or <see langword="null"/> for a direct Apply operation.
    /// </summary>
    public LayerEffect? Effect { get; }

    /// <summary>
    /// Gets the graphics options used to composite the processed pixels back onto the target.
    /// When <see langword="null"/> the processed pixels replace the region outright.
    /// </summary>
    public GraphicsOptions? WriteBackOptions { get; }

    /// <summary>
    /// Gets the offset, in device pixels, at which the processed pixels are written back relative
    /// to the region they were read from.
    /// </summary>
    public Point WriteBackOffset { get; }
}
