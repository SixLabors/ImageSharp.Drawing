// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Internal drawing backend abstraction used by processors.
/// </summary>
/// <remarks>
/// This boundary allows processor logic to stay stable while the implementation evolves
/// (for example: alternate CPU rasterizers or eventual non-CPU backends).
/// </remarks>
internal interface IDrawingBackend
{
    /// <summary>
    /// Determines whether the backend can compose the provided brush type directly for <typeparamref name="TPixel"/>.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="brush">The brush used by a pending composition command.</param>
    /// <returns>
    /// <see langword="true"/> when the backend can compose the brush directly; otherwise <see langword="false"/>.
    /// </returns>
    public bool IsCompositionBrushSupported<TPixel>(Brush brush)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Fills a path into a destination target region.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="target">Destination frame.</param>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="rasterizerOptions">Rasterizer options in target-local coordinates.</param>
    /// <param name="batcher">Batcher used to queue normalized composition commands.</param>
    public void FillPath<TPixel>(
        ICanvasFrame<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        DrawingCanvasBatcher<TPixel> batcher)
        where TPixel : unmanaged, IPixel<TPixel>;

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
}
