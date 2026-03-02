// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Immutable drawing state snapshot used by <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
public sealed class DrawingCanvasState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvasState"/> class.
    /// </summary>
    /// <param name="options">Drawing options for this state.</param>
    /// <param name="clipPaths">Clip paths for this state.</param>
    internal DrawingCanvasState(DrawingOptions options, IReadOnlyList<IPath> clipPaths)
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
}
