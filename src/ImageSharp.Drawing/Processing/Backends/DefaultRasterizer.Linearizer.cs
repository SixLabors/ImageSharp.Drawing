// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static partial class DefaultRasterizer
{
    private abstract class Linearizer<TL>
        where TL : class
    {
        private bool hasAnyCoverage;

        protected Linearizer(
            LinearGeometry geometry,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY,
            MemoryAllocator allocator)
        {
            this.Geometry = geometry;
            this.TranslateX = translateX;
            this.TranslateY = translateY;
            this.MinX = minX;
            this.MinY = minY;
            this.Width = width;
            this.Height = height;
            this.FirstBandIndex = firstBandIndex;
            this.RowBandCount = rowBandCount;
            this.SamplingOffsetX = samplingOffsetX;
            this.SamplingOffsetY = samplingOffsetY;
            this.Allocator = allocator;
            this.BandTopStart = (firstBandIndex * PreferredRowHeight) - minY;
            this.FirstBlockLineCounts = new int[rowBandCount];
            this.LineCounts = new int[rowBandCount];
            this.StartCoverTable = new IMemoryOwner<int>?[rowBandCount];
            this.LineArrays = new TL?[rowBandCount];
        }

        protected LinearGeometry Geometry { get; }

        protected int TranslateX { get; }

        protected int TranslateY { get; }

        protected int MinX { get; }

        protected int MinY { get; }

        protected int Width { get; }

        protected int Height { get; }

        protected int FirstBandIndex { get; }

        protected int RowBandCount { get; }

        protected float SamplingOffsetX { get; }

        protected float SamplingOffsetY { get; }

        protected MemoryAllocator Allocator { get; }

        protected int BandTopStart { get; }

        protected TL?[] LineArrays { get; }

        protected int[] FirstBlockLineCounts { get; }

        protected int[] LineCounts { get; }

        protected IMemoryOwner<int>?[] StartCoverTable { get; }

        protected ref bool HasAnyCoverage => ref this.hasAnyCoverage;

        protected bool ProcessCore()
        {
            RectangleF translatedBounds = this.Geometry.Info.Bounds;
            translatedBounds.Offset(this.TranslateX + this.SamplingOffsetX - this.MinX, this.TranslateY + this.SamplingOffsetY - this.MinY);

            bool contains =
                translatedBounds.Left >= 0F &&
                translatedBounds.Top >= 0F &&
                translatedBounds.Right <= this.Width &&
                translatedBounds.Bottom <= this.Height;

            if (contains)
            {
                this.ProcessContained();
            }
            else
            {
                this.ProcessUncontained();
            }

            if (!this.hasAnyCoverage)
            {
                return false;
            }

            this.FinalizeLines();
            return true;
        }

        protected void ProcessContained()
        {
            SegmentEnumerator enumerator = this.Geometry.GetSegments();
            while (enumerator.MoveNext())
            {
                LinearSegment segment = enumerator.Current;
                PointF p0 = segment.Start;
                PointF p1 = segment.End;
                this.AddContainedLineF24Dot8(
                    FloatToFixed24Dot8(((p0.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX),
                    FloatToFixed24Dot8(((p0.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY),
                    FloatToFixed24Dot8(((p1.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX),
                    FloatToFixed24Dot8(((p1.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY));
            }
        }

        protected void ProcessUncontained()
        {
            SegmentEnumerator enumerator = this.Geometry.GetSegments();
            while (enumerator.MoveNext())
            {
                LinearSegment segment = enumerator.Current;
                PointF p0 = segment.Start;
                PointF p1 = segment.End;
                this.AddUncontainedLine(
                    ((p0.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX,
                    ((p0.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY,
                    ((p1.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX,
                    ((p1.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY);
            }
        }

        protected void AddUncontainedLine(float x0, float y0, float x1, float y1)
        {
            if (y0 == y1)
            {
                return;
            }

            if (y0 <= 0F && y1 <= 0F)
            {
                return;
            }

            if (y0 >= this.Height && y1 >= this.Height)
            {
                return;
            }

            if (x0 >= this.Width && x1 >= this.Width)
            {
                return;
            }

            if (x0 == x1)
            {
                int x0c = Math.Clamp(FloatToFixed24Dot8(x0), 0, this.Width * FixedOne);
                int p0y = Math.Clamp(FloatToFixed24Dot8(y0), 0, this.Height * FixedOne);
                int p1y = Math.Clamp(FloatToFixed24Dot8(y1), 0, this.Height * FixedOne);

                if (x0c == 0)
                {
                    this.UpdateStartCoversClipped(p0y, p1y);
                    this.hasAnyCoverage = true;
                }
                else
                {
                    this.AddContainedLineF24Dot8(x0c, p0y, x0c, p1y);
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

                if (y1 > this.Height)
                {
                    double t = (this.Height - y0) / deltayV;
                    rx1 = x0 + (deltaxV * t);
                    ry1 = this.Height;
                }
            }
            else
            {
                if (y0 > this.Height)
                {
                    double t = (y0 - this.Height) / deltayV;
                    rx0 = x0 + (deltaxV * t);
                    ry0 = this.Height;
                }

                if (y1 < 0F)
                {
                    double t = y0 / deltayV;
                    rx1 = x0 + (deltaxV * t);
                    ry1 = 0D;
                }
            }

            if (rx0 >= this.Width && rx1 >= this.Width)
            {
                return;
            }

            if (rx0 > 0D && rx1 > 0D && rx0 < this.Width && rx1 < this.Width)
            {
                this.AddContainedLineF24Dot8(
                    Math.Clamp(FloatToFixed24Dot8((float)rx0), 0, this.Width * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.Height * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)rx1), 0, this.Width * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.Height * FixedOne));
                return;
            }

            if (rx0 <= 0D && rx1 <= 0D)
            {
                this.UpdateStartCoversClipped(
                    Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.Height * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.Height * FixedOne));
                this.hasAnyCoverage = true;
                return;
            }

            double deltayH = ry1 - ry0;
            double deltaxH = Math.Abs(rx1 - rx0);

            if (rx1 > rx0)
            {
                double bx1 = rx1;
                double by1 = ry1;

                if (rx1 > this.Width)
                {
                    double t = (this.Width - rx0) / deltaxH;
                    by1 = ry0 + (deltayH * t);
                    bx1 = this.Width;
                }

                if (rx0 < 0D)
                {
                    double t = -rx0 / deltaxH;
                    int a = Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.Height * FixedOne);
                    int by = Math.Clamp(FloatToFixed24Dot8((float)(ry0 + (deltayH * t))), 0, this.Height * FixedOne);
                    int cx = Math.Clamp(FloatToFixed24Dot8((float)bx1), 0, this.Width * FixedOne);
                    int cy = Math.Clamp(FloatToFixed24Dot8((float)by1), 0, this.Height * FixedOne);

                    this.UpdateStartCoversClipped(a, by);
                    this.hasAnyCoverage = true;
                    this.AddContainedLineF24Dot8(0, by, cx, cy);
                }
                else
                {
                    this.AddContainedLineF24Dot8(
                        Math.Clamp(FloatToFixed24Dot8((float)rx0), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.Height * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)bx1), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)by1), 0, this.Height * FixedOne));
                }
            }
            else
            {
                double bx0 = rx0;
                double by0 = ry0;

                if (rx0 > this.Width)
                {
                    double t = (rx0 - this.Width) / deltaxH;
                    by0 = ry0 + (deltayH * t);
                    bx0 = this.Width;
                }

                if (rx1 < 0D)
                {
                    double t = rx0 / deltaxH;
                    int ax = Math.Clamp(FloatToFixed24Dot8((float)bx0), 0, this.Width * FixedOne);
                    int ay = Math.Clamp(FloatToFixed24Dot8((float)by0), 0, this.Height * FixedOne);
                    int by = Math.Clamp(FloatToFixed24Dot8((float)(ry0 + (deltayH * t))), 0, this.Height * FixedOne);
                    int c = Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.Height * FixedOne);

                    this.AddContainedLineF24Dot8(ax, ay, 0, by);
                    this.UpdateStartCoversClipped(by, c);
                    this.hasAnyCoverage = true;
                }
                else
                {
                    this.AddContainedLineF24Dot8(
                        Math.Clamp(FloatToFixed24Dot8((float)bx0), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)by0), 0, this.Height * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)rx1), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.Height * FixedOne));
                }
            }
        }

        protected void AddContainedLineF24Dot8(int x0, int y0, int x1, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            if (x0 == x1)
            {
                if (y0 < y1)
                {
                    this.VerticalDown(x0, y0, y1);
                }
                else
                {
                    this.VerticalUp(x0, y0, y1);
                }

                return;
            }

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            if (dx > MaximumDelta || dy > MaximumDelta)
            {
                int mx = (x0 + x1) >> 1;
                int my = (y0 + y1) >> 1;
                this.AddContainedLineF24Dot8(x0, y0, mx, my);
                this.AddContainedLineF24Dot8(mx, my, x1, y1);
                return;
            }

            int rowIndex0;
            int rowIndex1;
            int bandTopStart = this.BandTopStart * FixedOne;
            int bandHeight = PreferredRowHeight * FixedOne;
            if (y0 < y1)
            {
                rowIndex0 = (y0 - bandTopStart) / bandHeight;
                rowIndex1 = ((y1 - 1) - bandTopStart) / bandHeight;
            }
            else
            {
                rowIndex0 = ((y0 - 1) - bandTopStart) / bandHeight;
                rowIndex1 = (y1 - bandTopStart) / bandHeight;
            }

            if ((uint)rowIndex0 >= (uint)this.RowBandCount || (uint)rowIndex1 >= (uint)this.RowBandCount)
            {
                return;
            }

            if (rowIndex0 == rowIndex1)
            {
                int rowTop = bandTopStart + (rowIndex0 * bandHeight);
                this.AppendLine(rowIndex0, x0, y0 - rowTop, x1, y1 - rowTop);
                this.LineCounts[rowIndex0]++;
                this.hasAnyCoverage = true;
                return;
            }

            this.SplitAcrossBands(x0, y0, x1, y1);
        }

        protected abstract TL CreateLineArray();

        protected abstract void AppendLine(int rowIndex, int x0, int y0, int x1, int y1);

        protected abstract void FinalizeLines();

        protected TL GetOrCreateLineArray(int rowIndex)
        {
            TL? lineArray = this.LineArrays[rowIndex];
            if (lineArray is not null)
            {
                return lineArray;
            }

            lineArray = this.CreateLineArray();
            this.LineArrays[rowIndex] = lineArray;
            return lineArray;
        }

        private void VerticalDown(int x, int y0, int y1) => this.SplitAcrossBands(x, y0, x, y1);

        private void VerticalUp(int x, int y0, int y1) => this.SplitAcrossBands(x, y0, x, y1);

        private void SplitAcrossBands(int x0, int y0, int x1, int y1)
        {
            int dy = y1 - y0;
            int dx = x1 - x0;
            int bandTopStart = this.BandTopStart * FixedOne;
            int bandHeight = PreferredRowHeight * FixedOne;
            int startBand = dy > 0 ? (y0 - bandTopStart) / bandHeight : ((y0 - 1) - bandTopStart) / bandHeight;
            int endBand = dy > 0 ? ((y1 - 1) - bandTopStart) / bandHeight : (y1 - bandTopStart) / bandHeight;
            int step = dy > 0 ? 1 : -1;
            int currentBand = startBand;
            int currentX = x0;
            int currentY = y0;

            while (currentBand != endBand)
            {
                int bandBoundaryY = dy > 0 ? bandTopStart + ((currentBand + 1) * bandHeight) : bandTopStart + (currentBand * bandHeight);
                int deltaY = bandBoundaryY - currentY;
                int nextX = currentX + (int)(((long)dx * deltaY) / dy);
                int rowTop = bandTopStart + (currentBand * bandHeight);
                this.AppendLine(currentBand, currentX, currentY - rowTop, nextX, bandBoundaryY - rowTop);
                this.LineCounts[currentBand]++;
                this.hasAnyCoverage = true;
                currentX = nextX;
                currentY = bandBoundaryY;
                currentBand += step;

                if ((uint)currentBand >= (uint)this.RowBandCount)
                {
                    return;
                }
            }

            int finalRowTop = bandTopStart + (endBand * bandHeight);
            this.AppendLine(endBand, currentX, currentY - finalRowTop, x1, y1 - finalRowTop);
            this.LineCounts[endBand]++;
            this.hasAnyCoverage = true;
        }

        private void UpdateStartCoversClipped(int y0, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            if (y0 < y1)
            {
                int bandTopStart = this.BandTopStart * FixedOne;
                int bandHeight = PreferredRowHeight * FixedOne;
                int rowIndex0 = (y0 - bandTopStart) / bandHeight;
                int rowIndex1 = ((y1 - 1) - bandTopStart) / bandHeight;
                rowIndex0 = Math.Clamp(rowIndex0, 0, this.RowBandCount - 1);
                rowIndex1 = Math.Clamp(rowIndex1, 0, this.RowBandCount - 1);
                int fy0 = y0 - (bandTopStart + (rowIndex0 * bandHeight));
                int fy1 = y1 - (bandTopStart + (rowIndex1 * bandHeight));
                this.UpdateStartCovers(rowIndex0, fy0, rowIndex0 == rowIndex1 ? fy1 : bandHeight);
                for (int i = rowIndex0 + 1; i < rowIndex1; i++)
                {
                    this.FillStartCovers(i, -FixedOne);
                }

                if (rowIndex0 != rowIndex1)
                {
                    this.UpdateStartCovers(rowIndex1, 0, fy1);
                }
            }
            else
            {
                int bandTopStart = this.BandTopStart * FixedOne;
                int bandHeight = PreferredRowHeight * FixedOne;
                int rowIndex0 = ((y0 - 1) - bandTopStart) / bandHeight;
                int rowIndex1 = (y1 - bandTopStart) / bandHeight;
                rowIndex0 = Math.Clamp(rowIndex0, 0, this.RowBandCount - 1);
                rowIndex1 = Math.Clamp(rowIndex1, 0, this.RowBandCount - 1);
                int fy0 = y0 - (bandTopStart + (rowIndex0 * bandHeight));
                int fy1 = y1 - (bandTopStart + (rowIndex1 * bandHeight));
                this.UpdateStartCovers(rowIndex0, fy0, rowIndex0 == rowIndex1 ? fy1 : 0);
                for (int i = rowIndex0 - 1; i > rowIndex1; i--)
                {
                    this.FillStartCovers(i, FixedOne);
                }

                if (rowIndex0 != rowIndex1)
                {
                    this.UpdateStartCovers(rowIndex1, bandHeight, fy1);
                }
            }
        }

        private void FillStartCovers(int localBandIndex, int value)
        {
            IMemoryOwner<int>? owner = this.StartCoverTable[localBandIndex];
            if (owner is null)
            {
                owner = this.Allocator.Allocate<int>(PreferredRowHeight);
                this.StartCoverTable[localBandIndex] = owner;
                owner.Memory.Span[..PreferredRowHeight].Fill(value);
                return;
            }

            Span<int> covers = owner.Memory.Span[..PreferredRowHeight];
            for (int i = 0; i < PreferredRowHeight; i++)
            {
                covers[i] += value;
            }
        }

        private void UpdateStartCovers(int localBandIndex, int y0, int y1)
        {
            IMemoryOwner<int>? owner = this.StartCoverTable[localBandIndex];
            if (owner is null)
            {
                owner = this.Allocator.Allocate<int>(PreferredRowHeight);
                this.StartCoverTable[localBandIndex] = owner;
            }

            Span<int> covers = owner.Memory.Span[..PreferredRowHeight];
            if (y0 < y1)
            {
                UpdateCoverTableDown(covers, y0, y1);
            }
            else
            {
                UpdateCoverTableUp(covers, y0, y1);
            }
        }

        private static void UpdateCoverTableDown(Span<int> covers, int y0, int y1)
        {
            int rowIndex0 = y0 >> FixedShift;
            int rowIndex1 = (y1 - 1) >> FixedShift;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);

            if (rowIndex0 == rowIndex1)
            {
                covers[rowIndex0] -= fy1 - fy0;
                return;
            }

            covers[rowIndex0] -= FixedOne - fy0;
            for (int i = rowIndex0 + 1; i < rowIndex1; i++)
            {
                covers[i] -= FixedOne;
            }

            covers[rowIndex1] -= fy1;
        }

        private static void UpdateCoverTableUp(Span<int> covers, int y0, int y1)
        {
            int rowIndex0 = (y0 - 1) >> FixedShift;
            int rowIndex1 = y1 >> FixedShift;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);

            if (rowIndex0 == rowIndex1)
            {
                covers[rowIndex0] += fy0 - fy1;
                return;
            }

            covers[rowIndex0] += fy0;
            for (int i = rowIndex0 - 1; i > rowIndex1; i--)
            {
                covers[i] += FixedOne;
            }

            covers[rowIndex1] += FixedOne - fy1;
        }
    }

#pragma warning disable SA1201 // Keep the finalized retained output types adjacent to the linearizer that produces them.
    /// <summary>
    /// Retained tile-space bounds for one linearized geometry payload.
    /// </summary>
    internal readonly struct TileBounds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TileBounds"/> struct.
        /// </summary>
        public TileBounds(int x, int y, int columnCount, int rowCount)
        {
            this.X = x;
            this.Y = y;
            this.ColumnCount = columnCount;
            this.RowCount = rowCount;
        }

        /// <summary>
        /// Gets the tile-space left coordinate.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// Gets the tile-space top coordinate.
        /// </summary>
        public int Y { get; }

        /// <summary>
        /// Gets the tile-space column count.
        /// </summary>
        public int ColumnCount { get; }

        /// <summary>
        /// Gets the tile-space row count.
        /// </summary>
        public int RowCount { get; }
    }

    /// <summary>
    /// Contract implemented by retained line-block payloads.
    /// </summary>
    /// <typeparam name="TSelf">The concrete retained line-block type.</typeparam>
    internal interface ILineBlock<TSelf>
        where TSelf : class, ILineBlock<TSelf>
    {
        /// <summary>
        /// Gets the number of lines stored in a full block.
        /// </summary>
        public static abstract int LineCount { get; }

        /// <summary>
        /// Gets the next block in the retained chain.
        /// </summary>
        public TSelf? Next { get; }

        /// <summary>
        /// Rasterizes the leading <paramref name="count"/> lines from this block.
        /// </summary>
        /// <param name="count">The number of leading lines to rasterize from this block.</param>
        /// <param name="context">The mutable scan-conversion context to write into.</param>
        public void Rasterize(int count, ref Context context);
    }

    /// <summary>
    /// Finalized retained raster data for one concrete line-block encoding.
    /// </summary>
    internal sealed class LinearizedRasterData<TLineBlock>
        where TLineBlock : class, ILineBlock<TLineBlock>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LinearizedRasterData{TLineBlock}"/> class.
        /// </summary>
        public LinearizedRasterData(
            LinearGeometry geometry,
            TileBounds bounds,
            TLineBlock?[] lines,
            int[] firstBlockLineCounts,
            IMemoryOwner<int>?[] startCoverTable)
        {
            this.Geometry = geometry;
            this.Bounds = bounds;
            this.Lines = lines;
            this.FirstBlockLineCounts = firstBlockLineCounts;
            this.StartCoverTable = startCoverTable;
        }

        public LinearGeometry Geometry { get; }

        public TileBounds Bounds { get; }

        public TLineBlock?[] Lines { get; }

        public int[] FirstBlockLineCounts { get; }

        public IMemoryOwner<int>?[] StartCoverTable { get; }

        public void Iterate(int rowIndex, ref Context context)
        {
            int count = this.FirstBlockLineCounts[rowIndex];
            TLineBlock? lineBlock = this.Lines[rowIndex];
            while (lineBlock is not null)
            {
                lineBlock.Rasterize(count, ref context);
                lineBlock = lineBlock.Next;
                count = TLineBlock.LineCount;
            }
        }
    }
#pragma warning restore SA1201

    private sealed class LinearizerX32Y16 : Linearizer<LineArrayX32Y16>
    {
        public LinearizerX32Y16(
            LinearGeometry geometry,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY,
            MemoryAllocator allocator)
            : base(geometry, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY, allocator)
            => this.FinalLines = new LineArrayX32Y16Block?[rowBandCount];

        public LineArrayX32Y16Block?[] FinalLines { get; }

        protected override LineArrayX32Y16 CreateLineArray() => new();

        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(this.Allocator, x0, y0, x1, y1);

        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX32Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        internal bool TryProcess(out LinearizedRasterData<LineArrayX32Y16Block> result)
        {
            if (!this.ProcessCore())
            {
                result = null!;
                return false;
            }

            result = new LinearizedRasterData<LineArrayX32Y16Block>(
                this.Geometry,
                new TileBounds(this.MinX, this.FirstBandIndex, this.Width, this.RowBandCount),
                this.FinalLines,
                this.FirstBlockLineCounts,
                this.StartCoverTable);
            return true;
        }
    }

    private sealed class LinearizerX16Y16 : Linearizer<LineArrayX16Y16>
    {
        public LinearizerX16Y16(
            LinearGeometry geometry,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY,
            MemoryAllocator allocator)
            : base(geometry, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY, allocator)
            => this.FinalLines = new LineArrayX16Y16Block?[rowBandCount];

        public LineArrayX16Y16Block?[] FinalLines { get; }

        protected override LineArrayX16Y16 CreateLineArray() => new();

        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(this.Allocator, x0, y0, x1, y1);

        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX16Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        internal bool TryProcess(out LinearizedRasterData<LineArrayX16Y16Block> result)
        {
            if (!this.ProcessCore())
            {
                result = null!;
                return false;
            }

            result = new LinearizedRasterData<LineArrayX16Y16Block>(
                this.Geometry,
                new TileBounds(this.MinX, this.FirstBandIndex, this.Width, this.RowBandCount),
                this.FinalLines,
                this.FirstBlockLineCounts,
                this.StartCoverTable);
            return true;
        }
    }
}
