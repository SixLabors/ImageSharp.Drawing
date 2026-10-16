// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code39Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, from the reference implementation's raw encoding API, less
/// the gap the reference emits after the stop character. Section 4.2 of
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
        => Assert.Equal(expected, Encode(new Code39Symbology(), text));

    /// <summary>
    /// The check character is the sum of the character values modulo the size of the set, and it is
    /// carried between the data and the stop character, where Annex A.1.1 places it.
    /// </summary>
    [Theory]
    [InlineData("CODE39", "13113131113131131111311131131111113311313111331111313311111111331131113331111111131131311")]
    [InlineData(
        "1234567890",
        "131131311131131111311133111131313311111111133111313113311111113331111111131131313113113111113311311111133131111133111131131131311")]
    public void MatchesReferenceRunsWithCheckCharacter(string text, string expected)
        => Assert.Equal(expected, Encode(new Code39Symbology(CheckCharacterMode.Compute), text));

    /// <summary>
    /// The worked example in Annex A.1.1: the data "CODE 39" has character values 12, 24, 13, 14, 38, 3
    /// and 9, which sum to 113; 113 divided by 43 leaves a remainder of 27, and the character of value 27
    /// is R, so the symbol carries "CODE 39R".
    /// </summary>
    [Fact]
    public void MatchesTheWorkedCheckCharacterExample()
        => Assert.Equal(
            Encode(new Code39Symbology(), "CODE 39R"),
            Encode(new Code39Symbology(CheckCharacterMode.Compute), "CODE 39"));

    /// <summary>
    /// A caller who has already calculated the check character supplies it, and the symbol carries it
    /// once, so the runs match those of the same data with the check character computed.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
        => Assert.Equal(
            Encode(new Code39Symbology(CheckCharacterMode.Compute), "CODE39"),
            Encode(new Code39Symbology(CheckCharacterMode.Validate), "CODE39W"));

    [Fact]
    public void RejectsWrongCheckCharacter()
        => Assert.Throws<ArgumentException>(
            () => Encode(new Code39Symbology(CheckCharacterMode.Validate), "CODE39X"));

    /// <summary>
    /// A check character alone has no data to check.
    /// </summary>
    [Fact]
    public void RejectsACheckCharacterWithNoData()
        => Assert.Throws<ArgumentException>(
            () => Encode(new Code39Symbology(CheckCharacterMode.Validate), "0"));

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
    [InlineData(CheckCharacterMode.None, true, "*CODE39*")]
    [InlineData(CheckCharacterMode.None, false, "*CODE39*")]
    [InlineData(CheckCharacterMode.Compute, true, "*CODE39W*")]
    [InlineData(CheckCharacterMode.Compute, false, "*CODE39*")]
    public void PrintsTheDataBetweenAsterisks(CheckCharacterMode checkCharacter, bool printCheckCharacter, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        Code39Symbology symbology = new(checkCharacter, printCheckCharacter);
        string[] texts = SymbolEncoder.Read(symbology, "CODE39", options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Every bar runs the full height, because Code 39 carries no guard bars.
    /// </summary>
    [Fact]
    public void DrawsEveryBarAtTheFullHeight()
        => SymbolEncoder.Encode(new Code39Symbology(), "CODE39", new BarcodeOptions(), (ref symbol) =>
        {
            float first = symbol.BarHeights[0];
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal(first, height));
            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0, top));
        });

    /// <summary>
    /// Section 4.4 e) recommends a bar height of at least "15 % of symbol width excluding quiet zones",
    /// which is what a symbol takes when the caller sets no height.
    /// </summary>
    [Fact]
    public void DefaultsTheBarHeightToTheRecommendedMinimum()
        => SymbolEncoder.Encode(new Code39Symbology(), "CODE39", new BarcodeOptions(), (ref symbol) =>
            Assert.Equal(SymbolEncoder.RunSum(ref symbol) * 0.15F, symbol.BarHeights[0], new ApproximateFloatComparer(1e-3F)));

    /// <summary>
    /// Section 4.1 c) gives every symbol character "5 bars and 4 spaces" of which "3 wide and 6 narrow",
    /// and section 4.4 c) gives the inter-character gap a minimum width of one narrow element, so each
    /// character and its gap take the sixteen modules section 4.1 h) allows at the widest ratio. Section
    /// 4.1 i) makes the start and stop character the only overhead.
    /// </summary>
    [Fact]
    public void TakesSixteenModulesPerCharacterWithoutTheGapBehindTheStopCharacter()
        => Assert.Equal(((6 + 2) * 16) - 1, SymbolEncoder.Read(new Code39Symbology(), "CODE39", new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// The note to section 4.4 gives the width of a symbol including its quiet zones as
    /// <c>W = (C+2)(3N + 6)X + (C+1)I + 2Q</c>, where C counts the data characters and the check
    /// character, N is the wide to narrow ratio, X the narrow element, I the inter-character gap and Q the
    /// quiet zone. Measured in modules this encoder uses X = 1, N = 3, I = 1 and Q = 10, and the drawn
    /// width has to come to the same number.
    /// </summary>
    [Theory]
    [InlineData("CODE39", CheckCharacterMode.None, 6)]
    [InlineData("CODE39", CheckCharacterMode.Compute, 7)]
    [InlineData("A", CheckCharacterMode.None, 1)]
    [InlineData("TEST$/+% .", CheckCharacterMode.None, 10)]
    public void MatchesTheSymbolWidthFormula(string text, CheckCharacterMode checkCharacter, int characters)
    {
        float drawn = SymbolEncoder.Read(
            new Code39Symbology(checkCharacter),
            text,
            new BarcodeOptions(),
            static (ref symbol) => symbol.LeadingQuietZone + symbol.TrailingQuietZone + SymbolEncoder.RunSum(ref symbol));

        Assert.Equal(((characters + 2) * ((3 * 3) + 6)) + (characters + 1) + (2 * 10), drawn);
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(Code39Symbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RunString);
}
