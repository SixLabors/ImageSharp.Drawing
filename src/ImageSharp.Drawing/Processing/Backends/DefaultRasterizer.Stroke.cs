// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.PolygonGeometry;
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
    /// <param name="path">The source stroke path.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="translateX">The destination-space X translation applied at composition time.</param>
    /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
    /// <param name="options">The rasterizer options used to generate coverage.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <returns>The retained rasterizable geometry for the stroke, or <see langword="null"/> when the stroke produces no coverage.</returns>
    internal static StrokeRasterizableGeometry? CreateStrokeRasterizableGeometry(
        IPath path,
        Pen pen,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator)
        => CreateOutlineStrokeRasterizableGeometry(path, pen, translateX, translateY, in options, allocator);

    /// <summary>
    /// Creates retained row-local raster payload for one stroked two-point line segment.
    /// </summary>
    /// <param name="start">The retained stroke start point.</param>
    /// <param name="end">The retained stroke end point.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="translateX">The destination-space X translation applied at composition time.</param>
    /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
    /// <param name="options">The rasterizer options used to generate coverage.</param>
    /// <returns>The retained rasterizable geometry for the stroke, or <see langword="null"/> when the stroke produces no coverage.</returns>
    internal static StrokeRasterizableGeometry? CreateStrokeRasterizableGeometry(
        PointF start,
        PointF end,
        Pen pen,
        int translateX,
        int translateY,
        in RasterizerOptions options)
    {
        if (pen.StrokeWidth <= 0F)
        {
            return null;
        }

        StrokeStyle stroke = new(pen);
        float samplingOffsetX = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        float samplingOffsetY = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        RectangleF bounds = RectangleF.FromLTRB(
            MathF.Min(start.X, end.X),
            MathF.Min(start.Y, end.Y),
            MathF.Max(start.X, end.X),
            MathF.Max(start.Y, end.Y));
        RectangleF translatedBounds = InflateStrokeBounds(bounds, stroke);
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
        int estimatedLineCount = EstimateStrokeBandLineCount(start, end, in options);
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
                stroke,
                translateX,
                translateY,
                firstRowBandIndex,
                rowBandCount,
                samplingOffsetX,
                samplingOffsetY));
    }

    /// <summary>
    /// Creates retained row-local raster payload for one stroked explicit open polyline.
    /// </summary>
    /// <param name="points">The retained polyline points.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="translateX">The destination-space X translation applied at composition time.</param>
    /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
    /// <param name="options">The rasterizer options used to generate coverage.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <returns>The retained rasterizable geometry for the stroke, or <see langword="null"/> when the stroke produces no coverage.</returns>
    internal static StrokeRasterizableGeometry? CreateStrokeRasterizableGeometry(
        PointF[] points,
        Pen pen,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator)
    {
        if (points.Length == 0 || pen.StrokeWidth <= 0F)
        {
            return null;
        }

        if (points.Length == 2)
        {
            return CreateStrokeRasterizableGeometry(points[0], points[1], pen, translateX, translateY, in options);
        }

        return CreateOutlineStrokeRasterizableGeometry(new Path(points), pen, translateX, translateY, in options, allocator);
    }

    private static StrokeRasterizableGeometry? CreateOutlineStrokeRasterizableGeometry(
        IPath path,
        Pen pen,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator)
    {
        if (pen.StrokeWidth <= 0F)
        {
            return null;
        }

        RasterizableGeometry? rasterizable = CreateRasterizableGeometry(
            pen.StrokePattern.Length >= 2
                ? path.GenerateOutline(pen.StrokeWidth, pen.StrokePattern.Span, pen.StrokeOptions).ToLinearGeometry()
                : StrokedShapeGenerator.GenerateStrokedGeometry(path, pen.StrokeWidth, pen.StrokeOptions),
            translateX,
            translateY,
            options,
            allocator);
        if (rasterizable is null)
        {
            return null;
        }

        RasterizableBandInfo[] bandInfos = new RasterizableBandInfo[rasterizable.RowBandCount];
        for (int i = 0; i < bandInfos.Length; i++)
        {
            bandInfos[i] = rasterizable.GetBandInfo(i);
        }

        return new StrokeRasterizableGeometry(
            rasterizable.FirstRowBandIndex,
            rasterizable.RowBandCount,
            rasterizable.Width,
            rasterizable.WordsPerRow,
            rasterizable.CoverStride,
            rasterizable.BandHeight,
            bandInfos,
            new OutlineStrokeRasterData(rasterizable),
            rasterizable);
    }

    /// <summary>
    /// Returns the conservative retained line count used for scene statistics and scheduling heuristics.
    /// </summary>
    private static int EstimateStrokeBandLineCount(LinearGeometry geometry, Pen pen, in RasterizerOptions options)
    {
        if (geometry.Info.PointCount == 0 || pen.StrokeWidth <= 0F)
        {
            return 0;
        }

        int segmentCount = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter
            ? geometry.Info.NonHorizontalSegmentCountPixelCenter
            : geometry.Info.NonHorizontalSegmentCountPixelBoundary;

        return Math.Max(segmentCount * 4, 1);
    }

    /// <summary>
    /// Returns the conservative retained line count used for one two-point stroke segment.
    /// </summary>
    private static int EstimateStrokeBandLineCount(PointF start, PointF end, in RasterizerOptions options)
    {
        float samplingOffset = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        int segmentCount = (int)MathF.Floor(start.Y + samplingOffset) != (int)MathF.Floor(end.Y + samplingOffset) ? 1 : 0;
        return Math.Max(segmentCount * 4, 1);
    }

    /// <summary>
    /// Returns the conservative retained line count used for one explicit open polyline.
    /// </summary>
    private static int EstimateStrokeBandLineCount(PointF[] points, in RasterizerOptions options)
    {
        float samplingOffset = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        int segmentCount = 0;
        for (int i = 1; i < points.Length; i++)
        {
            if ((int)MathF.Floor(points[i - 1].Y + samplingOffset) != (int)MathF.Floor(points[i].Y + samplingOffset))
            {
                segmentCount++;
            }
        }

        return Math.Max(segmentCount * 4, 1);
    }

    /// <summary>
    /// Inflates centerline bounds conservatively for the current stroke style.
    /// </summary>
    private static RectangleF InflateStrokeBounds(RectangleF bounds, StrokeStyle stroke)
    {
        float inflate = stroke.LineJoin switch
        {
            LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound
                => stroke.HalfWidth * (float)Math.Max(stroke.MiterLimit, 1D),
            _ => stroke.HalfWidth
        };

        bounds.Inflate(new SizeF(inflate, inflate));
        return bounds;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeRasterData"/> class.
    /// </summary>
    internal abstract class StrokeRasterData
    {
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

        public abstract void ExecuteBand<TRowHandler>(
            ref Context context,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            Span<float> strokeBandCoverage,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler;
    }

    /// <summary>
    /// Retained stroke source data for flattened path geometry.
    /// </summary>
    internal sealed class PathStrokeRasterData : StrokeRasterData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PathStrokeRasterData"/> class.
        /// </summary>
        public PathStrokeRasterData(
            LinearGeometry geometry,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY)
            : base(stroke, translateX, translateY, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY)
            => this.Geometry = geometry;

        /// <summary>
        /// Gets the retained stroke centerline geometry.
        /// </summary>
        public LinearGeometry Geometry { get; }

        public override bool RequiresBandCoverage => true;

        /// <inheritdoc/>
        public override void ExecuteBand<TRowHandler>(
            ref Context context,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            Span<float> strokeBandCoverage,
            ref TRowHandler rowHandler)
        {
            DirectMultiSegmentBandRasterizer rasterizer = new DirectMultiSegmentBandRasterizer(
                this.Geometry,
                this.Stroke,
                this.TranslateX,
                this.TranslateY,
                bandInfo.DestinationLeft,
                bandInfo.DestinationTop,
                bandInfo.Width,
                bandInfo.BandHeight,
                this.SamplingOffsetX,
                this.SamplingOffsetY)
                .WithRasterizationMode(bandInfo.RasterizationMode, bandInfo.AntialiasThreshold);

            rasterizer.Rasterize(strokeBandCoverage, scanline, ref rowHandler);
        }
    }

    /// <summary>
    /// Retained stroke source data for one explicit two-point line segment.
    /// </summary>
    internal sealed class LineSegmentStrokeRasterData : StrokeRasterData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LineSegmentStrokeRasterData"/> class.
        /// </summary>
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
    internal sealed class OutlineStrokeRasterData : StrokeRasterData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OutlineStrokeRasterData"/> class.
        /// </summary>
        public OutlineStrokeRasterData(RasterizableGeometry outline)
            : base(default, 0, 0, outline.FirstRowBandIndex, outline.RowBandCount, 0F, 0F)
            => this.Outline = outline;

        /// <summary>
        /// Gets the retained fill-style raster payload for the stroked outline.
        /// </summary>
        public RasterizableGeometry Outline { get; }

        /// <inheritdoc/>
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
    /// Retained stroke source data for one explicit open polyline.
    /// </summary>
    internal sealed class PolylineStrokeRasterData : StrokeRasterData
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PolylineStrokeRasterData"/> class.
        /// </summary>
        public PolylineStrokeRasterData(
            PointF[] points,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY)
            : base(stroke, translateX, translateY, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY)
            => this.Points = points;

        /// <summary>
        /// Gets the retained polyline points.
        /// </summary>
        public PointF[] Points { get; }

        public override bool RequiresBandCoverage => true;

        /// <inheritdoc/>
        public override void ExecuteBand<TRowHandler>(
            ref Context context,
            in RasterizableBandInfo bandInfo,
            Span<float> scanline,
            Span<float> strokeBandCoverage,
            ref TRowHandler rowHandler)
        {
            DirectMultiSegmentBandRasterizer rasterizer = new DirectMultiSegmentBandRasterizer(
                this.Points,
                this.Stroke,
                this.TranslateX,
                this.TranslateY,
                bandInfo.DestinationLeft,
                bandInfo.DestinationTop,
                bandInfo.Width,
                bandInfo.BandHeight,
                this.SamplingOffsetX,
                this.SamplingOffsetY)
                .WithRasterizationMode(bandInfo.RasterizationMode, bandInfo.AntialiasThreshold);

            rasterizer.Rasterize(strokeBandCoverage, scanline, ref rowHandler);
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

        public void Dispose() => this.ownedDisposable?.Dispose();
    }

    /// <summary>
    /// Direct execution-time rasterizer for one stroked explicit line segment.
    /// </summary>
    private readonly struct DirectLineSegmentBandRasterizer
    {
        private readonly Vector2 start;
        private readonly Vector2 end;
        private readonly StrokeStyle stroke;
        private readonly int width;
        private readonly int height;
        private readonly int destinationLeft;
        private readonly int destinationTop;
        private readonly RasterizationMode rasterizationMode;
        private readonly float antialiasThreshold;

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
            Vector2 translation = new(
                (translateX - bandInfo.DestinationLeft) + samplingOffsetX,
                (translateY - bandInfo.DestinationTop) + samplingOffsetY);
            Vector2 translatedStart = start;
            Vector2 translatedEnd = end;
            this.start = translatedStart + translation;
            this.end = translatedEnd + translation;
            this.stroke = stroke;
            this.width = bandInfo.Width;
            this.height = bandInfo.BandHeight;
            this.destinationLeft = bandInfo.DestinationLeft;
            this.destinationTop = bandInfo.DestinationTop;
            this.rasterizationMode = bandInfo.RasterizationMode;
            this.antialiasThreshold = bandInfo.AntialiasThreshold;
        }

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

        private void Rasterize<TRowHandler>(Span<float> scanline, ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            if (this.stroke.Width <= 0F || this.width <= 0 || this.height <= 0)
            {
                return;
            }

            PointF translatedStart = this.start;
            PointF translatedEnd = this.end;
            if (!TryGetDirection(translatedStart, translatedEnd, out Vector2 tangent, out _))
            {
                this.RasterizePointLike(this.start, scanline, ref rowHandler);
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 normal = GetLeftNormal(tangent) * halfWidth;
            Vector2 extension = this.stroke.LineCap == LineCap.Square ? tangent * halfWidth : Vector2.Zero;
            Vector2 p0 = this.start + normal - extension;
            Vector2 p1 = this.end + normal + extension;
            Vector2 p2 = this.end - normal + extension;
            Vector2 p3 = this.start - normal - extension;

            for (int row = 0; row < this.height; row++)
            {
                this.EmitLineCoverageRow(row, p0, p1, p2, p3, scanline, ref rowHandler);
            }
        }

        private void RasterizePointLike<TRowHandler>(Vector2 center, Span<float> scanline, ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            for (int row = 0; row < this.height; row++)
            {
                this.EmitPointCoverageRow(row, center, scanline, ref rowHandler);
            }
        }

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
    /// Direct execution-time rasterizer for one stroked path or explicit polyline.
    /// </summary>
    private readonly struct DirectMultiSegmentBandRasterizer
    {
        private readonly LinearGeometry? geometry;
        private readonly PointF[]? polylinePoints;
        private readonly StrokeStyle stroke;
        private readonly float translateX;
        private readonly float translateY;
        private readonly int width;
        private readonly int height;
        private readonly int destinationLeft;
        private readonly int destinationTop;
        private readonly RasterizationMode rasterizationMode;
        private readonly float antialiasThreshold;

        public DirectMultiSegmentBandRasterizer(
            LinearGeometry geometry,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int destinationLeft,
            int destinationTop,
            int width,
            int height,
            float samplingOffsetX,
            float samplingOffsetY)
        {
            this.geometry = geometry;
            this.polylinePoints = null;
            this.stroke = stroke;
            this.translateX = (translateX - destinationLeft) + samplingOffsetX;
            this.translateY = (translateY - destinationTop) + samplingOffsetY;
            this.width = width;
            this.height = height;
            this.destinationLeft = destinationLeft;
            this.destinationTop = destinationTop;
            this.rasterizationMode = RasterizationMode.Antialiased;
            this.antialiasThreshold = 0F;
        }

        public DirectMultiSegmentBandRasterizer(
            PointF[] points,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int destinationLeft,
            int destinationTop,
            int width,
            int height,
            float samplingOffsetX,
            float samplingOffsetY)
        {
            this.geometry = null;
            this.polylinePoints = points;
            this.stroke = stroke;
            this.translateX = (translateX - destinationLeft) + samplingOffsetX;
            this.translateY = (translateY - destinationTop) + samplingOffsetY;
            this.width = width;
            this.height = height;
            this.destinationLeft = destinationLeft;
            this.destinationTop = destinationTop;
            this.rasterizationMode = RasterizationMode.Antialiased;
            this.antialiasThreshold = 0F;
        }

        private DirectMultiSegmentBandRasterizer(
            LinearGeometry? geometry,
            PointF[]? polylinePoints,
            StrokeStyle stroke,
            float translateX,
            float translateY,
            int destinationLeft,
            int destinationTop,
            int width,
            int height,
            RasterizationMode rasterizationMode,
            float antialiasThreshold)
        {
            this.geometry = geometry;
            this.polylinePoints = polylinePoints;
            this.stroke = stroke;
            this.translateX = translateX;
            this.translateY = translateY;
            this.width = width;
            this.height = height;
            this.destinationLeft = destinationLeft;
            this.destinationTop = destinationTop;
            this.rasterizationMode = rasterizationMode;
            this.antialiasThreshold = antialiasThreshold;
        }

        public DirectMultiSegmentBandRasterizer WithRasterizationMode(RasterizationMode rasterizationMode, float antialiasThreshold)
            => new(
                this.geometry,
                this.polylinePoints,
                this.stroke,
                this.translateX,
                this.translateY,
                this.destinationLeft,
                this.destinationTop,
                this.width,
                this.height,
                rasterizationMode,
                antialiasThreshold);

        public void Rasterize<TRowHandler>(Span<float> strokeBandCoverage, Span<float> scanline, ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            if (this.stroke.Width <= 0F || this.width <= 0 || this.height <= 0)
            {
                return;
            }

            int requiredCoverageLength = checked(this.width * this.height * DirectStrokeVerticalSampleCount);
            Span<float> coverage = strokeBandCoverage[..requiredCoverageLength];
            coverage.Clear();

            if (this.polylinePoints is PointF[] explicitPolyline)
            {
                this.AccumulateOpenPolyline(explicitPolyline, coverage);
            }
            else
            {
                this.AccumulateGeometry(coverage);
            }

            this.EmitCoverageRows(coverage, scanline, ref rowHandler);
        }

        private void AccumulateGeometry(Span<float> coverage)
        {
            LinearGeometry sourceGeometry = this.geometry!;
            for (int contourIndex = 0; contourIndex < sourceGeometry.Contours.Count; contourIndex++)
            {
                LinearContour contour = sourceGeometry.Contours[contourIndex];
                if (contour.PointCount == 0)
                {
                    continue;
                }

                if (contour.IsClosed)
                {
                    this.AccumulateClosedContour(contour, coverage);
                }
                else
                {
                    this.AccumulateOpenContour(contour, coverage);
                }
            }
        }

        private void AccumulateOpenPolyline(PointF[] points, Span<float> coverage)
        {
            int pointCount = DefaultRasterizer.GetDistinctPointCount(points);
            if (pointCount == 0)
            {
                return;
            }

            if (pointCount == 1)
            {
                if (DefaultRasterizer.TryGetFirstDistinctPoint(points, out _, out PointF point))
                {
                    this.AccumulatePointLike(point, coverage);
                }

                return;
            }

            if (!DefaultRasterizer.TryGetFirstDistinctPoint(points, out int firstIndex, out PointF firstPoint) ||
                !DefaultRasterizer.TryGetNextDistinctPoint(points, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !DefaultRasterizer.TryGetLastDistinctPoint(points, out int lastIndex, out PointF lastPoint) ||
                !DefaultRasterizer.TryGetPreviousDistinctPoint(points, lastIndex, out _, out PointF beforeLastPoint) ||
                !TryGetDirection(firstPoint, secondPoint, out Vector2 startTangent, out _) ||
                !TryGetDirection(beforeLastPoint, lastPoint, out Vector2 endTangent, out _))
            {
                return;
            }

            this.AccumulateStartCap(firstPoint, startTangent, coverage);

            PointF currentPoint = firstPoint;
            int nextIndex = secondIndex;
            PointF nextPoint = secondPoint;
            while (true)
            {
                this.AccumulateSegmentBody(currentPoint, nextPoint, coverage);

                if (!DefaultRasterizer.TryGetNextDistinctPoint(points, nextIndex, out int futureIndex, out PointF futurePoint))
                {
                    break;
                }

                if (TryGetDirection(currentPoint, nextPoint, out Vector2 previousTangent, out float previousLength) &&
                    TryGetDirection(nextPoint, futurePoint, out Vector2 nextTangent, out float nextLength))
                {
                    this.AccumulateJoin(nextPoint, previousTangent, nextTangent, previousLength, nextLength, coverage);
                }

                currentPoint = nextPoint;
                nextIndex = futureIndex;
                nextPoint = futurePoint;
            }

            this.AccumulateEndCap(lastPoint, endTangent, coverage);
        }

        private void AccumulateOpenContour(in LinearContour contour, Span<float> coverage)
        {
            int pointCount = this.GetDistinctPointCount(contour);
            if (pointCount == 0)
            {
                return;
            }

            if (pointCount == 1)
            {
                if (this.TryGetFirstDistinctPoint(contour, out _, out PointF point))
                {
                    this.AccumulatePointLike(point, coverage);
                }

                return;
            }

            if (!this.TryGetFirstDistinctPoint(contour, out int firstIndex, out PointF firstPoint) ||
                !this.TryGetNextDistinctPoint(contour, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !this.TryGetLastDistinctPoint(contour, out int lastIndex, out PointF lastPoint) ||
                !this.TryGetPreviousDistinctPoint(contour, lastIndex, out _, out PointF beforeLastPoint) ||
                !TryGetDirection(firstPoint, secondPoint, out Vector2 startTangent, out _) ||
                !TryGetDirection(beforeLastPoint, lastPoint, out Vector2 endTangent, out _))
            {
                return;
            }

            this.AccumulateStartCap(firstPoint, startTangent, coverage);

            PointF currentPoint = firstPoint;
            int nextIndex = secondIndex;
            PointF nextPoint = secondPoint;
            while (true)
            {
                this.AccumulateSegmentBody(currentPoint, nextPoint, coverage);

                if (!this.TryGetNextDistinctPoint(contour, nextIndex, out int futureIndex, out PointF futurePoint))
                {
                    break;
                }

                if (TryGetDirection(currentPoint, nextPoint, out Vector2 previousTangent, out float previousLength) &&
                    TryGetDirection(nextPoint, futurePoint, out Vector2 nextTangent, out float nextLength))
                {
                    this.AccumulateJoin(nextPoint, previousTangent, nextTangent, previousLength, nextLength, coverage);
                }

                currentPoint = nextPoint;
                nextIndex = futureIndex;
                nextPoint = futurePoint;
            }

            this.AccumulateEndCap(lastPoint, endTangent, coverage);
        }

        private void AccumulateClosedContour(in LinearContour contour, Span<float> coverage)
        {
            int pointCount = this.GetDistinctPointCount(contour);
            if (pointCount == 0)
            {
                return;
            }

            if (pointCount == 1)
            {
                if (this.TryGetFirstDistinctPoint(contour, out _, out PointF point))
                {
                    this.AccumulatePointLike(point, coverage);
                }

                return;
            }

            if (pointCount == 2)
            {
                if (this.TryGetFirstDistinctPoint(contour, out int segmentFirstIndex, out PointF start) &&
                    this.TryGetNextDistinctPoint(contour, segmentFirstIndex, out _, out PointF end) &&
                    TryGetDirection(start, end, out Vector2 tangent, out _))
                {
                    this.AccumulateStartCap(start, tangent, coverage);
                    this.AccumulateSegmentBody(start, end, coverage);
                    this.AccumulateEndCap(end, tangent, coverage);
                }

                return;
            }

            if (!this.TryGetFirstDistinctPoint(contour, out int firstIndex, out PointF firstPoint) ||
                !this.TryGetNextDistinctPoint(contour, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !this.TryGetLastDistinctPoint(contour, out int lastIndex, out PointF lastPoint))
            {
                return;
            }

            PointF previousPoint = lastPoint;
            int currentIndex = firstIndex;
            PointF currentPoint = firstPoint;
            int nextIndex = secondIndex;
            PointF nextPoint = secondPoint;

            while (true)
            {
                this.AccumulateSegmentBody(currentPoint, nextPoint, coverage);

                if (TryGetDirection(previousPoint, currentPoint, out Vector2 previousTangent, out float previousLength) &&
                    TryGetDirection(currentPoint, nextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    this.AccumulateJoin(currentPoint, previousTangent, nextTangent, previousLength, nextLength, coverage);
                }

                if (currentIndex == lastIndex)
                {
                    break;
                }

                previousPoint = currentPoint;
                currentPoint = nextPoint;
                currentIndex = nextIndex;
                if (!this.TryGetNextDistinctPoint(contour, currentIndex, out nextIndex, out nextPoint))
                {
                    nextIndex = firstIndex;
                    nextPoint = firstPoint;
                }
            }
        }

        private void AccumulateStartCap(PointF point, Vector2 tangent, Span<float> coverage)
        {
            Vector2 localPoint = this.Transform(point);
            Vector2 normal = GetLeftNormal(tangent) * this.stroke.HalfWidth;
            switch (this.stroke.LineCap)
            {
                case LineCap.Round:
                    this.AccumulateSector(localPoint, normal, -normal, -tangent, coverage);
                    return;

                case LineCap.Square:
                    Vector2 extension = tangent * this.stroke.HalfWidth;
                    this.AccumulateQuadrilateral(
                        localPoint + normal - extension,
                        localPoint + normal,
                        localPoint - normal,
                        localPoint - normal - extension,
                        coverage);
                    return;

                default:
                    return;
            }
        }

        private void AccumulateEndCap(PointF point, Vector2 tangent, Span<float> coverage)
        {
            Vector2 localPoint = this.Transform(point);
            Vector2 normal = GetLeftNormal(tangent) * this.stroke.HalfWidth;
            switch (this.stroke.LineCap)
            {
                case LineCap.Round:
                    this.AccumulateSector(localPoint, normal, -normal, tangent, coverage);
                    return;

                case LineCap.Square:
                    Vector2 extension = tangent * this.stroke.HalfWidth;
                    this.AccumulateQuadrilateral(
                        localPoint + normal,
                        localPoint + normal + extension,
                        localPoint - normal + extension,
                        localPoint - normal,
                        coverage);
                    return;

                default:
                    return;
            }
        }

        private void AccumulatePointLike(PointF point, Span<float> coverage)
        {
            Vector2 center = this.Transform(point);
            float halfWidth = this.stroke.HalfWidth;
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AccumulateCircle(center, halfWidth, coverage);
            }
            else
            {
                this.AccumulateRectangle(center.X - halfWidth, center.Y - halfWidth, center.X + halfWidth, center.Y + halfWidth, coverage);
            }
        }

        private void AccumulateSegmentBody(PointF start, PointF end, Span<float> coverage)
        {
            if (!TryGetDirection(start, end, out Vector2 tangent, out _))
            {
                this.AccumulatePointLike(start, coverage);
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 normal = GetLeftNormal(tangent) * halfWidth;
            Vector2 localStart = this.Transform(start);
            Vector2 localEnd = this.Transform(end);
            this.AccumulateQuadrilateral(
                localStart + normal,
                localEnd + normal,
                localEnd - normal,
                localStart - normal,
                coverage);
        }

        private void AccumulateJoin(
            PointF point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            float previousLength,
            float nextLength,
            Span<float> coverage)
        {
            float dot = Vector2.Dot(previousTangent, nextTangent);
            float cross = Cross(previousTangent, nextTangent);
            if (MathF.Abs(cross) <= StrokeParallelEpsilon)
            {
                if (dot <= 0F)
                {
                    this.AccumulatePointLike(point, coverage);
                }

                return;
            }

            float sideSign = cross > 0F ? 1F : -1F;
            Vector2 previousOffset = GetLeftNormal(previousTangent) * (this.stroke.HalfWidth * sideSign);
            Vector2 nextOffset = GetLeftNormal(nextTangent) * (this.stroke.HalfWidth * sideSign);
            Vector2 localPoint = this.Transform(point);

            switch (this.stroke.LineJoin)
            {
                case LineJoin.Round:
                    this.AccumulateSector(localPoint, previousOffset, nextOffset, GetPreferredArcDirection(previousOffset, nextOffset), coverage);
                    return;

                case LineJoin.Bevel:
                    this.AccumulateTriangle(localPoint + previousOffset, localPoint, localPoint + nextOffset, coverage);
                    return;

                default:
                    this.AccumulateMiterJoin(
                        localPoint,
                        previousTangent,
                        nextTangent,
                        previousOffset,
                        nextOffset,
                        previousLength,
                        nextLength,
                        coverage);
                    return;
            }
        }

        private void AccumulateMiterJoin(
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            Vector2 previousOffset,
            Vector2 nextOffset,
            float previousLength,
            float nextLength,
            Span<float> coverage)
        {
            Vector2 previousPoint = point + previousOffset;
            Vector2 nextPoint = point + nextOffset;
            float bevelDistance = ((previousOffset + nextOffset) * 0.5F).Length();
            float limit = (float)(Math.Max(this.stroke.MiterLimit, 1D) * Math.Max(previousOffset.Length(), nextOffset.Length()));

            if (TryIntersectOffsetLines(point, previousOffset, previousTangent, nextOffset, nextTangent, out Vector2 intersection))
            {
                float intersectionDistance = Vector2.Distance(point, intersection);
                if (intersectionDistance <= limit)
                {
                    this.AccumulateTriangle(previousPoint, intersection, nextPoint, coverage);
                    return;
                }

                switch (this.stroke.LineJoin)
                {
                    case LineJoin.MiterRevert:
                        this.AccumulateTriangle(previousPoint, point, nextPoint, coverage);
                        return;

                    case LineJoin.MiterRound:
                        this.AccumulateSector(point, previousOffset, nextOffset, GetPreferredArcDirection(previousOffset, nextOffset), coverage);
                        return;

                    default:
                        if (intersectionDistance <= bevelDistance + StrokeDirectionEpsilon)
                        {
                            this.AccumulateTriangle(previousPoint, point, nextPoint, coverage);
                            return;
                        }

                        float ratio = (limit - bevelDistance) / (intersectionDistance - bevelDistance);
                        this.AccumulateQuadrilateral(
                            previousPoint,
                            previousPoint + ((intersection - previousPoint) * ratio),
                            nextPoint + ((intersection - nextPoint) * ratio),
                            nextPoint,
                            coverage);
                        return;
                }
            }

            switch (this.stroke.LineJoin)
            {
                case LineJoin.MiterRevert:
                    this.AccumulateTriangle(previousPoint, point, nextPoint, coverage);
                    return;

                case LineJoin.MiterRound:
                    this.AccumulateSector(point, previousOffset, nextOffset, GetPreferredArcDirection(previousOffset, nextOffset), coverage);
                    return;

                default:
                    float fallbackLimit = (float)Math.Max(this.stroke.MiterLimit, 1D) * (previousOffset.Length() <= 0F ? nextOffset.Length() : previousOffset.Length());
                    this.AccumulateQuadrilateral(
                        previousPoint,
                        previousPoint + (previousTangent * fallbackLimit),
                        nextPoint - (nextTangent * fallbackLimit),
                        nextPoint,
                        coverage);
                    return;
            }
        }

        private void AccumulateCircle(Vector2 center, float radius, Span<float> coverage)
        {
            if (radius <= StrokeDirectionEpsilon)
            {
                return;
            }

            int startRow = Math.Max(0, (int)MathF.Floor(center.Y - radius));
            int endRow = Math.Min(this.height - 1, (int)MathF.Ceiling(center.Y + radius) - 1);
            for (int row = startRow; row <= endRow; row++)
            {
                for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
                {
                    float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                    if (TryGetCircleIntervalAtY(center, radius, sampleY, out float left, out float right))
                    {
                        this.AccumulateSampleInterval(coverage, row, sampleIndex, left, right);
                    }
                }
            }
        }

        private void AccumulateRectangle(float left, float top, float right, float bottom, Span<float> coverage)
        {
            if (right <= left || bottom <= top)
            {
                return;
            }

            int startRow = Math.Max(0, (int)MathF.Floor(top));
            int endRow = Math.Min(this.height - 1, (int)MathF.Ceiling(bottom) - 1);
            for (int row = startRow; row <= endRow; row++)
            {
                for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
                {
                    float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                    if (TryGetAxisAlignedIntervalAtY(top, bottom, left, right, sampleY, out float intervalLeft, out float intervalRight))
                    {
                        this.AccumulateSampleInterval(coverage, row, sampleIndex, intervalLeft, intervalRight);
                    }
                }
            }
        }

        private void AccumulateTriangle(Vector2 p0, Vector2 p1, Vector2 p2, Span<float> coverage)
        {
            float minY = MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y));
            float maxY = MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y));
            int startRow = Math.Max(0, (int)MathF.Floor(minY));
            int endRow = Math.Min(this.height - 1, (int)MathF.Ceiling(maxY) - 1);
            for (int row = startRow; row <= endRow; row++)
            {
                for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
                {
                    float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                    if (TryGetTriangleIntervalAtY(p0, p1, p2, sampleY, out float left, out float right))
                    {
                        this.AccumulateSampleInterval(coverage, row, sampleIndex, left, right);
                    }
                }
            }
        }

        private void AccumulateQuadrilateral(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Span<float> coverage)
        {
            float minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
            float maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));
            int startRow = Math.Max(0, (int)MathF.Floor(minY));
            int endRow = Math.Min(this.height - 1, (int)MathF.Ceiling(maxY) - 1);
            for (int row = startRow; row <= endRow; row++)
            {
                for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
                {
                    float sampleY = row + ((sampleIndex + 0.5F) / DirectStrokeVerticalSampleCount);
                    if (TryGetQuadrilateralIntervalAtY(p0, p1, p2, p3, sampleY, out float left, out float right))
                    {
                        this.AccumulateSampleInterval(coverage, row, sampleIndex, left, right);
                    }
                }
            }
        }

        private void AccumulateSector(Vector2 center, Vector2 fromOffset, Vector2 toOffset, Vector2 preferredDirection, Span<float> coverage)
        {
            if (fromOffset == Vector2.Zero || toOffset == Vector2.Zero)
            {
                this.AccumulateTriangle(center, center + fromOffset, center + toOffset, coverage);
                return;
            }

            float radius = fromOffset.Length();
            if (radius <= StrokeDirectionEpsilon)
            {
                return;
            }

            Vector2 preferred = preferredDirection;
            if (preferred.LengthSquared() <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
            {
                preferred = Vector2.Normalize(fromOffset + toOffset);
                if (preferred.LengthSquared() <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
                {
                    preferred = Vector2.Normalize(fromOffset);
                }
            }

            double startAngle = Math.Atan2(fromOffset.Y, fromOffset.X);
            double endAngle = Math.Atan2(toOffset.Y, toOffset.X);
            double counterClockwiseSweep = NormalizePositiveAngle(endAngle - startAngle);
            double clockwiseSweep = counterClockwiseSweep - (Math.PI * 2D);
            double chosenSweep = ChooseArcSweep(startAngle, counterClockwiseSweep, clockwiseSweep, preferred);
            int subdivisionCount = GetArcSubdivisionCount(radius, Math.Abs(chosenSweep), this.stroke.ArcDetailScale);
            double step = chosenSweep / (subdivisionCount + 1);

            Vector2 previousPoint = center + fromOffset;
            for (int i = 1; i <= subdivisionCount; i++)
            {
                float angle = (float)(startAngle + (step * i));
                Vector2 nextPoint = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                this.AccumulateTriangle(center, previousPoint, nextPoint, coverage);
                previousPoint = nextPoint;
            }

            this.AccumulateTriangle(center, previousPoint, center + toOffset, coverage);
        }

        private void EmitCoverageRows<TRowHandler>(Span<float> coverage, Span<float> scanline, ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            for (int row = 0; row < this.height; row++)
            {
                Span<float> rowCoverage = scanline[..this.width];
                bool hasCoverage = false;
                int rowBase = row * this.width * DirectStrokeVerticalSampleCount;
                for (int x = 0; x < this.width; x++)
                {
                    float coverageValue = 0F;
                    for (int sampleIndex = 0; sampleIndex < DirectStrokeVerticalSampleCount; sampleIndex++)
                    {
                        coverageValue += coverage[rowBase + (sampleIndex * this.width) + x];
                    }

                    coverageValue /= DirectStrokeVerticalSampleCount;
                    if (this.rasterizationMode == RasterizationMode.Aliased)
                    {
                        coverageValue = coverageValue >= this.antialiasThreshold ? 1F : 0F;
                    }

                    rowCoverage[x] = coverageValue;
                    hasCoverage |= coverageValue > 0F;
                }

                if (hasCoverage)
                {
                    EmitCoverageRuns(rowCoverage, 0, this.destinationLeft, this.destinationTop + row, ref rowHandler);
                }
            }
        }

        private void AccumulateSampleInterval(Span<float> coverage, int row, int sampleIndex, float left, float right)
        {
            int sampleOffset = ((row * DirectStrokeVerticalSampleCount) + sampleIndex) * this.width;
            DefaultRasterizer.AccumulateSampleInterval(coverage.Slice(sampleOffset, this.width), left, right);
        }

        private Vector2 Transform(PointF point)
        {
            Vector2 localPoint = point;
            localPoint.X += this.translateX;
            localPoint.Y += this.translateY;
            return localPoint;
        }

        private int GetDistinctPointCount(in LinearContour contour)
        {
            LinearGeometry sourceGeometry = this.geometry!;
            int pointEnd = contour.PointStart + contour.PointCount;
            int count = 0;
            PointF previousPoint = default;
            bool hasPreviousPoint = false;

            for (int i = contour.PointStart; i < pointEnd; i++)
            {
                PointF point = sourceGeometry.Points[i];
                if (hasPreviousPoint && point == previousPoint)
                {
                    continue;
                }

                previousPoint = point;
                hasPreviousPoint = true;
                count++;
            }

            if (contour.IsClosed &&
                count > 1 &&
                this.TryGetFirstDistinctPoint(contour, out _, out PointF firstPoint) &&
                this.TryGetLastDistinctPoint(contour, out _, out PointF lastPoint) &&
                firstPoint == lastPoint)
            {
                count--;
            }

            return count;
        }

        private bool TryGetFirstDistinctPoint(in LinearContour contour, out int rawIndex, out PointF point)
        {
            if (contour.PointCount == 0)
            {
                rawIndex = -1;
                point = default;
                return false;
            }

            rawIndex = contour.PointStart;
            point = this.geometry!.Points[rawIndex];
            return true;
        }

        private bool TryGetNextDistinctPoint(in LinearContour contour, int rawIndex, out int nextRawIndex, out PointF point)
        {
            IReadOnlyList<PointF> points = this.geometry!.Points;
            PointF currentPoint = points[rawIndex];
            int pointEnd = contour.PointStart + contour.PointCount;
            for (int i = rawIndex + 1; i < pointEnd; i++)
            {
                PointF candidate = points[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                nextRawIndex = i;
                point = candidate;
                return true;
            }

            nextRawIndex = -1;
            point = default;
            return false;
        }

        private bool TryGetLastDistinctPoint(in LinearContour contour, out int rawIndex, out PointF point)
        {
            if (contour.PointCount == 0)
            {
                rawIndex = -1;
                point = default;
                return false;
            }

            IReadOnlyList<PointF> points = this.geometry!.Points;
            int start = contour.PointStart;
            rawIndex = start + contour.PointCount - 1;
            point = points[rawIndex];
            while (rawIndex > start && points[rawIndex - 1] == point)
            {
                rawIndex--;
            }

            return true;
        }

        private bool TryGetPreviousDistinctPoint(in LinearContour contour, int rawIndex, out int previousRawIndex, out PointF point)
        {
            IReadOnlyList<PointF> points = this.geometry!.Points;
            PointF currentPoint = points[rawIndex];
            for (int i = rawIndex - 1; i >= contour.PointStart; i--)
            {
                PointF candidate = points[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                previousRawIndex = i;
                point = candidate;
                return true;
            }

            previousRawIndex = -1;
            point = default;
            return false;
        }
    }

    private static void AccumulateSampleInterval(Span<float> sampleRow, float left, float right)
    {
        int width = sampleRow.Length;
        float clampedLeft = MathF.Max(left, 0F);
        float clampedRight = MathF.Min(right, width);
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
            sampleRow[startPixel] = MathF.Min(1F, sampleRow[startPixel] + (clampedRight - clampedLeft));
            return;
        }

        sampleRow[startPixel] = MathF.Min(1F, sampleRow[startPixel] + ((startPixel + 1) - clampedLeft));
        for (int x = startPixel + 1; x < endPixel - 1; x++)
        {
            sampleRow[x] = 1F;
        }

        int endPixelIndex = endPixel - 1;
        sampleRow[endPixelIndex] = MathF.Min(1F, sampleRow[endPixelIndex] + (clampedRight - endPixelIndex));
    }

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
                rowHandler.Handle(destinationY, destinationLeft + startColumn + runStart, rowCoverage[runStart..i]);
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            rowHandler.Handle(destinationY, destinationLeft + startColumn + runStart, rowCoverage[runStart..]);
        }
    }

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

    private static bool TryGetTriangleIntervalAtY(
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        float sampleY,
        out float intervalLeft,
        out float intervalRight)
    {
        intervalLeft = float.PositiveInfinity;
        intervalRight = float.NegativeInfinity;
        bool hasIntersection = false;
        AppendEdgeInterval(p0, p1, sampleY, ref hasIntersection, ref intervalLeft, ref intervalRight);
        AppendEdgeInterval(p1, p2, sampleY, ref hasIntersection, ref intervalLeft, ref intervalRight);
        AppendEdgeInterval(p2, p0, sampleY, ref hasIntersection, ref intervalLeft, ref intervalRight);
        return hasIntersection && intervalRight > intervalLeft;
    }

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

    private static int GetDistinctPointCount(PointF[] points)
    {
        int count = 0;
        PointF previousPoint = default;
        bool hasPreviousPoint = false;

        for (int i = 0; i < points.Length; i++)
        {
            PointF point = points[i];
            if (hasPreviousPoint && point == previousPoint)
            {
                continue;
            }

            previousPoint = point;
            hasPreviousPoint = true;
            count++;
        }

        return count;
    }

    private static bool TryGetFirstDistinctPoint(PointF[] points, out int index, out PointF point)
    {
        if (points.Length == 0)
        {
            index = -1;
            point = default;
            return false;
        }

        index = 0;
        point = points[0];
        return true;
    }

    private static bool TryGetNextDistinctPoint(PointF[] points, int index, out int nextIndex, out PointF point)
    {
        PointF currentPoint = points[index];
        for (int i = index + 1; i < points.Length; i++)
        {
            PointF candidate = points[i];
            if (candidate == currentPoint)
            {
                continue;
            }

            nextIndex = i;
            point = candidate;
            return true;
        }

        nextIndex = -1;
        point = default;
        return false;
    }

    private static bool TryGetLastDistinctPoint(PointF[] points, out int index, out PointF point)
    {
        if (points.Length == 0)
        {
            index = -1;
            point = default;
            return false;
        }

        index = points.Length - 1;
        point = points[index];
        while (index > 0 && points[index - 1] == point)
        {
            index--;
        }

        return true;
    }

    private static bool TryGetPreviousDistinctPoint(PointF[] points, int index, out int previousIndex, out PointF point)
    {
        PointF currentPoint = points[index];
        for (int i = index - 1; i >= 0; i--)
        {
            PointF candidate = points[i];
            if (candidate == currentPoint)
            {
                continue;
            }

            previousIndex = i;
            point = candidate;
            return true;
        }

        previousIndex = -1;
        point = default;
        return false;
    }

    /// <summary>
    /// Execution-time stroke rasterizer that expands centerlines directly into the active band context.
    /// </summary>
    private readonly struct StrokeBandRasterizer
    {
        private struct ContourState
        {
            public bool HasPoint;
            public PointF FirstPoint;
            public PointF PreviousPoint;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeBandRasterizer"/> struct.
        /// </summary>
        public StrokeBandRasterizer(
            LinearGeometry geometry,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            float samplingOffsetX,
            float samplingOffsetY)
        {
            this.geometry = geometry;
            this.stroke = stroke;
            this.translateX = translateX;
            this.translateY = translateY;
            this.minX = minX;
            this.minY = minY;
            this.width = width;
            this.height = height;
            this.samplingOffsetX = samplingOffsetX;
            this.samplingOffsetY = samplingOffsetY;
            this.singleSegmentStart = default;
            this.singleSegmentEnd = default;
            this.hasSingleSegment = false;
            this.polylinePoints = null;
            this.hasExplicitPolyline = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeBandRasterizer"/> struct for one two-point line segment.
        /// </summary>
        public StrokeBandRasterizer(
            PointF start,
            PointF end,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            float samplingOffsetX,
            float samplingOffsetY)
        {
            this.geometry = default!;
            this.stroke = stroke;
            this.translateX = translateX;
            this.translateY = translateY;
            this.minX = minX;
            this.minY = minY;
            this.width = width;
            this.height = height;
            this.samplingOffsetX = samplingOffsetX;
            this.samplingOffsetY = samplingOffsetY;
            this.singleSegmentStart = start;
            this.singleSegmentEnd = end;
            this.hasSingleSegment = true;
            this.polylinePoints = null;
            this.hasExplicitPolyline = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeBandRasterizer"/> struct for one explicit open polyline.
        /// </summary>
        public StrokeBandRasterizer(
            PointF[] points,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            float samplingOffsetX,
            float samplingOffsetY)
        {
            this.geometry = default!;
            this.stroke = stroke;
            this.translateX = translateX;
            this.translateY = translateY;
            this.minX = minX;
            this.minY = minY;
            this.width = width;
            this.height = height;
            this.samplingOffsetX = samplingOffsetX;
            this.samplingOffsetY = samplingOffsetY;
            this.singleSegmentStart = default;
            this.singleSegmentEnd = default;
            this.hasSingleSegment = false;
            this.polylinePoints = points;
            this.hasExplicitPolyline = true;
        }

        private readonly LinearGeometry geometry;
        private readonly StrokeStyle stroke;
        private readonly int translateX;
        private readonly int translateY;
        private readonly int minX;
        private readonly int minY;
        private readonly int width;
        private readonly int height;
        private readonly float samplingOffsetX;
        private readonly float samplingOffsetY;
        private readonly PointF singleSegmentStart;
        private readonly PointF singleSegmentEnd;
        private readonly bool hasSingleSegment;
        private readonly PointF[]? polylinePoints;
        private readonly bool hasExplicitPolyline;

        /// <summary>
        /// Rasterizes the retained stroke into the current band context.
        /// </summary>
        /// <param name="context">The mutable scan-conversion context.</param>
        public void Rasterize(ref Context context)
        {
            if (this.hasSingleSegment)
            {
                this.RasterizeSingleSegment(ref context);
                return;
            }

            if (this.hasExplicitPolyline)
            {
                this.RasterizeExplicitPolyline(ref context);
                return;
            }

            RectangleF translatedBounds = InflateStrokeBounds(this.geometry.Info.Bounds, this.stroke);
            translatedBounds.Offset(this.translateX + this.samplingOffsetX - this.minX, this.translateY + this.samplingOffsetY - this.minY);

            bool contained =
                translatedBounds.Left >= 0F &&
                translatedBounds.Top >= 0F &&
                translatedBounds.Right <= this.width &&
                translatedBounds.Bottom <= this.height;

            for (int contourIndex = 0; contourIndex < this.geometry.Contours.Count; contourIndex++)
            {
                LinearContour contour = this.geometry.Contours[contourIndex];
                if (contour.PointCount == 0)
                {
                    continue;
                }

                this.ProcessContour(contour, contained, ref context);
            }
        }

        /// <summary>
        /// Rasterizes one explicit line segment into the current band context.
        /// </summary>
        private void RasterizeSingleSegment(ref Context context)
        {
            RectangleF bounds = RectangleF.FromLTRB(
                MathF.Min(this.singleSegmentStart.X, this.singleSegmentEnd.X),
                MathF.Min(this.singleSegmentStart.Y, this.singleSegmentEnd.Y),
                MathF.Max(this.singleSegmentStart.X, this.singleSegmentEnd.X),
                MathF.Max(this.singleSegmentStart.Y, this.singleSegmentEnd.Y));
            RectangleF translatedBounds = InflateStrokeBounds(bounds, this.stroke);
            translatedBounds.Offset(this.translateX + this.samplingOffsetX - this.minX, this.translateY + this.samplingOffsetY - this.minY);

            bool contained =
                translatedBounds.Left >= 0F &&
                translatedBounds.Top >= 0F &&
                translatedBounds.Right <= this.width &&
                translatedBounds.Bottom <= this.height;

            this.EmitOpenSegmentStrokeContour(this.singleSegmentStart, this.singleSegmentEnd, contained, ref context);
        }

        /// <summary>
        /// Rasterizes one explicit open polyline into the current band context.
        /// </summary>
        private void RasterizeExplicitPolyline(ref Context context)
        {
            PointF[] points = this.polylinePoints!;
            int pointCount = GetDistinctPointCount(points);
            if (pointCount == 0)
            {
                return;
            }

            float minX = points[0].X;
            float minY = points[0].Y;
            float maxX = minX;
            float maxY = minY;
            for (int i = 1; i < points.Length; i++)
            {
                PointF point = points[i];
                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
            }

            RectangleF bounds = InflateStrokeBounds(RectangleF.FromLTRB(minX, minY, maxX, maxY), this.stroke);
            RectangleF translatedBounds = bounds;
            translatedBounds.Offset(this.translateX + this.samplingOffsetX - this.minX, this.translateY + this.samplingOffsetY - this.minY);

            bool contained =
                translatedBounds.Left >= 0F &&
                translatedBounds.Top >= 0F &&
                translatedBounds.Right <= this.width &&
                translatedBounds.Bottom <= this.height;

            if (pointCount == 1)
            {
                if (TryGetFirstDistinctPoint(points, out _, out PointF point))
                {
                    this.EmitPointStrokeContour(point, contained, ref context);
                }

                return;
            }

            if (pointCount == 2)
            {
                this.EmitDistinctSegmentContour(points, contained, ref context);
                return;
            }

            this.EmitOpenStrokeContour(points, contained, ref context);
        }

        /// <summary>
        /// Appends one contour point to the active stroke contour.
        /// </summary>
        private void AppendContourPoint(ref ContourState state, PointF point, bool contained, ref Context context)
        {
            if (!state.HasPoint)
            {
                state.HasPoint = true;
                state.FirstPoint = point;
                state.PreviousPoint = point;
                return;
            }

            if (state.PreviousPoint == point)
            {
                return;
            }

            this.EmitLine(state.PreviousPoint, point, contained, ref context);
            state.PreviousPoint = point;
        }

        /// <summary>
        /// Closes the active stroke contour.
        /// </summary>
        private void CloseContour(ref ContourState state, bool contained, ref Context context)
        {
            if (!state.HasPoint || state.PreviousPoint == state.FirstPoint)
            {
                return;
            }

            this.EmitLine(state.PreviousPoint, state.FirstPoint, contained, ref context);
            state.PreviousPoint = state.FirstPoint;
        }

        /// <summary>
        /// Processes one centerline contour.
        /// </summary>
        private void ProcessContour(in LinearContour contour, bool contained, ref Context context)
        {
            if (this.geometry.Points is PointF[] geometryPoints)
            {
                ReadOnlySpan<PointF> contourPoints = geometryPoints.AsSpan(contour.PointStart, contour.PointCount);
                int distinctPointCount = GetDistinctPointCount(contourPoints, contour.IsClosed);
                if (distinctPointCount == 0)
                {
                    return;
                }

                if (contour.IsClosed)
                {
                    switch (distinctPointCount)
                    {
                        case 1:
                            if (TryGetFirstDistinctPoint(contourPoints, out _, out PointF point))
                            {
                                this.EmitPointStrokeContour(point, contained, ref context);
                            }

                            return;
                        case 2:
                            this.EmitDistinctSegmentContour(contourPoints, contained, ref context);
                            return;
                        default:
                            this.EmitClosedStrokeContour(contourPoints, contained, ref context);
                            return;
                    }
                }

                if (distinctPointCount == 1)
                {
                    if (TryGetFirstDistinctPoint(contourPoints, out _, out PointF point))
                    {
                        this.EmitPointStrokeContour(point, contained, ref context);
                    }
                }
                else if (distinctPointCount == 2)
                {
                    this.EmitDistinctSegmentContour(contourPoints, contained, ref context);
                }
                else
                {
                    this.EmitOpenStrokeContour(contourPoints, contained, ref context);
                }

                return;
            }

            int pointCount = this.GetDistinctPointCount(contour);
            if (pointCount == 0)
            {
                return;
            }

            if (contour.IsClosed)
            {
                switch (pointCount)
                {
                    case 1:
                        if (this.TryGetFirstDistinctPoint(contour, out _, out PointF point))
                        {
                            this.EmitPointStrokeContour(point, contained, ref context);
                        }

                        return;
                    case 2:
                        this.EmitDistinctSegmentContour(contour, contained, ref context);
                        return;
                    default:
                        this.EmitClosedStrokeContour(contour, contained, ref context);
                        return;
                }
            }

            if (pointCount == 1)
            {
                if (this.TryGetFirstDistinctPoint(contour, out _, out PointF point))
                {
                    this.EmitPointStrokeContour(point, contained, ref context);
                }
            }
            else if (pointCount == 2)
            {
                this.EmitDistinctSegmentContour(contour, contained, ref context);
            }
            else
            {
                this.EmitOpenStrokeContour(contour, contained, ref context);
            }
        }

        /// <summary>
        /// Emits one distinct two-point contour without creating a temporary point buffer.
        /// </summary>
        private void EmitDistinctSegmentContour(in LinearContour contour, bool contained, ref Context context)
        {
            if (!this.TryGetFirstDistinctPoint(contour, out int firstIndex, out PointF start) ||
                !this.TryGetNextDistinctPoint(contour, firstIndex, out _, out PointF end))
            {
                return;
            }

            if (start == end)
            {
                this.EmitPointStrokeContour(start, contained, ref context);
                return;
            }

            this.EmitOpenSegmentStrokeContour(start, end, contained, ref context);
        }

        /// <summary>
        /// Emits one distinct two-point contour from contiguous point storage without creating a temporary point buffer.
        /// </summary>
        private void EmitDistinctSegmentContour(ReadOnlySpan<PointF> contourPoints, bool contained, ref Context context)
        {
            if (!TryGetFirstDistinctPoint(contourPoints, out int firstIndex, out PointF start) ||
                !TryGetNextDistinctPoint(contourPoints, firstIndex, out _, out PointF end))
            {
                return;
            }

            if (start == end)
            {
                this.EmitPointStrokeContour(start, contained, ref context);
                return;
            }

            this.EmitOpenSegmentStrokeContour(start, end, contained, ref context);
        }

        /// <summary>
        /// Emits one distinct two-point explicit polyline without creating a temporary point buffer.
        /// </summary>
        private void EmitDistinctSegmentContour(PointF[] points, bool contained, ref Context context)
        {
            if (!TryGetFirstDistinctPoint(points, out int firstIndex, out PointF start) ||
                !TryGetNextDistinctPoint(points, firstIndex, out _, out PointF end))
            {
                return;
            }

            if (start == end)
            {
                this.EmitPointStrokeContour(start, contained, ref context);
                return;
            }

            this.EmitOpenSegmentStrokeContour(start, end, contained, ref context);
        }

        /// <summary>
        /// Emits one stroked open segment.
        /// </summary>
        private void EmitOpenSegmentStrokeContour(PointF start, PointF end, bool contained, ref Context context)
        {
            if (!TryGetDirection(start, end, out Vector2 tangent, out _))
            {
                this.EmitPointStrokeContour(start, contained, ref context);
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 normal = GetLeftNormal(tangent) * halfWidth;
            Vector2 extension = this.stroke.LineCap == LineCap.Square ? tangent * halfWidth : Vector2.Zero;
            Vector2 startVector = start;
            Vector2 endVector = end;
            PointF p0 = startVector + normal - extension;
            PointF p1 = endVector + normal + extension;
            PointF p2 = endVector - normal + extension;
            PointF p3 = startVector - normal - extension;

            this.EmitLine(p0, p1, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.EmitDirectedArcContour(endVector, normal, -normal, tangent, contained, ref context);
            }
            else
            {
                this.EmitLine(p1, p2, contained, ref context);
            }

            this.EmitLine(p2, p3, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.EmitDirectedArcContour(startVector, -normal, normal, -tangent, contained, ref context);
            }
            else
            {
                this.EmitLine(p3, p0, contained, ref context);
            }
        }

        /// <summary>
        /// Emits one stroked open multi-segment contour.
        /// </summary>
        private void EmitOpenStrokeContour(in LinearContour contour, bool contained, ref Context context)
        {
            if (!this.TryGetFirstDistinctPoint(contour, out int firstIndex, out PointF firstPoint) ||
                !this.TryGetNextDistinctPoint(contour, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !this.TryGetLastDistinctPoint(contour, out int lastIndex, out PointF lastPoint) ||
                !this.TryGetPreviousDistinctPoint(contour, lastIndex, out _, out PointF beforeLastPoint) ||
                !TryGetDirection(firstPoint, secondPoint, out Vector2 startTangent, out _) ||
                !TryGetDirection(beforeLastPoint, lastPoint, out Vector2 endTangent, out _))
            {
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 startNormal = GetLeftNormal(startTangent) * halfWidth;
            Vector2 endNormal = GetLeftNormal(endTangent) * halfWidth;
            Vector2 startExtension = this.stroke.LineCap == LineCap.Square ? startTangent * halfWidth : Vector2.Zero;
            Vector2 endExtension = this.stroke.LineCap == LineCap.Square ? endTangent * halfWidth : Vector2.Zero;
            Vector2 startPoint = firstPoint;
            Vector2 endPoint = lastPoint;
            ContourState strokeContour = default;
            this.AppendContourPoint(ref strokeContour, startPoint + startNormal - startExtension, contained, ref context);

            PointF previousPoint = firstPoint;
            int currentIndex = secondIndex;
            PointF currentPoint = secondPoint;
            while (this.TryGetNextDistinctPoint(contour, currentIndex, out int nextIndex, out PointF nextPoint))
            {
                if (!TryGetDirection(previousPoint, currentPoint, out Vector2 previousTangent, out float previousLength) ||
                    !TryGetDirection(currentPoint, nextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    previousPoint = currentPoint;
                    currentIndex = nextIndex;
                    currentPoint = nextPoint;
                    continue;
                }

                this.AppendSideJoinContour(
                    ref strokeContour,
                    currentPoint,
                    previousTangent,
                    nextTangent,
                    previousLength,
                    nextLength,
                    1F,
                    contained,
                    ref context);

                previousPoint = currentPoint;
                currentIndex = nextIndex;
                currentPoint = nextPoint;
            }

            this.AppendContourPoint(ref strokeContour, endPoint + endNormal + endExtension, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, endPoint, endNormal, -endNormal, endTangent, contained, ref context);
            }
            else
            {
                this.AppendContourPoint(ref strokeContour, endPoint - endNormal + endExtension, contained, ref context);
            }

            PointF reversePreviousPoint = lastPoint;
            int reverseCurrentIndex = lastIndex;
            PointF reverseCurrentPoint = beforeLastPoint;
            if (this.TryGetPreviousDistinctPoint(contour, reverseCurrentIndex, out int beforeLastIndex, out _))
            {
                reverseCurrentIndex = beforeLastIndex;
            }

            while (this.TryGetPreviousDistinctPoint(contour, reverseCurrentIndex, out int nextReverseIndex, out PointF nextReversePoint))
            {
                if (!TryGetDirection(reversePreviousPoint, reverseCurrentPoint, out Vector2 previousTangent, out float previousLength) ||
                    !TryGetDirection(reverseCurrentPoint, nextReversePoint, out Vector2 nextTangent, out float nextLength))
                {
                    reversePreviousPoint = reverseCurrentPoint;
                    reverseCurrentIndex = nextReverseIndex;
                    reverseCurrentPoint = nextReversePoint;
                    continue;
                }

                this.AppendSideJoinContour(
                    ref strokeContour,
                    reverseCurrentPoint,
                    previousTangent,
                    nextTangent,
                    previousLength,
                    nextLength,
                    1F,
                    contained,
                    ref context);

                reversePreviousPoint = reverseCurrentPoint;
                reverseCurrentIndex = nextReverseIndex;
                reverseCurrentPoint = nextReversePoint;
            }

            this.AppendContourPoint(ref strokeContour, startPoint - startNormal - startExtension, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, startPoint, -startNormal, startNormal, -startTangent, contained, ref context);
            }

            this.CloseContour(ref strokeContour, contained, ref context);
        }

        /// <summary>
        /// Emits one stroked open multi-segment contour from contiguous point storage.
        /// </summary>
        private void EmitOpenStrokeContour(ReadOnlySpan<PointF> contourPoints, bool contained, ref Context context)
        {
            if (!TryGetFirstDistinctPoint(contourPoints, out int firstIndex, out PointF firstPoint) ||
                !TryGetNextDistinctPoint(contourPoints, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !TryGetLastDistinctPoint(contourPoints, out int lastIndex, out PointF lastPoint) ||
                !TryGetPreviousDistinctPoint(contourPoints, lastIndex, out _, out PointF beforeLastPoint) ||
                !TryGetDirection(firstPoint, secondPoint, out Vector2 startTangent, out _) ||
                !TryGetDirection(beforeLastPoint, lastPoint, out Vector2 endTangent, out _))
            {
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 startNormal = GetLeftNormal(startTangent) * halfWidth;
            Vector2 endNormal = GetLeftNormal(endTangent) * halfWidth;
            Vector2 startExtension = this.stroke.LineCap == LineCap.Square ? startTangent * halfWidth : Vector2.Zero;
            Vector2 endExtension = this.stroke.LineCap == LineCap.Square ? endTangent * halfWidth : Vector2.Zero;
            Vector2 startPoint = firstPoint;
            Vector2 endPoint = lastPoint;
            ContourState strokeContour = default;
            this.AppendContourPoint(ref strokeContour, startPoint + startNormal - startExtension, contained, ref context);

            PointF previousPoint = firstPoint;
            int currentIndex = secondIndex;
            PointF currentPoint = secondPoint;
            while (TryGetNextDistinctPoint(contourPoints, currentIndex, out int nextIndex, out PointF nextPoint))
            {
                if (!TryGetDirection(previousPoint, currentPoint, out Vector2 previousTangent, out float previousLength) ||
                    !TryGetDirection(currentPoint, nextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    previousPoint = currentPoint;
                    currentIndex = nextIndex;
                    currentPoint = nextPoint;
                    continue;
                }

                this.AppendSideJoinContour(
                    ref strokeContour,
                    currentPoint,
                    previousTangent,
                    nextTangent,
                    previousLength,
                    nextLength,
                    1F,
                    contained,
                    ref context);

                previousPoint = currentPoint;
                currentIndex = nextIndex;
                currentPoint = nextPoint;
            }

            this.AppendContourPoint(ref strokeContour, endPoint + endNormal + endExtension, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, endPoint, endNormal, -endNormal, endTangent, contained, ref context);
            }
            else
            {
                this.AppendContourPoint(ref strokeContour, endPoint - endNormal + endExtension, contained, ref context);
            }

            PointF reversePreviousPoint = lastPoint;
            int reverseCurrentIndex = lastIndex;
            PointF reverseCurrentPoint = beforeLastPoint;
            if (TryGetPreviousDistinctPoint(contourPoints, reverseCurrentIndex, out int beforeLastIndex, out _))
            {
                reverseCurrentIndex = beforeLastIndex;
            }

            while (TryGetPreviousDistinctPoint(contourPoints, reverseCurrentIndex, out int nextReverseIndex, out PointF nextReversePoint))
            {
                if (!TryGetDirection(reversePreviousPoint, reverseCurrentPoint, out Vector2 previousTangent, out float previousLength) ||
                    !TryGetDirection(reverseCurrentPoint, nextReversePoint, out Vector2 nextTangent, out float nextLength))
                {
                    reversePreviousPoint = reverseCurrentPoint;
                    reverseCurrentIndex = nextReverseIndex;
                    reverseCurrentPoint = nextReversePoint;
                    continue;
                }

                this.AppendSideJoinContour(
                    ref strokeContour,
                    reverseCurrentPoint,
                    previousTangent,
                    nextTangent,
                    previousLength,
                    nextLength,
                    1F,
                    contained,
                    ref context);

                reversePreviousPoint = reverseCurrentPoint;
                reverseCurrentIndex = nextReverseIndex;
                reverseCurrentPoint = nextReversePoint;
            }

            this.AppendContourPoint(ref strokeContour, startPoint - startNormal - startExtension, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, startPoint, -startNormal, startNormal, -startTangent, contained, ref context);
            }

            this.CloseContour(ref strokeContour, contained, ref context);
        }

        /// <summary>
        /// Emits one stroked explicit open multi-segment polyline.
        /// </summary>
        private void EmitOpenStrokeContour(PointF[] points, bool contained, ref Context context)
        {
            if (!TryGetFirstDistinctPoint(points, out int firstIndex, out PointF firstPoint) ||
                !TryGetNextDistinctPoint(points, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !TryGetLastDistinctPoint(points, out int lastIndex, out PointF lastPoint) ||
                !TryGetPreviousDistinctPoint(points, lastIndex, out _, out PointF beforeLastPoint) ||
                !TryGetDirection(firstPoint, secondPoint, out Vector2 startTangent, out _) ||
                !TryGetDirection(beforeLastPoint, lastPoint, out Vector2 endTangent, out _))
            {
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 startNormal = GetLeftNormal(startTangent) * halfWidth;
            Vector2 endNormal = GetLeftNormal(endTangent) * halfWidth;
            Vector2 startExtension = this.stroke.LineCap == LineCap.Square ? startTangent * halfWidth : Vector2.Zero;
            Vector2 endExtension = this.stroke.LineCap == LineCap.Square ? endTangent * halfWidth : Vector2.Zero;
            Vector2 startPoint = firstPoint;
            Vector2 endPoint = lastPoint;
            ContourState strokeContour = default;
            this.AppendContourPoint(ref strokeContour, startPoint + startNormal - startExtension, contained, ref context);

            PointF previousPoint = firstPoint;
            int currentIndex = secondIndex;
            PointF currentPoint = secondPoint;
            while (TryGetNextDistinctPoint(points, currentIndex, out int nextIndex, out PointF nextPoint))
            {
                if (!TryGetDirection(previousPoint, currentPoint, out Vector2 previousTangent, out float previousLength) ||
                    !TryGetDirection(currentPoint, nextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    previousPoint = currentPoint;
                    currentIndex = nextIndex;
                    currentPoint = nextPoint;
                    continue;
                }

                this.AppendSideJoinContour(
                    ref strokeContour,
                    currentPoint,
                    previousTangent,
                    nextTangent,
                    previousLength,
                    nextLength,
                    1F,
                    contained,
                    ref context);

                previousPoint = currentPoint;
                currentIndex = nextIndex;
                currentPoint = nextPoint;
            }

            this.AppendContourPoint(ref strokeContour, endPoint + endNormal + endExtension, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, endPoint, endNormal, -endNormal, endTangent, contained, ref context);
            }
            else
            {
                this.AppendContourPoint(ref strokeContour, endPoint - endNormal + endExtension, contained, ref context);
            }

            PointF reversePreviousPoint = lastPoint;
            int reverseCurrentIndex = lastIndex;
            PointF reverseCurrentPoint = beforeLastPoint;
            if (TryGetPreviousDistinctPoint(points, reverseCurrentIndex, out int beforeLastIndex, out _))
            {
                reverseCurrentIndex = beforeLastIndex;
            }

            while (TryGetPreviousDistinctPoint(points, reverseCurrentIndex, out int nextReverseIndex, out PointF nextReversePoint))
            {
                if (!TryGetDirection(reversePreviousPoint, reverseCurrentPoint, out Vector2 previousTangent, out float previousLength) ||
                    !TryGetDirection(reverseCurrentPoint, nextReversePoint, out Vector2 nextTangent, out float nextLength))
                {
                    reversePreviousPoint = reverseCurrentPoint;
                    reverseCurrentIndex = nextReverseIndex;
                    reverseCurrentPoint = nextReversePoint;
                    continue;
                }

                this.AppendSideJoinContour(
                    ref strokeContour,
                    reverseCurrentPoint,
                    previousTangent,
                    nextTangent,
                    previousLength,
                    nextLength,
                    1F,
                    contained,
                    ref context);

                reversePreviousPoint = reverseCurrentPoint;
                reverseCurrentIndex = nextReverseIndex;
                reverseCurrentPoint = nextReversePoint;
            }

            this.AppendContourPoint(ref strokeContour, startPoint - startNormal - startExtension, contained, ref context);
            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, startPoint, -startNormal, startNormal, -startTangent, contained, ref context);
            }

            this.CloseContour(ref strokeContour, contained, ref context);
        }

        /// <summary>
        /// Emits the two stroked contours for a closed centerline contour.
        /// </summary>
        private void EmitClosedStrokeContour(in LinearContour contour, bool contained, ref Context context)
        {
            if (!this.TryGetFirstDistinctPoint(contour, out int firstIndex, out PointF firstPoint) ||
                !this.TryGetNextDistinctPoint(contour, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !this.TryGetLastDistinctPoint(contour, out int lastIndex, out PointF lastPoint) ||
                !this.TryGetPreviousDistinctPoint(contour, lastIndex, out int beforeLastIndex, out PointF beforeLastPoint))
            {
                return;
            }

            ContourState leftContour = default;
            PointF previousPoint = lastPoint;
            int currentIndex = firstIndex;
            PointF currentPoint = firstPoint;
            int nextIndex = secondIndex;
            PointF nextPoint = secondPoint;

            while (true)
            {
                if (TryGetDirection(previousPoint, currentPoint, out Vector2 previousTangent, out float previousLength) &&
                    TryGetDirection(currentPoint, nextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    this.AppendSideJoinContour(
                        ref leftContour,
                        currentPoint,
                        previousTangent,
                        nextTangent,
                        previousLength,
                        nextLength,
                        1F,
                        contained,
                        ref context);
                }

                if (currentIndex == lastIndex)
                {
                    break;
                }

                previousPoint = currentPoint;
                currentIndex = nextIndex;
                currentPoint = nextPoint;
                if (!this.TryGetNextDistinctPoint(contour, currentIndex, out nextIndex, out nextPoint))
                {
                    nextIndex = firstIndex;
                    nextPoint = firstPoint;
                }
            }

            this.CloseContour(ref leftContour, contained, ref context);

            ContourState reversedContour = default;
            PointF reversePreviousPoint = firstPoint;
            int reverseCurrentIndex = lastIndex;
            PointF reverseCurrentPoint = lastPoint;
            int reverseNextIndex = beforeLastIndex;
            PointF reverseNextPoint = beforeLastPoint;

            while (true)
            {
                if (TryGetDirection(reversePreviousPoint, reverseCurrentPoint, out Vector2 previousTangent, out float previousLength) &&
                    TryGetDirection(reverseCurrentPoint, reverseNextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    this.AppendSideJoinContour(
                        ref reversedContour,
                        reverseCurrentPoint,
                        previousTangent,
                        nextTangent,
                        previousLength,
                        nextLength,
                        1F,
                        contained,
                        ref context);
                }

                if (reverseCurrentIndex == firstIndex)
                {
                    break;
                }

                reversePreviousPoint = reverseCurrentPoint;
                reverseCurrentIndex = reverseNextIndex;
                reverseCurrentPoint = reverseNextPoint;
                if (!this.TryGetPreviousDistinctPoint(contour, reverseCurrentIndex, out reverseNextIndex, out reverseNextPoint))
                {
                    reverseNextIndex = lastIndex;
                    reverseNextPoint = lastPoint;
                }
            }

            this.CloseContour(ref reversedContour, contained, ref context);
        }

        /// <summary>
        /// Emits the two stroked contours for a closed contour from contiguous point storage.
        /// </summary>
        private void EmitClosedStrokeContour(ReadOnlySpan<PointF> contourPoints, bool contained, ref Context context)
        {
            if (!TryGetFirstDistinctPoint(contourPoints, out int firstIndex, out PointF firstPoint) ||
                !TryGetNextDistinctPoint(contourPoints, firstIndex, out int secondIndex, out PointF secondPoint) ||
                !TryGetLastDistinctPoint(contourPoints, out int lastIndex, out PointF lastPoint) ||
                !TryGetPreviousDistinctPoint(contourPoints, lastIndex, out int beforeLastIndex, out PointF beforeLastPoint))
            {
                return;
            }

            ContourState leftContour = default;
            PointF previousPoint = lastPoint;
            int currentIndex = firstIndex;
            PointF currentPoint = firstPoint;
            int nextIndex = secondIndex;
            PointF nextPoint = secondPoint;

            while (true)
            {
                if (TryGetDirection(previousPoint, currentPoint, out Vector2 previousTangent, out float previousLength) &&
                    TryGetDirection(currentPoint, nextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    this.AppendSideJoinContour(
                        ref leftContour,
                        currentPoint,
                        previousTangent,
                        nextTangent,
                        previousLength,
                        nextLength,
                        1F,
                        contained,
                        ref context);
                }

                if (currentIndex == lastIndex)
                {
                    break;
                }

                previousPoint = currentPoint;
                currentIndex = nextIndex;
                currentPoint = nextPoint;
                if (!TryGetNextDistinctPoint(contourPoints, currentIndex, out nextIndex, out nextPoint))
                {
                    nextIndex = firstIndex;
                    nextPoint = firstPoint;
                }
            }

            this.CloseContour(ref leftContour, contained, ref context);

            ContourState reversedContour = default;
            PointF reversePreviousPoint = firstPoint;
            int reverseCurrentIndex = lastIndex;
            PointF reverseCurrentPoint = lastPoint;
            int reverseNextIndex = beforeLastIndex;
            PointF reverseNextPoint = beforeLastPoint;

            while (true)
            {
                if (TryGetDirection(reversePreviousPoint, reverseCurrentPoint, out Vector2 previousTangent, out float previousLength) &&
                    TryGetDirection(reverseCurrentPoint, reverseNextPoint, out Vector2 nextTangent, out float nextLength))
                {
                    this.AppendSideJoinContour(
                        ref reversedContour,
                        reverseCurrentPoint,
                        previousTangent,
                        nextTangent,
                        previousLength,
                        nextLength,
                        1F,
                        contained,
                        ref context);
                }

                if (reverseCurrentIndex == firstIndex)
                {
                    break;
                }

                reversePreviousPoint = reverseCurrentPoint;
                reverseCurrentIndex = reverseNextIndex;
                reverseCurrentPoint = reverseNextPoint;
                if (!TryGetPreviousDistinctPoint(contourPoints, reverseCurrentIndex, out reverseNextIndex, out reverseNextPoint))
                {
                    reverseNextIndex = lastIndex;
                    reverseNextPoint = lastPoint;
                }
            }

            this.CloseContour(ref reversedContour, contained, ref context);
        }

        /// <summary>
        /// Emits a point-like stroke as a cap contour.
        /// </summary>
        private void EmitPointStrokeContour(PointF point, bool contained, ref Context context)
        {
            Vector2 center = point;
            float halfWidth = this.stroke.HalfWidth;
            if (this.stroke.LineCap == LineCap.Round)
            {
                Vector2 startOffset = new(halfWidth, 0F);
                this.EmitDirectedArcContour(center, startOffset, -startOffset, Vector2.UnitY, contained, ref context);
                this.EmitDirectedArcContour(center, -startOffset, startOffset, -Vector2.UnitY, contained, ref context);
                return;
            }

            PointF p0 = center + new Vector2(-halfWidth, -halfWidth);
            PointF p1 = center + new Vector2(halfWidth, -halfWidth);
            PointF p2 = center + new Vector2(halfWidth, halfWidth);
            PointF p3 = center + new Vector2(-halfWidth, halfWidth);
            this.EmitLine(p0, p1, contained, ref context);
            this.EmitLine(p1, p2, contained, ref context);
            this.EmitLine(p2, p3, contained, ref context);
            this.EmitLine(p3, p0, contained, ref context);
        }

        /// <summary>
        /// Emits one round cap or join arc directly into the active band context.
        /// </summary>
        private void EmitDirectedArcContour(
            Vector2 center,
            Vector2 fromOffset,
            Vector2 toOffset,
            Vector2 preferredDirection,
            bool contained,
            ref Context context)
        {
            if (fromOffset == Vector2.Zero || toOffset == Vector2.Zero)
            {
                this.EmitLine(center + fromOffset, center + toOffset, contained, ref context);
                return;
            }

            float radius = fromOffset.Length();
            if (radius <= StrokeDirectionEpsilon)
            {
                this.EmitLine(center + fromOffset, center + toOffset, contained, ref context);
                return;
            }

            Vector2 preferred = preferredDirection;
            if (preferred.LengthSquared() <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
            {
                preferred = Vector2.Normalize(fromOffset + toOffset);
                if (preferred.LengthSquared() <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
                {
                    preferred = Vector2.Normalize(fromOffset);
                }
            }

            double startAngle = Math.Atan2(fromOffset.Y, fromOffset.X);
            double endAngle = Math.Atan2(toOffset.Y, toOffset.X);
            double ccwSweep = NormalizePositiveAngle(endAngle - startAngle);
            double cwSweep = ccwSweep - (Math.PI * 2D);
            double chosenSweep = ChooseArcSweep(startAngle, ccwSweep, cwSweep, preferred);
            int subdivisionCount = GetArcSubdivisionCount(radius, Math.Abs(chosenSweep), this.stroke.ArcDetailScale);
            double step = chosenSweep / (subdivisionCount + 1);

            PointF previousPoint = center + fromOffset;
            for (int i = 1; i <= subdivisionCount; i++)
            {
                float angle = (float)(startAngle + (step * i));
                PointF point = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                this.EmitLine(previousPoint, point, contained, ref context);
                previousPoint = point;
            }

            this.EmitLine(previousPoint, center + toOffset, contained, ref context);
        }

        /// <summary>
        /// Appends a contour arc directly to the active stroke contour.
        /// </summary>
        private void AppendDirectedArcContour(
            ref ContourState contour,
            Vector2 center,
            Vector2 fromOffset,
            Vector2 toOffset,
            Vector2 preferredDirection,
            bool contained,
            ref Context context)
        {
            if (fromOffset == Vector2.Zero || toOffset == Vector2.Zero)
            {
                this.AppendContourPoint(ref contour, center + toOffset, contained, ref context);
                return;
            }

            float radius = fromOffset.Length();
            if (radius <= StrokeDirectionEpsilon)
            {
                this.AppendContourPoint(ref contour, center + toOffset, contained, ref context);
                return;
            }

            Vector2 preferred = preferredDirection;
            if (preferred.LengthSquared() <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
            {
                preferred = Vector2.Normalize(fromOffset + toOffset);
                if (preferred.LengthSquared() <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
                {
                    preferred = Vector2.Normalize(fromOffset);
                }
            }

            double startAngle = Math.Atan2(fromOffset.Y, fromOffset.X);
            double endAngle = Math.Atan2(toOffset.Y, toOffset.X);
            double ccwSweep = NormalizePositiveAngle(endAngle - startAngle);
            double cwSweep = ccwSweep - (Math.PI * 2D);
            double chosenSweep = ChooseArcSweep(startAngle, ccwSweep, cwSweep, preferred);
            int subdivisionCount = GetArcSubdivisionCount(radius, Math.Abs(chosenSweep), this.stroke.ArcDetailScale);
            double step = chosenSweep / (subdivisionCount + 1);

            for (int i = 1; i <= subdivisionCount; i++)
            {
                float angle = (float)(startAngle + (step * i));
                this.AppendContourPoint(
                    ref contour,
                    center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius),
                    contained,
                    ref context);
            }

            this.AppendContourPoint(ref contour, center + toOffset, contained, ref context);
        }

        /// <summary>
        /// Appends one side join point sequence directly to the active stroke contour.
        /// </summary>
        private void AppendSideJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            float previousLength,
            float nextLength,
            float sideSign,
            bool contained,
            ref Context context)
        {
            Vector2 previousOffset = GetLeftNormal(previousTangent) * (this.stroke.HalfWidth * sideSign);
            Vector2 nextOffset = GetLeftNormal(nextTangent) * (this.stroke.HalfWidth * sideSign);
            float dot = Vector2.Dot(previousTangent, nextTangent);
            float cross = Cross(previousTangent, nextTangent);
            if (MathF.Abs(cross) <= StrokeParallelEpsilon && dot > 0F)
            {
                this.AppendContourPoint(ref contour, point + nextOffset, contained, ref context);
                return;
            }

            bool isOuterJoin = cross * sideSign > 0F;
            if (!isOuterJoin)
            {
                this.AppendInnerJoinContour(
                    ref contour,
                    point,
                    previousTangent,
                    nextTangent,
                    previousOffset,
                    nextOffset,
                    previousLength,
                    nextLength,
                    contained,
                    ref context);
                return;
            }

            this.AppendOuterJoinContour(
                ref contour,
                point,
                previousTangent,
                nextTangent,
                previousOffset,
                nextOffset,
                contained,
                ref context);
        }

        /// <summary>
        /// Appends one outer join directly to the active stroke contour.
        /// </summary>
        private void AppendOuterJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            Vector2 previousOffset,
            Vector2 nextOffset,
            bool contained,
            ref Context context)
        {
            switch (this.stroke.LineJoin)
            {
                case LineJoin.Round:
                    this.AppendContourPoint(ref contour, point + previousOffset, contained, ref context);
                    this.AppendDirectedArcContour(
                        ref contour,
                        point,
                        previousOffset,
                        nextOffset,
                        GetPreferredArcDirection(previousOffset, nextOffset),
                        contained,
                        ref context);
                    return;

                case LineJoin.Miter:
                case LineJoin.MiterRevert:
                case LineJoin.MiterRound:
                    this.AppendMiterJoinContour(
                        ref contour,
                        point,
                        previousTangent,
                        nextTangent,
                        previousOffset,
                        nextOffset,
                        this.stroke.LineJoin,
                        this.stroke.MiterLimit,
                        contained,
                        ref context);
                    return;

                default:
                    this.AppendContourPoint(ref contour, point + previousOffset, contained, ref context);
                    this.AppendContourPoint(ref contour, point + nextOffset, contained, ref context);
                    return;
            }
        }

        /// <summary>
        /// Appends one inner join directly to the active stroke contour.
        /// </summary>
        private void AppendInnerJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            Vector2 previousOffset,
            Vector2 nextOffset,
            float previousLength,
            float nextLength,
            bool contained,
            ref Context context)
        {
            double limit = Math.Min(previousLength, nextLength) / Math.Max(this.stroke.HalfWidth, StrokeDirectionEpsilon);
            if (limit < this.stroke.InnerMiterLimit)
            {
                limit = this.stroke.InnerMiterLimit;
            }

            switch (this.stroke.InnerJoin)
            {
                case InnerJoin.Miter:
                    this.AppendMiterJoinContour(
                        ref contour,
                        point,
                        previousTangent,
                        nextTangent,
                        previousOffset,
                        nextOffset,
                        LineJoin.MiterRevert,
                        limit,
                        contained,
                        ref context);
                    return;

                case InnerJoin.Jag:
                case InnerJoin.Round:
                    Vector2 offsetDelta = previousOffset - nextOffset;
                    float offsetDeltaSquared = offsetDelta.LengthSquared();
                    if (offsetDeltaSquared < previousLength * previousLength && offsetDeltaSquared < nextLength * nextLength)
                    {
                        this.AppendMiterJoinContour(
                            ref contour,
                            point,
                            previousTangent,
                            nextTangent,
                            previousOffset,
                            nextOffset,
                            LineJoin.MiterRevert,
                            limit,
                            contained,
                            ref context);
                        return;
                    }

                    this.AppendContourPoint(ref contour, point + previousOffset, contained, ref context);
                    this.AppendContourPoint(ref contour, point, contained, ref context);
                    if (this.stroke.InnerJoin == InnerJoin.Round)
                    {
                        this.AppendContourPoint(ref contour, point + nextOffset, contained, ref context);
                        this.AppendDirectedArcContour(
                            ref contour,
                            point,
                            nextOffset,
                            previousOffset,
                            -GetPreferredArcDirection(previousOffset, nextOffset),
                            contained,
                            ref context);
                    }

                    this.AppendContourPoint(ref contour, point, contained, ref context);
                    this.AppendContourPoint(ref contour, point + nextOffset, contained, ref context);
                    return;

                default:
                    this.AppendContourPoint(ref contour, point + previousOffset, contained, ref context);
                    this.AppendContourPoint(ref contour, point + nextOffset, contained, ref context);
                    return;
            }
        }

        /// <summary>
        /// Appends one miter-family join directly to the active stroke contour.
        /// </summary>
        private void AppendMiterJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            Vector2 previousOffset,
            Vector2 nextOffset,
            LineJoin lineJoin,
            double miterLimit,
            bool contained,
            ref Context context)
        {
            Vector2 previousPoint = point + previousOffset;
            Vector2 nextPoint = point + nextOffset;
            float bevelDistance = ((previousOffset + nextOffset) * 0.5F).Length();
            float limit = (float)(Math.Max(miterLimit, 1D) * Math.Max(previousOffset.Length(), nextOffset.Length()));

            if (TryIntersectOffsetLines(point, previousOffset, previousTangent, nextOffset, nextTangent, out Vector2 intersection))
            {
                float intersectionDistance = Vector2.Distance(point, intersection);
                if (intersectionDistance <= limit)
                {
                    this.AppendContourPoint(ref contour, intersection, contained, ref context);
                    return;
                }

                switch (lineJoin)
                {
                    case LineJoin.MiterRevert:
                        this.AppendContourPoint(ref contour, previousPoint, contained, ref context);
                        this.AppendContourPoint(ref contour, nextPoint, contained, ref context);
                        return;

                    case LineJoin.MiterRound:
                        this.AppendDirectedArcContour(
                            ref contour,
                            point,
                            previousOffset,
                            nextOffset,
                            GetPreferredArcDirection(previousOffset, nextOffset),
                            contained,
                            ref context);
                        return;

                    default:
                        if (intersectionDistance <= bevelDistance + StrokeDirectionEpsilon)
                        {
                            this.AppendContourPoint(ref contour, previousPoint, contained, ref context);
                            this.AppendContourPoint(ref contour, nextPoint, contained, ref context);
                            return;
                        }

                        float ratio = (limit - bevelDistance) / (intersectionDistance - bevelDistance);
                        this.AppendContourPoint(ref contour, previousPoint + ((intersection - previousPoint) * ratio), contained, ref context);
                        this.AppendContourPoint(ref contour, nextPoint + ((intersection - nextPoint) * ratio), contained, ref context);
                        return;
                }
            }

            switch (lineJoin)
            {
                case LineJoin.MiterRevert:
                    this.AppendContourPoint(ref contour, previousPoint, contained, ref context);
                    this.AppendContourPoint(ref contour, nextPoint, contained, ref context);
                    return;

                case LineJoin.MiterRound:
                    this.AppendDirectedArcContour(
                        ref contour,
                        point,
                        previousOffset,
                        nextOffset,
                        GetPreferredArcDirection(previousOffset, nextOffset),
                        contained,
                        ref context);
                    return;

                default:
                    float fallbackLimit = (float)Math.Max(miterLimit, 1D) * (previousOffset.Length() <= 0F ? nextOffset.Length() : previousOffset.Length());
                    this.AppendContourPoint(ref contour, previousPoint + (previousTangent * fallbackLimit), contained, ref context);
                    this.AppendContourPoint(ref contour, nextPoint - (nextTangent * fallbackLimit), contained, ref context);
                    return;
            }
        }

        /// <summary>
        /// Emits one stroked boundary edge into the active band context.
        /// </summary>
        private void EmitLine(PointF start, PointF end, bool contained, ref Context context)
        {
            if (contained)
            {
                RasterizeContainedLineF24Dot8(
                    ref context,
                    FloatToFixed24Dot8(((start.X + this.translateX) - this.minX) + this.samplingOffsetX),
                    FloatToFixed24Dot8(((start.Y + this.translateY) - this.minY) + this.samplingOffsetY),
                    FloatToFixed24Dot8(((end.X + this.translateX) - this.minX) + this.samplingOffsetX),
                    FloatToFixed24Dot8(((end.Y + this.translateY) - this.minY) + this.samplingOffsetY));
            }
            else
            {
                this.AddUncontainedLine(
                    ref context,
                    ((start.X + this.translateX) - this.minX) + this.samplingOffsetX,
                    ((start.Y + this.translateY) - this.minY) + this.samplingOffsetY,
                    ((end.X + this.translateX) - this.minX) + this.samplingOffsetX,
                    ((end.Y + this.translateY) - this.minY) + this.samplingOffsetY);
            }
        }

        /// <summary>
        /// Rasterizes one fully contained line in 24.8 fixed-point band-local coordinates.
        /// </summary>
        private static void RasterizeContainedLineF24Dot8(ref Context context, int x0, int y0, int x1, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            if (dx > MaximumDelta || dy > MaximumDelta)
            {
                int mx = (x0 + x1) >> 1;
                int my = (y0 + y1) >> 1;
                RasterizeContainedLineF24Dot8(ref context, x0, y0, mx, my);
                RasterizeContainedLineF24Dot8(ref context, mx, my, x1, y1);
                return;
            }

            context.RasterizeLineSegment(x0, y0, x1, y1);
        }

        /// <summary>
        /// Clips one edge to the active band and rasterizes the visible portion.
        /// </summary>
        private void AddUncontainedLine(ref Context context, float x0, float y0, float x1, float y1)
        {
            if (y0 == y1)
            {
                return;
            }

            if (y0 <= 0F && y1 <= 0F)
            {
                return;
            }

            if (y0 >= this.height && y1 >= this.height)
            {
                return;
            }

            if (x0 >= this.width && x1 >= this.width)
            {
                return;
            }

            if (x0 == x1)
            {
                int x0c = Math.Clamp(FloatToFixed24Dot8(x0), 0, this.width * FixedOne);
                int p0y = Math.Clamp(FloatToFixed24Dot8(y0), 0, this.height * FixedOne);
                int p1y = Math.Clamp(FloatToFixed24Dot8(y1), 0, this.height * FixedOne);

                if (x0c == 0)
                {
                    context.AddClippedStartCover(p0y, p1y);
                }
                else
                {
                    RasterizeContainedLineF24Dot8(ref context, x0c, p0y, x0c, p1y);
                }

                return;
            }

            double deltayV = Math.Abs(y1 - y0);
            double deltaxV = x1 - x0;
            double rx0 = x0;
            double ry0 = y0;
            double rx1 = x1;
            double ry1 = y1;

            if (y1 > y0)
            {
                if (y0 < 0F)
                {
                    double t = -y0 / deltayV;
                    rx0 = x0 + (deltaxV * t);
                    ry0 = 0D;
                }

                if (y1 > this.height)
                {
                    double t = (this.height - y0) / deltayV;
                    rx1 = x0 + (deltaxV * t);
                    ry1 = this.height;
                }
            }
            else
            {
                if (y0 > this.height)
                {
                    double t = (y0 - this.height) / deltayV;
                    rx0 = x0 + (deltaxV * t);
                    ry0 = this.height;
                }

                if (y1 < 0F)
                {
                    double t = y0 / deltayV;
                    rx1 = x0 + (deltaxV * t);
                    ry1 = 0D;
                }
            }

            if (rx0 >= this.width && rx1 >= this.width)
            {
                return;
            }

            if (rx0 > 0D && rx1 > 0D && rx0 < this.width && rx1 < this.width)
            {
                RasterizeContainedLineF24Dot8(
                    ref context,
                    Math.Clamp(FloatToFixed24Dot8((float)rx0), 0, this.width * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.height * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)rx1), 0, this.width * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.height * FixedOne));
                return;
            }

            if (rx0 <= 0D && rx1 <= 0D)
            {
                context.AddClippedStartCover(
                    Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.height * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.height * FixedOne));
                return;
            }

            double deltayH = ry1 - ry0;
            double deltaxH = Math.Abs(rx1 - rx0);

            if (rx1 > rx0)
            {
                double bx1 = rx1;
                double by1 = ry1;

                if (rx1 > this.width)
                {
                    double t = (this.width - rx0) / deltaxH;
                    by1 = ry0 + (deltayH * t);
                    bx1 = this.width;
                }

                if (rx0 < 0D)
                {
                    double t = -rx0 / deltaxH;
                    int a = Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.height * FixedOne);
                    int by = Math.Clamp(FloatToFixed24Dot8((float)(ry0 + (deltayH * t))), 0, this.height * FixedOne);
                    int cx = Math.Clamp(FloatToFixed24Dot8((float)bx1), 0, this.width * FixedOne);
                    int cy = Math.Clamp(FloatToFixed24Dot8((float)by1), 0, this.height * FixedOne);

                    context.AddClippedStartCover(a, by);
                    RasterizeContainedLineF24Dot8(ref context, 0, by, cx, cy);
                }
                else
                {
                    RasterizeContainedLineF24Dot8(
                        ref context,
                        Math.Clamp(FloatToFixed24Dot8((float)rx0), 0, this.width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.height * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)bx1), 0, this.width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)by1), 0, this.height * FixedOne));
                }
            }
            else
            {
                double bx0 = rx0;
                double by0 = ry0;

                if (rx0 > this.width)
                {
                    double t = (rx0 - this.width) / deltaxH;
                    by0 = ry0 + (deltayH * t);
                    bx0 = this.width;
                }

                if (rx1 < 0D)
                {
                    double t = rx0 / deltaxH;
                    int ax = Math.Clamp(FloatToFixed24Dot8((float)bx0), 0, this.width * FixedOne);
                    int ay = Math.Clamp(FloatToFixed24Dot8((float)by0), 0, this.height * FixedOne);
                    int by = Math.Clamp(FloatToFixed24Dot8((float)(ry0 + (deltayH * t))), 0, this.height * FixedOne);
                    int c = Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.height * FixedOne);

                    RasterizeContainedLineF24Dot8(ref context, ax, ay, 0, by);
                    context.AddClippedStartCover(by, c);
                }
                else
                {
                    RasterizeContainedLineF24Dot8(
                        ref context,
                        Math.Clamp(FloatToFixed24Dot8((float)bx0), 0, this.width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)by0), 0, this.height * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)rx1), 0, this.width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.height * FixedOne));
                }
            }
        }

        /// <summary>
        /// Counts the distinct contour points while skipping immediate duplicates.
        /// </summary>
        private int GetDistinctPointCount(in LinearContour contour)
        {
            int pointEnd = contour.PointStart + contour.PointCount;
            int count = 0;
            PointF previousPoint = default;
            bool hasPreviousPoint = false;

            for (int i = contour.PointStart; i < pointEnd; i++)
            {
                PointF point = this.geometry.Points[i];
                if (hasPreviousPoint && point == previousPoint)
                {
                    continue;
                }

                previousPoint = point;
                hasPreviousPoint = true;
                count++;
            }

            if (contour.IsClosed &&
                count > 1 &&
                this.TryGetFirstDistinctPoint(contour, out _, out PointF firstPoint) &&
                this.TryGetLastDistinctPoint(contour, out _, out PointF lastPoint) &&
                firstPoint == lastPoint)
            {
                count--;
            }

            return count;
        }

        /// <summary>
        /// Counts the distinct contour points in contiguous point storage while skipping immediate duplicates.
        /// </summary>
        private static int GetDistinctPointCount(ReadOnlySpan<PointF> contourPoints, bool isClosed)
        {
            int count = 0;
            PointF previousPoint = default;
            bool hasPreviousPoint = false;

            for (int i = 0; i < contourPoints.Length; i++)
            {
                PointF point = contourPoints[i];
                if (hasPreviousPoint && point == previousPoint)
                {
                    continue;
                }

                previousPoint = point;
                hasPreviousPoint = true;
                count++;
            }

            if (isClosed &&
                count > 1 &&
                TryGetFirstDistinctPoint(contourPoints, out _, out PointF firstPoint) &&
                TryGetLastDistinctPoint(contourPoints, out _, out PointF lastPoint) &&
                firstPoint == lastPoint)
            {
                count--;
            }

            return count;
        }

        /// <summary>
        /// Counts the distinct explicit polyline points while skipping immediate duplicates.
        /// </summary>
        private static int GetDistinctPointCount(PointF[] points)
        {
            int count = 0;
            PointF previousPoint = default;
            bool hasPreviousPoint = false;

            for (int i = 0; i < points.Length; i++)
            {
                PointF point = points[i];
                if (hasPreviousPoint && point == previousPoint)
                {
                    continue;
                }

                previousPoint = point;
                hasPreviousPoint = true;
                count++;
            }

            return count;
        }

        /// <summary>
        /// Finds the first distinct point of the contour.
        /// </summary>
        private bool TryGetFirstDistinctPoint(in LinearContour contour, out int rawIndex, out PointF point)
        {
            if (contour.PointCount == 0)
            {
                rawIndex = -1;
                point = default;
                return false;
            }

            rawIndex = contour.PointStart;
            point = this.geometry.Points[rawIndex];
            return true;
        }

        /// <summary>
        /// Finds the next distinct point after the supplied raw index.
        /// </summary>
        private bool TryGetNextDistinctPoint(in LinearContour contour, int rawIndex, out int nextRawIndex, out PointF point)
        {
            PointF currentPoint = this.geometry.Points[rawIndex];
            int pointEnd = contour.PointStart + contour.PointCount;
            for (int i = rawIndex + 1; i < pointEnd; i++)
            {
                PointF candidate = this.geometry.Points[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                nextRawIndex = i;
                point = candidate;
                return true;
            }

            nextRawIndex = -1;
            point = default;
            return false;
        }

        /// <summary>
        /// Finds the last distinct point of the contour.
        /// </summary>
        private bool TryGetLastDistinctPoint(in LinearContour contour, out int rawIndex, out PointF point)
        {
            if (contour.PointCount == 0)
            {
                rawIndex = -1;
                point = default;
                return false;
            }

            int start = contour.PointStart;
            rawIndex = start + contour.PointCount - 1;
            point = this.geometry.Points[rawIndex];
            while (rawIndex > start && this.geometry.Points[rawIndex - 1] == point)
            {
                rawIndex--;
            }

            return true;
        }

        /// <summary>
        /// Finds the previous distinct point before the supplied raw index.
        /// </summary>
        private bool TryGetPreviousDistinctPoint(in LinearContour contour, int rawIndex, out int previousRawIndex, out PointF point)
        {
            PointF currentPoint = this.geometry.Points[rawIndex];
            for (int i = rawIndex - 1; i >= contour.PointStart; i--)
            {
                PointF candidate = this.geometry.Points[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                previousRawIndex = i;
                point = candidate;
                return true;
            }

            previousRawIndex = -1;
            point = default;
            return false;
        }

        /// <summary>
        /// Finds the first distinct point of the explicit polyline.
        /// </summary>
        private static bool TryGetFirstDistinctPoint(PointF[] points, out int index, out PointF point)
        {
            if (points.Length == 0)
            {
                index = -1;
                point = default;
                return false;
            }

            index = 0;
            point = points[0];
            return true;
        }

        /// <summary>
        /// Finds the next distinct point after the supplied explicit polyline index.
        /// </summary>
        private static bool TryGetNextDistinctPoint(PointF[] points, int index, out int nextIndex, out PointF point)
        {
            PointF currentPoint = points[index];
            for (int i = index + 1; i < points.Length; i++)
            {
                PointF candidate = points[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                nextIndex = i;
                point = candidate;
                return true;
            }

            nextIndex = -1;
            point = default;
            return false;
        }

        /// <summary>
        /// Finds the last distinct point of the explicit polyline.
        /// </summary>
        private static bool TryGetLastDistinctPoint(PointF[] points, out int index, out PointF point)
        {
            if (points.Length == 0)
            {
                index = -1;
                point = default;
                return false;
            }

            index = points.Length - 1;
            point = points[index];
            while (index > 0 && points[index - 1] == point)
            {
                index--;
            }

            return true;
        }

        /// <summary>
        /// Finds the previous distinct point before the supplied explicit polyline index.
        /// </summary>
        private static bool TryGetPreviousDistinctPoint(PointF[] points, int index, out int previousIndex, out PointF point)
        {
            PointF currentPoint = points[index];
            for (int i = index - 1; i >= 0; i--)
            {
                PointF candidate = points[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                previousIndex = i;
                point = candidate;
                return true;
            }

            previousIndex = -1;
            point = default;
            return false;
        }

        /// <summary>
        /// Finds the first distinct point of the contiguous contour point run.
        /// </summary>
        private static bool TryGetFirstDistinctPoint(ReadOnlySpan<PointF> contourPoints, out int index, out PointF point)
        {
            if (contourPoints.IsEmpty)
            {
                index = -1;
                point = default;
                return false;
            }

            index = 0;
            point = contourPoints[0];
            return true;
        }

        /// <summary>
        /// Finds the next distinct point after the supplied contiguous contour index.
        /// </summary>
        private static bool TryGetNextDistinctPoint(ReadOnlySpan<PointF> contourPoints, int index, out int nextIndex, out PointF point)
        {
            PointF currentPoint = contourPoints[index];
            for (int i = index + 1; i < contourPoints.Length; i++)
            {
                PointF candidate = contourPoints[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                nextIndex = i;
                point = candidate;
                return true;
            }

            nextIndex = -1;
            point = default;
            return false;
        }

        /// <summary>
        /// Finds the last distinct point of the contiguous contour point run.
        /// </summary>
        private static bool TryGetLastDistinctPoint(ReadOnlySpan<PointF> contourPoints, out int index, out PointF point)
        {
            if (contourPoints.IsEmpty)
            {
                index = -1;
                point = default;
                return false;
            }

            index = contourPoints.Length - 1;
            point = contourPoints[index];
            while (index > 0 && contourPoints[index - 1] == point)
            {
                index--;
            }

            return true;
        }

        /// <summary>
        /// Finds the previous distinct point before the supplied contiguous contour index.
        /// </summary>
        private static bool TryGetPreviousDistinctPoint(ReadOnlySpan<PointF> contourPoints, int index, out int previousIndex, out PointF point)
        {
            PointF currentPoint = contourPoints[index];
            for (int i = index - 1; i >= 0; i--)
            {
                PointF candidate = contourPoints[i];
                if (candidate == currentPoint)
                {
                    continue;
                }

                previousIndex = i;
                point = candidate;
                return true;
            }

            previousIndex = -1;
            point = default;
            return false;
        }
    }

    /// <summary>
    /// Chooses the arc sweep whose midpoint best matches the preferred outward direction.
    /// </summary>
    private static double ChooseArcSweep(
        double startAngle,
        double counterClockwiseSweep,
        double clockwiseSweep,
        Vector2 preferredDirection)
    {
        Vector2 ccwMid = new(MathF.Cos((float)(startAngle + (counterClockwiseSweep * 0.5D))), MathF.Sin((float)(startAngle + (counterClockwiseSweep * 0.5D))));
        Vector2 cwMid = new(MathF.Cos((float)(startAngle + (clockwiseSweep * 0.5D))), MathF.Sin((float)(startAngle + (clockwiseSweep * 0.5D))));
        return Vector2.Dot(ccwMid, preferredDirection) >= Vector2.Dot(cwMid, preferredDirection)
            ? counterClockwiseSweep
            : clockwiseSweep;
    }

    /// <summary>
    /// Returns the tessellation segment count used for one round join or cap arc.
    /// </summary>
    private static int GetArcSubdivisionCount(float radius, double angle, double arcDetailScale)
    {
        double safeRadius = Math.Max(radius, 0.25F);
        double safeScale = Math.Max(arcDetailScale, 0.01D);
        double theta = Math.Max(0.0001D, 2D * Math.Acos(1D - (0.25D / safeRadius)));
        return Math.Max(0, (int)Math.Ceiling((angle / theta) * safeScale) - 1);
    }

    /// <summary>
    /// Returns the preferred outward direction used to pick between the clockwise and counter-clockwise arc sweeps.
    /// </summary>
    private static Vector2 GetPreferredArcDirection(Vector2 fromOffset, Vector2 toOffset)
    {
        Vector2 direction = fromOffset + toOffset;
        if (direction.LengthSquared() <= StrokeDirectionEpsilon * StrokeDirectionEpsilon)
        {
            return Vector2.Normalize(fromOffset);
        }

        return Vector2.Normalize(direction);
    }

    /// <summary>
    /// Returns the left-side unit normal for a normalized tangent.
    /// </summary>
    private static Vector2 GetLeftNormal(Vector2 tangent) => new(-tangent.Y, tangent.X);

    /// <summary>
    /// Attempts to normalize the direction from <paramref name="start"/> to <paramref name="end"/>.
    /// </summary>
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
    private static float Cross(Vector2 left, Vector2 right) => (left.X * right.Y) - (left.Y * right.X);

    /// <summary>
    /// Normalizes an angle into the inclusive-exclusive range [0, 2π).
    /// </summary>
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
        public StrokeStyle(Pen pen)
        {
            this.Width = pen.StrokeWidth;
            this.LineCap = pen.StrokeOptions.LineCap;
            this.LineJoin = pen.StrokeOptions.LineJoin;
            this.InnerJoin = pen.StrokeOptions.InnerJoin;
            this.MiterLimit = pen.StrokeOptions.MiterLimit;
            this.InnerMiterLimit = pen.StrokeOptions.InnerMiterLimit;
            this.ArcDetailScale = pen.StrokeOptions.ArcDetailScale;
        }

        /// <summary>
        /// Gets the stroke width in destination-space pixels.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets half the stroke width in destination-space pixels.
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
