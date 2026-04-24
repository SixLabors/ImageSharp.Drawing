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

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasRegionFrame{TPixel}"/> class.
    /// </summary>
    /// <param name="parent">The parent frame that owns the target pixels.</param>
    /// <param name="region">The child region in parent-local coordinates.</param>
    public CanvasRegionFrame(ICanvasFrame<TPixel> parent, Rectangle region)
    {
        Guard.NotNull(parent, nameof(parent));
        Guard.MustBeGreaterThanOrEqualTo(region.Width, 0, nameof(region));
        Guard.MustBeGreaterThanOrEqualTo(region.Height, 0, nameof(region));

        this.parent = parent;
        this.region = region;
    }

    /// <inheritdoc />
    public Rectangle Bounds => new(
        this.parent.Bounds.X + this.region.X,
        this.parent.Bounds.Y + this.region.Y,
        this.region.Width,
        this.region.Height);

    /// <inheritdoc />
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

    /// <inheritdoc />
    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface)
        => this.parent.TryGetNativeSurface(out surface);
}
