// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Default drawing backend.
/// </summary>
/// <remarks>
/// This backend keeps scanline handling internal so higher-level processors
/// can remain backend-agnostic.
/// </remarks>
internal sealed class DefaultDrawingBackend : IDrawingBackend
{
    private readonly ConcurrentDictionary<int, Buffer2D<float>> preparedCoverage = new();
    private int nextCoverageHandleId;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDrawingBackend"/> class.
    /// </summary>
    /// <param name="primaryRasterizer">Rasterizer used for coverage generation.</param>
    private DefaultDrawingBackend(IRasterizer primaryRasterizer)
    {
        Guard.NotNull(primaryRasterizer, nameof(primaryRasterizer));
        this.PrimaryRasterizer = primaryRasterizer;
    }

    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static DefaultDrawingBackend Instance { get; } = new(DefaultRasterizer.Instance);

    /// <summary>
    /// Gets the primary rasterizer used by this backend.
    /// </summary>
    public IRasterizer PrimaryRasterizer { get; }

    /// <summary>
    /// Creates a backend that uses the given rasterizer as the primary implementation.
    /// </summary>
    /// <param name="rasterizer">Primary rasterizer.</param>
    /// <returns>A backend instance.</returns>
    public static DefaultDrawingBackend Create(IRasterizer rasterizer)
    {
        Guard.NotNull(rasterizer, nameof(rasterizer));
        return ReferenceEquals(rasterizer, DefaultRasterizer.Instance) ? Instance : new DefaultDrawingBackend(rasterizer);
    }

    /// <inheritdoc />
    public void BeginCompositeSession<TPixel>(Configuration configuration, Buffer2DRegion<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));
    }

    /// <inheritdoc />
    public void EndCompositeSession<TPixel>(Configuration configuration, Buffer2DRegion<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));
    }

    /// <inheritdoc />
    public void FillPath<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions)
        where TPixel : unmanaged, IPixel<TPixel>
        => FillPath(
            configuration,
            target,
            path,
            brush,
            graphicsOptions,
            rasterizerOptions,
            configuration.MemoryAllocator,
            this.PrimaryRasterizer);

    /// <inheritdoc />
    public void FillRegion<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        Brush brush,
        GraphicsOptions graphicsOptions,
        Rectangle region)
        where TPixel : unmanaged, IPixel<TPixel>
        => FillRegionCore(configuration, target, brush, graphicsOptions, region);

    /// <inheritdoc />
    public bool SupportsCoverageComposition<TPixel>(Brush brush, in GraphicsOptions graphicsOptions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(brush, nameof(brush));
        _ = graphicsOptions;
        return true;
    }

    /// <inheritdoc />
    public DrawingCoverageHandle PrepareCoverage(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        CoveragePreparationMode preparationMode)
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(allocator, nameof(allocator));
        _ = preparationMode;

        Size size = rasterizerOptions.Interest.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return default;
        }

        Buffer2D<float> destination = allocator.Allocate2D<float>(size, AllocationOptions.Clean);

        CoverageRasterizationState state = new(destination);
        this.PrimaryRasterizer.Rasterize(path, rasterizerOptions, allocator, ref state, ProcessCoverageScanline);

        int handleId = Interlocked.Increment(ref this.nextCoverageHandleId);
        if (!this.preparedCoverage.TryAdd(handleId, destination))
        {
            destination.Dispose();
            throw new InvalidOperationException("Failed to cache prepared coverage.");
        }

        return new DrawingCoverageHandle(handleId);
    }

    /// <inheritdoc />
    public void CompositeCoverage<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        DrawingCoverageHandle coverageHandle,
        Point sourceOffset,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        Rectangle brushBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));
        Guard.NotNull(brush, nameof(brush));

        if (!coverageHandle.IsValid)
        {
            return;
        }

        if (!this.preparedCoverage.TryGetValue(coverageHandle.Value, out Buffer2D<float>? coverageMap))
        {
            throw new InvalidOperationException($"Prepared coverage handle '{coverageHandle.Value}' is not valid.");
        }

        if (!CoverageCompositor.TryGetCompositeRegions(
            target,
            coverageMap,
            sourceOffset,
            out Buffer2DRegion<TPixel> destinationRegion,
            out Buffer2DRegion<float> sourceRegion))
        {
            return;
        }

        CoverageCompositor.CompositeFloatCoverage(
            configuration,
            destinationRegion,
            sourceRegion,
            brush,
            graphicsOptions,
            brushBounds);
    }

    /// <inheritdoc />
    public void ReleaseCoverage(DrawingCoverageHandle coverageHandle)
    {
        if (!coverageHandle.IsValid)
        {
            return;
        }

        if (this.preparedCoverage.TryRemove(coverageHandle.Value, out Buffer2D<float>? coverage))
        {
            coverage.Dispose();
        }
    }

    /// <summary>
    /// Fills a path into a destination buffer using the configured rasterizer.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="destinationRegion">Destination pixel region.</param>
    /// <param name="path">The path to rasterize.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="rasterizerOptions">Rasterizer options.</param>
    /// <param name="allocator">Allocator for temporary data.</param>
    /// <param name="rasterizer">Rasterizer implementation.</param>
    private static void FillPath<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationRegion,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        IRasterizer rasterizer)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(destinationRegion.Buffer, nameof(destinationRegion));
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(allocator, nameof(allocator));
        Guard.NotNull(rasterizer, nameof(rasterizer));

        Rectangle destinationLocalBounds = new(0, 0, destinationRegion.Width, destinationRegion.Height);
        Rectangle interest = Rectangle.Intersect(rasterizerOptions.Interest, destinationLocalBounds);
        if (interest.Equals(Rectangle.Empty))
        {
            return;
        }

        RasterizerOptions clippedRasterizerOptions = rasterizerOptions;
        if (!interest.Equals(rasterizerOptions.Interest))
        {
            clippedRasterizerOptions = new RasterizerOptions(
                interest,
                rasterizerOptions.IntersectionRule,
                rasterizerOptions.RasterizationMode,
                rasterizerOptions.SamplingOrigin);
        }

        // Detect the common "opaque solid without blending" case and bypass brush sampling
        // for fully covered runs.
        TPixel solidBrushColor = default;
        bool isSolidBrushWithoutBlending = false;
        if (brush is SolidBrush solidBrush && graphicsOptions.IsOpaqueColorWithoutBlending(solidBrush.Color))
        {
            isSolidBrushWithoutBlending = true;
            solidBrushColor = solidBrush.Color.ToPixel<TPixel>();
        }

        int minX = interest.Left;
        using BrushApplicator<TPixel> applicator = brush.CreateApplicator(
            configuration,
            graphicsOptions,
            destinationRegion,
            path.Bounds);
        FillRasterizationState<TPixel> state = new(
            destinationRegion,
            applicator,
            minX,
            destinationRegion.Rectangle.X,
            destinationRegion.Rectangle.Y,
            isSolidBrushWithoutBlending,
            solidBrushColor);

        rasterizer.Rasterize(path, clippedRasterizerOptions, allocator, ref state, ProcessRasterizedScanline);
    }

    /// <summary>
    /// Fills a region in destination-local coordinates with the provided brush.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="destinationRegion">Destination pixel region.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    /// <param name="localRegion">Region to fill in destination-local coordinates.</param>
    private static void FillRegionCore<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationRegion,
        Brush brush,
        GraphicsOptions graphicsOptions,
        Rectangle localRegion)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(destinationRegion.Buffer, nameof(destinationRegion));
        Guard.NotNull(brush, nameof(brush));

        Rectangle destinationLocalBounds = new(0, 0, destinationRegion.Width, destinationRegion.Height);
        Rectangle clippedRegion = Rectangle.Intersect(destinationLocalBounds, localRegion);
        if (clippedRegion.Equals(Rectangle.Empty))
        {
            return;
        }

        Buffer2DRegion<TPixel> scopedDestination = destinationRegion.GetSubRegion(clippedRegion);

        if (brush is SolidBrush solidBrush && graphicsOptions.IsOpaqueColorWithoutBlending(solidBrush.Color))
        {
            TPixel solidBrushColor = solidBrush.Color.ToPixel<TPixel>();
            for (int y = 0; y < scopedDestination.Height; y++)
            {
                scopedDestination.DangerousGetRowSpan(y).Fill(solidBrushColor);
            }

            return;
        }

        RectangleF brushRegion = new(clippedRegion.X, clippedRegion.Y, clippedRegion.Width, clippedRegion.Height);
        using BrushApplicator<TPixel> applicator = brush.CreateApplicator(
            configuration,
            graphicsOptions,
            scopedDestination,
            brushRegion);
        using IMemoryOwner<float> amount = configuration.MemoryAllocator.Allocate<float>(scopedDestination.Width);
        Span<float> amountSpan = amount.Memory.Span;
        amountSpan.Fill(1F);

        int minX = scopedDestination.Rectangle.X;
        int minY = scopedDestination.Rectangle.Y;
        for (int localY = 0; localY < scopedDestination.Height; localY++)
        {
            applicator.Apply(amountSpan, minX, minY + localY);
        }
    }

    /// <summary>
    /// Dispatches rasterized coverage to either the generic brush path or the opaque-solid fast path.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="y">Destination row index.</param>
    /// <param name="scanline">Rasterized coverage row.</param>
    /// <param name="state">Callback state.</param>
    private static void ProcessRasterizedScanline<TPixel>(int y, Span<float> scanline, ref FillRasterizationState<TPixel> state)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int absoluteY = y + state.DestinationOffsetY;
        int absoluteMinX = state.MinX + state.DestinationOffsetX;
        if (state.IsSolidBrushWithoutBlending)
        {
            ApplyCoverageRunsForOpaqueSolidBrush(
                state.DestinationRegion,
                state.Applicator,
                scanline,
                absoluteMinX,
                absoluteY,
                state.SolidBrushColor);
        }
        else
        {
            ApplyPositiveCoverageRuns(state.Applicator, scanline, absoluteMinX, absoluteY);
        }
    }

    /// <summary>
    /// Copies one rasterized coverage row into the destination coverage buffer.
    /// </summary>
    /// <param name="y">Destination row index.</param>
    /// <param name="scanline">Source coverage row.</param>
    /// <param name="state">Callback state containing destination storage.</param>
    private static void ProcessCoverageScanline(int y, Span<float> scanline, ref CoverageRasterizationState state)
    {
        Span<float> destination = state.Buffer.DangerousGetRowSpan(y);
        scanline.CopyTo(destination);
    }

    /// <summary>
    /// Applies a brush to contiguous positive-coverage runs on a scanline.
    /// </summary>
    /// <remarks>
    /// The rasterizer has already resolved the fill rule (NonZero or EvenOdd) into per-pixel
    /// coverage values. This method simply consumes the resulting positive runs.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="applicator">Brush applicator.</param>
    /// <param name="scanline">Coverage values for one row.</param>
    /// <param name="minX">Absolute X of scanline index 0.</param>
    /// <param name="y">Destination row index.</param>
    private static void ApplyPositiveCoverageRuns<TPixel>(BrushApplicator<TPixel> applicator, Span<float> scanline, int minX, int y)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int i = 0;
        while (i < scanline.Length)
        {
            while (i < scanline.Length && scanline[i] <= 0F)
            {
                i++;
            }

            int runStart = i;
            while (i < scanline.Length && scanline[i] > 0F)
            {
                i++;
            }

            int runLength = i - runStart;
            if (runLength > 0)
            {
                // Apply only the positive-coverage run. This avoids invoking brush logic
                // for fully transparent gaps.
                applicator.Apply(scanline.Slice(runStart, runLength), minX + runStart, y);
            }
        }
    }

    /// <summary>
    /// Applies coverage using a mixed strategy for opaque solid brushes.
    /// </summary>
    /// <remarks>
    /// Semi-transparent edges still go through brush blending, but fully covered interior runs
    /// are written directly with <paramref name="solidBrushColor"/>.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="destinationRegion">Destination pixel region.</param>
    /// <param name="applicator">Brush applicator for non-opaque segments.</param>
    /// <param name="scanline">Coverage values for one row.</param>
    /// <param name="minX">Absolute X of scanline index 0.</param>
    /// <param name="y">Destination row index.</param>
    /// <param name="solidBrushColor">Pre-converted solid color for direct writes.</param>
    private static void ApplyCoverageRunsForOpaqueSolidBrush<TPixel>(
        Buffer2DRegion<TPixel> destinationRegion,
        BrushApplicator<TPixel> applicator,
        Span<float> scanline,
        int minX,
        int y,
        TPixel solidBrushColor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int localY = y - destinationRegion.Rectangle.Y;
        int localX = minX - destinationRegion.Rectangle.X;
        Span<TPixel> destinationRow = destinationRegion.DangerousGetRowSpan(localY).Slice(localX, scanline.Length);
        int i = 0;

        while (i < scanline.Length)
        {
            while (i < scanline.Length && scanline[i] <= 0F)
            {
                i++;
            }

            int runStart = i;
            while (i < scanline.Length && scanline[i] > 0F)
            {
                i++;
            }

            int runEnd = i;
            if (runEnd <= runStart)
            {
                continue;
            }

            // Leading partially-covered segment.
            int opaqueStart = runStart;
            while (opaqueStart < runEnd && scanline[opaqueStart] < 1F)
            {
                opaqueStart++;
            }

            if (opaqueStart > runStart)
            {
                int prefixLength = opaqueStart - runStart;
                applicator.Apply(scanline.Slice(runStart, prefixLength), minX + runStart, y);
            }

            // Trailing partially-covered segment.
            int opaqueEnd = runEnd;
            while (opaqueEnd > opaqueStart && scanline[opaqueEnd - 1] < 1F)
            {
                opaqueEnd--;
            }

            // Fully covered interior can skip blending entirely.
            if (opaqueEnd > opaqueStart)
            {
                destinationRow[opaqueStart..opaqueEnd].Fill(solidBrushColor);
            }

            if (runEnd > opaqueEnd)
            {
                int suffixLength = runEnd - opaqueEnd;
                applicator.Apply(scanline.Slice(opaqueEnd, suffixLength), minX + opaqueEnd, y);
            }
        }
    }

    /// <summary>
    /// Callback state used while writing coverage maps.
    /// </summary>
    private readonly struct CoverageRasterizationState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageRasterizationState"/> struct.
        /// </summary>
        /// <param name="buffer">Destination coverage buffer.</param>
        public CoverageRasterizationState(Buffer2D<float> buffer) => this.Buffer = buffer;

        /// <summary>
        /// Gets the destination coverage buffer.
        /// </summary>
        public Buffer2D<float> Buffer { get; }
    }

    /// <summary>
    /// Callback state used while filling into an image frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct FillRasterizationState<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FillRasterizationState{TPixel}"/> struct.
        /// </summary>
        /// <param name="destinationRegion">Destination pixel region.</param>
        /// <param name="applicator">Brush applicator for blended segments.</param>
        /// <param name="minX">Local X corresponding to scanline index 0.</param>
        /// <param name="destinationOffsetX">Destination region X offset in target coordinates.</param>
        /// <param name="destinationOffsetY">Destination region Y offset in target coordinates.</param>
        /// <param name="isSolidBrushWithoutBlending">
        /// Indicates whether opaque solid fast-path writes are allowed.
        /// </param>
        /// <param name="solidBrushColor">Pre-converted opaque solid color.</param>
        public FillRasterizationState(
            Buffer2DRegion<TPixel> destinationRegion,
            BrushApplicator<TPixel> applicator,
            int minX,
            int destinationOffsetX,
            int destinationOffsetY,
            bool isSolidBrushWithoutBlending,
            TPixel solidBrushColor)
        {
            this.DestinationRegion = destinationRegion;
            this.Applicator = applicator;
            this.MinX = minX;
            this.DestinationOffsetX = destinationOffsetX;
            this.DestinationOffsetY = destinationOffsetY;
            this.IsSolidBrushWithoutBlending = isSolidBrushWithoutBlending;
            this.SolidBrushColor = solidBrushColor;
        }

        /// <summary>
        /// Gets the destination pixel region.
        /// </summary>
        public Buffer2DRegion<TPixel> DestinationRegion { get; }

        /// <summary>
        /// Gets the brush applicator used for blended segments.
        /// </summary>
        public BrushApplicator<TPixel> Applicator { get; }

        /// <summary>
        /// Gets the local X origin of the current scanline.
        /// </summary>
        public int MinX { get; }

        /// <summary>
        /// Gets the destination region X offset in target coordinates.
        /// </summary>
        public int DestinationOffsetX { get; }

        /// <summary>
        /// Gets the destination region Y offset in target coordinates.
        /// </summary>
        public int DestinationOffsetY { get; }

        /// <summary>
        /// Gets a value indicating whether opaque interior runs can be direct-filled.
        /// </summary>
        public bool IsSolidBrushWithoutBlending { get; }

        /// <summary>
        /// Gets the pre-converted solid color used by the opaque fast path.
        /// </summary>
        public TPixel SolidBrushColor { get; }
    }
}
