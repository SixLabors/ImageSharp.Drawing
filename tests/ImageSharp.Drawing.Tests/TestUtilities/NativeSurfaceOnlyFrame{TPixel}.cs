// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

/// <summary>
/// Test frame wrapper that exposes only a native surface.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal sealed class NativeSurfaceOnlyFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly NativeSurface surface;

    public NativeSurfaceOnlyFrame(Rectangle bounds, NativeSurface surface)
    {
        this.Bounds = bounds;
        this.surface = surface;
    }

    public Rectangle Bounds { get; }

    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
    {
        region = default;
        return false;
    }

    public bool TryGetNativeSurface(out NativeSurface surface)
    {
        surface = this.surface;
        return true;
    }
}
