// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Default CPU drawing backend.
/// </summary>
internal sealed class CpuDrawingBackend : IDrawingBackend
{
    private readonly IRasterizer primaryRasterizer;

    private CpuDrawingBackend(IRasterizer primaryRasterizer)
    {
        Guard.NotNull(primaryRasterizer, nameof(primaryRasterizer));
        this.primaryRasterizer = primaryRasterizer;
    }

    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static CpuDrawingBackend Instance { get; } = new(DefaultRasterizer.Instance);

    /// <summary>
    /// Gets the primary rasterizer used by this backend.
    /// </summary>
    public IRasterizer PrimaryRasterizer => this.primaryRasterizer;

    /// <summary>
    /// Creates a backend that uses the given rasterizer as the primary implementation.
    /// </summary>
    /// <param name="rasterizer">Primary rasterizer.</param>
    /// <returns>A backend instance.</returns>
    public static CpuDrawingBackend Create(IRasterizer rasterizer)
    {
        Guard.NotNull(rasterizer, nameof(rasterizer));
        return ReferenceEquals(rasterizer, DefaultRasterizer.Instance) ? Instance : new CpuDrawingBackend(rasterizer);
    }

    /// <inheritdoc />
    public void RasterizePath<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
        => this.primaryRasterizer.Rasterize(path, options, allocator, ref state, scanlineHandler);
}
