// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Delegate invoked for each rasterized scanline.
/// </summary>
/// <typeparam name="TState">The caller-provided state type.</typeparam>
/// <param name="y">The destination y coordinate.</param>
/// <param name="scanline">Coverage values for the scanline.</param>
/// <param name="state">Caller-provided mutable state.</param>
internal delegate void RasterizerScanlineHandler<TState>(int y, Span<float> scanline, ref TState state)
    where TState : struct;

/// <summary>
/// Defines a rasterizer capable of converting vector paths into per-pixel scanline coverage.
/// </summary>
internal interface IRasterizer
{
    /// <summary>
    /// Rasterizes a path into scanline coverage and invokes <paramref name="scanlineHandler"/>
    /// for each non-empty destination row.
    /// </summary>
    /// <typeparam name="TState">The caller-provided state type.</typeparam>
    /// <param name="path">The path to rasterize.</param>
    /// <param name="options">Rasterization options.</param>
    /// <param name="allocator">The memory allocator used for temporary buffers.</param>
    /// <param name="state">Caller-provided mutable state passed to the callback.</param>
    /// <param name="scanlineHandler">
    /// Callback invoked for each rasterized scanline. Implementations should invoke this callback
    /// in ascending y order and not concurrently for a single <see cref="Rasterize{TState}"/> invocation.
    /// </param>
    void Rasterize<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct;
}
