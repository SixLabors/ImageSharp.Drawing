// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
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
        private int segmentOrdinal;

        /// <summary>
        /// Initializes a new instance of the <see cref="Linearizer{TL}"/> class.
        /// </summary>
        /// <param name="geometry">The source geometry to lower.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="translateX">The whole-pixel X translation applied to the geometry.</param>
        /// <param name="translateY">The whole-pixel Y translation applied to the geometry.</param>
        /// <param name="minX">The minimum destination X bound of the clipped interest region.</param>
        /// <param name="minY">The minimum destination Y bound of the clipped interest region.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index touched by the geometry.</param>
        /// <param name="rowBandCount">The number of retained row bands owned by the geometry.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        /// <param name="keepHorizontalLines">
        /// Whether to retain horizontal edges. They do not change row winding, but the aliased
        /// column pass needs them to detect horizontal features between two row centres.
        /// </param>
        protected Linearizer(
            LinearGeometry geometry,
            Matrix4x4 residual,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            MemoryAllocator allocator,
            bool keepHorizontalLines)
        {
            this.KeepHorizontalLines = keepHorizontalLines;
            this.Geometry = geometry;
            this.Residual = residual;
            this.HasResidual = !residual.IsIdentity;
            this.TranslateX = translateX;
            this.TranslateY = translateY;
            this.MinX = minX;
            this.MinY = minY;
            this.Width = width;
            this.Height = height;
            this.FirstBandIndex = firstBandIndex;
            this.RowBandCount = rowBandCount;
            this.Allocator = allocator;
            this.BandTopStart = (firstBandIndex * PreferredRowHeight) - minY;
            this.FirstBlockLineCounts = new int[rowBandCount];
            this.LineCounts = new int[rowBandCount];
            this.StartCoverTable = new IMemoryOwner<int>?[rowBandCount];
            this.LineArrays = new TL?[rowBandCount];
        }

        /// <summary>
        /// Gets a value indicating whether horizontal edges are retained for the aliased column pass.
        /// </summary>
        protected bool KeepHorizontalLines { get; }

        /// <summary>
        /// Gets the source geometry being lowered.
        /// </summary>
        protected LinearGeometry Geometry { get; }

        /// <summary>
        /// Gets the residual transform applied to each source point during emission.
        /// </summary>
        protected Matrix4x4 Residual { get; }

        /// <summary>
        /// Gets a value indicating whether <see cref="Residual"/> is non-identity.
        /// </summary>
        protected bool HasResidual { get; }

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
        /// Gets the geometry-space profile data, or an empty view outside aliased rendering.
        /// </summary>
        public LinearGeometryProfiles Profiles { get; private set; }

        /// <summary>
        /// Gets the absolute X translation applied to geometry-space profile extents.
        /// </summary>
        public float ProfileTranslateX { get; private set; }

        /// <summary>
        /// Gets the absolute Y translation applied to geometry-space profile extents.
        /// </summary>
        public float ProfileTranslateY { get; private set; }

        /// <summary>
        /// Prepares the profile tags used by aliased fill line emission.
        /// </summary>
        /// <param name="profileStorage">The partition-owned profile arena.</param>
        protected void PrepareProfiles(LinearGeometryProfileStorage profileStorage)
        {
            // Each profile stores a range on one original geometry axis. Translation preserves
            // that range. Scale, rotation, and shear do not, so those draws use sentinel tags and
            // skip the terminating-tip test.
            if (this.KeepHorizontalLines && IsAxisPreservingTranslation(this.Residual))
            {
                this.Profiles = profileStorage.GetOrAdd(this.Geometry);
                this.ProfileTranslateX = this.Residual.Translation.X + this.TranslateX;
                this.ProfileTranslateY = this.Residual.Translation.Y + this.TranslateY;
            }
        }

        /// <summary>
        /// Executes the linearization pass and finalizes the retained row payloads.
        /// </summary>
        /// <returns><see langword="true"/> when any retained coverage was produced; otherwise <see langword="false"/>.</returns>
        protected virtual bool ProcessCore()
        {
            RectangleF translatedBounds = this.HasResidual
                ? RectangleF.Transform(this.Geometry.Info.Bounds, this.Residual)
                : this.Geometry.Info.Bounds;
            translatedBounds.Offset(this.TranslateX - this.MinX, this.TranslateY - this.MinY);

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
            Matrix4x4 residual = this.Residual;
            bool hasResidual = this.HasResidual;
            while (enumerator.MoveNext())
            {
                LinearSegment segment = enumerator.Current;
                PointF p0 = segment.Start;
                PointF p1 = segment.End;
                if (hasResidual)
                {
                    p0 = PointF.Transform(p0, residual);
                    p1 = PointF.Transform(p1, residual);
                }

                this.AddContainedLineF24Dot8(
                    FloatToFixed24Dot8((p0.X + this.TranslateX) - this.MinX),
                    FloatToFixed24Dot8((p0.Y + this.TranslateY) - this.MinY),
                    FloatToFixed24Dot8((p1.X + this.TranslateX) - this.MinX),
                    FloatToFixed24Dot8((p1.Y + this.TranslateY) - this.MinY),
                    this.NextSegmentTag());
            }
        }

        /// <summary>
        /// Linearizes geometry that intersects the destination interest bounds and requires clipping.
        /// </summary>
        protected void ProcessUncontained()
        {
            SegmentEnumerator enumerator = this.Geometry.GetSegments();
            Matrix4x4 residual = this.Residual;
            bool hasResidual = this.HasResidual;
            while (enumerator.MoveNext())
            {
                LinearSegment segment = enumerator.Current;
                PointF p0 = segment.Start;
                PointF p1 = segment.End;
                if (hasResidual)
                {
                    p0 = PointF.Transform(p0, residual);
                    p1 = PointF.Transform(p1, residual);
                }

                this.AddUncontainedLine(
                    (p0.X + this.TranslateX) - this.MinX,
                    (p0.Y + this.TranslateY) - this.MinY,
                    (p1.X + this.TranslateX) - this.MinX,
                    (p1.Y + this.TranslateY) - this.MinY,
                    this.NextSegmentTag());
            }
        }

        /// <summary>
        /// Gets the X and Y profile identifiers for the next derived segment.
        /// </summary>
        /// <returns>The packed identifiers, or the all-sentinel tag when this draw has no profile data.</returns>
        private uint NextSegmentTag()
            => this.Profiles.IsEmpty ? LinearGeometryProfiles.SentinelTag : this.Profiles.GetSegmentTag(this.segmentOrdinal++);

        /// <summary>
        /// Tests whether device coordinates differ from geometry coordinates only by translation.
        /// </summary>
        /// <param name="residual">The residual transform to classify.</param>
        /// <returns><see langword="true"/> when the residual is a pure translation.</returns>
        private static bool IsAxisPreservingTranslation(in Matrix4x4 residual)
            => residual.M11 == 1F && residual.M22 == 1F && residual.M33 == 1F
            && residual.M12 == 0F && residual.M21 == 0F
            && residual.M13 == 0F && residual.M31 == 0F
            && residual.M23 == 0F && residual.M32 == 0F;

        /// <summary>
        /// Clips one geometry line against the destination interest and adds the retained result.
        /// </summary>
        /// <remarks>
        /// The interest region is the local rectangle [0, <see cref="Width"/>] x [0, <see cref="Height"/>].
        /// Segments above, below, or right of the interest are discarded outright, but segments left of
        /// it must be retained as start covers because winding accumulates left to right across a scanline.
        /// </remarks>
        /// <param name="x0">The starting X coordinate in translated float space.</param>
        /// <param name="y0">The starting Y coordinate in translated float space.</param>
        /// <param name="x1">The ending X coordinate in translated float space.</param>
        /// <param name="y1">The ending Y coordinate in translated float space.</param>
        /// <param name="tag">The segment profile tag, retained with every clipped piece.</param>
        protected void AddUncontainedLine(float x0, float y0, float x1, float y1, uint tag)
        {
            // A horizontal edge does not change winding along a row. Continuous coverage can
            // discard it. Aliased rendering retains its visible part because the column pass needs
            // both horizontal boundaries of a feature that lies between two row centres.
            if (y0 == y1)
            {
                if (this.KeepHorizontalLines && y0 >= 0F && y0 < this.Height)
                {
                    float hx0 = Math.Clamp(x0, 0F, this.Width);
                    float hx1 = Math.Clamp(x1, 0F, this.Width);
                    if (hx0 != hx1)
                    {
                        this.AddContainedLineF24Dot8(
                            Math.Clamp(FloatToFixed24Dot8(hx0), 0, this.Width * FixedOne),
                            Math.Clamp(FloatToFixed24Dot8(y0), 0, this.Height * FixedOne),
                            Math.Clamp(FloatToFixed24Dot8(hx1), 0, this.Width * FixedOne),
                            Math.Clamp(FloatToFixed24Dot8(y1), 0, this.Height * FixedOne),
                            tag);
                    }
                }

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

            // Fully right of the interest cannot affect any visible pixel; fully left would,
            // via winding, so that case is handled further down rather than rejected here.
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
                    this.AddContainedLineF24Dot8(x0c, p0y, x0c, p1y, tag);
                }

                return;
            }

            // Vertical clipping first: intersections with y == 0 and y == Height are computed
            // parametrically in double precision so the clipped endpoints stay on the original line.
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

            // Vertical clipping can reveal that the surviving portion lies fully right of the interest.
            if (rx0 >= this.Width && rx1 >= this.Width)
            {
                return;
            }

            // Fully inside horizontally: emit directly without edge splitting.
            if (rx0 > 0D && rx1 > 0D && rx0 < this.Width && rx1 < this.Width)
            {
                this.AddContainedLineF24Dot8(
                    Math.Clamp(FloatToFixed24Dot8((float)rx0), 0, this.Width * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.Height * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)rx1), 0, this.Width * FixedOne),
                    Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.Height * FixedOne),
                    tag);
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

            // Horizontal clipping, split by travel direction so the right-edge clip is always
            // applied to the far endpoint and the left-edge clip to the near one.
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
                    this.AddContainedLineF24Dot8(0, by, cx, cy, tag);
                }
                else
                {
                    this.AddContainedLineF24Dot8(
                        Math.Clamp(FloatToFixed24Dot8((float)rx0), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)ry0), 0, this.Height * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)bx1), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)by1), 0, this.Height * FixedOne),
                        tag);
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
                    this.AddContainedLineF24Dot8(ax, ay, 0, by, tag);
                    this.UpdateStartCoversClipped(by, c);
                    this.hasAnyCoverage = true;
                }
                else
                {
                    this.AddContainedLineF24Dot8(
                        Math.Clamp(FloatToFixed24Dot8((float)bx0), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)by0), 0, this.Height * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)rx1), 0, this.Width * FixedOne),
                        Math.Clamp(FloatToFixed24Dot8((float)ry1), 0, this.Height * FixedOne),
                        tag);
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
        /// <param name="tag">The segment profile tag, retained with every band piece.</param>
        protected void AddContainedLineF24Dot8(int x0, int y0, int x1, int y1, uint tag)
        {
            // A retained horizontal edge belongs to the band containing its Y coordinate. At an
            // exact band boundary it is copied to both adjacent bands. Only the band containing
            // the matching edge can form a closed column interval; the other band ignores its
            // unmatched crossing.
            if (y0 == y1)
            {
                if (!this.KeepHorizontalLines || x0 == x1)
                {
                    return;
                }

                int bandTop = this.BandTopStart * FixedOne;
                int bandExtent = PreferredRowHeight * FixedOne;
                int band = (y0 - bandTop) / bandExtent;
                if ((uint)band < (uint)this.RowBandCount)
                {
                    int rowTop = bandTop + (band * bandExtent);
                    this.AppendLine(band, x0, y0 - rowTop, x1, y1 - rowTop, tag);
                    this.LineCounts[band]++;
                    this.hasAnyCoverage = true;
                }

                if ((y0 - bandTop) % bandExtent == 0 && (uint)(band - 1) < (uint)this.RowBandCount)
                {
                    int rowTop = bandTop + ((band - 1) * bandExtent);
                    this.AppendLine(band - 1, x0, y0 - rowTop, x1, y1 - rowTop, tag);
                    this.LineCounts[band - 1]++;
                }

                return;
            }

            if (x0 == x1)
            {
                // Winding direction is carried by the y endpoint order itself, so both the
                // downward and upward cases hand the endpoints to the band splitter as-is.
                this.SplitAcrossBands(x0, y0, x0, y1, tag);
                return;
            }

            long dx = Math.Abs((long)x1 - x0);
            long dy = Math.Abs((long)y1 - y0);
            if (dx > MaximumDelta || dy > MaximumDelta)
            {
                // Halve overlong segments recursively so downstream fixed-point
                // interpolation always operates on bounded deltas. The midpoint must be
                // computed in 64-bit: an int sum overflows for coordinates beyond ~4.2M
                // pixels, placing the midpoint outside [x0, x1] so the segment never
                // shrinks and the recursion overflows the stack (issue #403).
                int mx = (int)(((long)x0 + x1) >> 1);
                int my = (int)(((long)y0 + y1) >> 1);
                this.AddContainedLineF24Dot8(x0, y0, mx, my, tag);
                this.AddContainedLineF24Dot8(mx, my, x1, y1, tag);
                return;
            }

            // Band indices treat the larger Y endpoint as exclusive (hence the -1) so a
            // segment ending exactly on a band boundary does not spill into the next band.
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

            // Bounds guard: never write outside the retained band range. The contained path
            // trusts the caller's bounds, so float-to-fixed rounding may land a hair outside.
            if ((uint)rowIndex0 >= (uint)this.RowBandCount || (uint)rowIndex1 >= (uint)this.RowBandCount)
            {
                return;
            }

            if (rowIndex0 == rowIndex1)
            {
                int rowTop = bandTopStart + (rowIndex0 * bandHeight);
                this.AppendLine(rowIndex0, x0, y0 - rowTop, x1, y1 - rowTop, tag);
                this.LineCounts[rowIndex0]++;
                this.hasAnyCoverage = true;
                return;
            }

            this.SplitAcrossBands(x0, y0, x1, y1, tag);
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
        /// <param name="tag">The segment profile tag.</param>
        protected abstract void AppendLine(int rowIndex, int x0, int y0, int x1, int y1, uint tag);

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
        /// Splits a contained line segment at row-band boundaries and appends each retained piece.
        /// </summary>
        /// <param name="x0">The starting X coordinate.</param>
        /// <param name="y0">The starting Y coordinate.</param>
        /// <param name="x1">The ending X coordinate.</param>
        /// <param name="y1">The ending Y coordinate.</param>
        /// <param name="tag">The segment profile tag, retained with every band piece.</param>
        private void SplitAcrossBands(int x0, int y0, int x1, int y1, uint tag)
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
                // Walk to the band boundary in the direction of travel, interpolating X in
                // 64-bit so the dx * deltaY product cannot overflow 32-bit fixed point.
                int bandBoundaryY = dy > 0 ? bandTopStart + ((currentBand + 1) * bandHeight) : bandTopStart + (currentBand * bandHeight);
                int deltaY = bandBoundaryY - currentY;
                int nextX = currentX + (int)(((long)dx * deltaY) / dy);
                int rowTop = bandTopStart + (currentBand * bandHeight);

                // Each retained segment is stored in the local coordinate space of its owning band.
                this.AppendLine(currentBand, currentX, currentY - rowTop, nextX, bandBoundaryY - rowTop, tag);
                this.LineCounts[currentBand]++;
                this.hasAnyCoverage = true;
                currentX = nextX;
                currentY = bandBoundaryY;
                currentBand += step;

                // Bounds guard: stop rather than write outside the retained band range.
                if ((uint)currentBand >= (uint)this.RowBandCount)
                {
                    return;
                }
            }

            int finalRowTop = bandTopStart + (endBand * bandHeight);
            this.AppendLine(endBand, currentX, currentY - finalRowTop, x1, y1 - finalRowTop, tag);
            this.LineCounts[endBand]++;
            this.hasAnyCoverage = true;
        }

        /// <summary>
        /// Updates retained start-cover rows for a line that has been clipped against the visible band.
        /// </summary>
        /// <remarks>
        /// Travel direction encodes winding sign: downward segments subtract cover and upward
        /// segments add it, matching the accumulation performed by the scanline rasterizer.
        /// </remarks>
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
                owner = this.Allocator.Allocate<int>(PreferredRowHeight, AllocationOptions.Clean);
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
                owner = this.Allocator.Allocate<int>(PreferredRowHeight, AllocationOptions.Clean);
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
        private readonly LinearGeometryProfileStorage profileStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearizerX32Y16"/> class.
        /// </summary>
        /// <param name="geometry">The source geometry to lower.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="translateX">The whole-pixel X translation applied to the geometry.</param>
        /// <param name="translateY">The whole-pixel Y translation applied to the geometry.</param>
        /// <param name="minX">The minimum destination X bound of the clipped interest region.</param>
        /// <param name="minY">The minimum destination Y bound of the clipped interest region.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index touched by the geometry.</param>
        /// <param name="rowBandCount">The number of retained row bands owned by the geometry.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        /// <param name="profileStorage">The partition-owned profile arena.</param>
        /// <param name="keepHorizontalLines">Whether to retain horizontal edges for aliased thin-feature recovery between row centres.</param>
        public LinearizerX32Y16(
            LinearGeometry geometry,
            Matrix4x4 residual,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            MemoryAllocator allocator,
            LinearGeometryProfileStorage profileStorage,
            bool keepHorizontalLines)
            : base(geometry, residual, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, allocator, keepHorizontalLines)
        {
            this.profileStorage = profileStorage;
            this.FinalLines = new LineArrayX32Y16Block?[rowBandCount];
        }

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX32Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX32Y16 CreateLineArray() => new(this.KeepHorizontalLines);

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1, uint tag)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1, tag);

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
        public bool TryProcess(out LinearizedRasterData<LineArrayX32Y16Block> result)
        {
            this.PrepareProfiles(this.profileStorage);

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
                this.StartCoverTable,
                this.Profiles,
                this.ProfileTranslateX,
                this.ProfileTranslateY);

            return true;
        }
    }

    /// <summary>
    /// Linearizer that finalizes retained lines into the packed 16-bit-X encoding.
    /// </summary>
    private sealed class LinearizerX16Y16 : Linearizer<LineArrayX16Y16>
    {
        private readonly LinearGeometryProfileStorage profileStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearizerX16Y16"/> class.
        /// </summary>
        /// <param name="geometry">The source geometry to lower.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="translateX">The whole-pixel X translation applied to the geometry.</param>
        /// <param name="translateY">The whole-pixel Y translation applied to the geometry.</param>
        /// <param name="minX">The minimum destination X bound of the clipped interest region.</param>
        /// <param name="minY">The minimum destination Y bound of the clipped interest region.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index touched by the geometry.</param>
        /// <param name="rowBandCount">The number of retained row bands owned by the geometry.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        /// <param name="profileStorage">The partition-owned profile arena.</param>
        /// <param name="keepHorizontalLines">Whether to retain horizontal edges for aliased thin-feature recovery between row centres.</param>
        public LinearizerX16Y16(
            LinearGeometry geometry,
            Matrix4x4 residual,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            MemoryAllocator allocator,
            LinearGeometryProfileStorage profileStorage,
            bool keepHorizontalLines)
            : base(geometry, residual, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, allocator, keepHorizontalLines)
        {
            this.profileStorage = profileStorage;
            this.FinalLines = new LineArrayX16Y16Block?[rowBandCount];
        }

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX16Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX16Y16 CreateLineArray() => new(this.KeepHorizontalLines);

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1, uint tag)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1, tag);

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
        public bool TryProcess(out LinearizedRasterData<LineArrayX16Y16Block> result)
        {
            this.PrepareProfiles(this.profileStorage);

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
                this.StartCoverTable,
                this.Profiles,
                this.ProfileTranslateX,
                this.ProfileTranslateY);

            return true;
        }
    }
}
