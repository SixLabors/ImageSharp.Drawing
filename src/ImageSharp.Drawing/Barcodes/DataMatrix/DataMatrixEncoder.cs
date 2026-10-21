// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds Data Matrix ECC 200 symbols: the symbol size, the pad codewords, the Reed-Solomon error correction codewords, the placement of the codewords in the
/// mapping matrix, and the finder and alignment patterns around the data regions.
/// </summary>
internal static class DataMatrixEncoder
{
    /// <summary>
    /// The largest number of message values a symbol carries: 3116 digits in the 144 x 144 symbol.
    /// </summary>
    public const int MaximumLength = 3116;

    /// <summary>
    /// The quiet zone on each of the four sides in modules, section 5.6.2 of the GS1 General Specifications: "one module wide on all four sides".
    /// </summary>
    private const int QuietZone = 1;

    /// <summary>
    /// The most modules a mapping matrix holds: the 132 x 132 of the 144 x 144 symbol.
    /// </summary>
    private const int MaximumMappingModules = 132 * 132;

    /// <summary>
    /// The most codewords a symbol holds, data and error correction: one for every eight modules of the largest mapping matrix.
    /// </summary>
    private const int MaximumTotalCodewords = MaximumMappingModules / 8;

    /// <summary>
    /// The number of symbol sizes in <see cref="SizeTable"/>.
    /// </summary>
    private const int SizeCount = 48;

    /// <summary>
    /// The number of values of one symbol size in <see cref="SizeTable"/>.
    /// </summary>
    private const int SizeFields = 7;

    /// <summary>
    /// The field of the error correction: GF(2^8) with the primitive polynomial x^8 + x^5 + x^3 + x^2 + 1, 301 decimal.
    /// </summary>
    private static readonly GaloisField Field = new(256, 301);

    /// <summary>
    /// Gets the symbol sizes, <see cref="SizeFields"/> values each: the rows and the columns with the finder pattern, the data regions down and across, the
    /// error correction codewords, the interleaved Reed-Solomon blocks, and 1 for a size of the rectangular extension only. The square sizes and the first six
    /// rectangular sizes are ISO/IEC 16022 Table 7, which Tables 5-32 and 5-33 of the GS1 General Specifications repeat. The other rectangular sizes are the
    /// rectangular extension, which only <see cref="DataMatrixFormat.RectangularExtension"/> takes. A data codeword count that two sizes share goes to the size
    /// listed first.
    /// </summary>
    private static ReadOnlySpan<ushort> SizeTable =>
    [
        10, 10, 1, 1, 5, 1, 0,
        12, 12, 1, 1, 7, 1, 0,
        14, 14, 1, 1, 10, 1, 0,
        16, 16, 1, 1, 12, 1, 0,
        18, 18, 1, 1, 14, 1, 0,
        20, 20, 1, 1, 18, 1, 0,
        22, 22, 1, 1, 20, 1, 0,
        24, 24, 1, 1, 24, 1, 0,
        26, 26, 1, 1, 28, 1, 0,
        32, 32, 2, 2, 36, 1, 0,
        36, 36, 2, 2, 42, 1, 0,
        40, 40, 2, 2, 48, 1, 0,
        44, 44, 2, 2, 56, 1, 0,
        48, 48, 2, 2, 68, 1, 0,
        52, 52, 2, 2, 84, 2, 0,
        64, 64, 4, 4, 112, 2, 0,
        72, 72, 4, 4, 144, 4, 0,
        80, 80, 4, 4, 192, 4, 0,
        88, 88, 4, 4, 224, 4, 0,
        96, 96, 4, 4, 272, 4, 0,
        104, 104, 4, 4, 336, 6, 0,
        120, 120, 6, 6, 408, 6, 0,
        132, 132, 6, 6, 496, 8, 0,
        144, 144, 6, 6, 620, 10, 0,
        8, 18, 1, 1, 7, 1, 0,
        8, 32, 1, 2, 11, 1, 0,
        8, 48, 1, 2, 15, 1, 1,
        8, 64, 1, 4, 18, 1, 1,
        8, 80, 1, 4, 22, 1, 1,
        8, 96, 1, 4, 28, 1, 1,
        8, 120, 1, 6, 32, 1, 1,
        8, 144, 1, 6, 36, 1, 1,
        12, 26, 1, 1, 14, 1, 0,
        12, 36, 1, 2, 18, 1, 0,
        12, 64, 1, 4, 27, 1, 1,
        12, 88, 1, 4, 36, 1, 1,
        16, 36, 1, 2, 24, 1, 0,
        16, 48, 1, 2, 28, 1, 0,
        16, 64, 1, 4, 36, 1, 1,
        20, 36, 1, 2, 28, 1, 1,
        20, 44, 1, 2, 34, 1, 1,
        20, 64, 1, 4, 42, 1, 1,
        22, 48, 1, 2, 38, 1, 1,
        24, 48, 1, 2, 41, 1, 1,
        24, 64, 1, 4, 46, 1, 1,
        26, 40, 1, 2, 38, 1, 1,
        26, 48, 1, 2, 42, 1, 1,
        26, 64, 1, 4, 50, 1, 1,
    ];

    /// <summary>
    /// Returns whether the format allows a symbol of the given size.
    /// </summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="format">The symbol format.</param>
    /// <returns><see langword="true"/> when the size is a size of the format.</returns>
    public static bool IsSize(int rows, int columns, DataMatrixFormat format)
    {
        for (int i = 0; i < SizeCount; i++)
        {
            DataMatrixSize size = new(i);
            if (size.Rows == rows && size.Columns == columns && size.Allows(format))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Encodes the text into a symbol with the quiet zone of section 5.6.2 of the GS1 General Specifications.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="gs1"> Whether the data is GS1 element strings, which start with the Function 1 Symbol Character and carry it for each
    /// <see cref="Gs1Data.Separator"/>. </param>
    /// <param name="format">The symbol format.</param>
    /// <param name="rows">The number of rows, or 0 to choose the size from the data.</param>
    /// <param name="columns">The number of columns, or 0 to choose the size from the data.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data is not valid or does not fit the symbol.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        bool gs1,
        DataMatrixFormat format,
        int rows,
        int columns,
        double xDimension,
        ref BarcodeSymbol symbol)
        => BuildSymbol(text, gs1, format, rows, columns, 0, QuietZone, xDimension, ref symbol);

    /// <summary>
    /// Encodes the text into a symbol.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="gs1"> Whether the data is GS1 element strings, which start with the Function 1 Symbol Character and carry it for each
    /// <see cref="Gs1Data.Separator"/>. </param>
    /// <param name="format">The symbol format.</param>
    /// <param name="rows">The number of rows, or 0 to choose the size from the data.</param>
    /// <param name="columns">The number of columns, or 0 to choose the size from the data.</param>
    /// <param name="c40Prefix">The number of characters at the start of the data that encode in C40 encodation, or 0.</param>
    /// <param name="quietZone">The quiet zone on each of the four sides, in modules.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data is not valid or does not fit the symbol.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        bool gs1,
        DataMatrixFormat format,
        int rows,
        int columns,
        int c40Prefix,
        int quietZone,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        // A message in format 05 or 06, between the header [)>RS05GS or [)>RS06GS and the trailer RS EOT, takes one Macro character for the header and the
        // trailer. The length limit applies to the message values, so it counts the Macro character, not the header and the trailer.
        bool macro = !gs1 && MacroMessage.Format(text) != 0;

        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(
            macro ? text.Length - MacroMessage.HeaderLength - MacroMessage.TrailerLength + 1 : text.Length,
            MaximumLength,
            nameof(text));

        // Clause 5.2.2 of ISO/IEC 16022: the default character set is ISO/IEC 8859-1, so each character must be one byte value. The check runs by code point so
        // a surrogate pair reports as one character.
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value > byte.MaxValue)
            {
                throw new ArgumentException($"Data Matrix carries only the characters U+0000 to U+00FF; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        // The message is the data as int values, so the function characters can take negative values that no byte has. It holds every character and at most one
        // function character in front. One rented block holds the message, the codewords of the largest symbol, the scratch of the encodation and the mapping
        // matrix of the largest symbol, so the symbol rents once. The text is at most MaximumLength characters, so the block is bounded.
        int messageCapacity = text.Length + 1;
        int scratchLength = DataMatrixCodewords.ScratchLength(messageCapacity);
        int mappingLength = MaximumMappingModules / sizeof(int);
        PooledBuffer<int> block = new(messageCapacity + MaximumTotalCodewords + scratchLength + mappingLength);
        try
        {
            Span<int> messageBuffer = block.Span[..messageCapacity];
            Span<int> codewords = block.Span.Slice(messageCapacity, MaximumTotalCodewords);
            Span<int> scratch = block.Span.Slice(messageCapacity + MaximumTotalCodewords, scratchLength);
            Span<sbyte> mapping = MemoryMarshal.Cast<int, sbyte>(block.Span.Slice(messageCapacity + MaximumTotalCodewords + scratchLength, mappingLength));

            // GS1 data starts with the Function 1 Symbol Character, and each separator is one too. Section 5.6.2 of the GS1 General Specifications transmits
            // either as <GS>. The element strings carry no <GS> of their own, so every <GS> in the prepared data is a separator.
            int messageLength = 0;
            if (gs1)
            {
                messageBuffer[messageLength++] = DataMatrixCodewords.Fnc1;
                foreach (char c in text)
                {
                    messageBuffer[messageLength++] = c == Gs1Data.Separator ? DataMatrixCodewords.Fnc1 : c;
                }
            }
            else
            {
                if (macro)
                {
                    messageBuffer[messageLength++] = text[5] == '5' ? DataMatrixCodewords.Macro05 : DataMatrixCodewords.Macro06;
                    text = text[MacroMessage.HeaderLength..^MacroMessage.TrailerLength];
                }

                foreach (char c in text)
                {
                    messageBuffer[messageLength++] = c;
                }
            }

            // The sizes the data can take, in table order: the index of each and its data codeword capacity. The encoder needs the capacities, because its end
            // rules depend on how many codewords are left in the symbol the data ends in. A fixed size leaves one capacity. The table has SizeCount sizes, so
            // 64 places hold them all.
            InlineArray64<int> allowedStorage = default;
            InlineArray64<int> capacityStorage = default;
            Span<int> allowed = allowedStorage;
            Span<int> capacities = capacityStorage;
            int allowedCount = 0;
            for (int i = 0; i < SizeCount; i++)
            {
                DataMatrixSize size = new(i);
                if (size.Allows(format) && (rows == 0 || (size.Rows == rows && size.Columns == columns)))
                {
                    allowed[allowedCount] = i;
                    capacities[allowedCount] = size.DataCodewords;
                    allowedCount++;
                }
            }

            int dataLength = DataMatrixCodewords.Encode(
                messageBuffer[..messageLength],
                capacities[..allowedCount],
                c40Prefix,
                codewords[..DataMatrixCodewords.MaximumCodewords],
                scratch);

            // The symbol is the smallest that holds the data codewords. Of two sizes with the same capacity, the one listed first wins.
            int chosen = -1;
            int chosenCapacity = int.MaxValue;
            for (int i = 0; i < allowedCount; i++)
            {
                if (capacities[i] >= dataLength && capacities[i] < chosenCapacity)
                {
                    chosen = allowed[i];
                    chosenCapacity = capacities[i];
                }
            }

            if (chosen < 0)
            {
                throw new ArgumentException($"The data takes {dataLength} data codewords, more than the Data Matrix symbol holds.", nameof(text));
            }

            // The pad characters fill the data codewords the data leaves empty. The first pad character is 129. The others take the 253-state randomising,
            // which adds ((149 x position) mod 253) + 1 for the position of the codeword counted from 1, and wraps above 254, so a run of pads does not repeat
            // one module pattern.
            DataMatrixSize chosenSize = new(chosen);
            Span<int> symbolCodewords = codewords[..(chosenCapacity + chosenSize.ErrorCodewords)];
            for (int i = dataLength; i < chosenCapacity; i++)
            {
                int pad = DataMatrixCodewords.Pad;
                if (i > dataLength)
                {
                    pad += ((149 * (i + 1)) % 253) + 1;
                    if (pad > 254)
                    {
                        pad -= 254;
                    }
                }

                symbolCodewords[i] = pad;
            }

            AppendErrorCorrection(chosenSize, symbolCodewords);

            // The codewords fill the mapping matrix, the data regions without their finder and alignment patterns, and the frame then puts the patterns around
            // each region.
            Span<sbyte> matrix = mapping[..(chosenSize.MappingRows * chosenSize.MappingColumns)];
            new MappingMatrix(matrix, chosenSize.MappingRows, chosenSize.MappingColumns).Place(symbolCodewords);
            Frame(chosenSize, matrix, symbol.BeginMatrix(chosenSize.Columns, chosenSize.Rows, quietZone, xDimension));
        }
        finally
        {
            block.Dispose();
        }
    }

    /// <summary>
    /// Writes the error correction codewords after the data codewords. The data codewords interleave across the blocks, codeword i in block i mod b, and so do
    /// the error correction codewords.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="codewords">The data codewords, followed by room for the error correction codewords.</param>
    private static void AppendErrorCorrection(DataMatrixSize size, Span<int> codewords)
    {
        // Every block has the same number of check codewords, so one generator polynomial serves all blocks. Its roots are alpha^1 to alpha^k. The largest
        // block takes 68 check codewords, the 48 x 48, 96 x 96 and 120 x 120 sizes, so the generator and the check codewords of one block are inline.
        int blocks = size.Blocks;
        int dataCount = size.DataCodewords;
        int perBlock = size.ErrorCodewords / blocks;
        InlineArray68<int> generatorStorage = default;
        Span<int> generator = ((Span<int>)generatorStorage)[..perBlock];
        GaloisField field = Field;
        field.Generator(1, generator);

        // The data codewords divide evenly over the blocks in every size but the 144 x 144 symbol. Its 1558 data codewords leave the first 8 of its 10 blocks
        // one codeword longer, 156 against 155, section 5.6.3.2 of the GS1 General Specifications.
        int shortLength = dataCount / blocks;
        int longBlocks = dataCount % blocks;

        // The check codewords of one block are calculated into an inline buffer, then written to their interleaved places. They go after the data codewords, so
        // the division of the next block still reads only data codewords.
        InlineArray68<int> checkStorage = default;
        Span<int> check = ((Span<int>)checkStorage)[..perBlock];
        Span<int> target = codewords[dataCount..];
        for (int block = 0; block < blocks; block++)
        {
            // The data codewords of block b are at b, b + blocks, b + 2 x blocks and so on, so the division reads them in place with a step of the block count.
            int count = block < longBlocks ? shortLength + 1 : shortLength;
            field.Remainder(codewords[block..], blocks, count, generator, check);

            // Check codeword j of a block goes to index j x blocks plus the column of the block, the same interleave as the data. The column of a block is the
            // block number, except in the 144 x 144 symbol, which interleaves the check codewords of its two short blocks first, then those of its eight long
            // blocks. With no long blocks the rotation leaves every column at its block number.
            int column = (block + blocks - longBlocks) % blocks;
            for (int j = 0; j < perBlock; j++)
            {
                target[(j * blocks) + column] = check[j];
            }
        }
    }

    /// <summary>
    /// Builds the module grid: each data region of the mapping matrix with its finder and alignment patterns. Each region has a solid dark column on its left
    /// and a solid dark row at its bottom, and alternating modules on its top row and its right column, which start dark at the top left.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="matrix">The mapping matrix, row by row, every module 0 or 1.</param>
    /// <param name="modules">The modules to write row by row, all light on entry.</param>
    private static void Frame(DataMatrixSize size, ReadOnlySpan<sbyte> matrix, Span<bool> modules)
    {
        // Each data region takes regionRows x regionColumns modules of the mapping matrix, and the patterns add one row above and below it and one column left
        // and right of it. The symbol is these framed regions side by side, so the position in a framed region tells which part a module is.
        int rows = size.Rows;
        int columns = size.Columns;
        int regionRows = size.MappingRows / size.RegionsDown;
        int regionColumns = size.MappingColumns / size.RegionsAcross;

        // The mapping matrix modules come out in reading order, region row by region row, so one running index reads them.
        int next = 0;
        for (int r = 0; r < rows; r++)
        {
            Span<bool> row = modules.Slice(r * columns, columns);
            int regionRow = r % (regionRows + 2);

            // The top row of a region alternates, dark in every even column. The symbol width is even, so the last column of the top row is light.
            if (regionRow == 0)
            {
                for (int c = 0; c < columns; c += 2)
                {
                    row[c] = true;
                }

                continue;
            }

            // The bottom row of a region is solid.
            if (regionRow == regionRows + 1)
            {
                row.Fill(true);
                continue;
            }

            // Inside a region row, each framed region is a solid left column, the regionColumns modules of the mapping matrix, and a right column that
            // alternates, dark in every odd row.
            bool rightColumn = (r & 1) == 1;
            for (int c = 0; c < columns; c += regionColumns + 2)
            {
                row[c] = true;
                Span<bool> region = row.Slice(c + 1, regionColumns);
                for (int i = 0; i < region.Length; i++)
                {
                    region[i] = matrix[next++] == 1;
                }

                row[c + regionColumns + 1] = rightColumn;
            }
        }
    }

    /// <summary>
    /// The mapping matrix of a symbol, row by row: the data regions without their finder and alignment patterns. A module holds -1 until a bit is placed in it,
    /// then 0 or 1.
    /// </summary>
    private readonly ref struct MappingMatrix
    {
        /// <summary>
        /// The modules, row by row.
        /// </summary>
        private readonly Span<sbyte> modules;

        /// <summary>
        /// The number of rows of the mapping matrix.
        /// </summary>
        private readonly int rows;

        /// <summary>
        /// The number of columns of the mapping matrix.
        /// </summary>
        private readonly int columns;

        /// <summary>
        /// Initializes a new instance of the <see cref="MappingMatrix"/> struct.
        /// </summary>
        /// <param name="modules">The modules, row by row, <paramref name="rows"/> x <paramref name="columns"/> long.</param>
        /// <param name="rows">The number of rows of the mapping matrix.</param>
        /// <param name="columns">The number of columns of the mapping matrix.</param>
        public MappingMatrix(Span<sbyte> modules, int rows, int columns)
        {
            this.modules = modules;
            this.rows = rows;
            this.columns = columns;
        }

        /// <summary>
        /// Places the codewords. Each codeword fills an eight module shape, and the shapes follow diagonal sweeps up and to the right and then down and to the
        /// left, with special shapes in the corners. A bit of 1 is a dark module. Every module holds 0 or 1 afterwards.
        /// </summary>
        /// <param name="codewords">The codewords in placement order.</param>
        public void Place(ReadOnlySpan<int> codewords)
        {
            // The placement starts at row 4, column 0, the lower right module of the first standard shape, and walks the matrix in diagonal sweeps. A module
            // that is not -1 already holds a bit, so a shape is placed only where its lower right module is still empty.
            Span<sbyte> matrix = this.modules;
            int rows = this.rows;
            int columns = this.columns;
            matrix.Fill(-1);
            int next = 0;
            int row = 4;
            int column = 0;
            do
            {
                // The four corner shapes, each as the row and the column of its eight modules. Each one applies only at the position and the matrix width it is
                // listed with, where the standard shape does not fit whole.
                if (row == rows && column == 0)
                {
                    this.PlaceCorner(
                        codewords[next++],
                        [
                            rows - 1, 0, rows - 1, 1, rows - 1, 2, 0, columns - 2,
                            0, columns - 1, 1, columns - 1, 2, columns - 1, 3, columns - 1
                        ]);
                }

                if (row == rows - 2 && column == 0 && columns % 4 != 0)
                {
                    this.PlaceCorner(
                        codewords[next++],
                        [
                            rows - 3, 0, rows - 2, 0, rows - 1, 0, 0, columns - 4,
                            0, columns - 3, 0, columns - 2, 0, columns - 1, 1, columns - 1
                        ]);
                }

                if (row == rows - 2 && column == 0 && columns % 8 == 4)
                {
                    this.PlaceCorner(
                        codewords[next++],
                        [
                            rows - 3, 0, rows - 2, 0, rows - 1, 0, 0, columns - 2,
                            0, columns - 1, 1, columns - 1, 2, columns - 1, 3, columns - 1
                        ]);
                }

                if (row == rows + 4 && column == 2 && columns % 8 == 0)
                {
                    this.PlaceCorner(
                        codewords[next++],
                        [
                            rows - 1, 0, rows - 1, columns - 1, 0, columns - 3, 0, columns - 2,
                            0, columns - 1, 1, columns - 3, 1, columns - 2, 1, columns - 1
                        ]);
                }

                // Sweep up and to the right, two rows up and two columns right per shape, until the sweep leaves the top or the right edge. A position below
                // the matrix or left of it can come up at the start of a sweep and is skipped.
                do
                {
                    if (row < rows && column >= 0 && matrix[(row * columns) + column] < 0)
                    {
                        this.PlaceShape(row, column, codewords[next++]);
                    }

                    row -= 2;
                    column += 2;
                }
                while (row >= 0 && column < columns);

                // The next sweep starts one row down and three columns right of where this one left the matrix, so the sweeps interlock without a gap.
                row++;
                column += 3;

                // Sweep down and to the left, the mirror of the sweep above.
                do
                {
                    if (row >= 0 && column < columns && matrix[(row * columns) + column] < 0)
                    {
                        this.PlaceShape(row, column, codewords[next++]);
                    }

                    row += 2;
                    column -= 2;
                }
                while (row < rows && column >= 0);

                row += 3;
                column++;
            }
            while (row < rows || column < columns);

            // A mapping matrix whose module count is 4 more than a multiple of 8 has four modules no codeword takes, in the bottom right corner. They take a
            // fixed pattern with the corner module and the module diagonally above it dark.
            int last = (rows * columns) - 1;
            if (matrix[last] < 0)
            {
                matrix[last] = 1;
                matrix[last - 1] = 0;
                matrix[last - columns] = 0;
                matrix[last - columns - 1] = 1;
            }
        }

        /// <summary>
        /// Places one codeword in the standard shape whose lower right module is at the given position: two modules in the row two above, three in the row
        /// above and three in the row itself.
        /// </summary>
        /// <param name="row">The row of the lower right module.</param>
        /// <param name="column">The column of the lower right module.</param>
        /// <param name="codeword">The codeword.</param>
        private void PlaceShape(int row, int column, int codeword)
        {
            // The bits go from the most significant, at the top left of the shape, row by row to the least significant at its lower right.
            this.PlaceBit(row - 2, column - 2, codeword, 0);
            this.PlaceBit(row - 2, column - 1, codeword, 1);
            this.PlaceBit(row - 1, column - 2, codeword, 2);
            this.PlaceBit(row - 1, column - 1, codeword, 3);
            this.PlaceBit(row - 1, column, codeword, 4);
            this.PlaceBit(row, column - 2, codeword, 5);
            this.PlaceBit(row, column - 1, codeword, 6);
            this.PlaceBit(row, column, codeword, 7);
        }

        /// <summary>
        /// Places one codeword in a corner shape.
        /// </summary>
        /// <param name="codeword">The codeword.</param>
        /// <param name="positions">The row and the column of each of the eight modules, the most significant bit first.</param>
        private void PlaceCorner(int codeword, ReadOnlySpan<int> positions)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                this.PlaceBit(positions[2 * bit], positions[(2 * bit) + 1], codeword, bit);
            }
        }

        /// <summary>
        /// Places one bit of a codeword. A position outside the matrix wraps to the opposite edge, with the offset that keeps the shape whole.
        /// </summary>
        /// <param name="row">The row, which can be outside the matrix.</param>
        /// <param name="column">The column, which can be outside the matrix.</param>
        /// <param name="codeword">The codeword.</param>
        /// <param name="bit">The bit, 0 for the most significant.</param>
        private void PlaceBit(int row, int column, int codeword, int bit)
        {
            // A shape that crosses the top edge continues at the bottom, and one that crosses the left edge continues at the right. The continuation also moves
            // along the other axis by 4 - ((n + 4) mod 8), where n is the length of the edge it crossed.
            int rows = this.rows;
            int columns = this.columns;
            if (row < 0)
            {
                row += rows;
                column += 4 - ((rows + 4) % 8);
            }

            if (column < 0)
            {
                column += columns;
                row += 4 - ((columns + 4) % 8);
            }

            // A rectangular extension symbol of eight rows has six mapping rows, so the column wrap can move a row past the bottom edge.
            if (row >= rows)
            {
                row -= rows;
            }

            // Bit 0 is the most significant bit of the codeword, so it is bit 7 of the byte.
            this.modules[(row * columns) + column] = (sbyte)((codeword >> (7 - bit)) & 1);
        }
    }

    /// <summary>
    /// The dimensions and error correction of one symbol size, read from <see cref="SizeTable"/>.
    /// </summary>
    private readonly struct DataMatrixSize
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataMatrixSize"/> struct from one size of <see cref="SizeTable"/>.
        /// </summary>
        /// <param name="index">The index of the size in the table, 0 to <see cref="SizeCount"/> - 1.</param>
        public DataMatrixSize(int index)
        {
            ReadOnlySpan<ushort> values = SizeTable.Slice(index * SizeFields, SizeFields);
            this.Rows = values[0];
            this.Columns = values[1];
            this.RegionsDown = values[2];
            this.RegionsAcross = values[3];
            this.ErrorCodewords = values[4];
            this.Blocks = values[5];
            this.Extension = values[6] != 0;
        }

        /// <summary>
        /// Gets the number of rows, finder pattern included.
        /// </summary>
        public int Rows { get; }

        /// <summary>
        /// Gets the number of columns, finder pattern included.
        /// </summary>
        public int Columns { get; }

        /// <summary>
        /// Gets the number of data regions in a column.
        /// </summary>
        public int RegionsDown { get; }

        /// <summary>
        /// Gets the number of data regions in a row.
        /// </summary>
        public int RegionsAcross { get; }

        /// <summary>
        /// Gets the number of error correction codewords.
        /// </summary>
        public int ErrorCodewords { get; }

        /// <summary>
        /// Gets the number of interleaved Reed-Solomon blocks.
        /// </summary>
        public int Blocks { get; }

        /// <summary>
        /// Gets a value indicating whether the size belongs to the rectangular extension only.
        /// </summary>
        public bool Extension { get; }

        /// <summary>
        /// Gets the number of rows of the mapping matrix: the symbol without the two rows of finder and alignment pattern around each data region.
        /// </summary>
        public int MappingRows => this.Rows - (2 * this.RegionsDown);

        /// <summary>
        /// Gets the number of columns of the mapping matrix.
        /// </summary>
        public int MappingColumns => this.Columns - (2 * this.RegionsAcross);

        /// <summary>
        /// Gets the number of data codewords: the eight module codewords the mapping matrix holds, less the error correction codewords.
        /// </summary>
        public int DataCodewords => (this.MappingRows * this.MappingColumns / 8) - this.ErrorCodewords;

        /// <summary>
        /// Returns whether the format allows this size.
        /// </summary>
        /// <param name="format">The symbol format.</param>
        /// <returns><see langword="true"/> when the format allows the size.</returns>
        public bool Allows(DataMatrixFormat format) => format switch
        {
            DataMatrixFormat.Square => this.Rows == this.Columns,
            DataMatrixFormat.Rectangular => this.Rows != this.Columns && !this.Extension,
            DataMatrixFormat.RectangularExtension => this.Rows != this.Columns,
            DataMatrixFormat.SquareOrRectangular => !this.Extension,
            _ => false,
        };
    }
}
