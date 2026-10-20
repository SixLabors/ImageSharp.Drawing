// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code49Symbology"/>. The tests check the code characters of the worked example and the numeric encodation of the Code 49
/// specification, the row structure, the height, the human readable interpretation and the input that the symbology rejects.
/// </summary>
public class Code49SymbologyTests
{
    /// <summary>
    /// Section 3.2: "The minimum standard Xdimension used is 0.0075 inches (0.191 mm)". At 1000 pixels a millimetre the smallest whole number of pixels at or
    /// over 0.1905 mm is 191 pixels, so an X dimension of 0.1 mm draws at 0.191 mm.
    /// </summary>
    [Fact]
    public void Code49_ClampsAnXDimensionUnderTheMinimumStandardValue()
        => SymbolEncoder.Encode(
            new Code49Symbology(),
            "12345",
            new BarcodeOptions { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 0.1 },
            static (ref symbol) => Assert.Equal(0.191, symbol.XDimension, 1e-9));

    /// <summary>
    /// Section 2.3.7 works "EXAMPLE 2" through to its code character matrix: the data with the row check character 41, then the last row with the two symbol
    /// check characters 1729 and 895 as code character pairs, the row count character 0 and the row check character 22.
    /// </summary>
    [Fact]
    public void Code49_MatchesTheWorkedExampleOfSection237()
        => Assert.Equal([14, 33, 10, 22, 25, 21, 14, 41, 38, 2, 35, 14, 18, 13, 0, 22], Codes("EXAMPLE 2"));

    /// <summary>
    /// Section 2.2.2 lists the code characters of five digit runs and their remainders.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    /// <param name="expected">The first code characters of the symbol.</param>
    [Theory]
    [InlineData("12345", new[] { 5, 17, 9 })]
    [InlineData("123456", new[] { 5, 17, 9, 6 })]
    [InlineData("12345678", new[] { 5, 17, 9, 14, 6 })]
    [InlineData("123456789", new[] { 5, 17, 9, 46, 16, 37 })]
    [InlineData("1234567", new[] { 43, 45, 2, 11, 39 })]
    public void Code49_NumericEncodationFollowsSection222(string text, int[] expected)
    {
        int[] codes = Codes(text);

        Assert.Equal(expected, codes[..expected.Length]);

        // Table 8: a symbol that starts in numeric encodation has starting mode 2, and section 2.1.3 puts it in Cr7 of a two row symbol as 7(2 - 2) + 2.
        Assert.Equal(2, codes[^2]);
    }

    /// <summary>
    /// Section 2.1: every row is 70 modules, and a one module separator bar stands between rows. Section 3.4 gives ten modules of quiet zone before the start
    /// pattern and one after the stop pattern.
    /// </summary>
    [Fact]
    public void Code49_RowsAreSeventyModulesWithSeparators()
        => Encode("MULTIPLE ROWS IN CODE 49", (ref symbol) =>
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
    /// Section 3.6.2: H = ((h + g) r + g) X. The bars are 8X, the recommended minimum of section 3.3, when no height is set, and a bar height in millimetres
    /// sets every data row.
    /// </summary>
    [Fact]
    public void Code49_HeightFollowsSection362()
    {
        Encode("MULTIPLE ROWS IN CODE 49", (ref symbol) =>
        {
            Assert.Equal(9, symbol.Rows.Length);
            Assert.Equal(46F, symbol.HeightInModules);
            Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[0]), height => Assert.Equal(8F, height));
        });

        BarcodeOptions options = new() { BarHeight = 12F * BarcodeSymbology.DefaultXDimension };
        ApproximateFloatComparer comparer = new(1e-4F);

        SymbolEncoder.Encode(new Code49Symbology(), "A", options, (ref symbol) =>
        {
            // The module is a whole number of pixels. Thus the row height in modules is the bar height divided by the X dimension of the symbol.
            float rowHeight = (float)(options.BarHeight.Value / symbol.XDimension);
            Assert.All(SymbolEncoder.RowBarHeights(ref symbol, symbol.Rows[0]), height => Assert.Equal(rowHeight, height, comparer));
            Assert.Equal((2 * rowHeight) + 3, symbol.HeightInModules, comparer);
        });
    }

    /// <summary>
    /// Appendix C: the human readable interpretation prints below the symbol, clear of the bottom separator bar. Without a font there is no text.
    /// </summary>
    [Fact]
    public void Code49_TextPrintsBelowTheBottomSeparator()
    {
        Encode("CODE 49", (ref symbol) => Assert.True(symbol.Text.IsEmpty));

        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        SymbolEncoder.Encode(new Code49Symbology(), "CODE 49", options, (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("CODE 49", symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
            Assert.Equal(0F, placement.Left);
            Assert.Equal(70F, placement.Right);
        });
    }

    /// <summary>
    /// Table 7 covers ASCII 0 to 127 and nothing else, Table 1 caps the data at 49 alphanumeric or 81 numeric characters, and an empty symbol carries no data.
    /// </summary>
    [Fact]
    public void Code49_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Code49Symbology(), string.Empty, new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Code49Symbology(), "café", new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Code49Symbology(), new string('A', 50), new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Code49Symbology(), new string('7', 82), new BarcodeOptions()));
        Encode(new string('A', 49), (ref symbol) => Assert.Equal(BarcodeSymbolKind.Stacked, symbol.Kind));
        Encode(new string('7', 81), (ref symbol) => Assert.Equal(BarcodeSymbolKind.Stacked, symbol.Kind));
    }

    /// <summary>
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new Code49Symbology(), text, new BarcodeOptions(), check);

    /// <summary>
    /// Encodes the text into the code characters of the whole symbol, eight per row for at most eight rows.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The code character values.</returns>
    private static int[] Codes(string text)
    {
        int[] codes = new int[64];
        int count = Code49Encoder.Encode(text, codes);

        return codes[..count];
    }
}
