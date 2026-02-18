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
    /// Rasterizes a path into scanline coverage.
    /// </summary>
    /// <typeparam name="TState">The caller-provided mutable state type.</typeparam>
    /// <param name="path">The path to rasterize.</param>
    /// <param name="options">Rasterizer options.</param>
    /// <param name="allocator">Allocator for temporary data.</param>
    /// <param name="state">Caller-owned mutable state passed to the scanline callback.</param>
    /// <param name="scanlineHandler">Scanline callback.</param>
    void RasterizePath<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct;
}
