// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="CodablockFSymbology"/>. Each expected row is the alternating bar and space module widths of one row, starting with a bar, taken
/// from an independent reference implementation through its raw encoding API. A row carries its start character, code set selector, row indicator, data
/// columns, row check character and stop character, so the encodation, the pad and the check characters all show up here.
/// </summary>
public class CodablockFSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="CodablockF_MatchesReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, int, string> CodablockF_MatchesReferenceRowsData { get; } = new()
    {
        // Upper case, a space and a long digit run over four rows of eight columns.
        {
            "CODABLOCK F 34567890123456789010040digit",
            8,
            "2114121141311214211313211331211123131113231311231321311331211313212122222331112|" +
            "2114121141312312121123312122221323112122221131411311233311212411121122322331112|" +
            "2114121131411321312141211122321311233311212411122141212213121213221323112331112|" +
            "2114121141311221321231221412211421121221141421121241122131311133212214112331112"
        },

        // Mixed case and a space, padded to two rows.
        {
            "Codablock F",
            8,
            "2114121141311114221313211341111412211211241214212211141341111411222211322331112|" +
            "2114121141312312122412112122221323111131411141311131412214111223122113312331112"
        },

        // Digits alone: every row starts in code set C.
        {
            "12345678",
            8,
            "2114121131412122221122321311233311212411121141311131411141311131412412112331112|" +
            "2114121141312312121131411141311131411141311131411141311322123221122131132331112"
        },

        // A single character.
        {
            "A",
            8,
            "2114121141311114221113231131411141311131411141311131411141311131412113312331112|" +
            "2114121141312312121131411141311131411141311131411141311123311114221312222331112"
        },

        // No data at all: two rows of pad characters round the check characters.
        {
            string.Empty,
            8,
            "2114121141311114221131411141311131411141311131411141311131411141313131212331112|" +
            "2114121141312312121131411141311131411141311131411141311114221114224121212331112"
        },

        // Punctuation and lower case.
        {
            "Hello, World!",
            8,
            "2114121141311114222311131122142211142211141341111122322122223113211241122331112|" +
            "2114121141312312121341111212412211141412212221221131411221143141112312122331112"
        },

        // Lower case, control characters and upper case: shifts and latches between code sets A and B.
        {
            "abcDEF",
            8,
            "2114121141311114221211241214211411223111411211241214211123131321132212132331112|" +
            "2114121141312312121323111131411141311131411141311131414131111212233122122331112"
        },

        // Forty digits over three rows.
        {
            "1234567890123456789012345678901234567890",
            8,
            "2114121131412221221122321311233311212411122141211122321311233311211323112331112|" +
            "2114121131411123312411122141211122321311233311212411122141211122322222212331112|" +
            "2114121131411321311311233311212411122141211141311131413221122411122131312331112"
        },

        // Twelve columns.
        {
            "The quick brown fox jumps over the lazy dog 0123456789",
            12,
            "2114121141311411222133111224111122142122221211421242111421121411222412112122221214211212411121332331112|" +
            "2114121141312312121341114211122411122122221124121341114212112122221422111242114131111112421211242331112|" +
            "2114121141311122321142122122221341114112121122141212412122221241121224111122142122222211142211142331112|" +
            "2114121141311221321211242141212121412122221412211341111221142122221131412221223121311131234311112331112|" +
            "2114121131411133211411222121411141311131411141311131411141311131411141311131412321214311112313112331112"
        },

        // Four columns, the narrowest symbol.
        {
            "ABCDEFGHIJ",
            4,
            "2114121141311211241113231311231313211123132321212331112|" +
            "2114121141312312121321131323112113132311131131412331112|" +
            "2114121141311122322313111121331212231111433113212331112"
        },

        // Four columns with an odd digit run.
        { "12345", 4, "2114121131412122221122321311231141312132121421122331112|2114121141312312121131411141311211241412211223122331112" },
    };

    /// <summary>
    /// The bar and space widths of every data row match the reference implementation. Each data row sits between two separator or edge rows.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="columns">The number of data columns in a row.</param>
    /// <param name="expected">The reference run widths of each data row, separated by vertical bars.</param>
    [Theory]
    [MemberData(nameof(CodablockF_MatchesReferenceRowsData))]
    public void CodablockF_MatchesReferenceRows(string text, int columns, string expected)
        => Encode(text, columns, (ref symbol) =>
        {
            string[] rows = expected.Split('|');

            Assert.Equal((rows.Length * 2) + 1, symbol.Rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.Equal(rows[i], SymbolEncoder.RunString(ref symbol, symbol.Rows[(i * 2) + 1]));
            }
        });

    /// <summary>
    /// A row is eleven modules per character and thirteen for the stop character. A separator row keeps the start and stop patterns of the rows above and below
    /// it and fills the columns between them, and a solid bar closes the symbol at the top and at the bottom. The quiet zones are ten modules.
    /// </summary>
    /// <param name="columns">The number of data columns in a row.</param>
    /// <param name="width">The symbol width in modules.</param>
    /// <param name="fill">The width in modules of the bar that fills a separator row between its start and stop patterns.</param>
    [Theory]
    [InlineData(4, 101, 79)]
    [InlineData(8, 145, 123)]
    [InlineData(12, 189, 167)]
    public void CodablockF_RowsSeparatorsAndEdgesFollowTheReferences(int columns, int width, int fill)
        => Encode("ABCDEFGHIJ", columns, (ref symbol) =>
        {
            Assert.Equal(width, symbol.WidthInModules);
            Assert.Equal([width], SymbolEncoder.RowRuns(ref symbol, symbol.Rows[0]));
            Assert.Equal([width], SymbolEncoder.RowRuns(ref symbol, symbol.Rows[^1]));
            Assert.Equal([2, 1, 1, 4, 1, 2, fill, 3, 3, 1, 1, 1, 2], SymbolEncoder.RowRuns(ref symbol, symbol.Rows[2]));
            Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[2]), height => Assert.Equal(1F, height));
            Assert.Equal(0F, symbol.FrameThickness);
            Assert.Equal(10F, symbol.LeadingQuietZone);
            Assert.Equal(10F, symbol.TrailingQuietZone);
            Assert.True(symbol.Text.IsEmpty);
        });

    /// <summary>
    /// The nominal row height is ten modules, or 0.55 modules per data column plus three when that is more, and a bar height in millimetres sets every data
    /// row.
    /// </summary>
    [Fact]
    public void CodablockF_RowHeightFollowsTheColumnCount()
    {
        ApproximateFloatComparer comparer = new(1e-4F);

        Encode("A", 8, (ref symbol) => Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[1]), height => Assert.Equal(10F, height)));
        Encode("A", 20, (ref symbol) => Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[1]), height => Assert.Equal(14F, height, comparer)));

        BarcodeOptions options = new() { BarHeight = 12F * BarcodeSymbology.DefaultXDimension };
        SymbolEncoder.Encode(new CodablockFSymbology(), "A", options, (ref symbol) =>
        {
            Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[1]), height => Assert.Equal(12F, height, comparer));
            Assert.Equal(27F, symbol.HeightInModules, comparer);
        });
    }

    /// <summary>
    /// K1 and K2 are the modulo 86 sums of the message characters weighted from one and from zero, and take the fourth and third places from the end of the
    /// last row. Every row ends in the modulo 103 check character of its own characters.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("CODABLOCK F 34567890123456789010040digit")]
    [InlineData("Hello, World!")]
    [InlineData("12345678")]
    public void CodablockF_CheckCharactersFollowTheReferences(string text)
    {
        int[] values = SymbolEncoder.Read(new CodablockFSymbology(8), text, new BarcodeOptions(), DataRowValues);
        int rowSize = 8 + CodablockFEncoder.Overhead;
        int k1 = 0;
        int k2 = 0;
        for (int i = 0; i < text.Length; i++)
        {
            k1 = (k1 + ((i + 1) * text[i])) % 86;
            k2 = (k2 + (i * text[i])) % 86;
        }

        // The pair maps through the code set the last row ends in. Either mapping of the pair is accepted, and a mix of the two is not.
        int[] last = values[^rowSize..];
        bool alphabetic = last[^4] == MapToAlphabetic(k1) && last[^3] == MapToAlphabetic(k2);
        bool numeric = last[^4] == k1 && last[^3] == k2;

        Assert.True(alphabetic || numeric);

        for (int row = 0; row < values.Length / rowSize; row++)
        {
            int at = row * rowSize;
            int sum = values[at];
            for (int k = 1; k <= 8 + 2; k++)
            {
                sum += values[at + k] * k;
            }

            Assert.Equal(sum % 103, values[at + 8 + 3]);
            Assert.Equal(103, values[at]);
            Assert.Equal(106, values[at + rowSize - 1]);
        }
    }

    /// <summary>
    /// The data prints below the bottom bar, clear of it, when a font is set. Without a font there is no text.
    /// </summary>
    [Fact]
    public void CodablockF_TextPrintsBelowTheBottomBar()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        SymbolEncoder.Encode(new CodablockFSymbology(), "Codablock F", options, (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("Codablock F", symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
            Assert.Equal(0F, placement.Left);
            Assert.Equal(145F, placement.Right);
        });
    }

    /// <summary>
    /// The column count is 4 to 62, the characters are ASCII 0 to 127, and 44 rows are the most a symbol holds.
    /// </summary>
    [Fact]
    public void CodablockF_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodablockFSymbology(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodablockFSymbology(63));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodablockFSymbology(8), "café", new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodablockFSymbology(4), new string('A', 44 * 4), new BarcodeOptions()));
        Encode(new string('A', (44 * 4) - 2), 4, (ref symbol) => Assert.Equal(BarcodeSymbolKind.Stacked, symbol.Kind));
    }

    /// <summary>
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="columns">The number of data columns in a row.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, int columns, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new CodablockFSymbology(columns), text, new BarcodeOptions(), check);

    /// <summary>
    /// Reads the Code 128 character values of every data row of a symbol, from the top. Every symbol character but the stop character is six runs; the stop
    /// character closes each row with seven.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The character values, one data row after another.</returns>
    private static int[] DataRowValues(ref BarcodeSymbol symbol)
    {
        ReadOnlySpan<uint> patterns = Code128Patterns.Patterns;
        List<int> values = [];

        // The data rows stand between the top edge bar and the separators, at the odd row indexes.
        for (int r = 1; r < symbol.Rows.Length; r += 2)
        {
            BarcodeRow row = symbol.Rows[r];
            ReadOnlySpan<int> runs = symbol.Runs.Slice(row.RunStart, row.RunCount);
            int index = 0;
            while (index < runs.Length)
            {
                // A pattern packs its elements four bits each, the first in the highest bits.
                int elements = runs.Length - index == 7 ? 7 : 6;
                uint pattern = 0;
                for (int e = 0; e < elements; e++)
                {
                    pattern = (pattern << 4) | (uint)runs[index + e];
                }

                int value = patterns.IndexOf(pattern);
                Assert.True(value >= 0);
                values.Add(value);
                index += elements;
            }
        }

        return [.. values];
    }

    /// <summary>
    /// Maps a check value to the code set A or B character that carries it: 0 to 31 map to 64 to 95, 32 to 47 map to 0 to 15, and 48 to 85 map to 26 to 63.
    /// In code set C a check value is its own character, so that case needs no mapping.
    /// </summary>
    /// <param name="value">The check value, 0 to 85.</param>
    /// <returns>The Code 128 character value in code set A or B.</returns>
    private static int MapToAlphabetic(int value)
    {
        if (value < 32)
        {
            return value + 64;
        }

        return value < 48 ? value - 32 : value - 22;
    }
}
