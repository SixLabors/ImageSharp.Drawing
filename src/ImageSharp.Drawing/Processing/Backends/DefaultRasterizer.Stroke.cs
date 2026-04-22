// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

#pragma warning disable SA1201 // Elements should appear in the correct order

internal static partial class DefaultRasterizer
{
    private const float StrokeDirectionEpsilon = 1e-6F;
    private const float StrokeParallelEpsilon = 1e-5F;
    private const int DirectStrokeVerticalSampleCount = 4;

    /// <summary>
    /// Creates retained row-local raster payload for one stroked centerline geometry.
    /// </summary>
    /// <param name="geometry">The source stroke centerline geometry.</param>
    /// <param name="residual">The residual transform applied to each source point during emission.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="translateX">The destination-space X translation applied at composition time.</param>
    /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
    /// <param name="options">The rasterizer options used to generate coverage.</param>
    /// <param name="widthScale">The isotropic scale factor applied to the stroke width so expansion runs in device-space pixels.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <returns>The retained rasterizable geometry for the stroke, or <see langword="null"/> when the stroke produces no coverage.</returns>
    internal static StrokeRasterizableGeometry? CreatePathStrokeRasterizableGeometry(
        LinearGeometry geometry,
        Matrix4x4 residual,
        Pen pen,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        float widthScale,
        MemoryAllocator allocator)
    {
        if (pen.StrokeWidth <= 0F)
        {
            return null;
        }

        return CreateRetainedStrokeRasterizableGeometry(
            geometry,
            residual,
            new StrokeStyle(pen, widthScale),
            translateX,
            translateY,
            in options,
            allocator);
    }

    /// <summary>
    /// Creates retained row-local raster payload for one stroked two-point line segment.
    /// </summary>
    /// <param name="start">The retained stroke start point.</param>
    /// <param name="end">The retained stroke end point.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="translateX">The destination-space X translation applied at composition time.</param>
    /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
    /// <param name="options">The rasterizer options used to generate coverage.</param>
    /// <param name="widthScale">The isotropic scale factor applied to the stroke width so expansion runs in device-space pixels.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <returns>The retained rasterizable geometry for the stroke, or <see langword="null"/> when the stroke produces no coverage.</returns>
    internal static StrokeRasterizableGeometry? CreateLineSegmentStrokeRasterizableGeometry(
        PointF start,
        PointF end,
        Pen pen,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        float widthScale,
        MemoryAllocator allocator)
    {
        if (pen.StrokeWidth <= 0F)
        {
            return null;
        }

        float samplingOffsetX = 0.5F;
        float samplingOffsetY = 0.5F;

        StrokeStyle strokeStyle = new(pen, widthScale);

        RectangleF bounds = RectangleF.FromLTRB(
            MathF.Min(start.X, end.X),
            MathF.Min(start.Y, end.Y),
            MathF.Max(start.X, end.X),
            MathF.Max(start.Y, end.Y));

        RectangleF translatedBounds = InflateStrokeBounds(bounds, strokeStyle);

        translatedBounds.Offset(translateX + samplingOffsetX, translateY + samplingOffsetY);

        Rectangle geometryBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(translatedBounds.Left),
            (int)MathF.Floor(translatedBounds.Top),
            (int)MathF.Ceiling(translatedBounds.Right) + 1,
            (int)MathF.Ceiling(translatedBounds.Bottom));

        Rectangle clippedBounds = Rectangle.Intersect(geometryBounds, options.Interest);
        if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
        {
            return null;
        }

        int width = clippedBounds.Width;
        int firstRowBandIndex = clippedBounds.Top / PreferredRowHeight;
        int lastRowBandIndex = (clippedBounds.Bottom - 1) / PreferredRowHeight;
        int rowBandCount = lastRowBandIndex - firstRowBandIndex + 1;
        int wordsPerRow = BitVectorsForMaxBitCount(width);
        int coverStride = checked(width << 1);

        if (wordsPerRow <= 0 || coverStride <= 0)
        {
            ThrowInterestBoundsTooLarge();
        }

        RasterizableBandInfo[] bandInfos = new RasterizableBandInfo[rowBandCount];
        int estimatedLineCount = EstimateStrokeBandLineCount(start, end);
        for (int i = 0; i < rowBandCount; i++)
        {
            int bandTop = (firstRowBandIndex + i) * PreferredRowHeight;
            bandInfos[i] = new RasterizableBandInfo(
                estimatedLineCount,
                PreferredRowHeight,
                width,
                wordsPerRow,
                coverStride,
                clippedBounds.Left,
                bandTop,
                options.IntersectionRule,
                options.RasterizationMode,
                options.AntialiasThreshold,
                hasStartCovers: false);
        }

        return new StrokeRasterizableGeometry(
            firstRowBandIndex,
            rowBandCount,
            width,
            wordsPerRow,
            coverStride,
            PreferredRowHeight,
            bandInfos,
            new LineSegmentStrokeRasterData(
                start,
                end,
                strokeStyle,
                translateX,
                translateY,
                firstRowBandIndex,
                rowBandCount,
                samplingOffsetX,
                samplingOffsetY));
    }

    /// <summary>
    /// Expands one stroked centerline geometry once into retained per-band line storage.
    /// </summary>
    /// <param name="geometry">The retained stroke centerline geometry.</param>
    /// <param name="residual">The residual transform applied to each source point during emission.</param>
    /// <param name="stroke">The stroke style.</param>
    /// <param name="translateX">The destination-space X translation applied at composition time.</param>
    /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
    /// <param name="options">The rasterizer options used for the retained bands.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <returns>The retained stroke rasterizable geometry, or <see langword="null"/> when the stroke produces no coverage.</returns>
    private static StrokeRasterizableGeometry? CreateRetainedStrokeRasterizableGeometry(
        LinearGeometry geometry,
        Matrix4x4 residual,
        in StrokeStyle stroke,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator)
    {
        if (geometry.Info.PointCount == 0)
        {
            return null;
        }

        float samplingOffsetX = 0.5F;
        float samplingOffsetY = 0.5F;

        RectangleF sourceBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        RectangleF translatedBounds = InflateStrokeBounds(sourceBounds, stroke);
        translatedBounds.Offset(translateX + samplingOffsetX, translateY + samplingOffsetY);

        Rectangle geometryBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(translatedBounds.Left),
            (int)MathF.Floor(translatedBounds.Top),
            (int)MathF.Ceiling(translatedBounds.Right) + 1,
            (int)MathF.Ceiling(translatedBounds.Bottom) + 1);

        Rectangle clippedBounds = Rectangle.Intersect(geometryBounds, options.Interest);
        if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
        {
            return null;
        }

        int width = clippedBounds.Width;
        int height = clippedBounds.Height;
        int firstRowBandIndex = clippedBounds.Top / PreferredRowHeight;
        int lastRowBandIndex = (clippedBounds.Bottom - 1) / PreferredRowHeight;
        int rowBandCount = lastRowBandIndex - firstRowBandIndex + 1;
        int wordsPerRow = BitVectorsForMaxBitCount(width);
        int coverStride = checked(width << 1);

        if (wordsPerRow <= 0 || coverStride <= 0)
        {
            ThrowInterestBoundsTooLarge();
        }

        if (width < 128)
        {
            StrokeLinearizerX16Y16 linearizer = new(
                geometry,
                residual,
                stroke,
                translateX,
                translateY,
                clippedBounds.Left,
                clippedBounds.Top,
                width,
                height,
                firstRowBandIndex,
                rowBandCount,
                samplingOffsetX,
                samplingOffsetY,
                allocator);

            if (!linearizer.TryProcess(out LinearizedRasterData<LineArrayX16Y16Block> result))
            {
                return null;
            }

            return CreateRetainedStrokeRasterizableGeometry(
                firstRowBandIndex,
                rowBandCount,
                width,
                wordsPerRow,
                coverStride,
                clippedBounds.Left,
                options,
                result);
        }

        StrokeLinearizerX32Y16 wideLinearizer = new(
            geometry,
            residual,
            stroke,
            translateX,
            translateY,
            clippedBounds.Left,
            clippedBounds.Top,
            width,
            height,
            firstRowBandIndex,
            rowBandCount,
            samplingOffsetX,
            samplingOffsetY,
            allocator);

        if (!wideLinearizer.TryProcess(out LinearizedRasterData<LineArrayX32Y16Block> wideResult))
        {
            return null;
        }

        return CreateRetainedStrokeRasterizableGeometry(
            firstRowBandIndex,
            rowBandCount,
            width,
            wordsPerRow,
            coverStride,
            clippedBounds.Left,
            options,
            wideResult);
    }

    /// <summary>
    /// Wraps finalized retained stroke line storage in the normal stroke rasterizable payload.
    /// </summary>
    private static StrokeRasterizableGeometry CreateRetainedStrokeRasterizableGeometry(
        int firstRowBandIndex,
        int rowBandCount,
        int width,
        int wordsPerRow,
        int coverStride,
        int destinationLeft,
        in RasterizerOptions options,
        LinearizedRasterData<LineArrayX16Y16Block> result)
    {
        RasterizableBandInfo[] bandInfos = new RasterizableBandInfo[rowBandCount];
        for (int i = 0; i < rowBandCount; i++)
        {
            int bandTop = (firstRowBandIndex + i) * PreferredRowHeight;
            bool hasStartCovers = result.StartCoverTable[i] is not null;
            bandInfos[i] = new RasterizableBandInfo(
                CountLines(result.Lines[i], result.FirstBlockLineCounts[i]),
                PreferredRowHeight,
                width,
                wordsPerRow,
                coverStride,
                destinationLeft,
                bandTop,
                options.IntersectionRule,
                options.RasterizationMode,
                options.AntialiasThreshold,
                hasStartCovers);
        }

        RasterizableGeometry retained = new(
            firstRowBandIndex,
            rowBandCount,
            width,
            wordsPerRow,
            coverStride,
            PreferredRowHeight,
            isX16: true,
            bandInfos,
            result.Lines,
            null,
            result.FirstBlockLineCounts,
            result.StartCoverTable);

        return new StrokeRasterizableGeometry(
            retained.FirstRowBandIndex,
            retained.RowBandCount,
            retained.Width,
            retained.WordsPerRow,
            retained.CoverStride,
            retained.BandHeight,
            bandInfos,
            new RetainedStrokeRasterData(retained),
            retained);
    }

    /// <summary>
    /// Wraps finalized retained wide stroke line storage in the normal stroke rasterizable payload.
    /// </summary>
    private static StrokeRasterizableGeometry CreateRetainedStrokeRasterizableGeometry(
        int firstRowBandIndex,
        int rowBandCount,
        int width,
        int wordsPerRow,
        int coverStride,
        int destinationLeft,
        in RasterizerOptions options,
        LinearizedRasterData<LineArrayX32Y16Block> result)
    {
        RasterizableBandInfo[] bandInfos = new RasterizableBandInfo[rowBandCount];
        for (int i = 0; i < rowBandCount; i++)
        {
            int bandTop = (firstRowBandIndex + i) * PreferredRowHeight;
            bool hasStartCovers = result.StartCoverTable[i] is not null;
            bandInfos[i] = new RasterizableBandInfo(
                CountLines(result.Lines[i], result.FirstBlockLineCounts[i]),
                PreferredRowHeight,
                width,
                wordsPerRow,
                coverStride,
                destinationLeft,
                bandTop,
                options.IntersectionRule,
                options.RasterizationMode,
                options.AntialiasThreshold,
                hasStartCovers);
        }

        RasterizableGeometry retained = new(
            firstRowBandIndex,
            rowBandCount,
            width,
            wordsPerRow,
            coverStride,
            PreferredRowHeight,
            isX16: false,
            bandInfos,
            null,
            result.Lines,
            result.FirstBlockLineCounts,
            result.StartCoverTable);

        return new StrokeRasterizableGeometry(
            retained.FirstRowBandIndex,
            retained.RowBandCount,
            retained.Width,
            retained.WordsPerRow,
            retained.CoverStride,
            retained.BandHeight,
            bandInfos,
            new RetainedStrokeRasterData(retained),
            retained);
    }

    /// <summary>
    /// Returns the conservative retained line count used for one two-point stroke segment.
    /// </summary>
    /// <param name="start">The stroke start point.</param>
    /// <param name="end">The stroke end point.</param>
    /// <returns>The estimated retained line count for the stroke.</returns>
    private static int EstimateStrokeBandLineCount(PointF start, PointF end)
    {
        float samplingOffset = 0.5F;
        int segmentCount = (int)MathF.Floor(start.Y + samplingOffset) != (int)MathF.Floor(end.Y + samplingOffset) ? 1 : 0;
        return Math.Max(segmentCount * 4, 1);
    }

    /// <summary>
    /// Inflates centerline bounds conservatively for the current stroke style.
    /// </summary>
    /// <param name="bounds">The centerline bounds.</param>
    /// <param name="stroke">The stroke style used for inflation.</param>
    /// <returns>The inflated stroke bounds.</returns>
    private static RectangleF InflateStrokeBounds(RectangleF bounds, in StrokeStyle stroke)
    {
        float joinInflate = stroke.LineJoin switch
        {
            LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound
            => stroke.HalfWidth * (float)Math.Max(stroke.MiterLimit, 1D),
            _ => stroke.HalfWidth
        };

        float capInflate = stroke.LineCap == LineCap.Square
            ? stroke.HalfWidth * MathF.Sqrt(2F)
            : stroke.HalfWidth;

        float inflate = MathF.Max(joinInflate, capInflate);

        bounds.Inflate(new SizeF(inflate, inflate));
        return bounds;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeRasterData"/> class.
    /// </summary>
    internal abstract class StrokeRasterData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeRasterData"/> class.
        /// </summary>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="firstBandIndex">The first retained row-band index touched by the stroke.</param>
        /// <param name="rowBandCount">The number of retained row bands touched by the stroke.</param>
        /// <param name="samplingOffsetX">The horizontal sampling offset.</param>
        /// <param name="samplingOffsetY">The vertical sampling offset.</param>
        protected StrokeRasterData(
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY)
        {
            this.Stroke = stroke;
            this.TranslateX = translateX;
            this.TranslateY = translateY;
            this.FirstBandIndex = firstBandIndex;
            this.RowBandCount = rowBandCount;
            this.SamplingOffsetX = samplingOffsetX;
            this.SamplingOffsetY = samplingOffsetY;
        }

        public StrokeStyle Stroke { get; }

        /// <summary>
        /// Gets the destination-space X translation applied at composition time.
        /// </summary>
        public int TranslateX { get; }

        /// <summary>
        /// Gets the destination-space Y translation applied at composition time.
        /// </summary>
        public int TranslateY { get; }

        /// <summary>
        /// Gets the first retained row-band index touched by this stroke.
        /// </summary>
        public int FirstBandIndex { get; }

        /// <summary>
        /// Gets the number of retained row bands touched by this stroke.
        /// </summary>
        public int RowBandCount { get; }

        /// <summary>
        /// Gets the horizontal sampling offset applied during rasterization.
        /// </summary>
        public float SamplingOffsetX { get; }

        /// <summary>
        /// Gets the vertical sampling offset applied during rasterization.
        /// </summary>
        public float SamplingOffsetY { get; }

        public virtual bool RequiresBandCoverage => false;

        /// <summary>
        /// Rasterizes one retained row band using the derived stroke payload.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="context">The mutable scan-conversion context.</param>
        /// <param name="bandInfo">The retained band metadata.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="strokeBandCoverage">The reusable per-band stroke coverage scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        public abstract void ExecuteBand<TRowHandler>(
            ref Context context,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            Span<float> strokeBandCoverage,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler;
    }

    /// <summary>
    /// Retained stroke source data for one explicit two-point line segment.
    /// </summary>
    internal sealed class LineSegmentStrokeRasterData : StrokeRasterData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LineSegmentStrokeRasterData"/> class.
        /// </summary>
        /// <param name="start">The retained line start point.</param>
        /// <param name="end">The retained line end point.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="firstBandIndex">The first retained row-band index touched by the stroke.</param>
        /// <param name="rowBandCount">The number of retained row bands touched by the stroke.</param>
        /// <param name="samplingOffsetX">The horizontal sampling offset.</param>
        /// <param name="samplingOffsetY">The vertical sampling offset.</param>
        public LineSegmentStrokeRasterData(
            PointF start,
            PointF end,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY)
            : base(stroke, translateX, translateY, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY)
        {
            this.Start = start;
            this.End = end;
        }

        /// <summary>
        /// Gets the retained line start point.
        /// </summary>
        public PointF Start { get; }

        /// <summary>
        /// Gets the retained line end point.
        /// </summary>
        public PointF End { get; }

        /// <inheritdoc/>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="context">The mutable scan-conversion context.</param>
        /// <param name="bandInfo">The retained band metadata.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="strokeBandCoverage">The reusable per-band stroke coverage scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        public override void ExecuteBand<TRowHandler>(
            ref Context context,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            Span<float> strokeBandCoverage,
            ref TRowHandler rowHandler)
            => DirectLineSegmentBandRasterizer.Rasterize(
                this.Start,
                this.End,
                this.Stroke,
                this.TranslateX,
                this.TranslateY,
                this.SamplingOffsetX,
                this.SamplingOffsetY,
                in bandInfo,
                scanline,
                ref rowHandler);
    }

    /// <summary>
    /// Retained stroke source data backed by one-time outline linearization.
    /// </summary>
    internal sealed class RetainedStrokeRasterData : StrokeRasterData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RetainedStrokeRasterData"/> class.
        /// </summary>
        /// <param name="outline">The retained fill-style raster payload replayed for the stroke.</param>
        public RetainedStrokeRasterData(RasterizableGeometry outline)
            : base(default, 0, 0, outline.FirstRowBandIndex, outline.RowBandCount, 0F, 0F)
            => this.Outline = outline;

        /// <summary>
        /// Gets the retained fill-style raster payload for the stroked outline.
        /// </summary>
        public RasterizableGeometry Outline { get; }

        /// <inheritdoc/>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="context">The mutable scan-conversion context.</param>
        /// <param name="bandInfo">The retained band metadata.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="strokeBandCoverage">The reusable per-band stroke coverage scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        public override void ExecuteBand<TRowHandler>(
            ref Context context,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            Span<float> strokeBandCoverage,
            ref TRowHandler rowHandler)
        {
            int localRowIndex = (bandInfo.DestinationTop / PreferredRowHeight) - this.FirstBandIndex;
            context.SeedStartCovers(this.Outline.GetActualCoversForRow(localRowIndex));

            if (this.Outline.IsX16)
            {
                LineArrayX16Y16Block? lines = this.Outline.GetLinesX16ForRow(localRowIndex);
                lines?.Iterate(this.Outline.GetFirstBlockLineCountForRow(localRowIndex), ref context);
            }
            else
            {
                LineArrayX32Y16Block? lines = this.Outline.GetLinesX32ForRow(localRowIndex);
                lines?.Iterate(this.Outline.GetFirstBlockLineCountForRow(localRowIndex), ref context);
            }

            context.EmitCoverageRows(bandInfo.DestinationTop, bandInfo.DestinationLeft, scanline, ref rowHandler);
            context.ResetTouchedRows();
        }
    }

    /// <summary>
    /// Flush-scoped retained row-local raster payload for one stroked centerline geometry.
    /// </summary>
    internal sealed class StrokeRasterizableGeometry : IDisposable
    {
        private readonly RasterizableBandInfo[] bandInfos;
        private readonly StrokeRasterData strokeData;
        private readonly IDisposable? ownedDisposable;

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeRasterizableGeometry"/> class.
        /// </summary>
        /// <param name="firstRowBandIndex">The first absolute row-band index touched by the stroke.</param>
        /// <param name="rowBandCount">The number of retained local row bands owned by the stroke.</param>
        /// <param name="width">The stroke-local visible band width in pixels.</param>
        /// <param name="wordsPerRow">The bit-vector width in machine words required by the stroke.</param>
        /// <param name="coverStride">The scanner cover/area stride required by the stroke.</param>
        /// <param name="bandHeight">The retained row-band height in pixels.</param>
        /// <param name="bandInfos">The retained metadata for each local row band.</param>
        /// <param name="strokeData">The retained stroke source data consumed during execution.</param>
        /// <param name="ownedDisposable">Optional retained storage owned by this stroke rasterizable.</param>
        public StrokeRasterizableGeometry(
            int firstRowBandIndex,
            int rowBandCount,
            int width,
            int wordsPerRow,
            int coverStride,
            int bandHeight,
            RasterizableBandInfo[] bandInfos,
            StrokeRasterData strokeData,
            IDisposable? ownedDisposable = null)
        {
            this.FirstRowBandIndex = firstRowBandIndex;
            this.RowBandCount = rowBandCount;
            this.Width = width;
            this.WordsPerRow = wordsPerRow;
            this.CoverStride = coverStride;
            this.BandHeight = bandHeight;
            this.bandInfos = bandInfos;
            this.strokeData = strokeData;
            this.ownedDisposable = ownedDisposable;
        }

        /// <summary>
        /// Gets the first absolute row-band index touched by this stroke.
        /// </summary>
        public int FirstRowBandIndex { get; }

        /// <summary>
        /// Gets the number of retained local row bands owned by this stroke.
        /// </summary>
        public int RowBandCount { get; }

        /// <summary>
        /// Gets the stroke-local visible band width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the bit-vector width in machine words required by this stroke.
        /// </summary>
        public int WordsPerRow { get; }

        /// <summary>
        /// Gets the scanner cover/area stride required by this stroke.
        /// </summary>
        public int CoverStride { get; }

        /// <summary>
        /// Gets the retained row-band height in pixels.
        /// </summary>
        public int BandHeight { get; }

        public bool RequiresBandCoverage => this.strokeData.RequiresBandCoverage;

        /// <summary>
        /// Returns <see langword="true"/> when the given local row band has retained coverage payload.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns><see langword="true"/> when the row band has retained coverage; otherwise <see langword="false"/>.</returns>
        public bool HasCoverage(int localRowIndex) => this.bandInfos[localRowIndex].HasCoverage;

        /// <summary>
        /// Gets retained metadata for one local row band.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns>The retained band metadata.</returns>
        public RasterizableBandInfo GetBandInfo(int localRowIndex) => this.bandInfos[localRowIndex];

        /// <summary>
        /// Rasterizes one retained row band directly from the stroke centerline data.
        /// </summary>
        /// <param name="context">The mutable scan-conversion context.</param>
        /// <param name="bandInfo">The retained band metadata.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="strokeBandCoverage">The reusable per-band stroke coverage scratch buffer.</param>
        /// <param name="rowHandler">The coverage handler that consumes emitted spans.</param>
        /// <typeparam name="TRowHandler">The row handler type.</typeparam>
        public void ExecuteBand<TRowHandler>(
            ref Context context,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            Span<float> strokeBandCoverage,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
            => this.strokeData.ExecuteBand(ref context, in bandInfo, scanline, strokeBandCoverage, ref rowHandler);

        /// <summary>
        /// Releases any retained disposable storage owned by this stroke rasterizable.
        /// </summary>
        public void Dispose() => this.ownedDisposable?.Dispose();
    }

    /// <summary>
    /// Direct execution-time rasterizer for one stroked explicit line segment.
    /// </summary>
    private readonly struct DirectLineSegmentBandRasterizer
    {
        private readonly Vector2 start;
        private readonly Vector2 end;
        private readonly Vector2 translation;
        private readonly StrokeStyle stroke;
        private readonly int width;
        private readonly int height;
        private readonly int destinationLeft;
        private readonly int destinationTop;
        private readonly RasterizationMode rasterizationMode;
        private readonly float antialiasThreshold;

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectLineSegmentBandRasterizer"/> struct.
        /// </summary>
        /// <param name="start">The retained stroke start point.</param>
        /// <param name="end">The retained stroke end point.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="samplingOffsetX">The horizontal sampling offset.</param>
        /// <param name="samplingOffsetY">The vertical sampling offset.</param>
        /// <param name="bandInfo">The retained band metadata.</param>
        private DirectLineSegmentBandRasterizer(
            PointF start,
            PointF end,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            float samplingOffsetX,
            float samplingOffsetY,
            in RasterizableBandInfo bandInfo)
        {
            this.translation = new(
                (translateX - bandInfo.DestinationLeft) + samplingOffsetX,
                (translateY - bandInfo.DestinationTop) + samplingOffsetY);

            this.start = start;
            this.end = end;
            this.stroke = stroke;
            this.width = bandInfo.Width;
            this.height = bandInfo.BandHeight;
            this.destinationLeft = bandInfo.DestinationLeft;
            this.destinationTop = bandInfo.DestinationTop;
            this.rasterizationMode = bandInfo.RasterizationMode;
            this.antialiasThreshold = bandInfo.AntialiasThreshold;
        }

        /// <summary>
        /// Rasterizes one explicit line segment directly into the supplied row handler.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="start">The retained stroke start point.</param>
        /// <param name="end">The retained stroke end point.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="samplingOffsetX">The horizontal sampling offset.</param>
        /// <param name="samplingOffsetY">The vertical sampling offset.</param>
        /// <param name="bandInfo">The retained band metadata.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        public static void Rasterize<TRowHandler>(
            PointF start,
            PointF end,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            float samplingOffsetX,
            float samplingOffsetY,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
            => new DirectLineSegmentBandRasterizer(
                start,
                end,
                stroke,
                translateX,
                translateY,
                samplingOffsetX,
                samplingOffsetY,
                in bandInfo).Rasterize(scanline, ref rowHandler);

        /// <summary>
        /// Rasterizes the stored segment across the active band, falling back to a point footprint for degenerate input.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        private void Rasterize<TRowHandler>(Span<float> scanline, ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            if (this.stroke.Width <= 0F || this.width <= 0 || this.height <= 0)
            {
                return;
            }

            Vector2 translatedStart = this.start + this.translation;
            Vector2 translatedEnd = this.end + this.translation;
            if (!TryGetDirection(translatedStart, translatedEnd, out Vector2 tangent, out _))
            {
                this.RasterizePointLike(translatedStart, scanline, ref rowHandler);
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 normal = GetStrokeOffsetNormal(tangent) * halfWidth;
            Vector2 extension = this.stroke.LineCap == LineCap.Square ? tangent * halfWidth : Vector2.Zero;
            Vector2 p0 = translatedStart + normal - extension;
            Vector2 p1 = translatedEnd + normal + extension;
            Vector2 p2 = translatedEnd - normal + extension;
            Vector2 p3 = translatedStart - normal - extension;

            for (int row = 0; row < this.height; row++)
            {
                this.EmitLineCoverageRow(row, p0, p1, p2, p3, scanline, ref rowHandler);
            }
        }

        /// <summary>
        /// Rasterizes a degenerate segment as a point-like cap footprint.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="center">The band-local center point.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        private void RasterizePointLike<TRowHandler>(Vector2 center, Span<float> scanline, ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            for (int row = 0; row < this.height; row++)
            {
                this.EmitPointCoverageRow(row, center, scanline, ref rowHandler);
            }
        }

        /// <summary>
        /// Computes and emits one raster row for the stroked line body and any cap overlap.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="row">The band-local row index.</param>
        /// <param name="p0">The first quad corner.</param>
        /// <param name="p1">The second quad corner.</param>
        /// <param name="p2">The third quad corner.</param>
        /// <param name="p3">The fourth quad corner.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        private void EmitLineCoverageRow<TRowHandler>(
            int row,
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            Span<float> scanline,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            float globalLeft = float.PositiveInfinity;
            float globalRight = float.NegativeInfinity;
            int sampleCount = 0;

            for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
            {
                // First pass finds the tight horizontal span touched by any vertical sample so the
                // accumulation pass only clears and updates the columns that can actually contribute.
                float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                bool hasInterval = false;

                if (!TryGetQuadrilateralIntervalAtY(p0, p1, p2, p3, sampleY, out float left, out float right))
                {
                    if (this.stroke.LineCap == LineCap.Round)
                    {
                        bool hasRoundInterval = false;
                        if (TryGetCircleIntervalAtY(this.start, this.stroke.HalfWidth, sampleY, out float startLeft, out float startRight))
                        {
                            hasRoundInterval = true;
                            left = startLeft;
                            right = startRight;
                        }

                        if (TryGetCircleIntervalAtY(this.end, this.stroke.HalfWidth, sampleY, out float endLeft, out float endRight))
                        {
                            if (!hasRoundInterval)
                            {
                                hasRoundInterval = true;
                                left = endLeft;
                                right = endRight;
                            }
                            else
                            {
                                left = MathF.Min(left, endLeft);
                                right = MathF.Max(right, endRight);
                            }
                        }

                        if (!hasRoundInterval)
                        {
                            continue;
                        }

                        hasInterval = true;
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (this.stroke.LineCap == LineCap.Round)
                {
                    if (TryGetCircleIntervalAtY(this.start, this.stroke.HalfWidth, sampleY, out float startLeft, out float startRight))
                    {
                        left = MathF.Min(left, startLeft);
                        right = MathF.Max(right, startRight);
                    }

                    if (TryGetCircleIntervalAtY(this.end, this.stroke.HalfWidth, sampleY, out float endLeft, out float endRight))
                    {
                        left = MathF.Min(left, endLeft);
                        right = MathF.Max(right, endRight);
                    }

                    hasInterval = true;
                }
                else
                {
                    hasInterval = true;
                }

                if (!hasInterval)
                {
                    continue;
                }

                globalLeft = MathF.Min(globalLeft, left);
                globalRight = MathF.Max(globalRight, right);
                sampleCount++;
            }

            if (sampleCount == 0)
            {
                return;
            }

            int startColumn = Math.Max(0, (int)MathF.Floor(globalLeft));
            int endColumn = Math.Min(this.width, (int)MathF.Ceiling(globalRight));
            if (endColumn <= startColumn)
            {
                return;
            }

            Span<float> rowCoverage = scanline[startColumn..endColumn];
            rowCoverage.Clear();

            float sampleWeight = 1F / DirectStrokeVerticalSampleCount;
            for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
            {
                // Second pass accumulates weighted horizontal coverage for each vertical supersample.
                float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                bool hasInterval;

                if (!TryGetQuadrilateralIntervalAtY(p0, p1, p2, p3, sampleY, out float left, out float right))
                {
                    if (this.stroke.LineCap == LineCap.Round)
                    {
                        bool hasRoundInterval = false;
                        left = default;
                        right = default;
                        if (TryGetCircleIntervalAtY(this.start, this.stroke.HalfWidth, sampleY, out float startLeft, out float startRight))
                        {
                            hasRoundInterval = true;
                            left = startLeft;
                            right = startRight;
                        }

                        if (TryGetCircleIntervalAtY(this.end, this.stroke.HalfWidth, sampleY, out float endLeft, out float endRight))
                        {
                            if (!hasRoundInterval)
                            {
                                hasRoundInterval = true;
                                left = endLeft;
                                right = endRight;
                            }
                            else
                            {
                                left = MathF.Min(left, endLeft);
                                right = MathF.Max(right, endRight);
                            }
                        }

                        hasInterval = hasRoundInterval;
                    }
                    else
                    {
                        hasInterval = false;
                    }
                }
                else
                {
                    if (this.stroke.LineCap == LineCap.Round)
                    {
                        if (TryGetCircleIntervalAtY(this.start, this.stroke.HalfWidth, sampleY, out float startLeft, out float startRight))
                        {
                            left = MathF.Min(left, startLeft);
                            right = MathF.Max(right, startRight);
                        }

                        if (TryGetCircleIntervalAtY(this.end, this.stroke.HalfWidth, sampleY, out float endLeft, out float endRight))
                        {
                            left = MathF.Min(left, endLeft);
                            right = MathF.Max(right, endRight);
                        }
                    }

                    hasInterval = true;
                }

                if (hasInterval)
                {
                    AccumulateIntervalCoverage(rowCoverage, startColumn, left, right, sampleWeight);
                }
            }

            this.FinalizeCoverageRow(row, startColumn, rowCoverage, ref rowHandler);
        }

        /// <summary>
        /// Computes and emits one raster row for a point-like stroke footprint.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="row">The band-local row index.</param>
        /// <param name="center">The band-local center point.</param>
        /// <param name="scanline">The reusable scanline scratch buffer.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        private void EmitPointCoverageRow<TRowHandler>(
            int row,
            Vector2 center,
            Span<float> scanline,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            float globalLeft = float.PositiveInfinity;
            float globalRight = float.NegativeInfinity;
            int sampleCount = 0;

            for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
            {
                float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                bool hasInterval = this.stroke.LineCap == LineCap.Round
                    ? TryGetCircleIntervalAtY(center, this.stroke.HalfWidth, sampleY, out float left, out float right)
                    : TryGetAxisAlignedIntervalAtY(
                        center.Y - this.stroke.HalfWidth,
                        center.Y + this.stroke.HalfWidth,
                        center.X - this.stroke.HalfWidth,
                        center.X + this.stroke.HalfWidth,
                        sampleY,
                        out left,
                        out right);

                if (!hasInterval)
                {
                    continue;
                }

                globalLeft = MathF.Min(globalLeft, left);
                globalRight = MathF.Max(globalRight, right);
                sampleCount++;
            }

            if (sampleCount == 0)
            {
                return;
            }

            int startColumn = Math.Max(0, (int)MathF.Floor(globalLeft));
            int endColumn = Math.Min(this.width, (int)MathF.Ceiling(globalRight));
            if (endColumn <= startColumn)
            {
                return;
            }

            Span<float> rowCoverage = scanline[startColumn..endColumn];
            rowCoverage.Clear();

            float sampleWeight = 1F / DirectStrokeVerticalSampleCount;
            for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
            {
                float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                bool hasInterval = this.stroke.LineCap == LineCap.Round
                    ? TryGetCircleIntervalAtY(center, this.stroke.HalfWidth, sampleY, out float left, out float right)
                    : TryGetAxisAlignedIntervalAtY(
                        center.Y - this.stroke.HalfWidth,
                        center.Y + this.stroke.HalfWidth,
                        center.X - this.stroke.HalfWidth,
                        center.X + this.stroke.HalfWidth,
                        sampleY,
                        out left,
                        out right);

                if (hasInterval)
                {
                    AccumulateIntervalCoverage(rowCoverage, startColumn, left, right, sampleWeight);
                }
            }

            this.FinalizeCoverageRow(row, startColumn, rowCoverage, ref rowHandler);
        }

        /// <summary>
        /// Applies the selected rasterization mode and emits the non-zero runs for one row.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="row">The band-local row index.</param>
        /// <param name="startColumn">The first covered column in the current scanline slice.</param>
        /// <param name="rowCoverage">The accumulated row coverage slice.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        private void FinalizeCoverageRow<TRowHandler>(
            int row,
            int startColumn,
            Span<float> rowCoverage,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            if (this.rasterizationMode == RasterizationMode.Aliased)
            {
                for (int i = 0; i < rowCoverage.Length; i++)
                {
                    rowCoverage[i] = rowCoverage[i] >= this.antialiasThreshold ? 1F : 0F;
                }
            }

            EmitCoverageRuns(rowCoverage, startColumn, this.destinationLeft, this.destinationTop + row, ref rowHandler);
        }

        /// <summary>
        /// Accumulates one horizontal sample interval into per-pixel row coverage.
        /// </summary>
        /// <param name="rowCoverage">The per-pixel row coverage buffer.</param>
        /// <param name="baseColumn">The destination column corresponding to index 0 in <paramref name="rowCoverage"/>.</param>
        /// <param name="left">The left edge of the sample interval.</param>
        /// <param name="right">The right edge of the sample interval.</param>
        /// <param name="sampleWeight">The contribution weight of the current vertical sample.</param>
        private static void AccumulateIntervalCoverage(
            Span<float> rowCoverage,
            int baseColumn,
            float left,
            float right,
            float sampleWeight)
        {
            int bandLeft = baseColumn;
            int bandRight = baseColumn + rowCoverage.Length;
            float clampedLeft = MathF.Max(left, bandLeft);
            float clampedRight = MathF.Min(right, bandRight);
            if (clampedRight <= clampedLeft)
            {
                return;
            }

            int startPixel = (int)MathF.Floor(clampedLeft);
            int endPixel = (int)MathF.Ceiling(clampedRight);
            if (endPixel <= startPixel)
            {
                return;
            }

            if (endPixel == startPixel + 1)
            {
                rowCoverage[startPixel - baseColumn] += (clampedRight - clampedLeft) * sampleWeight;
                return;
            }

            rowCoverage[startPixel - baseColumn] += ((startPixel + 1) - clampedLeft) * sampleWeight;
            for (int x = startPixel + 1; x < endPixel - 1; x++)
            {
                rowCoverage[x - baseColumn] += sampleWeight;
            }

            rowCoverage[(endPixel - 1) - baseColumn] += (clampedRight - (endPixel - 1)) * sampleWeight;
        }

        /// <summary>
        /// Emits contiguous non-zero coverage runs for one raster row.
        /// </summary>
        /// <typeparam name="TRowHandler">The coverage row handler type.</typeparam>
        /// <param name="rowCoverage">The per-pixel row coverage buffer.</param>
        /// <param name="startColumn">The destination column corresponding to index 0 in <paramref name="rowCoverage"/>.</param>
        /// <param name="destinationLeft">The destination-space band left edge.</param>
        /// <param name="destinationY">The destination-space row.</param>
        /// <param name="rowHandler">The coverage row handler that receives emitted runs.</param>
        private static void EmitCoverageRuns<TRowHandler>(
            Span<float> rowCoverage,
            int startColumn,
            int destinationLeft,
            int destinationY,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            int runStart = -1;
            for (int i = 0; i < rowCoverage.Length; i++)
            {
                if (rowCoverage[i] > 0F)
                {
                    runStart = runStart < 0 ? i : runStart;
                    continue;
                }

                if (runStart >= 0)
                {
                    rowHandler.Handle(
                        destinationY,
                        destinationLeft + startColumn + runStart,
                        rowCoverage[runStart..i]);
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                rowHandler.Handle(
                    destinationY,
                    destinationLeft + startColumn + runStart,
                    rowCoverage[runStart..]);
            }
        }

        /// <summary>
        /// Intersects a horizontal sample line with an axis-aligned rectangle.
        /// </summary>
        /// <param name="top">The rectangle top edge.</param>
        /// <param name="bottom">The rectangle bottom edge.</param>
        /// <param name="left">The rectangle left edge.</param>
        /// <param name="right">The rectangle right edge.</param>
        /// <param name="sampleY">The sample row in band-local coordinates.</param>
        /// <param name="intervalLeft">Receives the left intersection bound.</param>
        /// <param name="intervalRight">Receives the right intersection bound.</param>
        /// <returns><see langword="true"/> when the sample intersects the rectangle.</returns>
        private static bool TryGetAxisAlignedIntervalAtY(
            float top,
            float bottom,
            float left,
            float right,
            float sampleY,
            out float intervalLeft,
            out float intervalRight)
        {
            if (sampleY < top || sampleY > bottom)
            {
                intervalLeft = default;
                intervalRight = default;
                return false;
            }

            intervalLeft = left;
            intervalRight = right;
            return intervalRight > intervalLeft;
        }

        /// <summary>
        /// Intersects a horizontal sample line with a circle.
        /// </summary>
        /// <param name="center">The circle center.</param>
        /// <param name="radius">The circle radius.</param>
        /// <param name="sampleY">The sample row in band-local coordinates.</param>
        /// <param name="intervalLeft">Receives the left intersection bound.</param>
        /// <param name="intervalRight">Receives the right intersection bound.</param>
        /// <returns><see langword="true"/> when the sample intersects the circle.</returns>
        private static bool TryGetCircleIntervalAtY(
            Vector2 center,
            float radius,
            float sampleY,
            out float intervalLeft,
            out float intervalRight)
        {
            float dy = sampleY - center.Y;
            float radiusSquared = radius * radius;
            float dySquared = dy * dy;
            if (dySquared > radiusSquared)
            {
                intervalLeft = default;
                intervalRight = default;
                return false;
            }

            float dx = MathF.Sqrt(MathF.Max(0F, radiusSquared - dySquared));
            intervalLeft = center.X - dx;
            intervalRight = center.X + dx;
            return intervalRight > intervalLeft;
        }

        /// <summary>
        /// Intersects a horizontal sample line with a convex quadrilateral.
        /// </summary>
        /// <param name="p0">The first quadrilateral vertex.</param>
        /// <param name="p1">The second quadrilateral vertex.</param>
        /// <param name="p2">The third quadrilateral vertex.</param>
        /// <param name="p3">The fourth quadrilateral vertex.</param>
        /// <param name="sampleY">The sample row in band-local coordinates.</param>
        /// <param name="intervalLeft">Receives the left intersection bound.</param>
        /// <param name="intervalRight">Receives the right intersection bound.</param>
        /// <returns><see langword="true"/> when the sample intersects the quadrilateral.</returns>
        private static bool TryGetQuadrilateralIntervalAtY(
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            float sampleY,
            out float intervalLeft,
            out float intervalRight)
        {
            intervalLeft = float.PositiveInfinity;
            intervalRight = float.NegativeInfinity;
            bool hasIntersection = false;
            AppendEdgeInterval(p0, p1, sampleY, ref hasIntersection, ref intervalLeft, ref intervalRight);
            AppendEdgeInterval(p1, p2, sampleY, ref hasIntersection, ref intervalLeft, ref intervalRight);
            AppendEdgeInterval(p2, p3, sampleY, ref hasIntersection, ref intervalLeft, ref intervalRight);
            AppendEdgeInterval(p3, p0, sampleY, ref hasIntersection, ref intervalLeft, ref intervalRight);
            return hasIntersection && intervalRight > intervalLeft;
        }

        /// <summary>
        /// Expands the current sample interval bounds with one polygon edge intersection.
        /// </summary>
        /// <param name="start">The edge start point.</param>
        /// <param name="end">The edge end point.</param>
        /// <param name="sampleY">The sample row in band-local coordinates.</param>
        /// <param name="hasIntersection">Tracks whether any edge has intersected the sample row yet.</param>
        /// <param name="intervalLeft">The running left intersection bound.</param>
        /// <param name="intervalRight">The running right intersection bound.</param>
        private static void AppendEdgeInterval(
            Vector2 start,
            Vector2 end,
            float sampleY,
            ref bool hasIntersection,
            ref float intervalLeft,
            ref float intervalRight)
        {
            float minY = MathF.Min(start.Y, end.Y);
            float maxY = MathF.Max(start.Y, end.Y);
            if (sampleY < minY || sampleY > maxY)
            {
                return;
            }

            if (MathF.Abs(end.Y - start.Y) <= StrokeDirectionEpsilon)
            {
                intervalLeft = MathF.Min(intervalLeft, MathF.Min(start.X, end.X));
                intervalRight = MathF.Max(intervalRight, MathF.Max(start.X, end.X));
                hasIntersection = true;
                return;
            }

            float t = (sampleY - start.Y) / (end.Y - start.Y);
            float x = start.X + ((end.X - start.X) * t);
            intervalLeft = MathF.Min(intervalLeft, x);
            intervalRight = MathF.Max(intervalRight, x);
            hasIntersection = true;
        }
    }

    /// <summary>
    /// Returns the tessellation segment count used for one round join or cap arc.
    /// </summary>
    /// <param name="radius">The arc radius.</param>
    /// <param name="angle">The arc sweep angle in radians.</param>
    /// <param name="arcDetailScale">The tessellation detail scale.</param>
    /// <returns>The number of intermediate tessellation points.</returns>
    private static int GetArcSubdivisionCount(float radius, double angle, double arcDetailScale)
    {
        double safeRadius = Math.Max(radius, StrokeDirectionEpsilon);
        double safeScale = Math.Max(arcDetailScale, 0.01D);
        double ratio = safeRadius / (safeRadius + (0.125D / safeScale));
        ratio = Math.Clamp(ratio, -1D, 1D);
        double theta = Math.Acos(ratio) * 2D;
        return theta <= 0D
            ? 0
            : Math.Max(0, (int)(angle / theta));
    }

    /// <summary>
    /// Returns the stroke offset unit normal for a normalized tangent.
    /// </summary>
    /// <param name="tangent">The normalized tangent.</param>
    /// <returns>The stroke offset unit normal.</returns>
    private static Vector2 GetStrokeOffsetNormal(Vector2 tangent) => new(tangent.Y, -tangent.X);

    /// <summary>
    /// Attempts to normalize the direction from <paramref name="start"/> to <paramref name="end"/>.
    /// </summary>
    /// <param name="start">The segment start point.</param>
    /// <param name="end">The segment end point.</param>
    /// <param name="direction">Receives the normalized direction.</param>
    /// <param name="length">Receives the segment length.</param>
    /// <returns><see langword="true"/> when the segment has non-zero length.</returns>
    private static bool TryGetDirection(PointF start, PointF end, out Vector2 direction, out float length)
    {
        Vector2 delta = end - start;
        float lengthSquared = delta.LengthSquared();
        if (lengthSquared <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
        {
            direction = default;
            length = 0F;
            return false;
        }

        length = MathF.Sqrt(lengthSquared);
        direction = delta / length;
        return true;
    }

    /// <summary>
    /// Attempts to intersect the two infinite offset support lines used by a join.
    /// </summary>
    /// <param name="point">The join point.</param>
    /// <param name="previousOffset">The offset vector on the previous segment.</param>
    /// <param name="previousTangent">The normalized tangent of the previous segment.</param>
    /// <param name="nextOffset">The offset vector on the next segment.</param>
    /// <param name="nextTangent">The normalized tangent of the next segment.</param>
    /// <param name="intersection">Receives the line intersection when one exists.</param>
    /// <returns><see langword="true"/> when the offset lines intersect.</returns>
    private static bool TryIntersectOffsetLines(
        Vector2 point,
        Vector2 previousOffset,
        Vector2 previousTangent,
        Vector2 nextOffset,
        Vector2 nextTangent,
        out Vector2 intersection)
    {
        Vector2 a = point + previousOffset;
        Vector2 b = point + nextOffset;
        float denominator = Cross(previousTangent, nextTangent);
        if (MathF.Abs(denominator) <= StrokeParallelEpsilon)
        {
            intersection = default;
            return false;
        }

        float t = Cross(b - a, nextTangent) / denominator;
        intersection = a + (previousTangent * t);
        return true;
    }

    /// <summary>
    /// Returns the 2D cross product scalar of the supplied vectors.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The 2D cross product scalar.</returns>
    private static float Cross(Vector2 left, Vector2 right) => (left.X * right.Y) - (left.Y * right.X);

    /// <summary>
    /// Normalizes an angle into the inclusive-exclusive range [0, 2Ï€).
    /// </summary>
    /// <param name="angle">The angle to normalize.</param>
    /// <returns>The normalized angle.</returns>
    private static double NormalizePositiveAngle(double angle)
    {
        double fullTurn = Math.PI * 2D;
        while (angle < 0D)
        {
            angle += fullTurn;
        }

        while (angle >= fullTurn)
        {
            angle -= fullTurn;
        }

        return angle;
    }

    /// <summary>
    /// Holds the stroke style values consumed by the CPU direct-stroke rasterizer.
    /// </summary>
    internal readonly struct StrokeStyle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeStyle"/> struct.
        /// </summary>
        /// <param name="pen">The source pen.</param>
        /// <param name="widthScale">The isotropic scale factor applied to the stroke width so the expansion happens in device-space pixels.</param>
        public StrokeStyle(Pen pen, float widthScale)
        {
            this.Width = pen.StrokeWidth * widthScale;
            this.LineCap = pen.StrokeOptions.LineCap;
            this.LineJoin = pen.StrokeOptions.LineJoin;
            this.InnerJoin = pen.StrokeOptions.InnerJoin;
            this.MiterLimit = pen.StrokeOptions.MiterLimit;
            this.InnerMiterLimit = pen.StrokeOptions.InnerMiterLimit;
            this.ArcDetailScale = pen.StrokeOptions.ArcDetailScale;
        }

        /// <summary>
        /// Gets the stroke width in device-space pixels.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets half the stroke width in device-space pixels.
        /// </summary>
        public float HalfWidth => this.Width * 0.5F;

        /// <summary>
        /// Gets the cap style applied to open contour endpoints.
        /// </summary>
        public LineCap LineCap { get; }

        /// <summary>
        /// Gets the outer join style applied to contour corners.
        /// </summary>
        public LineJoin LineJoin { get; }

        /// <summary>
        /// Gets the inner join style applied to contour corners.
        /// </summary>
        public InnerJoin InnerJoin { get; }

        /// <summary>
        /// Gets the outer miter limit expressed in stroke-width units.
        /// </summary>
        public double MiterLimit { get; }

        /// <summary>
        /// Gets the inner miter limit expressed in stroke-width units.
        /// </summary>
        public double InnerMiterLimit { get; }

        /// <summary>
        /// Gets the round join/cap tessellation detail scale.
        /// </summary>
        public double ArcDetailScale { get; }
    }
}

#pragma warning restore SA1201 // Elements should appear in the correct order
