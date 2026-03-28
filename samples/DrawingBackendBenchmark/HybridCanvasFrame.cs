// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace DrawingBackendBenchmark;

/// <summary>
/// Small frame wrapper that exposes both a CPU pixel region and a native surface for the sample host.
/// </summary>
/// <remarks>
/// The benchmark backend renders to the native surface. The CPU region exists only to satisfy the
/// canvas frame contract expected by the drawing APIs used in the sample.
/// </remarks>
internal sealed class HybridCanvasFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Buffer2DRegion<TPixel> cpuRegion;
    private readonly NativeSurface surface;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridCanvasFrame{TPixel}"/> class.
    /// </summary>
    public HybridCanvasFrame(Rectangle bounds, Buffer2DRegion<TPixel> cpuRegion, NativeSurface surface)
    {
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
