// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Canvas frame backed by a <see cref="Buffer2DRegion{T}"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public sealed class MemoryCanvasFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Buffer2DRegion<TPixel> region;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCanvasFrame{TPixel}"/> class.
    /// </summary>
    /// <param name="region">The pixel buffer region backing this frame.</param>
    public MemoryCanvasFrame(Buffer2DRegion<TPixel> region)
    {
        Guard.NotNull(region.Buffer, nameof(region));
        this.region = region;
    }

    /// <inheritdoc />
    public Rectangle Bounds => this.region.Rectangle;

    /// <inheritdoc />
    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
    {
        region = this.region;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface)
    {
        surface = null;
        return false;
    }
}
