// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Canvas frame adapter that exposes both a CPU region and a native surface.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public sealed class HybridCanvasFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Buffer2DRegion<TPixel> cpuRegion;
    private readonly NativeSurface surface;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridCanvasFrame{TPixel}"/> class.
    /// </summary>
    /// <param name="bounds">The frame bounds.</param>
    /// <param name="cpuRegion">The CPU-accessible pixel buffer region backing this frame.</param>
    /// <param name="surface">The native surface backing this frame.</param>
    public HybridCanvasFrame(Rectangle bounds, Buffer2DRegion<TPixel> cpuRegion, NativeSurface surface)
    {
        Guard.NotNull(cpuRegion.Buffer, nameof(cpuRegion));
        Guard.NotNull(surface, nameof(surface));

        if (cpuRegion.Width != bounds.Width || cpuRegion.Height != bounds.Height)
        {
            throw new ArgumentException(
                $"CPU region dimensions ({cpuRegion.Width}x{cpuRegion.Height}) must match frame bounds ({bounds.Width}x{bounds.Height}).",
                nameof(cpuRegion));
        }

        this.Bounds = bounds;
        this.cpuRegion = cpuRegion;
        this.surface = surface;
    }

    /// <inheritdoc />
    public Rectangle Bounds { get; }

    /// <inheritdoc />
    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
    {
        region = this.cpuRegion;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface)
    {
        surface = this.surface;
        return true;
    }
}
