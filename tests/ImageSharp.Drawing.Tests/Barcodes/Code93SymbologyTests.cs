// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code93Symbology"/>. The patterns are Table 2 of ANSI/AIM BC5-1995.
/// </summary>
public class Code93SymbologyTests
{
    /// <summary>
    /// ANSI/AIM BC5-1995 fixes both check characters, so every symbol carries them. C weights the data from the right, 1 to 20 and around again, and K weights
    /// C and the data from the right, 1 to 15 and around again. Each takes the sum modulo 47. For CODE93 the values are 12, 24, 13, 14, 9 and 3, so C is
    /// 307 modulo 47, which is 25 or P, and K is 407 modulo 47, which is 31 or V. The expected patterns come from encoding those characters as data, because
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

        int[] runs = Encode(text);
        int[] expected = Encode(checks);

        Assert.Equal(
            expected.AsSpan(start, elements * checks.Length).ToArray(),
            runs.AsSpan(start + (elements * text.Length), elements * checks.Length).ToArray());
    }

    /// <summary>
    /// Section 2.6 measures a symbol as <c>(9 * (C + 4) + 1) * X</c> before its quiet zones, where C is the data character count. The four are the two check
    /// characters, the start character and the stop character, and the one is the bar that terminates the stop character.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("CODE93")]
    [InlineData("ABC-123")]
    [InlineData("A")]
    public void SpansNineModulesPerCharacter(string text)
    {
        int widthInModules = SymbolEncoder.Read(new Code93Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum);

        Assert.Equal((9 * (text.Length + 4)) + 1, widthInModules);
    }

    /// <summary>
    /// The data set is the 43 characters of Code 39. The four shift characters carry no data of their own, so the lowercase letters this library writes them as
    /// are not input.
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

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static int[] Encode(string text)
        => SymbolEncoder.Read(new Code93Symbology(), text, new BarcodeOptions(), static (ref symbol) => symbol.Runs.ToArray());
}
