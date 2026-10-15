// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds Data Matrix ECC 200 symbols: the symbol size, the pad codewords, the Reed-Solomon error
/// correction codewords, the placement of the codewords in the mapping matrix, and the finder and
/// alignment patterns around the data regions.
/// </summary>
internal static class DataMatrixEncoder
{
    /// <summary>
    /// The largest number of message values a symbol carries: 3116 digits in the 144 x 144 symbol.
    /// </summary>
    public const int MaximumLength = 3116;

    /// <summary>
    /// The quiet zone on each of the four sides in modules, section 5.6.2 of the GS1 General
    /// Specifications: "one module wide on all four sides".
    /// </summary>
    private const int QuietZone = 1;

    /// <summary>
    /// The length of the header of a message in format 05 or 06, [)&gt;RS05GS.
    /// </summary>
    private const int MacroHeaderLength = 7;

    /// <summary>
    /// The length of the trailer of a message in format 05 or 06, RS EOT.
    /// </summary>
    private const int MacroTrailerLength = 2;

    /// <summary>
    /// The symbol sizes. The square sizes and the first six rectangular sizes are ISO/IEC 16022 Table 7,
    /// which Tables 5-32 and 5-33 of the GS1 General Specifications repeat. The other rectangular sizes are
    /// the rectangular extension, which only <see cref="DataMatrixFormat.RectangularExtension"/> takes. A
    /// data codeword count that two sizes share goes to the size listed first.
    /// </summary>
    private static readonly DataMatrixSize[] Sizes =
    [
        new(10, 10, 1, 1, 5, 1, false),
        new(12, 12, 1, 1, 7, 1, false),
        new(14, 14, 1, 1, 10, 1, false),
        new(16, 16, 1, 1, 12, 1, false),
        new(18, 18, 1, 1, 14, 1, false),
        new(20, 20, 1, 1, 18, 1, false),
        new(22, 22, 1, 1, 20, 1, false),
        new(24, 24, 1, 1, 24, 1, false),
        new(26, 26, 1, 1, 28, 1, false),
        new(32, 32, 2, 2, 36, 1, false),
        new(36, 36, 2, 2, 42, 1, false),
        new(40, 40, 2, 2, 48, 1, false),
        new(44, 44, 2, 2, 56, 1, false),
        new(48, 48, 2, 2, 68, 1, false),
        new(52, 52, 2, 2, 84, 2, false),
        new(64, 64, 4, 4, 112, 2, false),
        new(72, 72, 4, 4, 144, 4, false),
        new(80, 80, 4, 4, 192, 4, false),
        new(88, 88, 4, 4, 224, 4, false),
        new(96, 96, 4, 4, 272, 4, false),
        new(104, 104, 4, 4, 336, 6, false),
        new(120, 120, 6, 6, 408, 6, false),
        new(132, 132, 6, 6, 496, 8, false),
        new(144, 144, 6, 6, 620, 10, false),
        new(8, 18, 1, 1, 7, 1, false),
        new(8, 32, 1, 2, 11, 1, false),
        new(8, 48, 1, 2, 15, 1, true),
        new(8, 64, 1, 4, 18, 1, true),
        new(8, 80, 1, 4, 22, 1, true),
        new(8, 96, 1, 4, 28, 1, true),
        new(8, 120, 1, 6, 32, 1, true),
        new(8, 144, 1, 6, 36, 1, true),
        new(12, 26, 1, 1, 14, 1, false),
        new(12, 36, 1, 2, 18, 1, false),
        new(12, 64, 1, 4, 27, 1, true),
        new(12, 88, 1, 4, 36, 1, true),
        new(16, 36, 1, 2, 24, 1, false),
        new(16, 48, 1, 2, 28, 1, false),
        new(16, 64, 1, 4, 36, 1, true),
        new(20, 36, 1, 2, 28, 1, true),
        new(20, 44, 1, 2, 34, 1, true),
        new(20, 64, 1, 4, 42, 1, true),
        new(22, 48, 1, 2, 38, 1, true),
        new(24, 48, 1, 2, 41, 1, true),
        new(24, 64, 1, 4, 46, 1, true),
        new(26, 40, 1, 2, 38, 1, true),
        new(26, 48, 1, 2, 42, 1, true),
        new(26, 64, 1, 4, 50, 1, true),
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
        foreach (DataMatrixSize size in Sizes)
        {
            if (size.Rows == rows && size.Columns == columns && size.Allows(format))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Encodes the text into a symbol with the quiet zone of section 5.6.2 of the GS1 General
    /// Specifications.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="gs1">
    /// Whether the data is GS1 element strings, which start with the Function 1 Symbol Character and
    /// carry it for each <see cref="Gs1Data.Separator"/>.
    /// </param>
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
    /// <param name="gs1">
    /// Whether the data is GS1 element strings, which start with the Function 1 Symbol Character and
    /// carry it for each <see cref="Gs1Data.Separator"/>.
    /// </param>
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
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // Clause 5.2.2 of ISO/IEC 16022: the default character set is ISO/IEC 8859-1, so each character
        // must be one byte value. The check runs by code point so a surrogate pair reports as one character.
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value > byte.MaxValue)
            {
                throw new ArgumentException(
                    $"Data Matrix carries only the characters U+0000 to U+00FF; got {current.ToDisplayString()}.",
                    nameof(text));
            }
        }

        // The message is the data as int values, so the function characters can take negative values that
        // no byte has. It holds every character and at most one function character in front, so its full
        // length is known here: a short message stays inline, and a longer one rents once. The codewords
        // go to one rented buffer of the largest symbol's capacity, which the encodation never passes.
        InlineArray64<int> messageStorage = default;
        int messageCapacity = text.Length + 1;
        ValueListBuilder<int> message = messageCapacity <= 64 ? new(messageStorage) : new(messageCapacity);
        PooledBuffer<int> codewords = new(DataMatrixCodewords.MaximumCodewords);
        try
        {
            // GS1 data starts with the Function 1 Symbol Character, and each separator is one too.
            // Section 5.6.2 of the GS1 General Specifications transmits either as <GS>. The element strings
            // carry no <GS> of their own, so every <GS> in the prepared data is a separator.
            if (gs1)
            {
                message.Append(DataMatrixCodewords.Fnc1);
                Span<int> values = message.AppendSpan(text.Length);
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    values[i] = c == Gs1Data.Separator ? DataMatrixCodewords.Fnc1 : c;
                }
            }
            else
            {
                // A message in format 05 or 06, between the header [)>RS05GS or [)>RS06GS and the trailer
                // RS EOT, takes one Macro character for the header and the trailer.
                if (text.Length >= MacroHeaderLength + MacroTrailerLength
                    && text.StartsWith("[)>\u001E0")
                    && (text[5] == '5' || text[5] == '6')
                    && text[6] == '\u001D'
                    && text.EndsWith("\u001E\u0004"))
                {
                    message.Append(text[5] == '5' ? DataMatrixCodewords.Macro05 : DataMatrixCodewords.Macro06);
                    text = text[MacroHeaderLength..^MacroTrailerLength];
                }

                Span<int> values = message.AppendSpan(text.Length);
                for (int i = 0; i < text.Length; i++)
                {
                    values[i] = text[i];
                }
            }

            // The sizes the data may take, in table order. The encoder needs their capacities, because its
            // end rules depend on how many codewords are left in the symbol the data ends in. A fixed size
            // leaves one capacity. The table has 48 sizes.
            InlineArray64<int> allowedStorage = default;
            InlineArray64<int> capacityStorage = default;
            Span<int> allowed = allowedStorage;
            Span<int> capacities = capacityStorage;
            int allowedCount = 0;
            for (int i = 0; i < Sizes.Length; i++)
            {
                DataMatrixSize size = Sizes[i];
                if (size.Allows(format) && (rows == 0 || (size.Rows == rows && size.Columns == columns)))
                {
                    allowed[allowedCount] = i;
                    capacities[allowedCount] = size.DataCodewords;
                    allowedCount++;
                }
            }

            int written = DataMatrixCodewords.Encode(
                message.AsSpan(),
                capacities[..allowedCount],
                c40Prefix,
                codewords.Span);

            // The symbol is the smallest that holds the data codewords. Of two sizes with the same
            // capacity, the one listed first wins.
            int chosen = -1;
            for (int i = 0; i < allowedCount; i++)
            {
                if (capacities[i] >= written && (chosen < 0 || capacities[i] < Sizes[chosen].DataCodewords))
                {
                    chosen = allowed[i];
                }
            }

            if (chosen < 0)
            {
                throw new ArgumentException($"The data takes {written} data codewords, more than the Data Matrix symbol holds.", nameof(text));
            }

            BuildMatrix(Sizes[chosen], codewords.Span[..written], quietZone, xDimension, ref symbol);
        }
        finally
        {
            codewords.Dispose();
            message.Dispose();
        }
    }

    /// <summary>
    /// Builds the module grid of a symbol from its data codewords and writes it to the symbol.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="data">The data codewords, without pad codewords.</param>
    /// <param name="quietZone">The quiet zone on each of the four sides, in modules.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildMatrix(DataMatrixSize size, ReadOnlySpan<int> data, int quietZone, double xDimension, ref BarcodeSymbol symbol)
    {
        int dataCount = size.DataCodewords;
        int total = dataCount + size.ErrorCodewords;

        // One rent holds the codewords and then the mapping matrix, whose sbyte values need no alignment.
        int mappingLength = size.MappingRows * size.MappingColumns;
        PooledBuffer<byte> buffer = new((total * sizeof(int)) + mappingLength);
        try
        {
            Span<int> codewords = MemoryMarshal.Cast<byte, int>(buffer.Span[..(total * sizeof(int))]);
            data.CopyTo(codewords);

            // The pad characters fill the data codewords the data leaves empty. The first pad character is
            // 129. The others take the 253-state randomising, which adds ((149 x position) mod 253) + 1 for
            // the position of the codeword counted from 1, and wraps above 254, so a run of pads does not
            // repeat one module pattern.
            for (int i = data.Length; i < dataCount; i++)
            {
                int pad = DataMatrixCodewords.Pad;
                if (i > data.Length)
                {
                    pad += ((149 * (i + 1)) % 253) + 1;
                    if (pad > 254)
                    {
                        pad -= 254;
                    }
                }

                codewords[i] = pad;
            }

            AppendErrorCorrection(size, codewords);

            // The codewords fill the mapping matrix, the data regions without their finder and alignment
            // patterns, and the frame then puts the patterns around each region. The mapping matrix holds
            // -1 for an empty module, then 0 or 1.
            Span<sbyte> matrix = MemoryMarshal.Cast<byte, sbyte>(buffer.Span.Slice(total * sizeof(int), mappingLength));
            Place(matrix, size.MappingRows, size.MappingColumns, codewords);
            Frame(size, matrix, symbol.BeginMatrix(size.Columns, size.Rows, quietZone, xDimension));
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Writes the error correction codewords after the data codewords. The data codewords interleave
    /// across the blocks, codeword i in block i mod b, and so do the error correction codewords.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="codewords">The data codewords, followed by room for the error correction codewords.</param>
    private static void AppendErrorCorrection(DataMatrixSize size, Span<int> codewords)
    {
        // Every block has the same number of check codewords, so one generator polynomial serves all
        // blocks. Its roots are alpha^1 to alpha^k. The largest block takes 68 check codewords, the 48 x 48,
        // 96 x 96 and 120 x 120 sizes, so the generator and the check codewords of one block are inline.
        int blocks = size.Blocks;
        int dataCount = size.DataCodewords;
        int perBlock = size.ErrorCodewords / blocks;
        InlineArray68<int> generatorStorage = default;
        Span<int> generator = ((Span<int>)generatorStorage)[..perBlock];
        GaloisField field = GaloisField.DataMatrix;
        field.Generator(1, generator);

        // The check codewords of one block are calculated into an inline buffer, then written to their
        // interleaved places. They go after the data codewords, so the division of the next block still
        // reads only data codewords.
        InlineArray68<int> checkStorage = default;
        Span<int> check = ((Span<int>)checkStorage)[..perBlock];
        Span<int> target = codewords[dataCount..];
        for (int block = 0; block < blocks; block++)
        {
            // The data codewords of block b are at b, b + blocks, b + 2 x blocks and so on, so the division
            // reads them in place with a step of the block count.
            // In the 144 x 144 symbol the first eight blocks carry 156 data codewords and the last two carry
            // 155, section 5.6.3.2 of the GS1 General Specifications.
            int count = dataCount == DataMatrixCodewords.MaximumCodewords
                ? block < 8 ? 156 : 155
                : dataCount / blocks;

            field.Remainder(codewords[block..], blocks, count, generator, check);

            // Check codeword j of a block goes to index j x blocks plus the column of the block, the same
            // interleave as the data. The column of a block is the block number, except in the 144 x 144
            // symbol, which interleaves the check codewords of the last two blocks first, then those of the
            // first eight blocks.
            int column = dataCount == DataMatrixCodewords.MaximumCodewords ? (block + blocks - 8) % blocks : block;
            for (int j = 0; j < perBlock; j++)
            {
                target[(j * blocks) + column] = check[j];
            }
        }
    }

    /// <summary>
    /// Places the codewords in the mapping matrix. Each codeword fills an eight module shape, and the
    /// shapes follow diagonal sweeps up and to the right and then down and to the left, with special
    /// shapes in the corners. A bit of 1 is a dark module.
    /// </summary>
    /// <param name="matrix">The mapping matrix, row by row.</param>
    /// <param name="rows">The number of rows of the mapping matrix.</param>
    /// <param name="columns">The number of columns of the mapping matrix.</param>
    /// <param name="codewords">The codewords in placement order.</param>
    private static void Place(Span<sbyte> matrix, int rows, int columns, ReadOnlySpan<int> codewords)
    {
        // The placement starts at row 4, column 0, the lower right module of the first standard shape,
        // and walks the matrix in diagonal sweeps. A module that is not -1 already holds a bit, so a
        // shape is placed only where its lower right module is still empty.
        matrix.Fill(-1);
        int next = 0;
        int row = 4;
        int column = 0;
        do
        {
            // The four corner shapes, each as the row and the column of its eight modules. Each one
            // applies only at the position and the matrix width it is listed with, where the standard
            // shape would not fit whole.
            if (row == rows && column == 0)
            {
                PlaceCorner(
                    matrix,
                    rows,
                    columns,
                    codewords[next++],
                    [
                        rows - 1, 0, rows - 1, 1, rows - 1, 2, 0, columns - 2,
                        0, columns - 1, 1, columns - 1, 2, columns - 1, 3, columns - 1
                    ]);
            }

            if (row == rows - 2 && column == 0 && columns % 4 != 0)
            {
                PlaceCorner(
                    matrix,
                    rows,
                    columns,
                    codewords[next++],
                    [
                        rows - 3, 0, rows - 2, 0, rows - 1, 0, 0, columns - 4,
                        0, columns - 3, 0, columns - 2, 0, columns - 1, 1, columns - 1
                    ]);
            }

            if (row == rows - 2 && column == 0 && columns % 8 == 4)
            {
                PlaceCorner(
                    matrix,
                    rows,
                    columns,
                    codewords[next++],
                    [
                        rows - 3, 0, rows - 2, 0, rows - 1, 0, 0, columns - 2,
                        0, columns - 1, 1, columns - 1, 2, columns - 1, 3, columns - 1
                    ]);
            }

            if (row == rows + 4 && column == 2 && columns % 8 == 0)
            {
                PlaceCorner(
                    matrix,
                    rows,
                    columns,
                    codewords[next++],
                    [
                        rows - 1, 0, rows - 1, columns - 1, 0, columns - 3, 0, columns - 2,
                        0, columns - 1, 1, columns - 3, 1, columns - 2, 1, columns - 1
                    ]);
            }

            // Sweep up and to the right, two rows up and two columns right per shape, until the sweep
            // leaves the top or the right edge. A position below the matrix or left of it can come up
            // at the start of a sweep and is skipped.
            do
            {
                if (row < rows && column >= 0 && matrix[(row * columns) + column] < 0)
                {
                    PlaceShape(matrix, rows, columns, row, column, codewords[next++]);
                }

                row -= 2;
                column += 2;
            }
            while (row >= 0 && column < columns);

            // The next sweep starts one row down and three columns right of where this one left the
            // matrix, so the sweeps interlock without a gap.
            row++;
            column += 3;

            // Sweep down and to the left, the mirror of the sweep above.
            do
            {
                if (row >= 0 && column < columns && matrix[(row * columns) + column] < 0)
                {
                    PlaceShape(matrix, rows, columns, row, column, codewords[next++]);
                }

                row += 2;
                column -= 2;
            }
            while (row < rows && column >= 0);

            row += 3;
            column++;
        }
        while (row < rows || column < columns);

        // A mapping matrix whose module count is 4 more than a multiple of 8 has four modules no codeword
        // takes, in the bottom right corner. They take a fixed pattern with the corner module and the
        // module diagonally above it dark.
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
    /// Places one codeword in the standard shape whose lower right module is at the given position: two
    /// modules in the row two above, three in the row above and three in the row itself.
    /// </summary>
    /// <param name="matrix">The mapping matrix, row by row.</param>
    /// <param name="rows">The number of rows of the mapping matrix.</param>
    /// <param name="columns">The number of columns of the mapping matrix.</param>
    /// <param name="row">The row of the lower right module.</param>
    /// <param name="column">The column of the lower right module.</param>
    /// <param name="codeword">The codeword.</param>
    private static void PlaceShape(Span<sbyte> matrix, int rows, int columns, int row, int column, int codeword)
    {
        // The bits go from the most significant, at the top left of the shape, row by row to the least
        // significant at its lower right.
        PlaceBit(matrix, rows, columns, row - 2, column - 2, codeword, 0);
        PlaceBit(matrix, rows, columns, row - 2, column - 1, codeword, 1);
        PlaceBit(matrix, rows, columns, row - 1, column - 2, codeword, 2);
        PlaceBit(matrix, rows, columns, row - 1, column - 1, codeword, 3);
        PlaceBit(matrix, rows, columns, row - 1, column, codeword, 4);
        PlaceBit(matrix, rows, columns, row, column - 2, codeword, 5);
        PlaceBit(matrix, rows, columns, row, column - 1, codeword, 6);
        PlaceBit(matrix, rows, columns, row, column, codeword, 7);
    }

    /// <summary>
    /// Places one codeword in a corner shape.
    /// </summary>
    /// <param name="matrix">The mapping matrix, row by row.</param>
    /// <param name="rows">The number of rows of the mapping matrix.</param>
    /// <param name="columns">The number of columns of the mapping matrix.</param>
    /// <param name="codeword">The codeword.</param>
    /// <param name="positions">The row and the column of each of the eight modules, the most significant bit first.</param>
    private static void PlaceCorner(Span<sbyte> matrix, int rows, int columns, int codeword, ReadOnlySpan<int> positions)
    {
        for (int bit = 0; bit < 8; bit++)
        {
            PlaceBit(matrix, rows, columns, positions[2 * bit], positions[(2 * bit) + 1], codeword, bit);
        }
    }

    /// <summary>
    /// Places one bit of a codeword. A position outside the matrix wraps to the opposite edge, with the
    /// offset that keeps the shape whole.
    /// </summary>
    /// <param name="matrix">The mapping matrix, row by row.</param>
    /// <param name="rows">The number of rows of the mapping matrix.</param>
    /// <param name="columns">The number of columns of the mapping matrix.</param>
    /// <param name="row">The row, which may be outside the matrix.</param>
    /// <param name="column">The column, which may be outside the matrix.</param>
    /// <param name="codeword">The codeword.</param>
    /// <param name="bit">The bit, 0 for the most significant.</param>
    private static void PlaceBit(Span<sbyte> matrix, int rows, int columns, int row, int column, int codeword, int bit)
    {
        // A shape that crosses the top edge continues at the bottom, and one that crosses the left edge
        // continues at the right. The continuation also moves along the other axis by 4 - ((n + 4) mod 8),
        // where n is the length of the edge it crossed.
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

        // A rectangular extension symbol of eight rows has six mapping rows, so the column wrap can move
        // a row past the bottom edge.
        if (row >= rows)
        {
            row -= rows;
        }

        // Bit 0 is the most significant bit of the codeword, so it is bit 7 of the byte.
        matrix[(row * columns) + column] = (sbyte)((codeword >> (7 - bit)) & 1);
    }

    /// <summary>
    /// Builds the module grid: each data region of the mapping matrix with its finder and alignment
    /// patterns. Each region has a solid dark column on its left and a solid dark row at its bottom, and
    /// alternating modules on its top row and its right column, which start dark at the top left.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="matrix">The mapping matrix, row by row.</param>
    /// <param name="modules">The modules to write row by row, all light on entry.</param>
    private static void Frame(DataMatrixSize size, ReadOnlySpan<sbyte> matrix, Span<bool> modules)
    {
        // Each data region takes regionRows x regionColumns modules of the mapping matrix, and the
        // patterns add one row above and below it and one column left and right of it. The symbol is these
        // framed regions side by side, so the position in a framed region tells which part a module is.
        int rows = size.Rows;
        int columns = size.Columns;
        int regionRows = size.MappingRows / size.RegionsDown;
        int regionColumns = size.MappingColumns / size.RegionsAcross;

        // The mapping matrix modules come out in reading order, region row by region row, so one running
        // index reads them.
        int next = 0;
        for (int r = 0; r < rows; r++)
        {
            Span<bool> row = modules.Slice(r * columns, columns);
            int regionRow = r % (regionRows + 2);

            // The top row of a region alternates, dark in every even column. The symbol width is even,
            // so the last column of the top row is light.
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

            // Inside a region row, the left column is solid, the right column alternates, dark in every
            // odd row, and the modules between are the mapping matrix.
            for (int c = 0; c < columns; c++)
            {
                int regionColumn = c % (regionColumns + 2);
                if (regionColumn == 0)
                {
                    row[c] = true;
                }
                else if (regionColumn == regionColumns + 1)
                {
                    row[c] = (r & 1) == 1;
                }
                else
                {
                    row[c] = matrix[next++] == 1;
                }
            }
        }
    }

    /// <summary>
    /// The dimensions and error correction of one symbol size.
    /// </summary>
    private readonly struct DataMatrixSize
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataMatrixSize"/> struct.
        /// </summary>
        /// <param name="rows">The number of rows, finder pattern included.</param>
        /// <param name="columns">The number of columns, finder pattern included.</param>
        /// <param name="regionsDown">The number of data regions in a column.</param>
        /// <param name="regionsAcross">The number of data regions in a row.</param>
        /// <param name="errorCodewords">The number of error correction codewords.</param>
        /// <param name="blocks">The number of interleaved Reed-Solomon blocks.</param>
        /// <param name="extension">Whether the size belongs to the rectangular extension only.</param>
        public DataMatrixSize(int rows, int columns, int regionsDown, int regionsAcross, int errorCodewords, int blocks, bool extension)
        {
            this.Rows = rows;
            this.Columns = columns;
            this.RegionsDown = regionsDown;
            this.RegionsAcross = regionsAcross;
            this.ErrorCodewords = errorCodewords;
            this.Blocks = blocks;
            this.Extension = extension;
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
        /// Gets the number of rows of the mapping matrix: the symbol without the two rows of finder and
        /// alignment pattern around each data region.
        /// </summary>
        public int MappingRows => this.Rows - (2 * this.RegionsDown);

        /// <summary>
        /// Gets the number of columns of the mapping matrix.
        /// </summary>
        public int MappingColumns => this.Columns - (2 * this.RegionsAcross);

        /// <summary>
        /// Gets the number of data codewords: the eight module codewords the mapping matrix holds, less the
        /// error correction codewords.
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
