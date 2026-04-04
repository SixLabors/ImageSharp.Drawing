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
    /// <param name="clipPaths">Clip paths for this state.</param>
    /// <param name="targetBounds">Absolute target bounds used for commands recorded in this state.</param>
    public DrawingCanvasState(
        DrawingOptions options,
        IReadOnlyList<IPath> clipPaths,
        Rectangle targetBounds)
    {
        this.Options = options;
        this.ClipPaths = clipPaths;
        this.TargetBounds = targetBounds;
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
    /// Gets the absolute target bounds used for commands recorded in this state.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets a value indicating whether this state represents a compositing layer.
    /// </summary>
    public bool IsLayer { get; init; }

    /// <summary>
    /// Gets the layer compositing options when this state represents a compositing layer.
    /// </summary>
    public GraphicsOptions? LayerOptions { get; init; }
}
