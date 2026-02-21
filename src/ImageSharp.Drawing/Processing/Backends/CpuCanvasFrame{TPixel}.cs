// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Canvas frame adapter over a CPU <see cref="Buffer2DRegion{T}"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal sealed class CpuCanvasFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Buffer2DRegion<TPixel> region;
    private readonly NativeSurface? nativeSurface;

    public CpuCanvasFrame(Buffer2DRegion<TPixel> region, NativeSurface? nativeSurface = null)
    {
        Guard.NotNull(region.Buffer, nameof(region));
        this.region = region;
        this.nativeSurface = nativeSurface;
    }

    public Rectangle Bounds => this.region.Rectangle;

    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> cpuRegion)
    {
        cpuRegion = this.region;
        return true;
    }

    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface)
    {
        surface = this.nativeSurface;
        return surface is not null;
    }
}
