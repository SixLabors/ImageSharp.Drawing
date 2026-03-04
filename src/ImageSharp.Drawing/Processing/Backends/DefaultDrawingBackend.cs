// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
/// <remarks>
/// <para>
/// This backend provides the reference CPU implementation for composition behavior.
/// </para>
/// <para>
/// Flush execution is intentionally split:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <see cref="FlushCompositions{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionScene)"/>
/// converts scene commands into prepared batches with <see cref="CompositionScenePlanner"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="FlushPreparedBatch{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionBatch)"/>
/// rasterizes shared coverage scanlines per batch and applies brushes in original command order.
/// </description>
/// </item>
/// </list>
/// </remarks>
internal sealed class DefaultDrawingBackend : IDrawingBackend
{
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
    public bool IsCompositionBrushSupported<TPixel>(Brush brush)
        where TPixel : unmanaged, IPixel<TPixel>
        => true;

    /// <inheritdoc />
    public void FillPath<TPixel>(
        ICanvasFrame<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        DrawingCanvasBatcher<TPixel> batcher)
        where TPixel : unmanaged, IPixel<TPixel>
        => batcher.AddComposition(
            CompositionCommand.Create(path, brush, graphicsOptions, rasterizerOptions, target.Bounds.Location));

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionScene.Commands.Count == 0)
        {
            return;
        }

        List<CompositionBatch> preparedBatches = CompositionScenePlanner.CreatePreparedBatches(
            compositionScene.Commands,
            target.Bounds);

        for (int i = 0; i < preparedBatches.Count; i++)
        {
            this.FlushPreparedBatch(configuration, target, preparedBatches[i]);
        }
    }

    /// <inheritdoc />
    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        [NotNullWhen(true)] out Image<TPixel>? image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));

        // CPU backend readback is available only when the target exposes CPU pixels.
        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            image = null;
            return false;
        }

        // Clamp the request to the target region to avoid out-of-range row slicing.
        Rectangle clipped = Rectangle.Intersect(
            new Rectangle(0, 0, sourceRegion.Width, sourceRegion.Height),
            sourceRectangle);

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            image = null;
            return false;
        }

        // Build a tightly packed temporary image for downstream processing operations.
        image = new(configuration, clipped.Width, clipped.Height);
        Buffer2D<TPixel> destination = image.Frames.RootFrame.PixelBuffer;
        for (int y = 0; y < clipped.Height; y++)
        {
            sourceRegion.DangerousGetRowSpan(clipped.Y + y)
                .Slice(clipped.X, clipped.Width)
                .CopyTo(destination.DangerousGetRowSpan(y));
        }

        return true;
    }

    /// <summary>
    /// Executes one prepared batch on the CPU.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The destination frame.</param>
    /// <param name="compositionBatch">
    /// One prepared batch where all commands share the same coverage definition and differ only by brush/options.
    /// </param>
    /// <remarks>
    /// This method is intentionally reusable so GPU backends can delegate unsupported batches
    /// without reconstructing a full <see cref="CompositionScene"/>.
    /// </remarks>
    internal void FlushPreparedBatch<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionBatch.Commands.Count == 0)
        {
            return;
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible frame targets.");
        }

        CompositionCoverageDefinition definition = compositionBatch.Definition;
        Rectangle destinationBounds = destinationFrame.Rectangle;
        IReadOnlyList<PreparedCompositionCommand> commands = compositionBatch.Commands;
        int commandCount = commands.Count;
        BrushApplicator<TPixel>[] applicators = new BrushApplicator<TPixel>[commandCount];
        try
        {
            for (int i = 0; i < commandCount; i++)
            {
                PreparedCompositionCommand command = commands[i];
                Buffer2DRegion<TPixel> commandRegion = destinationFrame.GetSubRegion(command.DestinationRegion);
                applicators[i] = command.Brush.CreateApplicator(
                    configuration,
                    command.GraphicsOptions,
                    commandRegion,
                    command.BrushBounds);
            }

            // Stream composition directly from rasterizer scanlines so we do not allocate
            // and then re-read an intermediate coverage map.
            RowOperation<TPixel> operation = new(
                commands,
                applicators,
                destinationBounds,
                definition.RasterizerOptions.Interest.Top);
            this.PrimaryRasterizer.Rasterize(
                definition.Path,
                definition.RasterizerOptions,
                configuration.MemoryAllocator,
                ref operation,
                static (int y, Span<float> scanline, ref RowOperation<TPixel> callbackState) =>
                    callbackState.InvokeScanline(y, scanline));
        }
        finally
        {
            foreach (BrushApplicator<TPixel>? applicator in applicators)
            {
                applicator?.Dispose();
            }
        }
    }

    private readonly struct RowOperation<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly IReadOnlyList<PreparedCompositionCommand> commands;
        private readonly BrushApplicator<TPixel>[] applicators;
        private readonly Rectangle destinationBounds;
        private readonly int coverageTop;

        public RowOperation(
            IReadOnlyList<PreparedCompositionCommand> commands,
            BrushApplicator<TPixel>[] applicators,
            Rectangle destinationBounds,
            int coverageTop)
        {
            this.commands = commands;
            this.applicators = applicators;
            this.destinationBounds = destinationBounds;
            this.coverageTop = coverageTop;
        }

        public void InvokeScanline(int y, Span<float> scanline)
        {
            int sourceY = y - this.coverageTop;
            for (int i = 0; i < this.commands.Count; i++)
            {
                PreparedCompositionCommand command = this.commands[i];
                int commandY = sourceY - command.SourceOffset.Y;
                if ((uint)commandY >= (uint)command.DestinationRegion.Height)
                {
                    continue;
                }

                int destinationX = this.destinationBounds.X + command.DestinationRegion.X;
                int destinationY = this.destinationBounds.Y + command.DestinationRegion.Y + commandY;
                int sourceStartX = command.SourceOffset.X;
                Span<float> rowSlice = scanline.Slice(sourceStartX, command.DestinationRegion.Width);
                ApplyCoverageSpans(this.applicators[i], rowSlice, destinationX, destinationY);
            }
        }

        /// <summary>
        /// Applies only contiguous non-zero coverage spans for a scanline.
        /// </summary>
        /// <param name="applicator">Brush applicator used to composite pixels.</param>
        /// <param name="coverage">Scanline coverage values for the current command row.</param>
        /// <param name="destinationX">Destination x coordinate for the start of <paramref name="coverage"/>.</param>
        /// <param name="destinationY">Destination y coordinate for the scanline.</param>
        private static void ApplyCoverageSpans(
            BrushApplicator<TPixel> applicator,
            Span<float> coverage,
            int destinationX,
            int destinationY)
        {
            // Use SIMD path when available and the span is large enough to amortize setup.
            if (Vector.IsHardwareAccelerated && coverage.Length >= (Vector<float>.Count * 2))
            {
                ApplyCoverageSpansSimd(applicator, coverage, destinationX, destinationY);
                return;
            }

            ApplyCoverageSpansScalar(applicator, coverage, destinationX, destinationY);
        }

        /// <summary>
        /// Applies contiguous non-zero coverage spans using SIMD-accelerated zero/non-zero chunk checks.
        /// </summary>
        /// <param name="applicator">Brush applicator used to composite pixels.</param>
        /// <param name="coverage">Scanline coverage values for the current command row.</param>
        /// <param name="destinationX">Destination x coordinate for the start of <paramref name="coverage"/>.</param>
        /// <param name="destinationY">Destination y coordinate for the scanline.</param>
        private static void ApplyCoverageSpansSimd(
            BrushApplicator<TPixel> applicator,
            Span<float> coverage,
            int destinationX,
            int destinationY)
        {
            int i = 0;
            int n = coverage.Length;
            int width = Vector<float>.Count;
            Vector<float> zero = Vector<float>.Zero;

            while (i < n)
            {
                // Phase 1: skip fully-zero SIMD blocks.
                while (i <= n - width)
                {
                    Vector<float> v = new(coverage.Slice(i, width));
                    if (!Vector.EqualsAll(v, zero))
                    {
                        break;
                    }

                    i += width;
                }

                while (i < n && coverage[i] == 0F)
                {
                    i++;
                }

                if (i >= n)
                {
                    return;
                }

                int runStart = i;

                // Phase 2: advance across fully non-zero SIMD blocks.
                while (i <= n - width)
                {
                    Vector<float> v = new(coverage.Slice(i, width));
                    Vector<int> eqZero = Vector.Equals(v, zero);
                    if (!Vector.EqualsAll(eqZero, Vector<int>.Zero))
                    {
                        break;
                    }

                    i += width;
                }

                while (i < n && coverage[i] != 0F)
                {
                    i++;
                }

                // Apply exactly one contiguous non-zero run.
                applicator.Apply(coverage[runStart..i], destinationX + runStart, destinationY);
            }
        }

        /// <summary>
        /// Applies contiguous non-zero coverage spans using a scalar scan.
        /// </summary>
        /// <param name="applicator">Brush applicator used to composite pixels.</param>
        /// <param name="coverage">Scanline coverage values for the current command row.</param>
        /// <param name="destinationX">Destination x coordinate for the start of <paramref name="coverage"/>.</param>
        /// <param name="destinationY">Destination y coordinate for the scanline.</param>
        private static void ApplyCoverageSpansScalar(
            BrushApplicator<TPixel> applicator,
            Span<float> coverage,
            int destinationX,
            int destinationY)
        {
            // Track the start of a contiguous non-zero coverage run.
            int runStart = -1;
            for (int i = 0; i < coverage.Length; i++)
            {
                if (coverage[i] > 0F)
                {
                    // Enter a new run when transitioning from zero to non-zero coverage.
                    if (runStart < 0)
                    {
                        runStart = i;
                    }
                }
                else if (runStart >= 0)
                {
                    // Coverage returned to zero: apply the finished run only.
                    applicator.Apply(coverage[runStart..i], destinationX + runStart, destinationY);
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                // Flush trailing run that reaches end-of-scanline.
                applicator.Apply(coverage[runStart..], destinationX + runStart, destinationY);
            }
        }
    }
}
