// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Frame adapter that exposes a clipped subregion of another frame.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal sealed class CanvasRegionFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ICanvasFrame<TPixel> parent;
    private readonly Rectangle region;

    public CanvasRegionFrame(ICanvasFrame<TPixel> parent, Rectangle region)
    {
        Guard.NotNull(parent, nameof(parent));
        Guard.MustBeGreaterThanOrEqualTo(region.Width, 0, nameof(region));
        Guard.MustBeGreaterThanOrEqualTo(region.Height, 0, nameof(region));
        this.parent = parent;
        this.region = region;
    }

    public Rectangle Bounds => new(
        this.parent.Bounds.X + this.region.X,
        this.parent.Bounds.Y + this.region.Y,
        this.region.Width,
        this.region.Height);

    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
    {
        if (!this.parent.TryGetCpuRegion(out Buffer2DRegion<TPixel> parentRegion))
        {
            region = default;
            return false;
        }

        region = parentRegion.GetSubRegion(this.region);
        return true;
    }

    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface)
        => this.parent.TryGetNativeSurface(out surface);
}
