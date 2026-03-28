// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static partial class DefaultRasterizer
{
    /// <summary>
    /// Base class that lowers translated geometry into retained per-row line storage.
    /// </summary>
    /// <typeparam name="TL">The mutable per-row line collector type.</typeparam>
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

        /// <summary>
        /// Gets the source geometry being lowered.
        /// </summary>
        protected LinearGeometry Geometry { get; }

        /// <summary>
        /// Gets the translated X offset applied to the geometry.
        /// </summary>
        protected int TranslateX { get; }

        /// <summary>
        /// Gets the translated Y offset applied to the geometry.
        /// </summary>
        protected int TranslateY { get; }

        /// <summary>
        /// Gets the minimum destination X bound after clipping.
        /// </summary>
        protected int MinX { get; }

        /// <summary>
        /// Gets the minimum destination Y bound after clipping.
        /// </summary>
        protected int MinY { get; }

        /// <summary>
        /// Gets the visible destination width in pixels.
        /// </summary>
        protected int Width { get; }

        /// <summary>
        /// Gets the visible destination height in pixels.
        /// </summary>
        protected int Height { get; }

        /// <summary>
        /// Gets the first retained row-band index touched by the geometry.
        /// </summary>
        protected int FirstBandIndex { get; }

        /// <summary>
        /// Gets the number of retained row bands owned by the geometry.
        /// </summary>
        protected int RowBandCount { get; }

        /// <summary>
        /// Gets the horizontal sampling offset applied before fixed-point conversion.
        /// </summary>
        protected float SamplingOffsetX { get; }

        /// <summary>
        /// Gets the vertical sampling offset applied before fixed-point conversion.
        /// </summary>
        protected float SamplingOffsetY { get; }

        /// <summary>
        /// Gets the allocator used for retained start-cover storage.
        /// </summary>
        protected MemoryAllocator Allocator { get; }

        /// <summary>
        /// Gets the top offset, in whole pixels, of the first retained row band.
        /// </summary>
        protected int BandTopStart { get; }

        /// <summary>
        /// Gets the mutable per-row line collectors used during lowering.
        /// </summary>
        protected TL?[] LineArrays { get; }

        /// <summary>
        /// Gets the valid front-block line count for each retained row band.
        /// </summary>
        protected int[] FirstBlockLineCounts { get; }

        /// <summary>
        /// Gets the total retained line count for each row band.
        /// </summary>
        protected int[] LineCounts { get; }

        /// <summary>
        /// Gets the retained start-cover storage for each row band.
        /// </summary>
        protected IMemoryOwner<int>?[] StartCoverTable { get; }

        /// <summary>
        /// Gets a value indicating whether any retained payload was produced.
        /// </summary>
        protected ref bool HasAnyCoverage => ref this.hasAnyCoverage;

        /// <summary>
        /// Executes the linearization pass and finalizes the retained row payloads.
        /// </summary>
        /// <returns><see langword="true"/> when any retained coverage was produced; otherwise <see langword="false"/>.</returns>
        protected bool ProcessCore()
        {
            RectangleF translatedBounds = this.Geometry.Info.Bounds;
            translatedBounds.Offset(this.TranslateX + this.SamplingOffsetX - this.MinX, this.TranslateY + this.SamplingOffsetY - this.MinY);

            bool contains =
                translatedBounds.Left >= 0F &&
                translatedBounds.Top >= 0F &&
                translatedBounds.Right <= this.Width &&
                translatedBounds.Bottom <= this.Height;

            // Contained geometry can skip clipping and go straight to the fixed-point band splitter.
            if (contains)
            {
                this.ProcessContained();
            }
            else
            {
                // Geometry that touches the interest edges needs clipping so start covers and line
                // segments still match the destination bounds seen by the rasterizer.
                this.ProcessUncontained();
            }

            if (!this.hasAnyCoverage)
            {
                return false;
            }

            this.FinalizeLines();
            return true;
        }

        /// <summary>
        /// Linearizes geometry that is fully contained inside the destination interest.
        /// </summary>
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

        /// <summary>
        /// Linearizes geometry that intersects the destination interest bounds and requires clipping.
        /// </summary>
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

        /// <summary>
        /// Clips one geometry line against the destination interest and adds the retained result.
        /// </summary>
        /// <param name="x0">The starting X coordinate in translated float space.</param>
        /// <param name="y0">The starting Y coordinate in translated float space.</param>
        /// <param name="x1">The ending X coordinate in translated float space.</param>
        /// <param name="y1">The ending Y coordinate in translated float space.</param>
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
                    // Segments clipped fully to the left edge do not produce a visible line, but they
                    // still change winding for rows they cross. Retain that effect as start covers.
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
                // A segment that stays left of the visible band contributes winding only.
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

                    // The visible portion begins exactly at x == 0 after the left-edge clip.
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

                    // The right-to-left case mirrors the left-edge handling above: emit the
                    // visible portion first, then retain the winding-only tail as start covers.
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

        /// <summary>
        /// Adds one fully-contained line segment in 24.8 fixed-point coordinates.
        /// </summary>
        /// <param name="x0">The starting X coordinate.</param>
        /// <param name="y0">The starting Y coordinate.</param>
        /// <param name="x1">The ending X coordinate.</param>
        /// <param name="y1">The ending Y coordinate.</param>
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

        /// <summary>
        /// Creates the mutable line collector used for one row band.
        /// </summary>
        /// <returns>The mutable line collector.</returns>
        protected abstract TL CreateLineArray();

        /// <summary>
        /// Appends one line segment into the retained row-band collector.
        /// </summary>
        /// <param name="rowIndex">The local row-band index.</param>
        /// <param name="x0">The starting X coordinate relative to the row band.</param>
        /// <param name="y0">The starting Y coordinate relative to the row band.</param>
        /// <param name="x1">The ending X coordinate relative to the row band.</param>
        /// <param name="y1">The ending Y coordinate relative to the row band.</param>
        protected abstract void AppendLine(int rowIndex, int x0, int y0, int x1, int y1);

        /// <summary>
        /// Finalizes the mutable collectors into the retained line-block representation.
        /// </summary>
        protected abstract void FinalizeLines();

        /// <summary>
        /// Gets the mutable line collector for a row band, creating it on first use.
        /// </summary>
        /// <param name="rowIndex">The local row-band index.</param>
        /// <returns>The mutable line collector.</returns>
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

        /// <summary>
        /// Adds a downward vertical segment by delegating to the shared band-splitting path.
        /// </summary>
        /// <param name="x">The fixed-point X coordinate.</param>
        /// <param name="y0">The starting fixed-point Y coordinate.</param>
        /// <param name="y1">The ending fixed-point Y coordinate.</param>
        private void VerticalDown(int x, int y0, int y1) => this.SplitAcrossBands(x, y0, x, y1);

        /// <summary>
        /// Adds an upward vertical segment by delegating to the shared band-splitting path.
        /// </summary>
        /// <param name="x">The fixed-point X coordinate.</param>
        /// <param name="y0">The starting fixed-point Y coordinate.</param>
        /// <param name="y1">The ending fixed-point Y coordinate.</param>
        private void VerticalUp(int x, int y0, int y1) => this.SplitAcrossBands(x, y0, x, y1);

        /// <summary>
        /// Splits a contained line segment at row-band boundaries and appends each retained piece.
        /// </summary>
        /// <param name="x0">The starting X coordinate.</param>
        /// <param name="y0">The starting Y coordinate.</param>
        /// <param name="x1">The ending X coordinate.</param>
        /// <param name="y1">The ending Y coordinate.</param>
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

                // Each retained segment is stored in the local coordinate space of its owning band.
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

        /// <summary>
        /// Updates retained start-cover rows for a line that has been clipped against the visible band.
        /// </summary>
        /// <param name="y0">The clipped starting Y coordinate.</param>
        /// <param name="y1">The clipped ending Y coordinate.</param>
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
                    // Full interior bands receive a constant winding contribution.
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
                    // Full interior bands receive a constant winding contribution.
                    this.FillStartCovers(i, FixedOne);
                }

                if (rowIndex0 != rowIndex1)
                {
                    this.UpdateStartCovers(rowIndex1, bandHeight, fy1);
                }
            }
        }

        /// <summary>
        /// Fills an entire retained start-cover row with a constant winding value.
        /// </summary>
        /// <param name="localBandIndex">The local row-band index.</param>
        /// <param name="value">The constant winding value to add.</param>
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

        /// <summary>
        /// Updates a retained start-cover row for one clipped vertical interval.
        /// </summary>
        /// <param name="localBandIndex">The local row-band index.</param>
        /// <param name="y0">The starting Y coordinate relative to the row band.</param>
        /// <param name="y1">The ending Y coordinate relative to the row band.</param>
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

        /// <summary>
        /// Applies a downward winding contribution to one retained start-cover table.
        /// </summary>
        /// <param name="covers">The retained start-cover rows.</param>
        /// <param name="y0">The starting Y coordinate relative to the row band.</param>
        /// <param name="y1">The ending Y coordinate relative to the row band.</param>
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

        /// <summary>
        /// Applies an upward winding contribution to one retained start-cover table.
        /// </summary>
        /// <param name="covers">The retained start-cover rows.</param>
        /// <param name="y0">The starting Y coordinate relative to the row band.</param>
        /// <param name="y1">The ending Y coordinate relative to the row band.</param>
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

    /// <summary>
    /// Linearizer that finalizes retained lines into the 32-bit-X encoding.
    /// </summary>
    private sealed class LinearizerX32Y16 : Linearizer<LineArrayX32Y16>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LinearizerX32Y16"/> class.
        /// </summary>
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

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX32Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX32Y16 CreateLineArray() => new();

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1);

        /// <inheritdoc />
        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX32Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        /// <summary>
        /// Executes the 32-bit-X linearization pass and returns the retained result.
        /// </summary>
        /// <param name="result">The finalized retained raster data.</param>
        /// <returns><see langword="true"/> when retained coverage was produced; otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Linearizer that finalizes retained lines into the packed 16-bit-X encoding.
    /// </summary>
    private sealed class LinearizerX16Y16 : Linearizer<LineArrayX16Y16>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LinearizerX16Y16"/> class.
        /// </summary>
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

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX16Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX16Y16 CreateLineArray() => new();

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1);

        /// <inheritdoc />
        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX16Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        /// <summary>
        /// Executes the 16-bit-X linearization pass and returns the retained result.
        /// </summary>
        /// <param name="result">The finalized retained raster data.</param>
        /// <returns><see langword="true"/> when retained coverage was produced; otherwise <see langword="false"/>.</returns>
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
