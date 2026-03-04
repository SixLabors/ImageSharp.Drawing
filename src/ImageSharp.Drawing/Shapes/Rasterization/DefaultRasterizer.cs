// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Default CPU rasterizer.
/// </summary>
/// <remarks>
/// This rasterizer delegates to <see cref="PolygonScanner"/>, which performs fixed-point
/// area/cover scanning and chooses an internal execution strategy (parallel row-tiles when
/// profitable, sequential fallback otherwise).
/// </remarks>
internal sealed class DefaultRasterizer
{
    /// <summary>
    /// Gets the singleton default rasterizer instance.
    /// </summary>
    public static DefaultRasterizer Instance { get; } = new();

    /// <summary>
    /// Rasterizes the path into scanline coverage.
    /// </summary>
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
