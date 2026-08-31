// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code39Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from an independent reference implementation through
/// its raw encoding API, less the gap the reference emits behind the stop character. Section 4.2 of
/// ISO/IEC 16388 separates the characters "within the symbol" with that gap, and section 4.3.3 puts the
/// stop character at the right end, so the runs end on a bar.
/// </summary>
public class Code39SymbologyTests
{
    [Theory]
    [InlineData("CODE39", "1311313111313113111131113113111111331131311133111131331111111133113111131131311")]
    [InlineData("123456", "1311313111311311113111331111313133111111111331113131133111111133311111131131311")]
    [InlineData("ABC-123", "13113131113111131131113113113131311311111311113131311311113111331111313133111111131131311")]
    [InlineData("A", "13113131113111131131131131311")]
    [InlineData("TEST$/+% .", "13113131111111313311311133111111311133111111313311131313111113131113111311131311111313131113311131113311113111131131311")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(new Code39Symbology(), text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// The check character is the sum of the character values modulo the size of the set, and it is
    /// carried between the data and the stop character, where Annex A.1.1 places it.
    /// </summary>
    [Theory]
    [InlineData("CODE39", "13113131113131131111311131131111113311313111331111313311111111331131113331111111131131311")]
    [InlineData("1234567890", "131131311131131111311133111131313311111111133111313113311111113331111111131131313113113111113311311111133131111133111131131131311")]
    public void MatchesReferenceRunsWithCheckCharacter(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(new Code39Symbology(Code39CheckCharacter.Compute), text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// The worked example in Annex A.1.1: the data "CODE 39" has character values 12, 24, 13, 14, 38, 3
    /// and 9, which sum to 113; 113 divided by 43 leaves a remainder of 27, and the character of value 27
    /// is R, so the symbol carries "CODE 39R".
    /// </summary>
    [Fact]
    public void MatchesTheWorkedCheckCharacterExample()
        => Assert.Equal(
            string.Concat(Encode(new Code39Symbology(), "CODE 39R").RunWidths),
            string.Concat(Encode(new Code39Symbology(Code39CheckCharacter.Compute), "CODE 39").RunWidths));

    /// <summary>
    /// A caller who has already worked out the check character supplies it, and the symbol carries it
    /// once, so the runs match those of the same data with the check character computed.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
        => Assert.Equal(
            string.Concat(Encode(new Code39Symbology(Code39CheckCharacter.Compute), "CODE39").RunWidths),
            string.Concat(Encode(new Code39Symbology(Code39CheckCharacter.Validate), "CODE39W").RunWidths));

    [Fact]
    public void RejectsWrongCheckCharacter()
        => Assert.Throws<ArgumentException>(
            () => Encode(new Code39Symbology(Code39CheckCharacter.Validate), "CODE39X"));

    /// <summary>
    /// The symbology carries digits, capital letters, spaces and the symbols -.$/+% and nothing else.
    /// </summary>
    [Theory]
    [InlineData("code39")]
    [InlineData("CODE*39")]
    [InlineData("CODE,39")]
    [InlineData("CAFÉ")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Code39Symbology(), text));

    /// <summary>
    /// The rejected character is named in the message by both its code point and the character it prints
    /// as, so a caller can tell U+0041 from U+FF21 without decoding hexadecimal.
    /// </summary>
    [Theory]
    [InlineData("code39", "U+0063 'c'")]
    [InlineData("CAFÉ", "U+00C9 'É'")]
    [InlineData("A😀B", "U+1F600 '😀'")]
    public void NamesTheRejectedCharacter(string text, string expected)
    {
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() => Encode(new Code39Symbology(), text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDataBeyondTheMaximumLength()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Code39Symbology(), new string('A', 501)));

    /// <summary>
    /// Annex A.2 prints the interpretation "of the data characters (and data and symbol check
    /// character(s), if used)", and section 4.3.3 depicts the start and stop character as an asterisk. A
    /// caller who does not want the check character read back turns it off.
    /// </summary>
    [Theory]
    [InlineData(Code39CheckCharacter.None, true, "*CODE39*")]
    [InlineData(Code39CheckCharacter.None, false, "*CODE39*")]
    [InlineData(Code39CheckCharacter.Compute, true, "*CODE39W*")]
    [InlineData(Code39CheckCharacter.Compute, false, "*CODE39*")]
    public void PrintsTheDataBetweenAsterisks(Code39CheckCharacter checkCharacter, bool printCheckCharacter, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        Code39Symbology symbology = new(checkCharacter, printCheckCharacter);
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)symbology.Encode("CODE39", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// Every bar runs the full height, because Code 39 carries no guard bars.
    /// </summary>
    [Fact]
    public void DrawsEveryBarAtTheFullHeight()
    {
        LinearBarcodeSymbol symbol = Encode(new Code39Symbology(), "CODE39");

        Assert.All(symbol.BarHeights, height => Assert.Equal(symbol.BarHeights[0], height));
        Assert.All(symbol.BarTops, top => Assert.Equal(0, top));
    }

    /// <summary>
    /// Section 4.4 e) recommends a bar height of at least "15 % of symbol width excluding quiet zones",
    /// which is what a symbol takes when the caller sets no height.
    /// </summary>
    [Fact]
    public void DefaultsTheBarHeightToTheRecommendedMinimum()
    {
        LinearBarcodeSymbol symbol = Encode(new Code39Symbology(), "CODE39");
        int widthInModules = 0;
        foreach (int run in symbol.RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal(widthInModules * 0.15F, symbol.BarHeights[0], 3);
    }

    /// <summary>
    /// Section 4.1 c) gives every symbol character "5 bars and 4 spaces" of which "3 wide and 6 narrow",
    /// and section 4.4 c) gives the inter-character gap a minimum width of one narrow element, so each
    /// character and its gap take the sixteen modules section 4.1 h) allows at the widest ratio. Section
    /// 4.1 i) makes the start and stop character the only overhead.
    /// </summary>
    [Fact]
    public void TakesSixteenModulesPerCharacterWithoutTheGapBehindTheStopCharacter()
    {
        LinearBarcodeSymbol symbol = Encode(new Code39Symbology(), "CODE39");
        int widthInModules = 0;
        foreach (int run in symbol.RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal(((6 + 2) * 16) - 1, widthInModules);
    }

    /// <summary>
    /// The note to section 4.4 gives the width of a symbol including its quiet zones as
    /// <c>W = (C+2)(3N + 6)X + (C+1)I + 2Q</c>, where C counts the data characters and the check
    /// character, N is the wide to narrow ratio, X the narrow element, I the inter-character gap and Q the
    /// quiet zone. Measured in modules this encoder uses X = 1, N = 3, I = 1 and Q = 10, and the drawn
    /// width has to come to the same number.
    /// </summary>
    [Theory]
    [InlineData("CODE39", Code39CheckCharacter.None, 6)]
    [InlineData("CODE39", Code39CheckCharacter.Compute, 7)]
    [InlineData("A", Code39CheckCharacter.None, 1)]
    [InlineData("TEST$/+% .", Code39CheckCharacter.None, 10)]
    public void MatchesTheSymbolWidthFormula(string text, Code39CheckCharacter checkCharacter, int characters)
    {
        LinearBarcodeSymbol symbol = Encode(new Code39Symbology(checkCharacter), text);
        int drawn = symbol.LeadingQuietZone + symbol.TrailingQuietZone;
        foreach (int run in symbol.RunWidths)
        {
            drawn += run;
        }

        Assert.Equal(((characters + 2) * ((3 * 3) + 6)) + (characters + 1) + (2 * 10), drawn);
    }

    private static LinearBarcodeSymbol Encode(Code39Symbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());
}
