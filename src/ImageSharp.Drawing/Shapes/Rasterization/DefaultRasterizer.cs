// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Default CPU rasterizer that processes large paths in parallel vertical bands.
/// </summary>
/// <remarks>
/// The algorithm preserves the public scanline callback contract (top-to-bottom emission) while
/// parallelizing internal work:
/// 1. Partition the interest rectangle into Y-bands.
/// 2. Rasterize each band independently into temporary coverage buffers.
/// 3. Emit bands back in deterministic top-to-bottom order.
///
/// This design avoids concurrent writes to destination pixels and keeps per-band work isolated.
/// It also lets the implementation fall back to the single-pass scanner when tiling would not pay
/// off (small workloads, huge temporary buffers, or low core counts).
/// </remarks>
internal sealed class DefaultRasterizer : IRasterizer
{
    // Keep bands reasonably tall so the overhead of per-band setup does not dominate tiny draws.
    private const int MinimumBandHeight = 96;

    // Require a minimum pixel workload per band so thread scheduling overhead stays amortized.
    private const int MinimumPixelsPerBand = 196608;

    // Hard cap on buffered pixels across all bands for a single rasterization invocation.
    // One float is buffered per pixel plus a dirty-row byte map per band.
    private const int MaximumBufferedPixels = 16777216; // 4096 x 4096

    // Bounding band count limits task fan-out and keeps allocator pressure predictable.
    private const int MaximumBandCount = 8;

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
        // Fast argument validation at entry keeps failure behavior consistent with other rasterizers.
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(allocator, nameof(allocator));
        Guard.NotNull(scanlineHandler, nameof(scanlineHandler));

        Rectangle interest = options.Interest;
        if (interest.Equals(Rectangle.Empty))
        {
            // Nothing intersects the destination; skip all work.
            return;
        }

        if (!TryCreateBandPlan(interest, out Band[]? plannedBands) || plannedBands is null)
        {
            // For small or extreme workloads, single-pass rasterization is cheaper and avoids
            // temporary band buffers.
            ScanlineRasterizer.Instance.Rasterize(path, options, allocator, ref state, scanlineHandler);
            return;
        }

        Band[] bands = plannedBands;
        RasterizerOptions bandedOptions = options;

        // Prime lazy path state once on the caller thread to avoid N workers racing to
        // materialize the same internal path structures.
        PrimePathState(path);

        try
        {
            // Limit parallelism to planned band count. This keeps work partition deterministic
            // and avoids oversubscribing worker threads for this operation.
            ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = bands.Length };
            _ = Parallel.For(
                0,
                bands.Length,
                parallelOptions,
                i => RasterizeBand(path, bandedOptions, allocator, bands[i]));

            // Emit in deterministic order so downstream compositing observes stable scanline order.
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

    /// <summary>
    /// Forces lazy path materialization before worker threads start.
    /// </summary>
    /// <param name="path">The source path.</param>
    private static void PrimePathState(IPath path)
    {
        if (path is IInternalPathOwner owner)
        {
            // Force ring extraction once for paths that expose internal rings. This is the
            // hot path for ComplexPolygon and avoids repeated per-band conversion cost.
            _ = owner.GetRingsAsInternalPath().Count;
            return;
        }

        // Fallback for generic paths: force flattening once so lazy point arrays are available
        // before worker threads begin.
        foreach (ISimplePath simplePath in path.Flatten())
        {
            _ = simplePath.Points.Length;
        }
    }

    /// <summary>
    /// Computes a band partitioning plan for the destination rectangle.
    /// </summary>
    /// <param name="interest">Destination interest rectangle.</param>
    /// <param name="bands">
    /// When this method returns <see langword="true"/>, contains the planned rasterization bands.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when banding should be used; otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryCreateBandPlan(Rectangle interest, out Band[]? bands)
    {
        bands = null;

        int width = interest.Width;
        int height = interest.Height;
        long totalPixels = (long)width * height;
        if (totalPixels > MaximumBufferedPixels)
        {
            // Refuse banding for extremely large interests to cap temporary memory use.
            return false;
        }

        int processorCount = Environment.ProcessorCount;
        if (processorCount < 2 || height < (MinimumBandHeight * 2) || totalPixels < (MinimumPixelsPerBand * 2L))
        {
            // Not enough parallel work: prefer single-pass path.
            return false;
        }

        // Bound candidate band count by three limits:
        // - image height (minimum band height),
        // - total pixels (minimum pixels per band),
        // - hardware + hard cap.
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
            // Distribute remainder rows to the earliest bands to keep shapes balanced.
            int bandHeight = baseHeight + (i < remainder ? 1 : 0);
            bands[i] = new Band(y, bandHeight);
            y += bandHeight;
        }

        return true;
    }

    /// <summary>
    /// Rasterizes a single band using the fallback scanline rasterizer into temporary buffers.
    /// </summary>
    /// <param name="path">Path to rasterize.</param>
    /// <param name="options">Rasterization options.</param>
    /// <param name="allocator">Memory allocator.</param>
    /// <param name="band">The destination band to populate.</param>
    private static void RasterizeBand(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        Band band)
    {
        // Band-local buffers keep writes private to the worker and avoid shared state.
        // coverageLength is width * bandHeight and is bounded by band planning constraints.
        int width = options.Interest.Width;
        int coverageLength = checked(width * band.Height);

        IMemoryOwner<float> coverageOwner = allocator.Allocate<float>(coverageLength, AllocationOptions.Clean);
        IMemoryOwner<byte> dirtyRowsOwner = allocator.Allocate<byte>(band.Height, AllocationOptions.Clean);

        try
        {
            RasterizerOptions bandOptions = options.WithInterest(
                new Rectangle(options.Interest.Left, band.Top, width, band.Height));

            // Capture state collects scanline output from the fallback scanner into local buffers.
            BandCaptureState captureState = new(band.Top, width, coverageOwner.Memory, dirtyRowsOwner.Memory);
            ScanlineRasterizer.Instance.Rasterize(path, bandOptions, allocator, ref captureState, CaptureBandScanline);

            band.SetBuffers(coverageOwner, dirtyRowsOwner);
        }
        catch
        {
            coverageOwner.Dispose();
            dirtyRowsOwner.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Emits all buffered bands in top-to-bottom scanline order.
    /// </summary>
    /// <typeparam name="TState">The rasterization callback state type.</typeparam>
    /// <param name="bands">Bands containing buffered coverage.</param>
    /// <param name="scanlineWidth">Width of each scanline.</param>
    /// <param name="state">Mutable callback state.</param>
    /// <param name="scanlineHandler">Scanline callback.</param>
    private static void EmitBands<TState>(
        Band[] bands,
        int scanlineWidth,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        // Serialize final emission in band order so callback consumers receive stable rows.
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
                    // Sparse rows are skipped to avoid unnecessary callback invocations.
                    continue;
                }

                Span<float> scanline = coverage.Slice(row * scanlineWidth, scanlineWidth);
                scanlineHandler(band.Top + row, scanline, ref state);
            }
        }
    }

    /// <summary>
    /// Captures one scanline from the fallback scanner into band-local storage.
    /// </summary>
    /// <param name="y">Absolute destination Y.</param>
    /// <param name="scanline">Coverage values for the row.</param>
    /// <param name="state">Band capture state.</param>
    private static void CaptureBandScanline(int y, Span<float> scanline, ref BandCaptureState state)
    {
        // The fallback scanner writes one row at a time; copy into contiguous band storage.
        int row = y - state.Top;
        Span<float> coverage = state.Coverage.Span;
        scanline.CopyTo(coverage.Slice(row * state.Width, state.Width));
        state.DirtyRows.Span[row] = 1;
    }

    /// <summary>
    /// Mutable capture state used while rasterizing a single band.
    /// </summary>
    private readonly struct BandCaptureState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BandCaptureState"/> struct.
        /// </summary>
        /// <param name="top">Top-most Y of the target band.</param>
        /// <param name="width">Scanline width for the band.</param>
        /// <param name="coverage">Contiguous storage for band coverage rows.</param>
        /// <param name="dirtyRows">Row activity map for sparse emission.</param>
        public BandCaptureState(int top, int width, Memory<float> coverage, Memory<byte> dirtyRows)
        {
            this.Top = top;
            this.Width = width;
            this.Coverage = coverage;
            this.DirtyRows = dirtyRows;
        }

        /// <summary>
        /// Gets the top-most destination Y of the band.
        /// </summary>
        public int Top { get; }

        /// <summary>
        /// Gets the number of pixels in each band row.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets contiguous per-row coverage storage for this band.
        /// </summary>
        public Memory<float> Coverage { get; }

        /// <summary>
        /// Gets the row activity map where non-zero indicates row data is present.
        /// </summary>
        public Memory<byte> DirtyRows { get; }
    }

    /// <summary>
    /// Owns temporary buffers and metadata for a single planned band.
    /// </summary>
    private sealed class Band : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Band"/> class.
        /// </summary>
        /// <param name="top">Top-most destination Y for the band.</param>
        /// <param name="height">Number of rows in the band.</param>
        public Band(int top, int height)
        {
            this.Top = top;
            this.Height = height;
        }

        /// <summary>
        /// Gets the top-most destination Y for this band.
        /// </summary>
        public int Top { get; }

        /// <summary>
        /// Gets the band height in rows.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the owner of the coverage buffer for this band.
        /// </summary>
        public IMemoryOwner<float>? CoverageOwner { get; private set; }

        /// <summary>
        /// Gets the owner of the dirty-row map buffer for this band.
        /// </summary>
        public IMemoryOwner<byte>? DirtyRowsOwner { get; private set; }

        /// <summary>
        /// Assigns buffer ownership to this band instance.
        /// </summary>
        /// <param name="coverageOwner">Coverage buffer owner.</param>
        /// <param name="dirtyRowsOwner">Dirty-row buffer owner.</param>
        public void SetBuffers(IMemoryOwner<float> coverageOwner, IMemoryOwner<byte> dirtyRowsOwner)
        {
            // Ownership is transferred to the band container and released in Dispose().
            this.CoverageOwner = coverageOwner;
            this.DirtyRowsOwner = dirtyRowsOwner;
        }

        /// <summary>
        /// Disposes all band-owned buffers.
        /// </summary>
        public void Dispose()
        {
            // Always release pooled buffers even if rasterization fails in other bands.
            this.CoverageOwner?.Dispose();
            this.DirtyRowsOwner?.Dispose();
            this.CoverageOwner = null;
            this.DirtyRowsOwner = null;
        }
    }
}
