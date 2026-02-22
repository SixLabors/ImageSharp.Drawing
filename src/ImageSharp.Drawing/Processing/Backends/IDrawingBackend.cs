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
    /// Fills a path into a destination target region.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination frame.</param>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="rasterizerOptions">Rasterizer options in target-local coordinates.</param>
    /// <param name="batcher">Batcher used to queue normalized composition commands.</param>
    public void FillPath<TPixel>(
        Configuration configuration,
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
    /// <param name="compositionBatch">Prepared composition definitions and commands in batch order.</param>
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch)
        where TPixel : unmanaged, IPixel<TPixel>;
}
