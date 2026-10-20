// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="CodablockFSymbology"/>. The tests check the row height, the human readable interpretation and the input that the symbology
/// rejects.
/// </summary>
public class CodablockFSymbologyTests
{
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
    /// The quiet zones before and after the rows are at least 10X wide.
    /// </summary>
    [Fact]
    public void CodablockF_QuietZonesAreTenModules()
        => Encode("Codablock F", 8, static (ref symbol) =>
        {
            Assert.Equal(10F, symbol.LeadingQuietZone);
            Assert.Equal(10F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// The rows are Code 128 characters. Clause 4.3.4.2 d) of ISO/IEC 15417 encodes a character above 127 as FNC4 and the character 128 below it, so each
    /// such character takes two data columns. With four columns, four of them fill two rows, and the last row's check characters K1 and K2 take a third row.
    /// Four ASCII characters fill one row, and K1 and K2 take the second. The symbol has an edge bar above and below and a separator between rows.
    /// </summary>
    [Fact]
    public void CodablockF_EncodesCharactersAbove127WithFnc4()
    {
        Encode("éàéà", 4, static (ref symbol) => Assert.Equal((3 * 2) + 1, symbol.Rows.Length));
        Encode("iaia", 4, static (ref symbol) => Assert.Equal((2 * 2) + 1, symbol.Rows.Length));
    }

    /// <summary>
    /// The column count is 4 to 62, the characters are U+0000 to U+00FF, and 44 rows are the most a symbol holds.
    /// </summary>
    [Fact]
    public void CodablockF_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodablockFSymbology(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodablockFSymbology(63));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodablockFSymbology(8), "Ā", new BarcodeOptions()));
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
}
