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
    /// Fills a path into the destination image using the given brush and drawing options.
    /// </summary>
    /// <remarks>
    /// This operation-level API keeps processors independent from scanline rasterization details,
    /// allowing alternate backend implementations (for example GPU backends) to consume brush
    /// and path data directly.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="source">Destination image frame.</param>
    /// <param name="path">The path to rasterize.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="rasterizerOptions">Rasterizer options.</param>
    /// <param name="brushBounds">Brush bounds used when creating the applicator.</param>
    /// <param name="allocator">Allocator for temporary data.</param>
    public void FillPath<TPixel>(
        Configuration configuration,
        ImageFrame<TPixel> source,
        IPath path,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle brushBounds,
        MemoryAllocator allocator)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Rasterizes path coverage into a floating-point destination map.
    /// </summary>
    /// <remarks>
    /// Coverage values are written in local destination coordinates where <c>(0,0)</c> maps to
    /// the top-left of <paramref name="destination"/>.
    /// </remarks>
    /// <param name="path">The path to rasterize.</param>
    /// <param name="rasterizerOptions">Rasterizer options.</param>
    /// <param name="allocator">Allocator for temporary data.</param>
    /// <param name="destination">Destination coverage map.</param>
    public void RasterizeCoverage(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        Buffer2D<float> destination);
}
