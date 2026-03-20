// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Drawing backend abstraction used by processors.
/// </summary>
public interface IDrawingBackend
{
    /// <summary>
    /// Gets a value indicating whether this backend is available on the current system.
    /// </summary>
    public bool IsSupported => true;

    /// <summary>
    /// Flushes queued composition operations for the target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination frame.</param>
    /// <param name="compositionScene">Scene commands in submission order.</param>
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Attempts to read source pixels from the target into a caller-provided buffer.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="sourceRectangle">Source rectangle in target-local coordinates.</param>
    /// <param name="destination">
    /// The caller-allocated region to receive the pixel data.
    /// Must be at least as large as <paramref name="sourceRectangle"/> (clamped to target bounds).
    /// </param>
    /// <returns><see langword="true"/> when readback succeeds; otherwise <see langword="false"/>.</returns>
    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>;
}
