// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code16KSymbology"/>. Each expected row is the alternating bar and space
/// module widths of one row, starting with a bar, taken from an independent reference implementation
/// through its raw encoding API. A row carries its start character, the guard bar, five symbol characters
/// and its stop character, so the mode, the code and shift characters, the pad and the check characters
/// all show up here.
/// </summary>
public class Code16KSymbologyTests
{
    [Theory]

    // Two lower case letters and ten digits: mode 6, Code C with an implied Double Shift B, section 2.3.1.
    [InlineData("ab0123456789", "321111222131211241214212221223121313211|222111131231411222121412211321121332221")]

    // Mixed case, digits and punctuation over four rows.
    [InlineData("Abcd-1234567890-wxyZ", "321111132221113231214211411221412213211|222111221321131411122321311233311212221|212212411122141211141311221324211122122|141114212112121413123111221141113231411")]

    // Upper case and a space: mode 1, code set B, section 2.3.1.
    [InlineData("CODE 16K", "321111322121313211331211123131321133211|222112122221232212231121123312114122221|212212114122114122114124113111114222122")]

    // An even run of digits: mode 2, code set C.
    [InlineData("1234567890", "321112222211122321311233311212411123211|222112141212114122114121141131321312221")]

    // An odd run of five digits: mode 5, Code C with an implied Shift B for the first digit.
    [InlineData("12345", "321111312221232213121311131232114123211|222112114122114122114121321311131232221")]

    // Three digits, the shortest odd run that takes mode 5.
    [InlineData("123", "321111312221232213121312114122114123211|222112114122114122114121221322112322221")]

    // Lower case and control characters alternating, which shifts between code sets A and B.
    [InlineData("ab", "321112221221211244113111211241214213211|222113111411214212114123131212231122221")]

    // Punctuation and lower case together.
    [InlineData("Hello, World!", "321111132222311131122142211142211143211|222111341111122322122223113211341112221|212211212412211141412212221222114122122|141112114122114122114122112141241121411")]

    // A single character, padded to the two row minimum.
    [InlineData("A", "321112221221113232114122114122114123211|222112114122114122114122133111212412221")]

    // No data at all: a start character, pad characters and the two check characters.
    [InlineData("", "321112221222114122114122114122114123211|222112114122114122114123131211341112221")]

    // Two digits between letters, too short for code set C.
    [InlineData("AB12CD", "321111222131113231311231122321141313211|222111313211123132114121221321111432221")]

    // Four digits between letters: Double Shift C, Appendix G rule 4 d.
    [InlineData("ab1234cd", "321111222131211241214211122321311233211|222111141311411221412211132222131312221")]

    // Six digits between letters: Triple Shift C, Appendix G rule 4 e.
    [InlineData("ab123456cd", "321111221321211241214211122321311233211|222113311211141311411221412212114122221|212212114122114122114123221124311112122")]

    // Control characters before lower case: mode 0, code set A, then a latch to B.
    [InlineData("ab", "321112122221211241214211141311211243211|222111214212114122114122123211112242221")]

    // A lower case letter, four digits and a control character: mode 5, then code set A at the end.
    [InlineData("x1234", "321111312224212111122321311233111413211|222111211242114122114121311412221222221")]

    // Seventy seven upper case letters, the most a sixteen row symbol carries, so rows 9 to 16 use the
    // second half of Table 6.
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXY", "321111131411113231311231313211123133211|222111321131323112113132311132313112221|212211121331123311321311131231133212122|141111331213131212113312311312131131411|113212133112131313111233113213311211132|123113121133123111113231311231313211231|111411123131321131323112113132311131114|311212313111121331123311321311131233112|321111133211331213131212113312311311132|222112131132133112131313111233113211231|212213311213121133123111113231311231114|141111313211123131321131323112113133112|113212311132313111121331123311321313211|123111131231133211331213131212113312221|111412311312131132133112131313111232122|311213113213311213121132112142231121411")]
    public void Code16K_MatchesReferenceRows(string text, string expected)
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
    /// Section 2.1: every row is 70 modules, and a one module separator bar stands between rows. Section
    /// 3.4 gives ten modules of quiet zone before the start pattern and one after the stop pattern.
    /// </summary>
    [Fact]
    public void Code16K_RowsAreSeventyModulesWithSeparators()
    {
        StackedBarcodeSymbol symbol = Encode("Abcd-1234567890-wxyZ");
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
    /// Section 3.6.2: H = ((h + g) r + g) X, with h the bar height and g the separator height. The bars
    /// are 8X, the recommended minimum of section 3.3, when no height is set.
    /// </summary>
    [Theory]
    [InlineData("A", 2)]
    [InlineData("CODE 16K", 3)]
    [InlineData("Abcd-1234567890-wxyZ", 4)]
    public void Code16K_HeightFollowsSection362(string text, int rows)
    {
        StackedBarcodeSymbol symbol = Encode(text);
        Assert.Equal((rows * 2) - 1, symbol.Rows.Length);
        Assert.Equal((9 * rows) + 1, symbol.HeightInModules);
        Assert.All(symbol.Rows[0].BarHeights, height => Assert.Equal(8F, height));
    }

    /// <summary>
    /// A bar height in millimetres sets the height of every data row.
    /// </summary>
    [Fact]
    public void Code16K_BarHeightOptionSetsRowHeight()
    {
        BarcodeOptions options = new() { BarHeight = 12F * BarcodeSymbology.PointXDimension };
        StackedBarcodeSymbol symbol = (StackedBarcodeSymbol)new Code16KSymbology().Encode("A", options);
        Assert.All(symbol.Rows[0].BarHeights, height => Assert.Equal(12F, height, 0.0001F));
        Assert.Equal(27F, symbol.HeightInModules, 0.0001F);
    }

    /// <summary>
    /// Section 2.3.1: the starting symbol character is s = 7(r - 2) + m, and Table 3 gives the mode.
    /// Appendix G rule 1 picks the mode from the first characters.
    /// </summary>
    [Theory]
    [InlineData("ab", 0)]
    [InlineData("CODE 16K", 1)]
    [InlineData("1234567890", 2)]
    [InlineData("12345", 5)]
    [InlineData("x1234", 5)]
    [InlineData("ab0123456789", 6)]
    public void Code16K_StartingSymbolCharacterFollowsSection231(string text, int mode)
    {
        int[] values = Code16KEncoder.Encode(text);
        int rows = values.Length / 5;
        Assert.Equal((7 * (rows - 2)) + mode, values[0]);
    }

    /// <summary>
    /// Section 2.6: C1 is the modulo 107 sum of the symbol characters weighted from two upward, and C2 is
    /// the modulo 107 sum weighted from one upward with C1 as its last term.
    /// </summary>
    [Theory]
    [InlineData("Abcd-1234567890-wxyZ")]
    [InlineData("Hello, World!")]
    [InlineData("")]
    public void Code16K_CheckCharactersFollowSection26(string text)
    {
        int[] values = Code16KEncoder.Encode(text);
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
    /// Appendix D: the human readable interpretation prints below the symbol, clear of the bottom
    /// separator bar. Without a font there is no text.
    /// </summary>
    [Fact]
    public void Code16K_TextPrintsBelowTheBottomSeparator()
    {
        Assert.Empty(Encode("CODE 16K").Text);

        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        StackedBarcodeSymbol symbol = (StackedBarcodeSymbol)new Code16KSymbology().Encode("CODE 16K", options);
        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("CODE 16K", placement.Text);
        Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
        Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
        Assert.Equal(0F, placement.Left);
        Assert.Equal(70F, placement.Right);
    }

    /// <summary>
    /// Section 2.2 gives the code sets ASCII 0 to 127 between them, and section 2.1 caps a symbol at
    /// sixteen rows of five symbol characters, three of them overhead.
    /// </summary>
    [Fact]
    public void Code16K_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => Encode("café"));
        Assert.Throws<ArgumentException>(() => Encode(new string('A', 78)));
        Assert.NotNull(Encode(new string('A', 77)));
    }

    private static StackedBarcodeSymbol Encode(string text)
        => (StackedBarcodeSymbol)new Code16KSymbology().Encode(text, new BarcodeOptions());
}
