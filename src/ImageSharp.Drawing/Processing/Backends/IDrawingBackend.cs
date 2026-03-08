// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Drawing backend abstraction used by processors.
/// </summary>
public interface IDrawingBackend
{
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
    /// Attempts to read source pixels from the target into a temporary image.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="sourceRectangle">Source rectangle in target-local coordinates.</param>
    /// <param name="image">
    /// When this method returns <see langword="true"/>, receives a newly allocated source image.
    /// </param>
    /// <returns><see langword="true"/> when readback succeeds; otherwise <see langword="false"/>.</returns>
    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        [NotNullWhen(true)] out Image<TPixel>? image)
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
