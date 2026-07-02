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
    internal ApplyBarrier(
        IPath path,
        DrawingOptions options,
        Rectangle canvasBounds,
        Rectangle targetBounds,
        Point destinationOffset,
        DrawingCanvasLayer? ownerLayer,
        Action<IImageProcessingContext> operation)
    {
        this.Path = path;
        this.Options = options;
        this.CanvasBounds = canvasBounds;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
        this.OwnerLayer = ownerLayer;
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
}
