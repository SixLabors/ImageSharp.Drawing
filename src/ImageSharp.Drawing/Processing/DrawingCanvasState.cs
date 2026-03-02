// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Immutable drawing state used by <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
public sealed class DrawingCanvasState : IDisposable
{
    private readonly Action? releaseScopedState;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvasState"/> class.
    /// </summary>
    /// <param name="options">Drawing options for this state.</param>
    /// <param name="clipPaths">Clip paths for this state.</param>
    /// <param name="releaseScopedState">Optional callback invoked when a scoped state is disposed.</param>
    internal DrawingCanvasState(DrawingOptions options, IReadOnlyList<IPath> clipPaths, Action? releaseScopedState = null)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        this.Options = options;
        this.ClipPaths = clipPaths;
        this.releaseScopedState = releaseScopedState;
    }

    /// <summary>
    /// Gets drawing options associated with this state.
    /// </summary>
    public DrawingOptions Options { get; }

    /// <summary>
    /// Gets clip paths associated with this state.
    /// </summary>
    public IReadOnlyList<IPath> ClipPaths { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.releaseScopedState?.Invoke();
        this.disposed = true;
    }
}
