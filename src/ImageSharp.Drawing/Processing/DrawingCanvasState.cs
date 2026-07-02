// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Immutable drawing state snapshot used by <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
internal sealed class DrawingCanvasState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvasState"/> class.
    /// </summary>
    /// <param name="options">Drawing options for this state.</param>
    /// <param name="clipState">The normalized clip state.</param>
    /// <param name="targetBounds">Absolute target bounds used for commands recorded in this state.</param>
    /// <param name="destinationOffset">Absolute destination offset for paths recorded in local canvas coordinates.</param>
    public DrawingCanvasState(
        DrawingOptions options,
        DrawingClipState clipState,
        Rectangle targetBounds,
        Point destinationOffset)
    {
        this.Options = options;
        this.ClipState = clipState;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
    }

    /// <summary>
    /// Gets drawing options associated with this state.
    /// </summary>
    /// <remarks>
    /// This is the original <see cref="DrawingOptions"/> reference supplied to the state.
    /// It is not deep-cloned.
    /// </remarks>
    public DrawingOptions Options { get; }

    /// <summary>
    /// Gets the normalized clip state associated with this state.
    /// </summary>
    public DrawingClipState ClipState { get; }

    /// <summary>
    /// Gets the absolute target bounds used for commands recorded in this state.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the absolute destination offset for paths recorded in local canvas coordinates.
    /// </summary>
    public Point DestinationOffset { get; }

    /// <summary>
    /// Gets a value indicating whether this state represents a compositing layer.
    /// </summary>
    public bool IsLayer { get; init; }

    /// <summary>
    /// Gets the layer currently receiving commands for this state.
    /// </summary>
    public DrawingCanvasLayer? Layer { get; init; }
}

/// <summary>
/// Mutable layer state shared by the layer's begin and end commands.
/// </summary>
internal sealed class DrawingCanvasLayer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvasLayer"/> class.
    /// </summary>
    /// <param name="options">The compositing options used when the layer closes.</param>
    public DrawingCanvasLayer(GraphicsOptions options)
    {
        this.Options = options;
    }

    /// <summary>
    /// Gets the compositing options used when the layer closes.
    /// </summary>
    public GraphicsOptions Options { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this layer contains a target-wide apply barrier.
    /// </summary>
    public bool RequiresScopedApply { get; set; }
}
