// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static partial class DefaultRasterizer
{
    /// <summary>
    /// References one retained rasterizable geometry row inside a prepared scene item.
    /// </summary>
    internal readonly struct RasterizableItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RasterizableItem"/> struct.
        /// </summary>
        /// <param name="rasterizable">The retained rasterizable geometry.</param>
        /// <param name="localRowIndex">The local row index within <paramref name="rasterizable"/>.</param>
        public RasterizableItem(RasterizableGeometry rasterizable, int localRowIndex)
        {
            this.Rasterizable = rasterizable;
            this.LocalRowIndex = localRowIndex;
        }

        /// <summary>
        /// Gets the retained rasterizable geometry.
        /// </summary>
        public RasterizableGeometry Rasterizable { get; }

        /// <summary>
        /// Gets the local row index within <see cref="Rasterizable"/>.
        /// </summary>
        public int LocalRowIndex { get; }

        /// <summary>
        /// Gets the number of lines stored in the first retained block for this row.
        /// </summary>
        /// <returns>The number of valid lines in the leading block.</returns>
        public int GetFirstBlockLineCount() => this.Rasterizable.GetFirstBlockLineCountForRow(this.LocalRowIndex);

        /// <summary>
        /// Gets the 16-bit X retained line block for the row when the geometry uses the compact encoding.
        /// </summary>
        /// <returns>The retained block chain, or <see langword="null"/> when the row uses the 32-bit encoding.</returns>
        public LineArrayX16Y16Block? GetLineArrayX16() => this.Rasterizable.GetLinesX16ForRow(this.LocalRowIndex);

        /// <summary>
        /// Gets the 32-bit X retained line block for the row when the geometry uses the wide encoding.
        /// </summary>
        /// <returns>The retained block chain, or <see langword="null"/> when the row uses the 16-bit encoding.</returns>
        public LineArrayX32Y16Block? GetLineArrayX32() => this.Rasterizable.GetLinesX32ForRow(this.LocalRowIndex);

        /// <summary>
        /// Gets the retained start-cover seeds for the row.
        /// </summary>
        /// <returns>The retained start-cover span.</returns>
        public ReadOnlySpan<int> GetActualCovers() => this.Rasterizable.GetActualCoversForRow(this.LocalRowIndex);
    }

    /// <summary>
    /// References one retained stroke row inside a prepared scene item.
    /// </summary>
    internal readonly struct StrokeRasterizableItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeRasterizableItem"/> struct.
        /// </summary>
        /// <param name="rasterizable">The retained stroke rasterizable geometry.</param>
        /// <param name="localRowIndex">The local row index within <paramref name="rasterizable"/>.</param>
        public StrokeRasterizableItem(StrokeRasterizableGeometry rasterizable, int localRowIndex)
        {
            this.Rasterizable = rasterizable;
            this.LocalRowIndex = localRowIndex;
        }

        /// <summary>
        /// Gets the retained stroke rasterizable geometry.
        /// </summary>
        public StrokeRasterizableGeometry Rasterizable { get; }

        /// <summary>
        /// Gets the local row index within <see cref="Rasterizable"/>.
        /// </summary>
        public int LocalRowIndex { get; }
    }

    /// <summary>
    /// Metadata that describes one prepared rasterizable band.
    /// </summary>
    internal readonly struct RasterizableBandInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RasterizableBandInfo"/> struct.
        /// </summary>
        /// <param name="lineCount">The number of retained visible lines in the band.</param>
        /// <param name="bandHeight">The band height in pixels.</param>
        /// <param name="width">The visible band width in pixels.</param>
        /// <param name="wordsPerRow">The bit-vector width in machine words.</param>
        /// <param name="coverStride">The scanner cover/area stride.</param>
        /// <param name="destinationLeft">The absolute destination X coordinate of the band's left column.</param>
        /// <param name="destinationTop">The absolute destination Y coordinate of the band's top row.</param>
        /// <param name="intersectionRule">The fill rule used when resolving accumulated winding.</param>
        /// <param name="rasterizationMode">The rasterization mode used by the band.</param>
        /// <param name="antialiasThreshold">The aliased threshold used when the band runs in aliased mode.</param>
        /// <param name="hasStartCovers">Indicates whether the band has non-zero start-cover seeds.</param>
        public RasterizableBandInfo(
            int lineCount,
            int bandHeight,
            int width,
            int wordsPerRow,
            int coverStride,
            int destinationLeft,
            int destinationTop,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float antialiasThreshold,
            bool hasStartCovers)
        {
            this.LineCount = lineCount;
            this.BandHeight = bandHeight;
            this.Width = width;
            this.WordsPerRow = wordsPerRow;
            this.CoverStride = coverStride;
            this.DestinationLeft = destinationLeft;
            this.DestinationTop = destinationTop;
            this.IntersectionRule = intersectionRule;
            this.RasterizationMode = rasterizationMode;
            this.AntialiasThreshold = antialiasThreshold;
            this.HasStartCovers = hasStartCovers;
        }

        /// <summary>
        /// Gets the number of visible raster lines stored for the band.
        /// </summary>
        public int LineCount { get; }

        /// <summary>
        /// Gets the band height in pixels.
        /// </summary>
        public int BandHeight { get; }

        /// <summary>
        /// Gets the visible band width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the bit-vector width in machine words.
        /// </summary>
        public int WordsPerRow { get; }

        /// <summary>
        /// Gets the scanner cover/area stride.
        /// </summary>
        public int CoverStride { get; }

        /// <summary>
        /// Gets the absolute destination X coordinate of the band's left column.
        /// </summary>
        public int DestinationLeft { get; }

        /// <summary>
        /// Gets the absolute destination Y coordinate of the band's top row.
        /// </summary>
        public int DestinationTop { get; }

        /// <summary>
        /// Gets the fill rule used when resolving accumulated winding.
        /// </summary>
        public IntersectionRule IntersectionRule { get; }

        /// <summary>
        /// Gets the coverage mode used by the band.
        /// </summary>
        public RasterizationMode RasterizationMode { get; }

        /// <summary>
        /// Gets the aliased threshold used when the band runs in aliased mode.
        /// </summary>
        public float AntialiasThreshold { get; }

        /// <summary>
        /// Gets a value indicating whether the band has non-zero start-cover seeds.
        /// </summary>
        public bool HasStartCovers { get; }

        /// <summary>
        /// Gets a value indicating whether the band would emit any coverage.
        /// </summary>
        public bool HasCoverage => this.LineCount > 0 || this.HasStartCovers;
    }

    /// <summary>
    /// Collects retained line segments whose X coordinates require 32-bit storage.
    /// </summary>
    internal sealed class LineArrayX32Y16
    {
        private LineArrayX32Y16Block? current;
        private int count = LineArrayX32Y16Block.LineCount;

        /// <summary>
        /// Gets the front block in the retained line chain.
        /// </summary>
        /// <returns>The front retained block, or <see langword="null"/> when no lines were appended.</returns>
        public LineArrayX32Y16Block? GetFrontBlock() => this.current;

        /// <summary>
        /// Gets the number of valid lines in the front retained block.
        /// </summary>
        /// <returns>The number of valid front-block lines.</returns>
        public int GetFrontBlockLineCount() => this.current is null ? 0 : this.count;

        /// <summary>
        /// Appends one retained line to the chain.
        /// </summary>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point.</param>
        public void AppendLine(int x0, int y0, int x1, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            int packedY0Y1 = Pack(y0, y1);
            LineArrayX32Y16Block? block = this.current;
            int currentCount = this.count;
            if (currentCount < LineArrayX32Y16Block.LineCount)
            {
                block!.Set(currentCount, packedY0Y1, x0, x1);
                this.count = currentCount + 1;
            }
            else
            {
                LineArrayX32Y16Block next = new(block);
                next.Set(0, packedY0Y1, x0, x1);
                this.current = next;
                this.count = 1;
            }
        }

        /// <summary>
        /// Packs two signed 16-bit fixed-point values into one 32-bit integer.
        /// </summary>
        /// <param name="lo">The low 16-bit value.</param>
        /// <param name="hi">The high 16-bit value.</param>
        /// <returns>The packed value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pack(int lo, int hi) => (lo & 0xFFFF) | (hi << 16);
    }

    /// <summary>
    /// Represents one retained 32-bit-X line block.
    /// </summary>
    internal sealed class LineArrayX32Y16Block : ILineBlock<LineArrayX32Y16Block>
    {
        private const int BlockLineCount = 32;
        private PackedLineX32Y16Buffer lines;

        /// <summary>
        /// Initializes a new instance of the <see cref="LineArrayX32Y16Block"/> class.
        /// </summary>
        /// <param name="next">The next block in the retained chain.</param>
        public LineArrayX32Y16Block(LineArrayX32Y16Block? next) => this.Next = next;

        /// <inheritdoc />
        public static int LineCount => BlockLineCount;

        /// <inheritdoc />
        public LineArrayX32Y16Block? Next { get; }

        /// <summary>
        /// Stores one retained line into the block.
        /// </summary>
        /// <param name="index">The block-local line index.</param>
        /// <param name="packedY0Y1">The packed 16-bit Y endpoints.</param>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int index, int packedY0Y1, int x0, int x1)
        {
            ref PackedLineX32Y16 line = ref this.lines[index];
            line.PackedY0Y1 = packedY0Y1;
            line.X0 = x0;
            line.X1 = x1;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rasterize(int count, ref Context context)
        {
            for (int i = 0; i < count; i++)
            {
                PackedLineX32Y16 line = this.lines[i];
                context.RasterizeLineSegment(line.X0, UnpackLo(line.PackedY0Y1), line.X1, UnpackHi(line.PackedY0Y1));
            }
        }

        /// <summary>
        /// Iterates the retained block chain and rasterizes each block in sequence.
        /// </summary>
        /// <param name="firstBlockLineCount">The number of valid lines stored in the front block.</param>
        /// <param name="context">The mutable scan-conversion context.</param>
        public void Iterate(int firstBlockLineCount, ref Context context)
        {
            int count = firstBlockLineCount;
            LineArrayX32Y16Block? lineBlock = this;
            while (lineBlock is not null)
            {
                lineBlock.Rasterize(count, ref context);
                lineBlock = lineBlock.Next;
                count = LineCount;
            }
        }

        /// <summary>
        /// Unpacks the low signed 16-bit value from a packed endpoint pair.
        /// </summary>
        /// <param name="packed">The packed endpoint pair.</param>
        /// <returns>The unpacked low value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackLo(int packed) => (short)(packed & 0xFFFF);

        /// <summary>
        /// Unpacks the high signed 16-bit value from a packed endpoint pair.
        /// </summary>
        /// <param name="packed">The packed endpoint pair.</param>
        /// <returns>The unpacked high value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackHi(int packed) => packed >> 16;

        /// <summary>
        /// Holds one retained 32-bit-X line record in block-local storage.
        /// </summary>
        private struct PackedLineX32Y16
        {
            /// <summary>
            /// Gets or sets the packed Y endpoints.
            /// </summary>
            public int PackedY0Y1;

            /// <summary>
            /// Gets or sets the starting X coordinate.
            /// </summary>
            public int X0;

            /// <summary>
            /// Gets or sets the ending X coordinate.
            /// </summary>
            public int X1;
        }

        /// <summary>
        /// Holds the fixed-capacity retained line payload inline with the block object.
        /// </summary>
        [InlineArray(BlockLineCount)]
        private struct PackedLineX32Y16Buffer
        {
            private PackedLineX32Y16 element0;
        }
    }

    /// <summary>
    /// Collects retained line segments whose X coordinates fit in packed 16-bit storage.
    /// </summary>
    internal sealed class LineArrayX16Y16
    {
        private LineArrayX16Y16Block? current;
        private int count = LineArrayX16Y16Block.LineCount;

        /// <summary>
        /// Gets the front block in the retained line chain.
        /// </summary>
        /// <returns>The front retained block, or <see langword="null"/> when no lines were appended.</returns>
        public LineArrayX16Y16Block? GetFrontBlock() => this.current;

        /// <summary>
        /// Gets the number of valid lines in the front retained block.
        /// </summary>
        /// <returns>The number of valid front-block lines.</returns>
        public int GetFrontBlockLineCount() => this.current is null ? 0 : this.count;

        /// <summary>
        /// Appends one retained line to the chain.
        /// </summary>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point.</param>
        public void AppendLine(int x0, int y0, int x1, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            int packedY0Y1 = Pack(y0, y1);
            int packedX0X1 = Pack(x0, x1);
            LineArrayX16Y16Block? block = this.current;
            int currentCount = this.count;
            if (currentCount < LineArrayX16Y16Block.LineCount)
            {
                block!.Set(currentCount, packedY0Y1, packedX0X1);
                this.count = currentCount + 1;
            }
            else
            {
                LineArrayX16Y16Block next = new(block);
                next.Set(0, packedY0Y1, packedX0X1);
                this.current = next;
                this.count = 1;
            }
        }

        /// <summary>
        /// Packs two signed 16-bit fixed-point values into one 32-bit integer.
        /// </summary>
        /// <param name="lo">The low 16-bit value.</param>
        /// <param name="hi">The high 16-bit value.</param>
        /// <returns>The packed value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pack(int lo, int hi) => (lo & 0xFFFF) | (hi << 16);
    }

    /// <summary>
    /// Represents one retained 16-bit-X line block.
    /// </summary>
    internal sealed class LineArrayX16Y16Block : ILineBlock<LineArrayX16Y16Block>
    {
        private const int BlockLineCount = 32;
        private PackedLineX16Y16Buffer lines;

        /// <summary>
        /// Initializes a new instance of the <see cref="LineArrayX16Y16Block"/> class.
        /// </summary>
        /// <param name="next">The next block in the retained chain.</param>
        public LineArrayX16Y16Block(LineArrayX16Y16Block? next) => this.Next = next;

        /// <inheritdoc />
        public static int LineCount => BlockLineCount;

        /// <inheritdoc />
        public LineArrayX16Y16Block? Next { get; }

        /// <summary>
        /// Stores one retained line into the block.
        /// </summary>
        /// <param name="index">The block-local line index.</param>
        /// <param name="packedY0Y1">The packed 16-bit Y endpoints.</param>
        /// <param name="packedX0X1">The packed 16-bit X endpoints.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int index, int packedY0Y1, int packedX0X1)
        {
            ref PackedLineX16Y16 line = ref this.lines[index];
            line.PackedY0Y1 = packedY0Y1;
            line.PackedX0X1 = packedX0X1;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rasterize(int count, ref Context context)
        {
            for (int i = 0; i < count; i++)
            {
                PackedLineX16Y16 line = this.lines[i];
                context.RasterizeLineSegment(
                    UnpackLo(line.PackedX0X1),
                    UnpackLo(line.PackedY0Y1),
                    UnpackHi(line.PackedX0X1),
                    UnpackHi(line.PackedY0Y1));
            }
        }

        /// <summary>
        /// Iterates the retained block chain and rasterizes each block in sequence.
        /// </summary>
        /// <param name="firstBlockLineCount">The number of valid lines stored in the front block.</param>
        /// <param name="context">The mutable scan-conversion context.</param>
        public void Iterate(int firstBlockLineCount, ref Context context)
        {
            int count = firstBlockLineCount;
            LineArrayX16Y16Block? lineBlock = this;
            while (lineBlock is not null)
            {
                lineBlock.Rasterize(count, ref context);
                lineBlock = lineBlock.Next;
                count = LineCount;
            }
        }

        /// <summary>
        /// Unpacks the low signed 16-bit value from a packed endpoint pair.
        /// </summary>
        /// <param name="packed">The packed endpoint pair.</param>
        /// <returns>The unpacked low value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackLo(int packed) => (short)(packed & 0xFFFF);

        /// <summary>
        /// Unpacks the high signed 16-bit value from a packed endpoint pair.
        /// </summary>
        /// <param name="packed">The packed endpoint pair.</param>
        /// <returns>The unpacked high value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackHi(int packed) => packed >> 16;

        /// <summary>
        /// Holds one retained 16-bit-X line record in block-local storage.
        /// </summary>
        private struct PackedLineX16Y16
        {
            /// <summary>
            /// Gets or sets the packed Y endpoints.
            /// </summary>
            public int PackedY0Y1;

            /// <summary>
            /// Gets or sets the packed X endpoints.
            /// </summary>
            public int PackedX0X1;
        }

        /// <summary>
        /// Holds the fixed-capacity retained line payload inline with the block object.
        /// </summary>
        [InlineArray(BlockLineCount)]
        private struct PackedLineX16Y16Buffer
        {
            private PackedLineX16Y16 element0;
        }
    }
}
