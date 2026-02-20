// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

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
    /// Begins a composition session over a target region.
    /// </summary>
    /// <remarks>
    /// Backends can use this as an optional batching boundary (for example: keep the destination
    /// resident on an accelerator while multiple composite calls are applied).
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination target region.</param>
    public void BeginCompositeSession<TPixel>(Configuration configuration, Buffer2DRegion<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Ends a composition session over a target region.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination target region.</param>
    public void EndCompositeSession<TPixel>(Configuration configuration, Buffer2DRegion<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Fills a path into a destination target region.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination target region.</param>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="rasterizerOptions">Rasterizer options in target-local coordinates.</param>
    public void FillPath<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Fills a local region in a destination target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination target region.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="region">Region in target-local coordinates.</param>
    public void FillRegion<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        Brush brush,
        GraphicsOptions graphicsOptions,
        Rectangle region)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Determines whether this backend can composite coverage using the accelerated path
    /// for the given brush/options combination.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <returns><see langword="true"/> when accelerated composition is supported.</returns>
    public bool SupportsCoverageComposition<TPixel>(Brush brush, in GraphicsOptions graphicsOptions)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Prepares coverage for a path and returns a backend-owned handle.
    /// </summary>
    /// <param name="path">The local path to rasterize.</param>
    /// <param name="rasterizerOptions">Rasterizer options.</param>
    /// <param name="allocator">Allocator for temporary data.</param>
    /// <param name="preparationMode">Coverage preparation mode (<see cref="CoveragePreparationMode.Default"/> or <see cref="CoveragePreparationMode.Fallback"/>).</param>
    /// <returns>An opaque handle to prepared coverage data.</returns>
    public DrawingCoverageHandle PrepareCoverage(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        CoveragePreparationMode preparationMode);

    /// <summary>
    /// Composites prepared coverage into a destination region using a brush.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination target region.</param>
    /// <param name="coverageHandle">Handle to prepared coverage data.</param>
    /// <param name="sourceOffset">Source offset inside the prepared coverage.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="brushBounds">Brush bounds used when creating the applicator.</param>
    public void CompositeCoverage<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        DrawingCoverageHandle coverageHandle,
        Point sourceOffset,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        Rectangle brushBounds)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Releases a prepared coverage handle.
    /// </summary>
    /// <param name="coverageHandle">Handle to release.</param>
    public void ReleaseCoverage(DrawingCoverageHandle coverageHandle);
}
