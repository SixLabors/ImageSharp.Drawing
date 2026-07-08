// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A render destination for <see cref="DrawingCanvas{TPixel}"/>. Implementations back the canvas
/// with either a CPU-accessible pixel region or an opaque native surface; a usable frame must
/// expose at least one of the two.
/// </summary>
/// <remarks>
/// A backend inspects the frame through <see cref="TryGetCpuRegion"/> and
/// <see cref="TryGetNativeSurface"/> to choose its render path. Exactly one of the two succeeds
/// for a given frame: a CPU frame yields a <see cref="Buffer2DRegion{T}"/>, a native frame yields
/// a <see cref="NativeSurface"/>.
/// </remarks>
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
