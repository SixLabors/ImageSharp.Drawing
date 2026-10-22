// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code128Symbology"/>.
/// </summary>
public class Code128SymbologyTests
{
    /// <summary>
    /// Every symbol starts and ends on a bar, so the run count is odd, and the stop character contributes the final four bars and three spaces of section
    /// 5.4.1.
    /// </summary>
    [Fact]
    public void Code128_StartsAndEndsOnABar()
        => Encode("CODE128", (ref symbol) =>
        {
            Assert.Equal(1, symbol.Runs.Length % 2);
            Assert.Equal("2331112", string.Concat(symbol.Runs[^7..].ToArray()));
        });

    /// <summary>
    /// Section 5.4.4.2 requires a quiet zone of ten times the X-dimension on both sides.
    /// </summary>
    [Fact]
    public void Code128_ReservesTheQuietZones()
        => Encode("CODE128", (ref symbol) =>
        {
            Assert.Equal(10, symbol.LeadingQuietZone);
            Assert.Equal(10, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// Code 128 carries no guard bars, so every bar runs the full height from the symbol top.
    /// </summary>
    [Fact]
    public void Code128_DrawsEveryBarAtOneHeight()
        => Encode("CODE128", (ref symbol) =>
        {
            float first = symbol.BarHeights[0];

            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0, top));
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal(first, height));
        });

    /// <summary>
    /// Clause 4.3.4.2 d) of ISO/IEC 15417 extends the code sets to the byte values 128 to 255 and no further. The encoder rejects a character above U+00FF and
    /// does not replace it.
    /// </summary>
    [Fact]
    public void Code128_RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => Encode("Ā"));

    /// <summary>
    /// Note 3 of Annex E of ISO/IEC 15417 encodes a run of one to four characters above 127, or one to two at the end of the data, with a single FNC4 before
    /// each character. A longer run takes two FNC4 characters before it and, when data follows, two after it. Each of é, à and A is in code set B, so no code
    /// set change or Shift adds to the count. Clause 4.4.3 gives the width as 11(C + 2) + 2 modules, where C counts the symbol characters with the check
    /// character.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="symbolCharacters">The symbol characters C, with the check character.</param>
    [Theory]
    [InlineData("é", 1 + 1 + 1)]
    [InlineData("éà", 2 + 2 + 1)]
    [InlineData("éàé", 2 + 3 + 1)]
    [InlineData("éàéàA", 4 + 4 + 1 + 1)]
    [InlineData("éàéàéA", 2 + 5 + 2 + 1 + 1)]
    public void Code128_EncodesCharactersAbove127WithFnc4(string text, int symbolCharacters)
        => Encode(text, (ref symbol) => Assert.Equal((11 * (symbolCharacters + 2)) + 2, symbol.WidthInModules));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    private static void Encode(string text)
        => SymbolEncoder.Encode(new Code128Symbology(), text, new BarcodeOptions());

    /// <summary>
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new Code128Symbology(), text, new BarcodeOptions(), check);
}
