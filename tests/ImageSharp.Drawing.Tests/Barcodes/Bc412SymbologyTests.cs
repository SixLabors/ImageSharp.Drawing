// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Bc412Symbology"/>.
/// </summary>
public class Bc412SymbologyTests
{
    /// <summary>
    /// For BC412AB the symbol is B, the check character counted as 0, C, 4, 1, 2, A and B, whose values are 25, 0, 20, 11, 15, 17, 7 and 25. The odd positions
    /// add to 67, which leaves 32, the even positions add to 53, which leaves 18 and doubles to 36, and 68 leaves 33. Then 33 times 17 is 561, which leaves 1,
    /// and the character with value 1 is R. The printed line shows it after the first data character.
    /// </summary>
    [Fact]
    public void CalculatesTheCheckCharacterAfterTheFirstDataCharacter()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Bc412Symbology(), "BC412AB", options, SymbolEncoder.Texts);

        Assert.Equal("BRC412AB", Assert.Single(texts));
    }

    /// <summary>
    /// A supplied check character in the second position is validated against the data and carried once, so the symbol is the one the calculated check
    /// character produces.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
    {
        string validated = SymbolEncoder.Read(new Bc412Symbology(true), "BRC412AB", new BarcodeOptions(), SymbolEncoder.RunString);

        Assert.Equal(Encode("BC412AB"), validated);
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Bc412Symbology(true), "BAC412AB", new BarcodeOptions()));
    }

    /// <summary>
    /// The start character is three modules, every character is twelve, and the stop character is three, so a symbol of N data characters and its check
    /// character spans 12(N + 1) + 6 modules before its quiet zones.
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

    /// <summary>
    /// The encoder rejects text with fewer than 7 or more than 18 data characters. It also rejects a character other than a digit or a capital letter
    /// other than O.
    /// </summary>
    /// <param name="text">The text to encode.</param>
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
