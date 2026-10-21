// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes Ultracode symbols: rows of colour tiles between black and white timing patterns. Each codeword, 0 to 284, is a tile of five modules in a column,
/// each cyan, magenta, yellow or green. The message is the 8-bit characters of the text, with Reed-Solomon error correction over the prime field of 283. A
/// data column count code beside the left finder tells a reader how many tile columns follow.
/// </summary>
internal static class UltracodeEncoder
{
    /// <summary>
    /// The longest text, in characters: at level EC1, 263 characters make 266 message codewords and 16 check codewords, the 282 tiles of the largest symbol.
    /// </summary>
    private const int MaximumLength = 263;

    /// <summary>
    /// The quiet zone on each of the four sides in modules.
    /// </summary>
    private const int QuietZone = 1;

    /// <summary>
    /// The prime modulus of the Reed-Solomon field.
    /// </summary>
    private const int Modulus = 283;

    /// <summary>
    /// The start codeword of a message of 8-bit characters, ISO/IEC 8859-1.
    /// </summary>
    private const int StartLatin1 = 257;

    /// <summary>
    /// The codeword that separates the error correction from the data in the tile sequence.
    /// </summary>
    private const int Separator = 283;

    /// <summary>
    /// The pad codeword that fills the tiles after the data.
    /// </summary>
    private const int Pad = 284;

    /// <summary>
    /// The codewords before the data in the message: the start codeword, the message codeword count and the auxiliary codeword count.
    /// </summary>
    private const int MessageHeader = 3;

    /// <summary>
    /// The tiles outside the message and its check codewords: the tile count, the separator and the check codeword count.
    /// </summary>
    private const int StructuralTiles = 3;

    /// <summary>
    /// The modules of a tile, all in one column.
    /// </summary>
    private const int TileHeight = 5;

    /// <summary>
    /// The bits of the colour code of one tile module.
    /// </summary>
    private const int ColorBits = 2;

    /// <summary>
    /// The modules a tile row takes with the timing row below it.
    /// </summary>
    private const int RowPitch = TileHeight + 1;

    /// <summary>
    /// The columns left of the first tile column: the black edge, a timing column, the column of the data column count code, a black column and a light
    /// column. The right edge adds one more column, so a symbol is its tile columns plus six.
    /// </summary>
    private const int LeftColumns = 5;

    /// <summary>
    /// The column that the vertical timing columns count from. They stand every <see cref="TimingSpacing"/> columns after it. The finder draws this column
    /// black.
    /// </summary>
    private const int FirstTimingColumn = 3;

    /// <summary>
    /// The distance between two vertical timing columns.
    /// </summary>
    private const int TimingSpacing = 16;

    /// <summary>
    /// The column of the data column count code.
    /// </summary>
    private const int CountColumn = 2;

    /// <summary>
    /// The most tile columns, timing columns included.
    /// </summary>
    private const int MaximumColumns = 61;

    /// <summary>
    /// The values of one symbol height in <see cref="Heights"/>.
    /// </summary>
    private const int HeightFields = 4;

    /// <summary>
    /// Gets the symbol heights, <see cref="HeightFields"/> values each: the tile rows, the fewest and the most tiles a symbol of that height holds, and the
    /// fewest tile columns, timing columns included.
    /// </summary>
    private static ReadOnlySpan<short> Heights =>
    [
        2, 7, 37, 5,
        3, 36, 84, 13,
        4, 85, 161, 22,
        5, 142, 282, 29,
    ];

    /// <summary>
    /// Gets the factor of each error correction level, EC1 to EC5: the check codewords are the factor times the started groups of 25 message codewords,
    /// plus 5.
    /// </summary>
    private static ReadOnlySpan<byte> CheckFactors => [1, 2, 4, 6, 8];

    /// <summary>
    /// Gets the tile of each codeword, 0 to 284: the five module colours from the top, two bits each with the top module in the highest bits, 0 cyan,
    /// 1 magenta, 2 yellow and 3 green.
    /// </summary>
    private static ReadOnlySpan<ushort> Tiles =>
    [
        70, 71, 73, 75, 77, 78, 97, 98, 99, 100, 102, 103, 108, 109, 110, 113, 114, 115, 116, 118,
        119, 120, 121, 123, 134, 135, 137, 141, 142, 145, 146, 147, 152, 153, 155, 156, 157, 158, 177, 178,
        179, 180, 182, 183, 184, 185, 187, 198, 199, 201, 203, 206, 209, 210, 211, 216, 217, 219, 220, 221,
        222, 225, 226, 227, 228, 230, 231, 236, 237, 238, 274, 275, 280, 283, 284, 286, 289, 290, 291, 292,
        294, 295, 300, 301, 302, 305, 306, 308, 310, 311, 312, 313, 315, 388, 390, 391, 392, 393, 395, 396,
        397, 398, 402, 403, 408, 411, 412, 414, 433, 434, 435, 436, 438, 439, 440, 441, 443, 452, 454, 455,
        456, 457, 459, 461, 462, 466, 467, 472, 475, 476, 478, 481, 482, 483, 484, 486, 487, 492, 493, 494,
        529, 530, 531, 536, 537, 539, 540, 541, 542, 545, 547, 548, 551, 556, 557, 561, 562, 563, 564, 566,
        567, 568, 569, 571, 580, 582, 583, 584, 585, 587, 588, 589, 590, 609, 611, 612, 615, 620, 621, 625,
        626, 627, 628, 630, 631, 632, 633, 635, 708, 710, 711, 712, 713, 715, 716, 717, 718, 721, 722, 723,
        728, 729, 731, 732, 733, 734, 737, 739, 740, 743, 748, 749, 785, 786, 787, 792, 793, 795, 796, 797,
        798, 801, 802, 803, 804, 806, 807, 812, 813, 814, 818, 820, 822, 824, 825, 836, 838, 839, 840, 841,
        843, 844, 845, 846, 865, 866, 867, 868, 870, 871, 876, 877, 878, 881, 882, 884, 886, 888, 889, 900,
        902, 903, 904, 905, 907, 908, 909, 910, 913, 914, 915, 920, 921, 923, 924, 925, 926, 945, 946, 948,
        950, 952, 953, 750, 546,
    ];

    /// <summary>
    /// Gets the upper tile of the data column count code for each count of tile columns beyond the fewest the height allows, 0 to 31, coded as
    /// <see cref="Tiles"/>.
    /// </summary>
    private static ReadOnlySpan<ushort> CountUpper =>
    [
        147, 211, 99, 227, 115, 179, 71, 135, 199, 103, 231, 119, 75, 203, 155, 123,
        145, 209, 97, 225, 113, 177, 73, 137, 201, 217, 121, 185, 77, 141, 157, 109,
    ];

    /// <summary>
    /// Gets the lower tile of the data column count code for each count of tile columns beyond the fewest the height allows, 0 to 31, coded as
    /// <see cref="Tiles"/>.
    /// </summary>
    private static ReadOnlySpan<ushort> CountLower =>
    [
        466, 482, 434, 390, 454, 294, 486, 310, 438, 398, 462, 286, 414, 478, 302, 494,
        786, 914, 866, 818, 902, 806, 870, 822, 886, 950, 846, 910, 798, 926, 814, 878,
    ];

    /// <summary>
    /// Builds an Ultracode symbol.
    /// </summary>
    /// <param name="text">The text, characters U+0000 to U+00FF.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text is empty, too long, has a character above U+00FF, or does not fit at the level.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, UltracodeErrorCorrectionLevel level, double xDimension, ref BarcodeSymbol symbol)
    {
        Guard.MustBeBetweenOrEqualTo(text.Length, 1, MaximumLength, nameof(text));
        int invalid = text.IndexOfAnyExceptInRange('\0', 'ÿ');
        if (invalid >= 0)
        {
            throw new ArgumentException($"Ultracode encodes U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.", nameof(text));
        }

        // The message is the start codeword, the message codeword count, the auxiliary codeword count and the data, one codeword a character. The check
        // codewords grow with the message, and the auxiliary codewords are three fewer than the check codewords.
        int messageCount = text.Length + MessageHeader;
        int checkCount = (CheckFactors[(int)level] * ((messageCount + 24) / 25)) + 5;
        int auxiliaryCount = checkCount - 3;
        int tileCount = messageCount + checkCount;

        // The height is the first whose tile range holds the tiles.
        if (!SymbolHeight.TryFind(tileCount, out SymbolHeight height))
        {
            throw new ArgumentException("The text does not fit in an Ultracode symbol at the requested error correction level.", nameof(text));
        }

        // The width is the fewest tile columns that hold every tile with the three structural tiles; the tiles left over are pads. At MaximumColumns, 58 data
        // columns, the heights of 2 to 5 tile rows hold 113, 171, 229 and 287 tiles, at least the most tiles of each height, so the loop ends by then.
        int tileColumns = height.FewestColumns;
        int pads = (DataColumns(tileColumns) * height.TileRows) - StructuralTiles - tileCount;
        while (pads < 0)
        {
            tileColumns++;
            pads = (DataColumns(tileColumns) * height.TileRows) - StructuralTiles - tileCount;
        }

        // The message is three header codewords and then one codeword a character, so the division reads it from the text and needs no buffer. The check
        // codewords are at most 101: the factor of level EC5, 8, times the 12 started groups of 25 message codewords that 282 tiles allow, plus 5.
        InlineArray4<int> headerStorage = default;
        Span<int> header = ((Span<int>)headerStorage)[..MessageHeader];
        header[0] = StartLatin1;
        header[1] = messageCount;
        header[2] = auxiliaryCount;
        InlineArray128<int> checkStorage = default;
        Span<int> check = ((Span<int>)checkStorage)[..checkCount];
        AddErrorCorrection(header, text, check);

        int rows = (height.TileRows * RowPitch) + 1;
        int columns = tileColumns + LeftColumns + 1;
        TileGrid grid = new(symbol.BeginColored(columns, rows, QuietZone, xDimension), columns, rows);
        grid.DrawStructure();
        grid.DrawColumnCount(tileColumns - height.FewestColumns);

        // The tiles run down each tile column, then on to the next: the start codeword, the message codeword count, the check codewords, the tile count,
        // the separator, the auxiliary codeword count, the data, the pads and the check codeword count.
        grid.Write(StartLatin1);
        grid.Write(messageCount);
        foreach (int codeword in check)
        {
            grid.Write(codeword);
        }

        grid.Write(tileCount);
        grid.Write(Separator);
        grid.Write(auxiliaryCount);
        foreach (char c in text)
        {
            grid.Write(c);
        }

        for (int p = 0; p < pads; p++)
        {
            grid.Write(Pad);
        }

        grid.Write(checkCount);
    }

    /// <summary>
    /// Returns the tile columns of a width that hold tiles. The vertical timing columns stand at tile columns 15, 31 and 47, one every 16 from 15, and hold
    /// none.
    /// </summary>
    /// <param name="tileColumns">The tile columns, timing columns included.</param>
    /// <returns>The tile columns that hold tiles.</returns>
    private static int DataColumns(int tileColumns) => tileColumns - ((tileColumns + 1) / TimingSpacing);

    /// <summary>
    /// Writes the Reed-Solomon check codewords of the message over the prime field of 283. The message is the header codewords followed by one codeword a
    /// character, the first codeword the highest term.
    /// </summary>
    /// <param name="header">The header codewords.</param>
    /// <param name="text">The characters, each one codeword.</param>
    /// <param name="check">The check codewords to write, the first one first.</param>
    private static void AddErrorCorrection(ReadOnlySpan<int> header, ReadOnlySpan<char> text, Span<int> check)
    {
        InlineArray128<int> generatorStorage = default;
        Span<int> generator = ((Span<int>)generatorStorage)[..(check.Length + 1)];
        Generator(generator);
        check.Clear();
        foreach (int codeword in header)
        {
            Divide(codeword, generator, check);
        }

        foreach (char c in text)
        {
            Divide(c, generator, check);
        }
    }

    /// <summary>
    /// Feeds one codeword into the division of the message polynomial times x^k by the generator, run as a shift register: the codeword feeds back its
    /// difference from the first register value, and the register shifts down by one place with the generator terms added in. After the last codeword the
    /// register holds the check codewords.
    /// </summary>
    /// <param name="codeword">The codeword.</param>
    /// <param name="generator">The generator coefficients, the coefficient of x^0 first.</param>
    /// <param name="register">The register, the check codewords.</param>
    private static void Divide(int codeword, ReadOnlySpan<int> generator, Span<int> register)
    {
        int k = register.Length;
        int feedback = (codeword - register[0] + Modulus) % Modulus;
        for (int j = 0; j < k - 1; j++)
        {
            register[j] = (register[j + 1] + (generator[k - 1 - j] * feedback)) % Modulus;
        }

        register[k - 1] = (generator[0] * feedback) % Modulus;
    }

    /// <summary>
    /// Writes the generator coefficients of k check codewords, the coefficient of x^0 first. The polynomial is the product of (x + 3^i) for i = 1 to k over
    /// the field of 283, without its leading 1, with the sign of every second coefficient from the top turned.
    /// </summary>
    /// <param name="generator">The buffer of k + 1 coefficients; the first k receive the result.</param>
    private static void Generator(Span<int> generator)
    {
        // Each factor (x + a) moves every coefficient up one degree and adds a times the coefficient below it, from the top down, so each update reads the
        // coefficient below before that coefficient changes.
        int k = generator.Length - 1;
        generator.Clear();
        generator[0] = 1;
        int root = 1;
        for (int i = 1; i <= k; i++)
        {
            root = (root * 3) % Modulus;
            generator[i] = generator[i - 1];
            for (int j = i - 1; j >= 1; j--)
            {
                generator[j] = ((generator[j] * root) + generator[j - 1]) % Modulus;
            }

            generator[0] = (generator[0] * root) % Modulus;
        }

        // The loop negates the coefficients of x^(k-1), x^(k-3) and so on. This turns the product of (x + a) into the product of (x - a) for the terms that the
        // division uses.
        for (int j = k - 1; j >= 0; j -= 2)
        {
            generator[j] = (Modulus - generator[j]) % Modulus;
        }
    }

    /// <summary>
    /// One symbol height of <see cref="Heights"/>.
    /// </summary>
    private readonly struct SymbolHeight
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolHeight"/> struct from one entry of <see cref="Heights"/>.
        /// </summary>
        /// <param name="index">The index of the height, 0 to 3.</param>
        private SymbolHeight(int index)
        {
            ReadOnlySpan<short> values = Heights.Slice(index * HeightFields, HeightFields);
            this.TileRows = values[0];
            this.FewestTiles = values[1];
            this.MostTiles = values[2];
            this.FewestColumns = values[3];
        }

        /// <summary>
        /// Gets the tile rows.
        /// </summary>
        public int TileRows { get; }

        /// <summary>
        /// Gets the fewest tiles a symbol of this height holds.
        /// </summary>
        public int FewestTiles { get; }

        /// <summary>
        /// Gets the most tiles a symbol of this height holds.
        /// </summary>
        public int MostTiles { get; }

        /// <summary>
        /// Gets the fewest tile columns, timing columns included.
        /// </summary>
        public int FewestColumns { get; }

        /// <summary>
        /// Finds the first height whose tile range holds a tile count.
        /// </summary>
        /// <param name="tileCount">The tiles of the message and its check codewords.</param>
        /// <param name="height">The height.</param>
        /// <returns><see langword="true"/> when a height holds the tiles.</returns>
        public static bool TryFind(int tileCount, out SymbolHeight height)
        {
            for (int i = 0; i < Heights.Length / HeightFields; i++)
            {
                height = new SymbolHeight(i);
                if (tileCount >= height.FewestTiles && tileCount <= height.MostTiles)
                {
                    return true;
                }
            }

            height = default;
            return false;
        }
    }

    /// <summary>
    /// Writes the modules of an Ultracode symbol: the structure of finder and timing patterns, the data column count code and the tiles in order.
    /// </summary>
    private ref struct TileGrid
    {
        /// <summary>
        /// The module colours, row-major.
        /// </summary>
        private readonly Span<BarcodeModuleColor> modules;

        /// <summary>
        /// The number of columns.
        /// </summary>
        private readonly int columns;

        /// <summary>
        /// The number of rows.
        /// </summary>
        private readonly int rows;

        /// <summary>
        /// The column of the next tile.
        /// </summary>
        private int x;

        /// <summary>
        /// The row of the top module of the next tile.
        /// </summary>
        private int y;

        /// <summary>
        /// Initializes a new instance of the <see cref="TileGrid"/> struct.
        /// </summary>
        /// <param name="modules">The module colours, all light.</param>
        /// <param name="columns">The number of columns.</param>
        /// <param name="rows">The number of rows.</param>
        public TileGrid(Span<BarcodeModuleColor> modules, int columns, int rows)
        {
            this.modules = modules;
            this.columns = columns;
            this.rows = rows;
            this.x = LeftColumns;
            this.y = 1;
        }

        /// <summary>
        /// Draws the black edges, the horizontal timing rows between the tile rows, the vertical timing columns and the finder at the left. A timing pattern
        /// alternates dark and light; the vertical patterns are drawn last, so they cross the horizontal ones.
        /// </summary>
        public readonly void DrawStructure()
        {
            int columns = this.columns;
            int rows = this.rows;
            for (int x = 0; x < columns; x++)
            {
                if (x >= LeftColumns)
                {
                    BarcodeModuleColor timing = (x & 1) == 1 ? BarcodeModuleColor.Dark : BarcodeModuleColor.Light;
                    for (int y = 0; y < rows; y += RowPitch)
                    {
                        this.Set(x, y, timing);
                    }
                }

                this.Set(x, 0, BarcodeModuleColor.Dark);
                this.Set(x, rows - 1, BarcodeModuleColor.Dark);
            }

            for (int y = 1; y < rows - 1; y++)
            {
                BarcodeModuleColor timing = (y & 1) == 0 ? BarcodeModuleColor.Dark : BarcodeModuleColor.Light;
                for (int x = FirstTimingColumn; x < columns; x += TimingSpacing)
                {
                    this.Set(x, y, timing);
                }

                // The finder: a black edge, a timing column, a light column, a black column and a light column before the first tile column.
                this.Set(0, y, BarcodeModuleColor.Dark);
                this.Set(1, y, timing);
                this.Set(2, y, BarcodeModuleColor.Light);
                this.Set(3, y, BarcodeModuleColor.Dark);
                this.Set(4, y, BarcodeModuleColor.Light);
                this.Set(columns - 1, y, BarcodeModuleColor.Dark);
            }
        }

        /// <summary>
        /// Draws the data column count code in the middle of column 2: the upper tile, a light module and the lower tile of the count of tile columns beyond
        /// the fewest the height allows.
        /// </summary>
        /// <param name="extraColumns">The tile columns beyond the fewest, 0 to 31.</param>
        public readonly void DrawColumnCount(int extraColumns)
        {
            int top = (this.rows / 2) - TileHeight;
            this.DrawTile(CountColumn, top, CountUpper[extraColumns]);
            this.Set(CountColumn, top + TileHeight, BarcodeModuleColor.Light);
            this.DrawTile(CountColumn, top + TileHeight + 1, CountLower[extraColumns]);
        }

        /// <summary>
        /// Writes the tile of a codeword at the next place: down the tile column, past each horizontal timing row, then to the top of the next tile column,
        /// past each vertical timing column.
        /// </summary>
        /// <param name="codeword">The codeword, 0 to 284.</param>
        public void Write(int codeword)
        {
            this.DrawTile(this.x, this.y, Tiles[codeword]);
            this.y += TileHeight;
            if (this.y != this.rows - 1)
            {
                this.y++;
                return;
            }

            this.x++;
            this.y = 1;
            if ((this.x - FirstTimingColumn) % TimingSpacing == 0)
            {
                this.x++;
            }
        }

        /// <summary>
        /// Draws a tile of five modules down from a cell. Each module's 2-bit code counts from cyan, so the colour is cyan plus the code.
        /// </summary>
        /// <param name="x">The column.</param>
        /// <param name="y">The row of the top module.</param>
        /// <param name="tile">The five module colours, the top module in the highest bits.</param>
        private readonly void DrawTile(int x, int y, int tile)
        {
            for (int k = 0; k < TileHeight; k++)
            {
                int code = (tile >> ((TileHeight - 1 - k) * ColorBits)) & ((1 << ColorBits) - 1);
                this.Set(x, y + k, BarcodeModuleColor.Cyan + (byte)code);
            }
        }

        /// <summary>
        /// Sets the colour of a module.
        /// </summary>
        /// <param name="x">The column.</param>
        /// <param name="y">The row.</param>
        /// <param name="color">The colour.</param>
        private readonly void Set(int x, int y, BarcodeModuleColor color) => this.modules[(y * this.columns) + x] = color;
    }
}
