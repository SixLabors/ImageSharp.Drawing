// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
public sealed partial class DefaultDrawingBackend
{
    /// <summary>
    /// Adapts rasterizer coverage callbacks into brush application against the active band target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct FillCoverageRowHandler<TPixel> : IRasterizerCoverageRowHandler
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly BrushRenderer<TPixel> renderer;
        private readonly BandTarget<TPixel> target;
        private readonly DrawingClipState clipState;
        private readonly PreparedPathClipState? pathClipState;
        private readonly Point destinationOffset;
        private readonly BrushWorkspace<TPixel> brushWorkspace;
        private readonly WorkerState<TPixel> workerState;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillCoverageRowHandler{TPixel}"/> struct.
        /// </summary>
        /// <param name="renderer">The brush renderer that will consume emitted coverage spans.</param>
        /// <param name="target">The active band target being rendered.</param>
        /// <param name="clipState">The exact clip state captured with the retained scene item.</param>
        /// <param name="pathClipState">The retained path clip raster data captured with the retained scene item.</param>
        /// <param name="destinationOffset">The destination offset used to place clip descriptors.</param>
        /// <param name="brushWorkspace">The worker-local brush workspace.</param>
        /// <param name="workerState">The worker-local execution state.</param>
        public FillCoverageRowHandler(
            BrushRenderer<TPixel> renderer,
            BandTarget<TPixel> target,
            DrawingClipState clipState,
            PreparedPathClipState? pathClipState,
            Point destinationOffset,
            BrushWorkspace<TPixel> brushWorkspace,
            WorkerState<TPixel> workerState)
        {
            this.renderer = renderer;
            this.target = target;
            this.clipState = clipState;
            this.pathClipState = pathClipState;
            this.destinationOffset = destinationOffset;
            this.brushWorkspace = brushWorkspace;
            this.workerState = workerState;
        }

        /// <summary>
        /// Applies one emitted coverage span to the active destination band.
        /// </summary>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of the coverage span.</param>
        /// <param name="coverage">The emitted coverage values.</param>
        public void Handle(int y, int startX, Span<float> coverage)
        {
            int localY = y - this.target.AbsoluteTop;
            if ((uint)localY >= (uint)this.target.Region.Height)
            {
                return;
            }

            int clipStartX = Math.Max(startX, this.target.AbsoluteLeft);
            int clipEndX = Math.Min(startX + coverage.Length, this.target.AbsoluteLeft + this.target.Region.Width);
            if (clipEndX <= clipStartX)
            {
                return;
            }

            // The rasterizer emits absolute coordinates; clip them once here so the brush
            // renderer can operate against a tight destination span with no extra bounds work.
            int coverageOffset = clipStartX - startX;
            int clippedLength = clipEndX - clipStartX;
            Span<TPixel> destinationRow = this.target.Region
                .DangerousGetRowSpan(localY)
                .Slice(clipStartX - this.target.AbsoluteLeft, clippedLength);

            Span<float> clippedCoverage = coverage.Slice(coverageOffset, clippedLength);
            this.ApplyClipDescriptors(0, y, clipStartX, destinationRow, clippedCoverage);
        }

        /// <summary>
        /// Applies the retained clip stack to a coverage span, then forwards surviving coverage to the brush renderer.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyClipDescriptors(
            int clipIndex,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            if (coverage.Length == 0)
            {
                return;
            }

            if (clipIndex == this.clipState.Count)
            {
                // Clip descriptors are applied recursively by narrowing or scaling the active span.
                // Once the stack is exhausted, the brush sees only pixels that survived every clip.
                this.renderer.Apply(destination, coverage, startX, y, this.brushWorkspace);
                return;
            }

            DrawingClipDescriptor descriptor = this.clipState.GetDescriptor(clipIndex);
            switch (descriptor.Kind)
            {
                case DrawingClipKind.Rectangle:
                    this.ApplyRectangleClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;

                case DrawingClipKind.IntegerRegion:
                    this.ApplyIntegerRegionClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;

                case DrawingClipKind.Region:
                    this.ApplyRegionClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;

                default:
                    this.ApplyPathClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;
            }
        }

        /// <summary>
        /// Applies one rectangle clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            // Clip descriptors are recorded in canvas space; the item's destination offset
            // anchors them into the space the rasterizer emits so clips track the drawn geometry.
            RectangleF rectangle = descriptor.Rectangle;
            rectangle.Offset(this.destinationOffset.X, this.destinationOffset.Y);

            if (descriptor.Operation == ClipOperation.Difference)
            {
                this.ApplyRectangleDifference(clipIndex, descriptor, rectangle, y, startX, destination, coverage);
                return;
            }

            this.ApplyRectangleIntersection(clipIndex, descriptor, rectangle, y, startX, destination, coverage);
        }

        /// <summary>
        /// Applies an intersecting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleIntersection(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            float yCoverage = GetAxisCoverage(y, rectangle.Top, rectangle.Bottom, descriptor.EdgeMode, descriptor.AntialiasThreshold);
            if (yCoverage <= 0F)
            {
                return;
            }

            int clipStart = Math.Max(startX, (int)MathF.Floor(rectangle.Left));
            int clipEnd = Math.Min(startX + coverage.Length, (int)MathF.Ceiling(rectangle.Right));
            if (clipEnd <= clipStart)
            {
                return;
            }

            this.ApplyRectangleInterior(clipIndex, descriptor, rectangle, yCoverage, y, startX, clipStart, clipEnd, destination, coverage);
        }

        /// <summary>
        /// Applies a subtracting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleDifference(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            float yCoverage = GetAxisCoverage(y, rectangle.Top, rectangle.Bottom, descriptor.EdgeMode, descriptor.AntialiasThreshold);
            if (yCoverage <= 0F)
            {
                this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                return;
            }

            int spanEnd = startX + coverage.Length;
            int clipStart = Math.Max(startX, (int)MathF.Floor(rectangle.Left));
            int clipEnd = Math.Min(spanEnd, (int)MathF.Ceiling(rectangle.Right));
            if (clipEnd <= clipStart)
            {
                this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                return;
            }

            // Difference clips preserve the span outside the rectangle and invert only the
            // overlapping interior, so the outside ranges can advance to the next clip unchanged.
            this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination[..(clipStart - startX)], coverage[..(clipStart - startX)]);
            this.ApplyRectangleDifferenceInterior(clipIndex, descriptor, rectangle, yCoverage, y, startX, clipStart, clipEnd, destination, coverage);
            this.ApplyClipDescriptors(clipIndex + 1, y, clipEnd, destination[(clipEnd - startX)..], coverage[(clipEnd - startX)..]);
        }

        /// <summary>
        /// Applies the interior of an intersecting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="yCoverage">The vertical coverage contribution for the current row.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="clipStart">The first absolute column overlapped by the rectangle.</param>
        /// <param name="clipEnd">The exclusive absolute column where rectangle overlap ends.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleInterior(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            float yCoverage,
            int y,
            int spanStart,
            int clipStart,
            int clipEnd,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            int x = clipStart;
            while (x < clipEnd)
            {
                float xCoverage = GetAxisCoverage(x, rectangle.Left, rectangle.Right, descriptor.EdgeMode, descriptor.AntialiasThreshold);
                int runStart = x++;
                float multiplier = xCoverage * yCoverage;

                // Rectangle edge coverage is constant across interior pixels and only changes
                // at the left/right edge pixels, so collapse adjacent equal-coverage columns.
                while (x < clipEnd && GetAxisCoverage(x, rectangle.Left, rectangle.Right, descriptor.EdgeMode, descriptor.AntialiasThreshold) == xCoverage)
                {
                    x++;
                }

                this.ApplyCoverageRun(clipIndex + 1, y, runStart, x, spanStart, multiplier, destination, coverage);
            }
        }

        /// <summary>
        /// Applies the interior of a subtracting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="yCoverage">The vertical coverage contribution for the current row.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="clipStart">The first absolute column overlapped by the rectangle.</param>
        /// <param name="clipEnd">The exclusive absolute column where rectangle overlap ends.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleDifferenceInterior(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            float yCoverage,
            int y,
            int spanStart,
            int clipStart,
            int clipEnd,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            int x = clipStart;
            while (x < clipEnd)
            {
                float xCoverage = GetAxisCoverage(x, rectangle.Left, rectangle.Right, descriptor.EdgeMode, descriptor.AntialiasThreshold);
                int runStart = x++;
                float multiplier = 1F - (xCoverage * yCoverage);

                // Subtraction uses the inverse coverage of the rectangle, but the same run
                // coalescing applies: edge pixels differ, interior pixels share one value.
                while (x < clipEnd && GetAxisCoverage(x, rectangle.Left, rectangle.Right, descriptor.EdgeMode, descriptor.AntialiasThreshold) == xCoverage)
                {
                    x++;
                }

                this.ApplyCoverageRun(clipIndex + 1, y, runStart, x, spanStart, multiplier, destination, coverage);
            }
        }

        /// <summary>
        /// Applies an integer-region clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The integer-region clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyIntegerRegionClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            IReadOnlyList<Rectangle> rectangles = descriptor.IntegerRectangles;
            if (descriptor.Operation == ClipOperation.Difference)
            {
                int cursor = startX;
                int endX = startX + coverage.Length;

                // Integer regions are hard-edged, so difference clips can split the span into
                // retained gaps without allocating or rasterizing a mask.
                for (int i = 0; i < rectangles.Count && cursor < endX; i++)
                {
                    Rectangle rectangle = rectangles[i];
                    rectangle.Offset(this.destinationOffset);
                    if ((uint)(y - rectangle.Top) >= (uint)rectangle.Height || rectangle.Right <= cursor)
                    {
                        continue;
                    }

                    if (rectangle.Left > cursor)
                    {
                        int runEnd = Math.Min(rectangle.Left, endX);
                        this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, runEnd, destination, coverage);
                    }

                    cursor = Math.Max(cursor, rectangle.Right);
                }

                this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, endX, destination, coverage);
                return;
            }

            for (int i = 0; i < rectangles.Count; i++)
            {
                Rectangle rectangle = rectangles[i];
                rectangle.Offset(this.destinationOffset);
                if ((uint)(y - rectangle.Top) >= (uint)rectangle.Height)
                {
                    continue;
                }

                int runStart = Math.Max(startX, rectangle.Left);
                int runEnd = Math.Min(startX + coverage.Length, rectangle.Right);
                this.ApplyIntegerRun(clipIndex + 1, y, runStart, startX, runEnd, destination, coverage);
            }
        }

        /// <summary>
        /// Applies a floating-point region clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The region clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRegionClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            IReadOnlyList<RectangleF> rectangles = descriptor.Rectangles;
            if (descriptor.Operation == ClipOperation.Difference)
            {
                this.ApplyFloatingRegionDifference(clipIndex, descriptor, rectangles, y, startX, destination, coverage);
                return;
            }

            for (int i = 0; i < rectangles.Count; i++)
            {
                RectangleF rectangle = rectangles[i];
                rectangle.Offset(this.destinationOffset.X, this.destinationOffset.Y);
                this.ApplyRectangleIntersection(clipIndex, descriptor, rectangle, y, startX, destination, coverage);
            }
        }

        /// <summary>
        /// Applies a subtracting floating-point region clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The region clip descriptor.</param>
        /// <param name="rectangles">The region rectangles in descriptor order.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyFloatingRegionDifference(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            IReadOnlyList<RectangleF> rectangles,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            int cursor = startX;
            int endX = startX + coverage.Length;

            // The cursor marks the prefix already emitted to the next clip. This avoids
            // revisiting or re-emitting spans when a region contributes multiple rectangles.
            for (int i = 0; i < rectangles.Count && cursor < endX; i++)
            {
                RectangleF rectangle = rectangles[i];
                rectangle.Offset(this.destinationOffset.X, this.destinationOffset.Y);

                float yCoverage = GetAxisCoverage(y, rectangle.Top, rectangle.Bottom, descriptor.EdgeMode, descriptor.AntialiasThreshold);
                if (yCoverage <= 0F)
                {
                    continue;
                }

                int clipStart = Math.Max(startX, (int)MathF.Floor(rectangle.Left));
                int clipEnd = Math.Min(endX, (int)MathF.Ceiling(rectangle.Right));
                if (clipEnd <= cursor)
                {
                    continue;
                }

                if (clipStart > cursor)
                {
                    int runEnd = Math.Min(clipStart, endX);
                    this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, runEnd, destination, coverage);
                }

                this.ApplyRectangleDifferenceInterior(clipIndex, descriptor, rectangle, yCoverage, y, startX, Math.Max(cursor, clipStart), clipEnd, destination, coverage);
                cursor = Math.Max(cursor, clipEnd);
            }

            this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, endX, destination, coverage);
        }

        /// <summary>
        /// Applies a retained path clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The path clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyPathClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            // A missing or empty clip raster means the clip path contributes no coverage here:
            // a Difference clip removes nothing so the subject span passes through unchanged,
            // while an intersecting clip keeps nothing so the span is dropped entirely.
            DefaultRasterizer.RasterizableGeometry? rasterizable = this.pathClipState?.GetRasterizable(clipIndex);
            if (rasterizable is null)
            {
                if (descriptor.Operation == ClipOperation.Difference)
                {
                    this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                }

                return;
            }

            int rowBandIndex = y / DefaultRasterizer.DefaultTileHeight;
            int localRowIndex = rowBandIndex - rasterizable.FirstRowBandIndex;
            if ((uint)localRowIndex >= (uint)rasterizable.RowBandCount || !rasterizable.HasCoverage(localRowIndex))
            {
                if (descriptor.Operation == ClipOperation.Difference)
                {
                    this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                }

                return;
            }

            Span<float> clipCoverage = this.workerState.GetOrCreatePathClipCoverage(coverage.Length);
            clipCoverage = clipCoverage[..coverage.Length];
            clipCoverage.Fill(descriptor.Operation == ClipOperation.Difference ? 1F : 0F);

            DefaultRasterizer.RasterizableItem item = new(rasterizable, localRowIndex);
            DefaultRasterizer.RasterizableBandInfo bandInfo = rasterizable.GetBandInfo(localRowIndex);
            DefaultRasterizer.WorkerScratch scratch = this.workerState.GetOrCreatePathClipScratch(rasterizable.Width);
            DefaultRasterizer.Context context = scratch.CreateContext(
                bandInfo.IntersectionRule,
                bandInfo.RasterizationMode,
                bandInfo.AntialiasThreshold);

            PathClipCoverageRowHandler<TPixel> rowHandler = new(
                this.workerState,
                descriptor.Operation,
                y,
                startX,
                coverage.Length);

            // Path clips use the same retained rasterizer as fills. The clip row is materialized
            // into worker-local coverage only for the subject span currently being painted, so
            // rectangle and region clips stay on their cheaper span-slicing paths.
            DefaultRasterizer.ExecuteRasterizableItem(
                ref context,
                in item,
                in bandInfo,
                scratch.Scanline,
                ref rowHandler);

            int spanEnd = startX + coverage.Length;
            int runStart = startX;
            float runCoverage = clipCoverage[0];
            for (int x = startX + 1; x < spanEnd; x++)
            {
                float pixelCoverage = clipCoverage[x - startX];
                if (pixelCoverage == runCoverage)
                {
                    continue;
                }

                // Coalesce equal clip coverage into runs so deeper clips and brush rendering
                // are invoked per coverage run instead of once per pixel.
                this.ApplyCoverageRun(clipIndex + 1, y, runStart, x, startX, runCoverage, destination, coverage);

                runStart = x;
                runCoverage = pixelCoverage;
            }

            this.ApplyCoverageRun(clipIndex + 1, y, runStart, spanEnd, startX, runCoverage, destination, coverage);
        }

        /// <summary>
        /// Advances one hard-edged integer run to the next clip descriptor.
        /// </summary>
        /// <param name="clipIndex">The next clip descriptor index.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="runStart">The absolute first column in the run.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="runEnd">The exclusive absolute end column in the run.</param>
        /// <param name="destination">The destination pixels covered by the parent span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyIntegerRun(
            int clipIndex,
            int y,
            int runStart,
            int spanStart,
            int runEnd,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            if (runEnd <= runStart)
            {
                return;
            }

            int offset = runStart - spanStart;
            this.ApplyClipDescriptors(clipIndex, y, runStart, destination.Slice(offset, runEnd - runStart), coverage.Slice(offset, runEnd - runStart));
        }

        /// <summary>
        /// Applies a coverage multiplier to one run before advancing it to the next clip descriptor.
        /// </summary>
        /// <param name="clipIndex">The next clip descriptor index.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="runStart">The absolute first column in the run.</param>
        /// <param name="runEnd">The exclusive absolute end column in the run.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="multiplier">The clip coverage multiplier applied to the run.</param>
        /// <param name="destination">The destination pixels covered by the parent span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyCoverageRun(
            int clipIndex,
            int y,
            int runStart,
            int runEnd,
            int spanStart,
            float multiplier,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            if (multiplier <= 0F)
            {
                return;
            }

            int offset = runStart - spanStart;
            int length = runEnd - runStart;

            Span<float> runCoverage = coverage.Slice(offset, length);
            if (multiplier != 1F)
            {
                int i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    int vectorCount = Vector<float>.Count;
                    int vectorLength = runCoverage.Length - (runCoverage.Length % vectorCount);
                    Vector<float> multiplierVector = new(multiplier);
                    Span<Vector<float>> vectors = MemoryMarshal.Cast<float, Vector<float>>(runCoverage[..vectorLength]);

                    // The coverage span is borrowed from the rasterizer. Scale it in place so
                    // downstream clipping and blending consume the combined coverage without
                    // allocating a temporary span for every clipped run.
                    for (int j = 0; j < vectors.Length; j++)
                    {
                        vectors[j] *= multiplierVector;
                    }

                    i = vectorLength;
                }

                for (; i < runCoverage.Length; i++)
                {
                    runCoverage[i] *= multiplier;
                }
            }

            this.ApplyClipDescriptors(clipIndex, y, runStart, destination.Slice(offset, length), runCoverage);

            if (multiplier != 1F)
            {
                int i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    int vectorCount = Vector<float>.Count;
                    int vectorLength = runCoverage.Length - (runCoverage.Length % vectorCount);
                    Vector<float> multiplierVector = new(multiplier);
                    Span<Vector<float>> vectors = MemoryMarshal.Cast<float, Vector<float>>(runCoverage[..vectorLength]);

                    // Restore the borrowed coverage before returning; sibling runs and outer
                    // clip descriptors still observe the original rasterizer coverage buffer.
                    for (int j = 0; j < vectors.Length; j++)
                    {
                        vectors[j] /= multiplierVector;
                    }

                    i = vectorLength;
                }

                for (; i < runCoverage.Length; i++)
                {
                    runCoverage[i] /= multiplier;
                }
            }
        }

        /// <summary>
        /// Computes one-dimensional pixel coverage against a clip interval.
        /// </summary>
        /// <param name="pixel">The integer pixel coordinate.</param>
        /// <param name="clipStart">The inclusive clip interval start.</param>
        /// <param name="clipEnd">The exclusive clip interval end.</param>
        /// <param name="edgeMode">The edge mode used to quantize coverage.</param>
        /// <param name="antialiasThreshold">The hard-edge threshold used when <paramref name="edgeMode"/> is hard.</param>
        /// <returns>The coverage contribution for the pixel.</returns>
        private static float GetAxisCoverage(
            int pixel,
            float clipStart,
            float clipEnd,
            DrawingClipEdgeMode edgeMode,
            float antialiasThreshold)
        {
            float coverage = MathF.Min(pixel + 1F, clipEnd) - MathF.Max(pixel, clipStart);
            coverage = Math.Clamp(coverage, 0F, 1F);

            return edgeMode == DrawingClipEdgeMode.Hard
                ? coverage > antialiasThreshold ? 1F : 0F
                : coverage;
        }
    }

    /// <summary>
    /// Writes retained path clip coverage into a worker-local span for one subject row.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct PathClipCoverageRowHandler<TPixel> : IRasterizerCoverageRowHandler
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly WorkerState<TPixel> workerState;
        private readonly ClipOperation operation;
        private readonly int targetY;
        private readonly int targetStartX;
        private readonly int targetLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathClipCoverageRowHandler{TPixel}"/> struct.
        /// </summary>
        /// <param name="workerState">The worker-local execution state that owns the clip coverage span.</param>
        /// <param name="operation">The clip operation represented by the path descriptor.</param>
        /// <param name="targetY">The subject row being clipped.</param>
        /// <param name="targetStartX">The subject span start.</param>
        /// <param name="targetLength">The subject span length.</param>
        public PathClipCoverageRowHandler(
            WorkerState<TPixel> workerState,
            ClipOperation operation,
            int targetY,
            int targetStartX,
            int targetLength)
        {
            this.workerState = workerState;
            this.operation = operation;
            this.targetY = targetY;
            this.targetStartX = targetStartX;
            this.targetLength = targetLength;
        }

        /// <summary>
        /// Records clip coverage emitted by the rasterizer for the target subject row.
        /// </summary>
        /// <param name="y">The absolute row emitted by the clip rasterizer.</param>
        /// <param name="startX">The absolute start column emitted by the clip rasterizer.</param>
        /// <param name="coverage">The emitted clip coverage values.</param>
        public void Handle(int y, int startX, Span<float> coverage)
        {
            if (y != this.targetY)
            {
                return;
            }

            int targetEndX = this.targetStartX + this.targetLength;
            int overlapStart = Math.Max(startX, this.targetStartX);
            int overlapEnd = Math.Min(startX + coverage.Length, targetEndX);
            if (overlapEnd <= overlapStart)
            {
                return;
            }

            Span<float> clipCoverage = this.workerState.PathClipCoverage;
            int sourceOffset = overlapStart - startX;
            int targetOffset = overlapStart - this.targetStartX;
            int length = overlapEnd - overlapStart;
            Span<float> source = coverage.Slice(sourceOffset, length);
            Span<float> target = clipCoverage.Slice(targetOffset, length);

            if (this.operation == ClipOperation.Difference)
            {
                int i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    int vectorCount = Vector<float>.Count;
                    int vectorLength = length - (length % vectorCount);
                    Vector<float> one = Vector<float>.One;
                    ReadOnlySpan<Vector<float>> sourceVectors = MemoryMarshal.Cast<float, Vector<float>>(source[..vectorLength]);
                    Span<Vector<float>> targetVectors = MemoryMarshal.Cast<float, Vector<float>>(target[..vectorLength]);

                    // Difference clips invert the clip coverage into the reusable target span.
                    // Casting the vector-width prefix avoids per-lane loads and stores when SIMD is available.
                    for (int j = 0; j < sourceVectors.Length; j++)
                    {
                        targetVectors[j] = one - sourceVectors[j];
                    }

                    i = vectorLength;
                }

                for (; i < length; i++)
                {
                    target[i] = 1F - source[i];
                }

                return;
            }

            source.CopyTo(target);
        }
    }

    /// <summary>
    /// Walks retained row-operation blocks without flattening them into another list.
    /// </summary>
    private struct SceneOperationCursor
    {
        private FlushScene.SceneOperationBlock? block;
        private int index;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperationCursor"/> struct.
        /// </summary>
        /// <param name="block">The first retained row-operation block.</param>
        public SceneOperationCursor(FlushScene.SceneOperationBlock? block)
        {
            this.block = block;
            this.index = 0;
        }

        /// <summary>
        /// Reads the next retained row operation.
        /// </summary>
        /// <param name="operation">The next operation.</param>
        /// <returns>True when an operation was read; otherwise false.</returns>
        public bool TryRead(out FlushScene.SceneOperation operation)
        {
            while (this.block is not null)
            {
                Span<FlushScene.SceneOperation> items = this.block.Items;
                if (this.index < items.Length)
                {
                    operation = items[this.index++];
                    return true;
                }

                this.block = this.block.Next;
                this.index = 0;
            }

            operation = default;
            return false;
        }
    }

    /// <summary>
    /// Represents one active composition target for a retained row.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct BandTarget<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Buffer2D<TPixel>? owner;

        /// <summary>
        /// Initializes a new instance of the <see cref="BandTarget{TPixel}"/> struct over an existing region.
        /// </summary>
        /// <param name="region">The destination region.</param>
        /// <param name="absoluteLeft">The absolute X origin of the region.</param>
        /// <param name="absoluteTop">The absolute Y origin of the region.</param>
        /// <param name="graphicsOptions">The graphics options used when this target is later composited.</param>
        public BandTarget(Buffer2DRegion<TPixel> region, int absoluteLeft, int absoluteTop, GraphicsOptions? graphicsOptions)
        {
            this.Region = region;
            this.AbsoluteLeft = absoluteLeft;
            this.AbsoluteTop = absoluteTop;
            this.GraphicsOptions = graphicsOptions;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BandTarget{TPixel}"/> struct over an owned temporary buffer.
        /// </summary>
        /// <param name="owner">The owned buffer backing the target.</param>
        /// <param name="bounds">The absolute bounds represented by the target.</param>
        /// <param name="graphicsOptions">The graphics options used when this target is later composited.</param>
        public BandTarget(Buffer2D<TPixel> owner, Rectangle bounds, GraphicsOptions? graphicsOptions)
        {
            this.owner = owner;
            this.Region = owner.GetRegion();
            this.AbsoluteLeft = bounds.X;
            this.AbsoluteTop = bounds.Y;
            this.GraphicsOptions = graphicsOptions;
        }

        /// <summary>
        /// Gets the writable pixel region for the target.
        /// </summary>
        public Buffer2DRegion<TPixel> Region { get; }

        /// <summary>
        /// Gets the absolute bounds represented by <see cref="Region"/>.
        /// </summary>
        public Rectangle Bounds => new(this.AbsoluteLeft, this.AbsoluteTop, this.Region.Width, this.Region.Height);

        /// <summary>
        /// Gets the absolute X origin of <see cref="Region"/>.
        /// </summary>
        public int AbsoluteLeft { get; }

        /// <summary>
        /// Gets the absolute Y origin of <see cref="Region"/>.
        /// </summary>
        public int AbsoluteTop { get; }

        /// <summary>
        /// Gets the graphics options associated with the target when it is used as a layer.
        /// </summary>
        public GraphicsOptions? GraphicsOptions { get; }

        /// <summary>
        /// Releases the owned temporary buffer when the target represents a layer.
        /// </summary>
        public void Dispose() => this.owner?.Dispose();
    }

    /// <summary>
    /// Holds the reusable worker-local scratch used while executing retained scene rows.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class WorkerState<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Worker states hold only allocator-owned buffers (brush workspace, raster scratch,
        // clip coverage), never scene references, so instances can be pooled across flushes
        // exactly like the WebGPU backend's scheduling arenas. One Parallel.For's worth of
        // states covers steady-state rendering; anything beyond the cap is released.
        //
        // The pool trims itself on Gen2 collections, mirroring the memory allocator's own
        // trimming: when a Gen2 GC arrives and nothing rented since the previous one, the
        // renderer is idle and every pooled state is disposed, returning its buffers to the
        // allocator. Active rendering keeps the pool untouched. This bounds idle retention
        // without an explicit lifetime or shutdown hook.
        private static readonly Stack<WorkerState<TPixel>> Pool = new();
        private static readonly object PoolSync = new();
        private static readonly int MaxPooledStates = Environment.ProcessorCount;

        // 1 when Rent ran since the last Gen2 trim check; the sentinel resets it each Gen2.
        private static int rentedSinceLastGen2;

        private readonly MemoryAllocator allocator;
        private DefaultRasterizer.WorkerScratch? scratch;
        private DefaultRasterizer.WorkerScratch? pathClipScratch;
        private IMemoryOwner<float>? pathClipCoverageOwner;
        private int pathClipCoverageLength;

        /// <summary>
        /// Initializes static members of the <see cref="WorkerState{TPixel}"/> class,
        /// arming the Gen2 trim sentinel. The instance is intentionally dropped: it lives
        /// on the finalizer queue and resurrects itself after every collection.
        /// </summary>
        static WorkerState() => _ = new Gen2TrimSentinel();

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerState{TPixel}"/> class.
        /// </summary>
        /// <param name="allocator">The memory allocator used for scratch growth.</param>
        /// <param name="destinationWidth">The destination width used to size the brush workspace.</param>
        public WorkerState(
            MemoryAllocator allocator,
            int destinationWidth)
        {
            this.allocator = allocator;
            this.DestinationWidth = destinationWidth;
            this.BrushWorkspace = new BrushWorkspace<TPixel>(allocator, destinationWidth);
        }

        /// <summary>
        /// Gets the destination width the brush workspace was sized for. A wider state can
        /// service any narrower target, so pooling reuses states whose width is sufficient.
        /// </summary>
        public int DestinationWidth { get; }

        /// <summary>
        /// Gets the reusable brush workspace for the worker.
        /// </summary>
        public BrushWorkspace<TPixel> BrushWorkspace { get; }

        /// <summary>
        /// Gets the worker-local path clip coverage span last requested by <see cref="GetOrCreatePathClipCoverage"/>.
        /// </summary>
        public Span<float> PathClipCoverage
        {
            get
            {
                IMemoryOwner<float>? owner = this.pathClipCoverageOwner;
                return owner is null ? [] : owner.Memory.Span[..this.pathClipCoverageLength];
            }
        }

        /// <summary>
        /// Rents a pooled worker state sized for at least the requested width, or creates one.
        /// A popped state with a different allocator or an insufficient width is released and
        /// replaced, so the pool converges to full-width states for the active allocator.
        /// </summary>
        /// <param name="allocator">The memory allocator that must own the state's buffers.</param>
        /// <param name="destinationWidth">The minimum destination width the state must service.</param>
        /// <returns>A worker state ready for one worker's row execution.</returns>
        public static WorkerState<TPixel> Rent(MemoryAllocator allocator, int destinationWidth)
        {
            Volatile.Write(ref rentedSinceLastGen2, 1);

            WorkerState<TPixel>? pooled = null;
            lock (PoolSync)
            {
                if (Pool.Count > 0)
                {
                    pooled = Pool.Pop();
                }
            }

            if (pooled is not null)
            {
                if (ReferenceEquals(pooled.allocator, allocator) && pooled.DestinationWidth >= destinationWidth)
                {
                    return pooled;
                }

                pooled.Dispose();
            }

            return new WorkerState<TPixel>(allocator, destinationWidth);
        }

        /// <summary>
        /// Returns a worker state to the pool for a later flush, or releases it when the pool
        /// is already holding one full set of worker states.
        /// </summary>
        /// <param name="state">The state to recycle.</param>
        public static void Return(WorkerState<TPixel> state)
        {
            lock (PoolSync)
            {
                if (Pool.Count < MaxPooledStates)
                {
                    Pool.Push(state);
                    return;
                }
            }

            state.Dispose();
        }

        /// <summary>
        /// Disposes every pooled state when no rent occurred since the previous Gen2
        /// collection, returning all held buffers to the memory allocator while the
        /// renderer is idle. Invoked from the trim sentinel's finalizer.
        /// </summary>
        private static void TrimPoolIfIdle()
        {
            if (Interlocked.Exchange(ref rentedSinceLastGen2, 0) == 1)
            {
                return;
            }

            WorkerState<TPixel>[] trimmed;
            lock (PoolSync)
            {
                if (Pool.Count == 0)
                {
                    return;
                }

                trimmed = Pool.ToArray();
                Pool.Clear();
            }

            foreach (WorkerState<TPixel> state in trimmed)
            {
                state.Dispose();
            }
        }

        /// <summary>
        /// Returns a reusable raster scratch instance sized for the requested width.
        /// </summary>
        /// <param name="requiredWidth">The minimum scanline width required by the current row.</param>
        /// <returns>A scratch instance that can execute the row.</returns>
        public DefaultRasterizer.WorkerScratch GetOrCreateScratch(int requiredWidth)
        {
            DefaultRasterizer.WorkerScratch? current = this.scratch;
            if (current is not null && current.CanReuse(requiredWidth))
            {
                return current;
            }

            current?.Dispose();
            this.scratch = DefaultRasterizer.CreateWorkerScratch(this.allocator, requiredWidth);
            return this.scratch;
        }

        /// <summary>
        /// Returns a reusable raster scratch instance reserved for path clip rasterization.
        /// </summary>
        /// <param name="requiredWidth">The minimum scanline width required by the current clip row.</param>
        /// <returns>A scratch instance that can execute the clip row.</returns>
        public DefaultRasterizer.WorkerScratch GetOrCreatePathClipScratch(int requiredWidth)
        {
            DefaultRasterizer.WorkerScratch? current = this.pathClipScratch;
            if (current is not null && current.CanReuse(requiredWidth))
            {
                return current;
            }

            current?.Dispose();
            this.pathClipScratch = DefaultRasterizer.CreateWorkerScratch(this.allocator, requiredWidth);
            return this.pathClipScratch;
        }

        /// <summary>
        /// Returns a reusable coverage span used to combine one path clip with one subject span.
        /// </summary>
        /// <param name="requiredLength">The minimum coverage length required by the current subject span.</param>
        /// <returns>A worker-local coverage span.</returns>
        public Span<float> GetOrCreatePathClipCoverage(int requiredLength)
        {
            IMemoryOwner<float>? current = this.pathClipCoverageOwner;
            if (current is null || current.Memory.Length < requiredLength)
            {
                current?.Dispose();
                this.pathClipCoverageOwner = this.allocator.Allocate<float>(requiredLength);
            }

            this.pathClipCoverageLength = requiredLength;
            return this.PathClipCoverage;
        }

        /// <summary>
        /// Releases the worker-local scratch and brush workspace.
        /// </summary>
        public void Dispose()
        {
            this.scratch?.Dispose();
            this.pathClipScratch?.Dispose();
            this.pathClipCoverageOwner?.Dispose();
            this.BrushWorkspace.Dispose();
        }

        /// <summary>
        /// A finalization-resurrected sentinel that runs one pool trim check per garbage
        /// collection of its generation. After the first resurrection the instance is
        /// tenured, so in steady state the check runs once per Gen2 collection, matching
        /// the trimming cadence of the pooled memory allocator itself.
        /// </summary>
        private sealed class Gen2TrimSentinel
        {
            /// <summary>
            /// Finalizes an instance of the <see cref="Gen2TrimSentinel"/> class.
            /// Runs the idle trim and resurrects the sentinel for the next collection.
            /// </summary>
            ~Gen2TrimSentinel()
            {
                // Never resurrect during teardown: the runtime is finalizing for exit and
                // the pool's buffers are reclaimed with the process.
                if (Environment.HasShutdownStarted || AppDomain.CurrentDomain.IsFinalizingForUnload())
                {
                    return;
                }

                TrimPoolIfIdle();
                GC.ReRegisterForFinalize(this);
            }
        }
    }
}
