// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Per-frame destination for <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public interface ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Gets the frame bounds in root target coordinates.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Attempts to get a CPU-accessible destination region.
    /// </summary>
    /// <param name="region">The CPU region when available.</param>
    /// <returns><see langword="true"/> when a CPU region is available.</returns>
    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region);

    /// <summary>
    /// Attempts to get an opaque native destination surface.
    /// </summary>
    /// <param name="surface">The native surface when available.</param>
    /// <returns><see langword="true"/> when a native surface is available.</returns>
    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface);
}
