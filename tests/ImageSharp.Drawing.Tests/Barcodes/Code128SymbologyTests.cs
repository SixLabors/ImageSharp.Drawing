// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code128Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from the BWIPP reference implementation at
/// <c>D:\GitHub\metafloor\bwip-js</c> through its raw encoding API. The runs carry the start character,
/// the code set switches the encoder chose, the check character and the stop character, so a difference in
/// any of those shows up here.
/// </summary>
public class Code128SymbologyTests
{
    [Theory]

    // Upper case text starts in code set B and stays there.
    [InlineData("CODE128", "2112141313211331211123131321131232212232113112223212212331112")]

    // An even run of six digits starts in code set C, two digits per symbol character.
    [InlineData("123456", "2112321122321311233311211321312331112")]

    // An odd run of five digits starts in code set B for one digit, then latches to code set C.
    [InlineData("12345", "2112321122321311231141312132123111232331112")]

    // A short digit run does not pay for code set C, so the whole symbol stays in code set B.
    [InlineData("ABC-123", "2112141113231311231313211221321232212232112211321124122331112")]

    // Mixed case with a space, which only code set B carries.
    [InlineData("Test 1", "2112142133111122141142121241122122221232213111232331112")]

    // Nineteen digits: an odd run, so one digit in code set B and eighteen in code set C.
    [InlineData("0123456789012345678", "2112322221223121311131231411222121412221223121311131231411221141313112224113112331112")]

    // Punctuation and lower case together.
    [InlineData("Hello, World!", "2112142311131122142211142211141341111122322122223113211341111212412211141412212221222211142331112")]

    // Two characters, one letter and one digit, too short for code set C.
    [InlineData("A1", "2112141113231232211412212331112")]

    // Exactly two digits, which section 5.4.7.6 encodes in code set C.
    [InlineData("99", "2112321131413111412331112")]

    // A single character.
    [InlineData("X", "2112143311213121132331112")]
    public void Code128_MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Every symbol starts and ends on a bar, so the run count is odd, and the stop character contributes
    /// the final four bars and three spaces of section 5.4.1.
    /// </summary>
    [Fact]
    public void Code128_StartsAndEndsOnABar()
    {
        LinearBarcodeSymbol symbol = Encode("CODE128");
        Assert.Equal(1, symbol.RunWidths.Length % 2);
        Assert.Equal("2331112", string.Concat(symbol.RunWidths[^7..]));
    }

    /// <summary>
    /// Section 5.4.4.2 requires a quiet zone of ten times the X-dimension on both sides.
    /// </summary>
    [Fact]
    public void Code128_ReservesTheQuietZones()
    {
        LinearBarcodeSymbol symbol = Encode("CODE128");
        Assert.Equal(10, symbol.LeadingQuietZone);
        Assert.Equal(10, symbol.TrailingQuietZone);
    }

    /// <summary>
    /// Code 128 carries no guard bars, so every bar runs the full height from the symbol top.
    /// </summary>
    [Fact]
    public void Code128_DrawsEveryBarAtOneHeight()
    {
        LinearBarcodeSymbol symbol = Encode("CODE128");
        Assert.All(symbol.BarTops, top => Assert.Equal(0, top));
        Assert.All(symbol.BarHeights, height => Assert.Equal(symbol.BarHeights[0], height));
    }

    /// <summary>
    /// The symbology encodes ASCII 0 to 127; anything above that is rejected rather than mangled.
    /// </summary>
    [Fact]
    public void Code128_RejectsNonAscii()
        => Assert.Throws<ArgumentException>(() => Encode("caf\u00e9"));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Code128Symbology().Encode(text, new BarcodeOptions());
}
