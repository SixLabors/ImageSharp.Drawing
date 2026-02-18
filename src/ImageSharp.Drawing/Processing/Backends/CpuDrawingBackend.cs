// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Default CPU drawing backend.
/// </summary>
/// <remarks>
/// This backend currently dispatches to the existing scanline rasterizer pipeline.
/// A tiled rasterizer path is wired behind an AppContext switch for incremental rollout.
/// </remarks>
internal sealed class CpuDrawingBackend : IDrawingBackend
{
    private const string ExperimentalTiledRasterizerSwitch = "SixLabors.ImageSharp.Drawing.ExperimentalTiledRasterizer";

    private readonly IRasterizer defaultRasterizer;
    private readonly TiledRasterizer tiledRasterizer;

    private CpuDrawingBackend(IRasterizer defaultRasterizer)
    {
        Guard.NotNull(defaultRasterizer, nameof(defaultRasterizer));
        this.defaultRasterizer = defaultRasterizer;
        this.tiledRasterizer = TiledRasterizer.Instance;
    }

    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static CpuDrawingBackend Instance { get; } = new(DefaultRasterizer.Instance);

    /// <summary>
    /// Gets the primary rasterizer used by this backend.
    /// </summary>
    public IRasterizer PrimaryRasterizer => this.defaultRasterizer;

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
    {
        if (UseExperimentalTiledRasterizer())
        {
            this.tiledRasterizer.Rasterize(path, options, allocator, ref state, scanlineHandler);
            return;
        }

        this.defaultRasterizer.Rasterize(path, options, allocator, ref state, scanlineHandler);
    }

    private static bool UseExperimentalTiledRasterizer()
        => AppContext.TryGetSwitch(ExperimentalTiledRasterizerSwitch, out bool enabled) && enabled;
}
