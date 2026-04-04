// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Canvas frame adapter over a <see cref="NativeSurface"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public sealed class NativeCanvasFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly NativeSurface surface;

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeCanvasFrame{TPixel}"/> class.
    /// </summary>
    /// <param name="bounds">The frame bounds.</param>
    /// <param name="surface">The native surface backing this frame.</param>
    public NativeCanvasFrame(Rectangle bounds, NativeSurface surface)
    {
        Guard.NotNull(surface, nameof(surface));
        this.Bounds = bounds;
        this.surface = surface;
    }

    /// <inheritdoc />
    public Rectangle Bounds { get; }

    /// <inheritdoc />
    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
    {
        region = default;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface)
    {
        surface = this.surface;
        return true;
    }
}
