// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code49Symbology"/>. Each expected row is the alternating bar and space
/// module widths of one row, starting with a bar, taken from an independent reference implementation
/// through its raw encoding API. A row carries its start pattern, four symbol characters in the parity of
/// Table 2 and its stop pattern, so the encodation, the row check characters, the row count character and
/// the symbol check characters all show up here.
/// </summary>
public class Code49SymbologyTests
{
    [Theory]

    // The symbol of Figure 1, five rows.
    [InlineData("MULTIPLE ROWS IN CODE 49", "11512211311132242141112151114113144|11111414131243113112234112131133224|11222511212131341122112431311143214|11122222144112132211113162211312514|11224211311611123151311311211321514")]

    // The worked example of section 2.3.7.
    [InlineData("EXAMPLE 2", "11233132114212132122421113134312114|11231222132311221414211241161211314")]

    // Five digits: numeric mode, section 2.2.2, three code characters for the block.
    [InlineData("12345", "11242141114112132222421131221216114|11224211311313123213321312122221334")]

    // Six digits: a block and one digit as a code character from Table 6.
    [InlineData("123456", "11242141111421114222421131131213414|11224211313332121131112341331313114")]

    // Eight digits: a block and three digits as two code characters.
    [InlineData("12345678", "11242141111151123213133311311213414|11224211312431211212213223114312224")]

    // Nine digits: a block and four digits as three code characters with 100,000 added.
    [InlineData("123456789", "11242141114111223211116141122121614|11224211311125231111112145132221324")]

    // Seven digits: four then three, section 2.2.2.
    [InlineData("1234567", "11222214213313131151114121131215214|11224211313421112221322213313313114")]

    // Upper case, a space and a short digit run, which stays alphanumeric.
    [InlineData("CODE 49", "11121311251122241312222214415112114|11224211312223141113232311125213114")]

    // Punctuation and lower case, which Table 7 encodes with Shift 1 and Shift 2.
    [InlineData("Hello, World!", "11411152113111432111123332432112214|11221342111222133213222213132124214|11143121312114113311123332213311414|11131512121211311612322222141312314")]

    // A single character, padded with numeric shifts.
    [InlineData("A", "11332212122242113122421131311124314|11224211312161113131124221125122214")]

    // Lower case at the start: mode 5, an implied Shift 2, Table 8.
    [InlineData("abc", "11241121231242114122122232112214324|11224211311112242312312241132323114")]

    // Letters round a digit run long enough for numeric mode in the middle of the symbol.
    [InlineData("ABC123456789DEF", "11612122112212223213232131324112124|11215112221221323211222413221232134|11224211311113222422522111211343114")]

    // Letters and digits alternating, all alphanumeric.
    [InlineData("A1B2C3", "11221212153222132114221321122214314|11224211314122123112213142251123114")]

    // Forty nine letters, the most an eight row symbol carries, with three symbol check characters.
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVW", "11612122111241241114131411123231224|11212115131115221312213241341212214|11224212211223131322114114412151114|11151511114141121233112231143211224|11411312131413141122232212141115124|11222211513421113132221312214211144|11122313132213222222142221212111444|11112112263122231211221126221242124")]

    // Eighty one digits, the numeric capacity of Table 1.
    [InlineData("012345678901234567890123456789012345678901234567890123456789012345678901234567890", "11132211152111221631333111113121164|11161211131241222223114131421313114|11421121411225131115212131225111224|11421121142311413132122132143212214|11122513111521213142112141222331124|11231141313212213212412222132141314|11132211154211214112251311113121164|11151214112111342211513311111341234")]
    public void Code49_MatchesReferenceRows(string text, string expected)
    {
        StackedBarcodeSymbol symbol = Encode(text);
        string[] rows = expected.Split('|');
        Assert.Equal((rows.Length * 2) - 1, symbol.Rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            Assert.Equal(rows[i], string.Concat(symbol.Rows[i * 2].RunWidths));
        }
    }

    /// <summary>
    /// Section 2.3.7 works "EXAMPLE 2" through to its code character matrix: the data with the row check
    /// character 41, then the last row with the two symbol check characters 1729 and 895 as code
    /// character pairs, the row count character 0 and the row check character 22.
    /// </summary>
    [Fact]
    public void Code49_MatchesTheWorkedExampleOfSection237()
    {
        int[] codes = Code49Encoder.Encode("EXAMPLE 2");
        Assert.Equal([14, 33, 10, 22, 25, 21, 14, 41, 38, 2, 35, 14, 18, 13, 0, 22], codes);
    }

    /// <summary>
    /// Section 2.2.2 lists the code characters of five digit runs and their remainders.
    /// </summary>
    [Theory]
    [InlineData("12345", new[] { 5, 17, 9 })]
    [InlineData("123456", new[] { 5, 17, 9, 6 })]
    [InlineData("12345678", new[] { 5, 17, 9, 14, 6 })]
    [InlineData("123456789", new[] { 5, 17, 9, 46, 16, 37 })]
    [InlineData("1234567", new[] { 43, 45, 2, 11, 39 })]
    public void Code49_NumericEncodationFollowsSection222(string text, int[] expected)
    {
        int[] codes = Code49Encoder.Encode(text);
        Assert.Equal(expected, codes[..expected.Length]);

        // Table 8: a symbol that starts in numeric encodation has starting mode 2, and section 2.1.3
        // puts it in Cr7 of a two row symbol as 7(2 - 2) + 2.
        Assert.Equal(2, codes[^2]);
    }

    /// <summary>
    /// Section 2.1: every row is 70 modules, and a one module separator bar stands between rows. Section
    /// 3.4 gives ten modules of quiet zone before the start pattern and one after the stop pattern.
    /// </summary>
    [Fact]
    public void Code49_RowsAreSeventyModulesWithSeparators()
    {
        StackedBarcodeSymbol symbol = Encode("MULTIPLE ROWS IN CODE 49");
        for (int i = 0; i < symbol.Rows.Length; i++)
        {
            LinearBarcodeSymbol row = symbol.Rows[i];
            Assert.Equal(70F, row.WidthInModules);
            if ((i & 1) == 1)
            {
                Assert.Equal([70], row.RunWidths);
                Assert.Equal([1F], row.BarHeights);
            }
        }

        Assert.Equal(1F, symbol.EdgeBarHeight);
        Assert.Equal(10F, symbol.LeadingQuietZone);
        Assert.Equal(1F, symbol.TrailingQuietZone);
    }

    /// <summary>
    /// Section 3.6.2: H = ((h + g) r + g) X. The bars are 8X, the recommended minimum of section 3.3,
    /// when no height is set, and a bar height in millimetres sets every data row.
    /// </summary>
    [Fact]
    public void Code49_HeightFollowsSection362()
    {
        StackedBarcodeSymbol symbol = Encode("MULTIPLE ROWS IN CODE 49");
        Assert.Equal(9, symbol.Rows.Length);
        Assert.Equal(46F, symbol.HeightInModules);
        Assert.All(symbol.Rows[0].BarHeights, height => Assert.Equal(8F, height));

        BarcodeOptions options = new() { BarHeight = 12F * BarcodeSymbology.PointXDimension };
        symbol = (StackedBarcodeSymbol)new Code49Symbology().Encode("A", options);
        ApproximateFloatComparer comparer = new(1e-4F);
        Assert.All(symbol.Rows[0].BarHeights, height => Assert.Equal(12F, height, comparer));
        Assert.Equal(27F, symbol.HeightInModules, comparer);
    }

    /// <summary>
    /// Appendix C: the human readable interpretation prints below the symbol, clear of the bottom
    /// separator bar. Without a font there is no text.
    /// </summary>
    [Fact]
    public void Code49_TextPrintsBelowTheBottomSeparator()
    {
        Assert.Empty(Encode("CODE 49").Text);

        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        StackedBarcodeSymbol symbol = (StackedBarcodeSymbol)new Code49Symbology().Encode("CODE 49", options);
        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("CODE 49", placement.Text);
        Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
        Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
        Assert.Equal(0F, placement.Left);
        Assert.Equal(70F, placement.Right);
    }

    /// <summary>
    /// Table 7 covers ASCII 0 to 127 and nothing else, Table 1 caps the data at 49 alphanumeric or 81
    /// numeric characters, and an empty symbol carries no data.
    /// </summary>
    [Fact]
    public void Code49_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => Encode(string.Empty));
        Assert.Throws<ArgumentException>(() => Encode("café"));
        Assert.Throws<ArgumentException>(() => Encode(new string('A', 50)));
        Assert.Throws<ArgumentException>(() => Encode(new string('7', 82)));
        Assert.NotNull(Encode(new string('A', 49)));
        Assert.NotNull(Encode(new string('7', 81)));
    }

    private static StackedBarcodeSymbol Encode(string text)
        => (StackedBarcodeSymbol)new Code49Symbology().Encode(text, new BarcodeOptions());
}
