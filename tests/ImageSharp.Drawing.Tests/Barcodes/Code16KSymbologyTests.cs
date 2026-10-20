// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code16KSymbology"/>. The tests check the row structure, the height, the starting symbol character, the check characters,
/// the human readable interpretation and the input that the symbology rejects, as the Code 16K specification states them.
/// </summary>
public class Code16KSymbologyTests
{
    /// <summary>
    /// Section 4.5 d) of EN 12323: "row height (Y dimension): minimum 8X". At a 0.5 mm module 8X is 4 mm, so a bar height of 1 mm draws every row at 8X. The
    /// three rows of CODE 16K and their four 1X separators are then 3 x 9 + 1 modules high.
    /// </summary>
    [Fact]
    public void Code16K_DrawsARowUnderEightModulesAtEightModules()
        => SymbolEncoder.Encode(
            new Code16KSymbology(),
            "CODE 16K",
            new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi, XDimension = 0.5, BarHeight = 1 },
            static (ref symbol) =>
            {
                Assert.Equal(28F, symbol.HeightInModules);
                Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[0]), height => Assert.Equal(8F, height));
            });

    /// <summary>
    /// Section 2.1: every row is 70 modules, and a one module separator bar stands between rows. Section 3.4 gives ten modules of quiet zone before the start
    /// pattern and one after the stop pattern.
    /// </summary>
    [Fact]
    public void Code16K_RowsAreSeventyModulesWithSeparators()
        => Encode("Abcd-1234567890-wxyZ", (ref symbol) =>
        {
            for (int i = 0; i < symbol.Rows.Length; i++)
            {
                BarcodeRow row = symbol.Rows[i];

                Assert.Equal(70, SymbolEncoder.RowRuns(ref symbol, row).Sum());
                if ((i & 1) == 1)
                {
                    Assert.Equal([70], SymbolEncoder.RowRuns(ref symbol, row));
                    Assert.Equal([1F], SymbolEncoder.RowBarHeights(ref symbol, row));
                }
            }

            Assert.Equal(1F, symbol.FrameThickness);
            Assert.Equal(10F, symbol.LeadingQuietZone);
            Assert.Equal(1F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// Section 3.6.2: H = ((h + g) r + g) X, with h the bar height and g the separator height. The bars are 8X, the recommended minimum of section 3.3, when no
    /// height is set.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="rows">The number of data rows.</param>
    [Theory]
    [InlineData("A", 2)]
    [InlineData("CODE 16K", 3)]
    [InlineData("Abcd-1234567890-wxyZ", 4)]
    public void Code16K_HeightFollowsSection362(string text, int rows)
        => Encode(text, (ref symbol) =>
        {
            Assert.Equal((rows * 2) - 1, symbol.Rows.Length);
            Assert.Equal((9 * rows) + 1, symbol.HeightInModules);
            Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[0]), height => Assert.Equal(8F, height));
        });

    /// <summary>
    /// A bar height in millimetres sets the height of every data row.
    /// </summary>
    [Fact]
    public void Code16K_BarHeightOptionSetsRowHeight()
    {
        BarcodeOptions options = new() { BarHeight = 12F * BarcodeSymbology.DefaultXDimension };
        ApproximateFloatComparer comparer = new(1e-4F);

        SymbolEncoder.Encode(new Code16KSymbology(), "A", options, (ref symbol) =>
        {
            Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[0]), height => Assert.Equal(12F, height, comparer));
            Assert.Equal(27F, symbol.HeightInModules, comparer);
        });
    }

    /// <summary>
    /// Section 2.3.1: the starting symbol character is s = 7(r - 2) + m, and Table 3 gives the mode. Appendix G rule 1 picks the mode from the first
    /// characters.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="mode">The mode of Table 3 that the first characters select.</param>
    [Theory]
    [InlineData("\u0001\u0002ab", 0)]
    [InlineData("CODE 16K", 1)]
    [InlineData("1234567890", 2)]
    [InlineData("12345", 5)]
    [InlineData("x1234\u0001", 5)]
    [InlineData("ab0123456789", 6)]
    public void Code16K_StartingSymbolCharacterFollowsSection231(string text, int mode)
    {
        int[] values = SymbolEncoder.Read(new Code16KSymbology(), text, new BarcodeOptions(), DataRowValues);
        int rows = values.Length / 5;

        Assert.Equal((7 * (rows - 2)) + mode, values[0]);
    }

    /// <summary>
    /// Section 2.6: C1 is the modulo 107 sum of the symbol characters weighted from two upward, and C2 is the modulo 107 sum weighted from one upward with C1
    /// as its last term.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("Abcd-1234567890-wxyZ")]
    [InlineData("Hello, World!")]
    [InlineData("")]
    public void Code16K_CheckCharactersFollowSection26(string text)
    {
        int[] values = SymbolEncoder.Read(new Code16KSymbology(), text, new BarcodeOptions(), DataRowValues);
        int c1 = 0;
        int c2 = 0;
        for (int i = 0; i < values.Length - 2; i++)
        {
            c1 += (i + 2) * values[i];
            c2 += (i + 1) * values[i];
        }

        c1 %= 107;
        c2 = (c2 + ((values.Length - 1) * c1)) % 107;

        Assert.Equal(c1, values[^2]);
        Assert.Equal(c2, values[^1]);
    }

    /// <summary>
    /// Appendix D: the human readable interpretation prints below the symbol, clear of the bottom separator bar. Without a font there is no text.
    /// </summary>
    [Fact]
    public void Code16K_TextPrintsBelowTheBottomSeparator()
    {
        Encode("CODE 16K", (ref symbol) => Assert.True(symbol.Text.IsEmpty));

        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        SymbolEncoder.Encode(new Code16KSymbology(), "CODE 16K", options, (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("CODE 16K", symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
            Assert.Equal(0F, placement.Left);
            Assert.Equal(70F, placement.Right);
        });
    }

    /// <summary>
    /// Section 2.2 gives the code sets ASCII 0 to 127 between them, and section 2.1 caps a symbol at sixteen rows of five symbol characters, three of them
    /// overhead.
    /// </summary>
    [Fact]
    public void Code16K_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Code16KSymbology(), "café", new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Code16KSymbology(), new string('A', 78), new BarcodeOptions()));
        Encode(new string('A', 77), (ref symbol) => Assert.Equal(BarcodeSymbolKind.Stacked, symbol.Kind));
    }

    /// <summary>
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new Code16KSymbology(), text, new BarcodeOptions(), check);

    /// <summary>
    /// Reads the symbol character values of every data row of a symbol, from the top. Section 2.1 lays a row out as a start pattern of four elements, a guard
    /// bar, five symbol characters of six elements and a stop pattern of four elements. Table 2 gives the characters the Code 128 patterns, and gives the value
    /// 106 the pattern 2 1 1 1 3 3.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The symbol character values, one data row after another.</returns>
    private static int[] DataRowValues(ref BarcodeSymbol symbol)
    {
        const uint tripleShiftPattern = 0x211133;
        ReadOnlySpan<uint> patterns = Code128Patterns.Patterns;
        List<int> values = [];

        // The data rows stand at the even row indexes, with a separator row between two of them.
        for (int r = 0; r < symbol.Rows.Length; r += 2)
        {
            BarcodeRow row = symbol.Rows[r];
            ReadOnlySpan<int> runs = symbol.Runs.Slice(row.RunStart, row.RunCount);
            for (int k = 0; k < 5; k++)
            {
                // A pattern packs its six elements four bits each, the first in the highest bits.
                uint pattern = 0;
                for (int e = 0; e < 6; e++)
                {
                    pattern = (pattern << 4) | (uint)runs[5 + (k * 6) + e];
                }

                int value = pattern == tripleShiftPattern ? 106 : patterns.IndexOf(pattern);
                Assert.True(value >= 0);
                values.Add(value);
            }
        }

        return [.. values];
    }
}
