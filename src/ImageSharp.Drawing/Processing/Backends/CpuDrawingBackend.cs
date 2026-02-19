// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Default CPU drawing backend.
/// </summary>
/// <remarks>
/// This backend keeps all CPU-specific scanline handling internal so higher-level processors
/// can remain backend-agnostic.
/// </remarks>
internal sealed class CpuDrawingBackend : IDrawingBackend
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CpuDrawingBackend"/> class.
    /// </summary>
    /// <param name="primaryRasterizer">Rasterizer used for CPU coverage generation.</param>
    private CpuDrawingBackend(IRasterizer primaryRasterizer)
    {
        Guard.NotNull(primaryRasterizer, nameof(primaryRasterizer));
        this.PrimaryRasterizer = primaryRasterizer;
    }

    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static CpuDrawingBackend Instance { get; } = new(DefaultRasterizer.Instance);

    /// <summary>
    /// Gets the primary rasterizer used by this backend.
    /// </summary>
    public IRasterizer PrimaryRasterizer { get; }

    /// <summary>
    /// Creates a backend that uses the given rasterizer as the primary implementation.
    /// </summary>
    /// <param name="rasterizer">Primary rasterizer.</param>
    /// <returns>A backend instance.</returns>
    public static CpuDrawingBackend Create(IRasterizer rasterizer)
    {
        Guard.NotNull(rasterizer, nameof(rasterizer));
        return ReferenceEquals(rasterizer, DefaultRasterizer.Instance) ? Instance : new CpuDrawingBackend(rasterizer);
    }

    /// <inheritdoc />
    public void FillPath<TPixel>(
        Configuration configuration,
        ImageFrame<TPixel> source,
        IPath path,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle brushBounds,
        MemoryAllocator allocator)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(allocator, nameof(allocator));

        Rectangle interest = rasterizerOptions.Interest;
        if (interest.Equals(Rectangle.Empty))
        {
            return;
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
        using BrushApplicator<TPixel> applicator = brush.CreateApplicator(configuration, graphicsOptions, source, brushBounds);
        FillRasterizationState<TPixel> state = new(
            source,
            applicator,
            minX,
            isSolidBrushWithoutBlending,
            solidBrushColor);

        this.PrimaryRasterizer.Rasterize(path, rasterizerOptions, allocator, ref state, ProcessRasterizedScanline);
    }

    /// <inheritdoc />
    public void RasterizeCoverage(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        Buffer2D<float> destination)
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(allocator, nameof(allocator));
        Guard.NotNull(destination, nameof(destination));

        CoverageRasterizationState state = new(destination);
        this.PrimaryRasterizer.Rasterize(path, rasterizerOptions, allocator, ref state, ProcessCoverageScanline);
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
    /// Dispatches rasterized coverage to either the generic brush path or the opaque-solid fast path.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="y">Destination row index.</param>
    /// <param name="scanline">Rasterized coverage row.</param>
    /// <param name="state">Callback state.</param>
    private static void ProcessRasterizedScanline<TPixel>(int y, Span<float> scanline, ref FillRasterizationState<TPixel> state)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (state.IsSolidBrushWithoutBlending)
        {
            ApplyCoverageRunsForOpaqueSolidBrush(state.Source, state.Applicator, scanline, state.MinX, y, state.SolidBrushColor);
        }
        else
        {
            ApplyPositiveCoverageRuns(state.Applicator, scanline, state.MinX, y);
        }
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
    /// <param name="source">Destination frame.</param>
    /// <param name="applicator">Brush applicator for non-opaque segments.</param>
    /// <param name="scanline">Coverage values for one row.</param>
    /// <param name="minX">Absolute X of scanline index 0.</param>
    /// <param name="y">Destination row index.</param>
    /// <param name="solidBrushColor">Pre-converted solid color for direct writes.</param>
    private static void ApplyCoverageRunsForOpaqueSolidBrush<TPixel>(
        ImageFrame<TPixel> source,
        BrushApplicator<TPixel> applicator,
        Span<float> scanline,
        int minX,
        int y,
        TPixel solidBrushColor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Span<TPixel> destinationRow = source.PixelBuffer.DangerousGetRowSpan(y).Slice(minX, scanline.Length);
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
        /// <param name="source">Destination frame.</param>
        /// <param name="applicator">Brush applicator for blended segments.</param>
        /// <param name="minX">Absolute X corresponding to scanline index 0.</param>
        /// <param name="isSolidBrushWithoutBlending">
        /// Indicates whether opaque solid fast-path writes are allowed.
        /// </param>
        /// <param name="solidBrushColor">Pre-converted opaque solid color.</param>
        public FillRasterizationState(
            ImageFrame<TPixel> source,
            BrushApplicator<TPixel> applicator,
            int minX,
            bool isSolidBrushWithoutBlending,
            TPixel solidBrushColor)
        {
            this.Source = source;
            this.Applicator = applicator;
            this.MinX = minX;
            this.IsSolidBrushWithoutBlending = isSolidBrushWithoutBlending;
            this.SolidBrushColor = solidBrushColor;
        }

        /// <summary>
        /// Gets the destination frame.
        /// </summary>
        public ImageFrame<TPixel> Source { get; }

        /// <summary>
        /// Gets the brush applicator used for blended segments.
        /// </summary>
        public BrushApplicator<TPixel> Applicator { get; }

        /// <summary>
        /// Gets the absolute X origin of the current scanline.
        /// </summary>
        public int MinX { get; }

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
