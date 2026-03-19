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

    /// <summary>
    /// Composites a layer surface onto a destination frame using the specified graphics options.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="source">The layer frame to composite.</param>
    /// <param name="destination">The destination frame to composite onto.</param>
    /// <param name="destinationOffset">
    /// The offset in the destination where the layer's top-left corner is placed.
    /// </param>
    /// <param name="options">
    /// Graphics options controlling blend mode, alpha composition, and opacity.
    /// </param>
    public void ComposeLayer<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> destination,
        Point destinationOffset,
        GraphicsOptions options)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Creates a layer frame for use during <c>SaveLayer</c>.
    /// The returned frame is initialized to transparent and must be disposed by the caller.
    /// CPU backends return a frame backed by an <see cref="Image{TPixel}"/>;
    /// GPU backends may return a native frame backed by a GPU texture.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="parentTarget">The current target frame that the layer will composite onto.</param>
    /// <param name="width">Layer width in pixels.</param>
    /// <param name="height">Layer height in pixels.</param>
    /// <returns>A new disposable layer frame.</returns>
    public ICanvasFrame<TPixel> CreateLayerFrame<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> parentTarget,
        int width,
        int height)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Releases any backend resources cached against the specified target frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">The target frame whose resources should be released.</param>
    public void ReleaseFrameResources<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>;
}
