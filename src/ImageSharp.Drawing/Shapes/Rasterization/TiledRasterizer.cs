// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Experimental tiled CPU rasterizer.
/// </summary>
/// <remarks>
/// The implementation splits the Y range into independent bands, rasterizes each band into a
/// temporary coverage buffer, then emits scanlines in deterministic top-to-bottom order.
/// This keeps the external callback contract unchanged while enabling parallel work internally.
/// </remarks>
internal sealed class TiledRasterizer : IRasterizer
{
    // Keep tiles reasonably tall so small text/glyph workloads stay on the scalar fast path.
    private const int MinimumBandHeight = 96;

    // Minimum amount of work assigned to each band.
    private const int MinimumPixelsPerBand = 196608;

    // Bounded temporary memory: one float coverage value per destination pixel.
    private const int MaximumBufferedPixels = 16777216; // 4096 x 4096

    private const int MaximumBandCount = 8;

    /// <summary>
    /// Gets the singleton tiled rasterizer instance.
    /// </summary>
    public static TiledRasterizer Instance { get; } = new();

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

        if (!TryCreateBandPlan(interest, out Band[]? plannedBands) || plannedBands is null)
        {
            DefaultRasterizer.Instance.Rasterize(path, options, allocator, ref state, scanlineHandler);
            return;
        }

        Band[] bands = plannedBands;
        RasterizerOptions bandedOptions = options;

        // Most path implementations lazily materialize flattened point buffers.
        // Priming this once avoids duplicated cache-building across worker threads.
        PrimePathCaches(path);

        try
        {
            ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = bands.Length };
            Parallel.For(
                0,
                bands.Length,
                parallelOptions,
                i => RasterizeBand(path, bandedOptions, allocator, bands[i]));

            EmitBands(bands, interest.Width, ref state, scanlineHandler);
        }
        finally
        {
            foreach (Band band in bands)
            {
                band.Dispose();
            }
        }
    }

    private static void PrimePathCaches(IPath path)
    {
        foreach (ISimplePath simplePath in path.Flatten())
        {
            _ = simplePath.Points.Length;
        }
    }

    private static bool TryCreateBandPlan(Rectangle interest, out Band[]? bands)
    {
        bands = null;

        int width = interest.Width;
        int height = interest.Height;
        long totalPixels = (long)width * height;
        if (totalPixels > MaximumBufferedPixels)
        {
            return false;
        }

        int processorCount = Environment.ProcessorCount;
        if (processorCount < 2 || height < (MinimumBandHeight * 2) || totalPixels < (MinimumPixelsPerBand * 2L))
        {
            return false;
        }

        int byHeight = height / MinimumBandHeight;
        int byPixels = (int)(totalPixels / MinimumPixelsPerBand);
        int bandCount = Math.Min(MaximumBandCount, Math.Min(processorCount, Math.Min(byHeight, byPixels)));
        if (bandCount < 2)
        {
            return false;
        }

        bands = new Band[bandCount];
        int baseHeight = height / bandCount;
        int remainder = height % bandCount;
        int y = interest.Top;

        for (int i = 0; i < bandCount; i++)
        {
            int bandHeight = baseHeight + (i < remainder ? 1 : 0);
            bands[i] = new Band(y, bandHeight);
            y += bandHeight;
        }

        return true;
    }

    private static void RasterizeBand(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        Band band)
    {
        int width = options.Interest.Width;
        int coverageLength = checked(width * band.Height);

        IMemoryOwner<float> coverageOwner = allocator.Allocate<float>(coverageLength, AllocationOptions.Clean);
        IMemoryOwner<byte> dirtyRowsOwner = allocator.Allocate<byte>(band.Height, AllocationOptions.Clean);

        try
        {
            RasterizerOptions bandOptions = options.WithInterest(
                new Rectangle(options.Interest.Left, band.Top, width, band.Height));

            BandCaptureState captureState = new(band.Top, width, coverageOwner.Memory, dirtyRowsOwner.Memory);
            DefaultRasterizer.Instance.Rasterize(path, bandOptions, allocator, ref captureState, CaptureBandScanline);

            band.SetBuffers(coverageOwner, dirtyRowsOwner);
        }
        catch
        {
            coverageOwner.Dispose();
            dirtyRowsOwner.Dispose();
            throw;
        }
    }

    private static void EmitBands<TState>(
        Band[] bands,
        int scanlineWidth,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        foreach (Band band in bands)
        {
            if (band.CoverageOwner is null || band.DirtyRowsOwner is null)
            {
                continue;
            }

            Span<float> coverage = band.CoverageOwner.Memory.Span;
            Span<byte> dirtyRows = band.DirtyRowsOwner.Memory.Span;

            for (int row = 0; row < band.Height; row++)
            {
                if (dirtyRows[row] == 0)
                {
                    continue;
                }

                Span<float> scanline = coverage.Slice(row * scanlineWidth, scanlineWidth);
                scanlineHandler(band.Top + row, scanline, ref state);
            }
        }
    }

    private static void CaptureBandScanline(int y, Span<float> scanline, ref BandCaptureState state)
    {
        int row = y - state.Top;
        Span<float> coverage = state.Coverage.Span;
        scanline.CopyTo(coverage.Slice(row * state.Width, state.Width));
        state.DirtyRows.Span[row] = 1;
    }

    private struct BandCaptureState
    {
        public BandCaptureState(int top, int width, Memory<float> coverage, Memory<byte> dirtyRows)
        {
            this.Top = top;
            this.Width = width;
            this.Coverage = coverage;
            this.DirtyRows = dirtyRows;
        }

        public int Top { get; }

        public int Width { get; }

        public Memory<float> Coverage { get; }

        public Memory<byte> DirtyRows { get; }
    }

    private sealed class Band : IDisposable
    {
        public Band(int top, int height)
        {
            this.Top = top;
            this.Height = height;
        }

        public int Top { get; }

        public int Height { get; }

        public IMemoryOwner<float>? CoverageOwner { get; private set; }

        public IMemoryOwner<byte>? DirtyRowsOwner { get; private set; }

        public void SetBuffers(IMemoryOwner<float> coverageOwner, IMemoryOwner<byte> dirtyRowsOwner)
        {
            this.CoverageOwner = coverageOwner;
            this.DirtyRowsOwner = dirtyRowsOwner;
        }

        public void Dispose()
        {
            this.CoverageOwner?.Dispose();
            this.DirtyRowsOwner?.Dispose();
            this.CoverageOwner = null;
            this.DirtyRowsOwner = null;
        }
    }
}
