// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Bc412Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, from the reference implementation's raw encoding API in its
/// SEMI form. The reference ends on the last bar of the stop character, so the strings compare directly.
/// </summary>
public class Bc412SymbologyTests
{
    private const string Sample = "121113131113111212111411121111151111111214111113131113121211131311111";

    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceRuns"/>.
    /// </summary>
    public static TheoryData<string, string> MatchesReferenceRunsData { get; } = new()
    {
        { "BC412AB", Sample },
        { "1234567", "121111121413121211111113131111141211111511111211141112121311121312111" },
        { "ZZZZZZZ", "121511111111141112151111111511111115111111151111111511111115111111111" },
        {
            "0123456789ABCDEFGH",
            "12111111151113121211111214111113131111141211111511111211141112121311121312111214111113111311131212111313111114111211141211111511111211111412" +
            "11121312111312111"
        },
    };

    [Theory]
    [MemberData(nameof(MatchesReferenceRunsData))]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference, Encode(text));

    /// <summary>
    /// For BC412AB the symbol is B, the check character counted as 0, C, 4, 1, 2, A and B, whose values
    /// are 25, 0, 20, 11, 15, 17, 7 and 25. The odd positions add to 67, which leaves 32, the even
    /// positions add to 53, which leaves 18 and doubles to 36, and 68 leaves 33. Then 33 times 17 is 561,
    /// which leaves 1, and the character with value 1 is R. The printed line shows it after the first
    /// data character.
    /// </summary>
    [Fact]
    public void CalculatesTheCheckCharacterAfterTheFirstDataCharacter()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Bc412Symbology(), "BC412AB", options, SymbolEncoder.Texts);
        Assert.Equal("BRC412AB", Assert.Single(texts));
    }

    /// <summary>
    /// A supplied check character in the second position is validated against the data and carried
    /// once, so the symbol is the one the calculated check character produces.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
    {
        string validated = SymbolEncoder.Read(new Bc412Symbology(true), "BRC412AB", new BarcodeOptions(), SymbolEncoder.RunString);

        Assert.Equal(Sample, validated);
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Bc412Symbology(true), "BAC412AB", new BarcodeOptions()));
    }

    /// <summary>
    /// The start character is three modules, every character is twelve, and the stop character is
    /// three, so a symbol of N data characters and its check character spans 12(N + 1) + 6 modules
    /// before its quiet zones.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("BC412AB")]
    [InlineData("0123456789ABCDEFGH")]
    public void SpansTwelveModulesPerCharacter(string text)
    {
        int widthInModules = SymbolEncoder.Read(new Bc412Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum);

        Assert.Equal((12 * (text.Length + 1)) + 6, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BC412A")]
    [InlineData("0123456789ABCDEFGHI")]
    [InlineData("BC412AO")]
    [InlineData("bc412ab")]
    [InlineData("BC412A ")]
    [InlineData("ＢＣ412AB")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Bc412Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
