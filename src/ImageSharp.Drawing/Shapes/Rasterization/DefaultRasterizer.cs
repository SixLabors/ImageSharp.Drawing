// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Default CPU scanline rasterizer used by ImageSharp.Drawing.
/// </summary>
internal sealed class DefaultRasterizer : IRasterizer
{
    /// <summary>
    /// Gets the singleton default rasterizer instance.
    /// </summary>
    public static DefaultRasterizer Instance { get; } = new();

    /// <inheritdoc />
    public void Rasterize<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(allocator, nameof(allocator));
        Guard.NotNull(scanlineHandler, nameof(scanlineHandler));

        Rectangle interest = options.Interest;
        if (interest.Equals(Rectangle.Empty))
        {
            return;
        }

        if (SharpBlazeScanner.TryRasterize(path, options, allocator, ref state, scanlineHandler))
        {
            return;
        }

        RasterizeWithPolygonScanner(path, options, allocator, ref state, scanlineHandler);
    }

    private static void RasterizeWithPolygonScanner<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        Rectangle interest = options.Interest;
        int minX = interest.Left;
        int scanlineWidth = interest.Width;
        float xOffset = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        bool scanlineDirty = true;

        PolygonScanner scanner = PolygonScanner.Create(
            path,
            interest.Top,
            interest.Bottom,
            options.SubpixelCount,
            options.IntersectionRule,
            allocator);

        try
        {
            using IMemoryOwner<float> scanlineOwner = allocator.Allocate<float>(scanlineWidth);
            Span<float> scanline = scanlineOwner.Memory.Span;

            while (scanner.MoveToNextPixelLine())
            {
                if (scanlineDirty)
                {
                    scanline.Clear();
                }

                scanlineDirty = scanner.ScanCurrentPixelLineInto(minX, xOffset, scanline);
                if (scanlineDirty)
                {
                    scanlineHandler(scanner.PixelLineY, scanline, ref state);
                }
            }
        }
        finally
        {
            scanner.Dispose();
        }
    }
}
