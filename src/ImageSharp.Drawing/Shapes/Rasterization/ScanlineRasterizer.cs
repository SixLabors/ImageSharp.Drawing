// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Single-pass CPU scanline rasterizer.
/// </summary>
/// <remarks>
/// This implementation directly rasterizes the whole interest rectangle in one pass.
/// It is retained as a compact fallback/reference implementation and as an explicit
/// non-tiled option for profiling and comparison.
/// </remarks>
internal sealed class ScanlineRasterizer : IRasterizer
{
    /// <summary>
    /// Gets the singleton scanline rasterizer instance.
    /// </summary>
    public static ScanlineRasterizer Instance { get; } = new();

    /// <inheritdoc />
    public void Rasterize<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(allocator, nameof(allocator));
        Guard.NotNull(scanlineHandler, nameof(scanlineHandler));

        Rectangle interest = options.Interest;
        if (interest.Equals(Rectangle.Empty))
        {
            return;
        }

        PolygonScanner.Rasterize(path, options, allocator, ref state, scanlineHandler);
    }
}
