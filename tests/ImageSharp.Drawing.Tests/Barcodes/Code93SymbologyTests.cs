// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code93Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from an independent reference implementation through
/// its raw encoding API with the check characters turned on. The patterns are Table 2 of ANSI/AIM
/// BC5-1995, and the two reference implementations consulted agree on them element for element.
/// </summary>
public class Code93SymbologyTests
{
    [Theory]
    [InlineData("CODE93", "1111412113111211222211122212111411111114111311212221111111411")]
    [InlineData("ABC-123", "1111412111132112122113111211311112131113121114111111231113211111411")]
    [InlineData("TEST93", "1111412112212212112111222112211411111114111131211213111111411")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// ANSI/AIM BC5-1995 fixes both check characters, so every symbol carries them. Decoding the
    /// reference vectors gives CODE93PV, ABC-123LN and TEST93+6, which names the two characters that
    /// follow the data. Their expected patterns come from encoding those characters as data, because
    /// Table 2 gives a symbol character one pattern for every position.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="checks">The two check characters the symbol carries.</param>
    [Theory]
    [InlineData("CODE93", "PV")]
    [InlineData("ABC-123", "LN")]
    [InlineData("TEST93", "+6")]
    public void CarriesTwoCheckCharacters(string text, string checks)
    {
        const int start = 6;
        const int elements = 6;

        int[] runs = Encode(text).RunWidths;
        int[] expected = Encode(checks).RunWidths;

        Assert.Equal(
            expected.AsSpan(start, elements * checks.Length).ToArray(),
            runs.AsSpan(start + (elements * text.Length), elements * checks.Length).ToArray());
    }

    /// <summary>
    /// Section 2.6 measures a symbol as <c>(9 * (C + 4) + 1) * X</c> before its quiet zones, where C is
    /// the data character count. The four are the two check characters, the start character and the stop
    /// character, and the one is the bar that terminates the stop character.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("CODE93")]
    [InlineData("ABC-123")]
    [InlineData("A")]
    public void SpansNineModulesPerCharacter(string text)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((9 * (text.Length + 4)) + 1, widthInModules);
    }

    /// <summary>
    /// The data set is the 43 characters of Code 39. The four shift characters carry no data of their
    /// own, so the lowercase letters this library writes them as are not input.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("code93")]
    [InlineData("CODE93!")]
    [InlineData("a")]
    [InlineData("CAFÉ")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Code93Symbology().Encode(text, new BarcodeOptions());
}
