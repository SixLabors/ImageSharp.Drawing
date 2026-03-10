// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

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
    /// <param name="clipPaths">Clip paths for this state.</param>
    public DrawingCanvasState(DrawingOptions options, IReadOnlyList<IPath> clipPaths)
    {
        this.Options = options;
        this.ClipPaths = clipPaths;
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
    /// Gets clip paths associated with this state.
    /// </summary>
    public IReadOnlyList<IPath> ClipPaths { get; }

    /// <summary>
    /// Gets a value indicating whether this state represents a compositing layer.
    /// </summary>
    public bool IsLayer { get; init; }

    /// <summary>
    /// Gets the graphics options used to composite this layer on restore.
    /// Only set when <see cref="IsLayer"/> is <see langword="true"/>.
    /// </summary>
    public GraphicsOptions? LayerOptions { get; init; }

    /// <summary>
    /// Gets the local bounds of this layer relative to the parent canvas.
    /// Only set when <see cref="IsLayer"/> is <see langword="true"/>.
    /// </summary>
    public Rectangle? LayerBounds { get; init; }
}

/// <summary>
/// Typed layer data that holds the parent batcher and temporary layer buffer.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal sealed class LayerData<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LayerData{TPixel}"/> class.
    /// </summary>
    /// <param name="parentBatcher">The parent batcher to restore on layer pop.</param>
    /// <param name="layerFrame">The canvas frame wrapping the layer buffer.</param>
    /// <param name="layerBounds">The local bounds of this layer relative to the parent.</param>
    public LayerData(
        DrawingCanvasBatcher<TPixel> parentBatcher,
        ICanvasFrame<TPixel> layerFrame,
        Rectangle layerBounds)
    {
        this.ParentBatcher = parentBatcher;
        this.LayerFrame = layerFrame;
        this.LayerBounds = layerBounds;
    }

    /// <summary>
    /// Gets the batcher that was active before this layer was pushed.
    /// </summary>
    public DrawingCanvasBatcher<TPixel> ParentBatcher { get; }

    /// <summary>
    /// Gets the canvas frame wrapping the layer buffer.
    /// </summary>
    public ICanvasFrame<TPixel> LayerFrame { get; }

    /// <summary>
    /// Gets the local bounds of this layer relative to the parent canvas.
    /// </summary>
    public Rectangle LayerBounds { get; }
}
