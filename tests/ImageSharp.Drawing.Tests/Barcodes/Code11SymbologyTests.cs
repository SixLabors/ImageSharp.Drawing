// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code11Symbology"/>.
/// </summary>
public class Code11SymbologyTests
{
    /// <summary>
    /// The encoder validates supplied check characters against the data and carries them once. The symbol is then the one that the calculated check
    /// characters produce.
    /// </summary>
    /// <param name="text">The data with its check characters.</param>
    /// <param name="data">The data alone.</param>
    [Theory]
    [InlineData("123-455", "123-45")]
    [InlineData("012345678903", "0123456789")]
    public void ValidatesSuppliedCheckCharacters(string text, string data)
    {
        string validated = SymbolEncoder.Read(new Code11Symbology(CheckCharacterMode.Validate), text, new BarcodeOptions(), SymbolEncoder.RunString);
        string computed = SymbolEncoder.Read(new Code11Symbology(CheckCharacterMode.Compute), data, new BarcodeOptions(), SymbolEncoder.RunString);

        Assert.Equal(computed, validated);
    }

    /// <summary>
    /// The encoder rejects a supplied check character that does not match the data. Ten characters carry one check character and twelve carry two, so the
    /// encoder also rejects eleven characters. A single character cannot carry data and a check character, so the encoder rejects it too.
    /// </summary>
    /// <param name="text">The data with its check characters.</param>
    [Theory]
    [InlineData("123-456")]
    [InlineData("012345678904")]
    [InlineData("01234567890")]
    [InlineData("5")]
    public void RejectsWrongOrAmbiguousCheckCharacters(string text)
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Code11Symbology(CheckCharacterMode.Validate), text, new BarcodeOptions()));

    /// <summary>
    /// The printed line shows the data and, by default, the check characters. It never shows the start and stop character.
    /// </summary>
    /// <param name="checkCharacters">Whether the symbol carries the check characters, and whether the encoder calculates them or validates them.</param>
    /// <param name="printCheckCharacters">Whether the check characters are part of the printed line.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, true, "123-45", "123-45")]
    [InlineData(CheckCharacterMode.Compute, true, "123-45", "123-455")]
    [InlineData(CheckCharacterMode.Compute, false, "123-45", "123-45")]
    [InlineData(CheckCharacterMode.Compute, true, "0123456789", "012345678903")]
    [InlineData(CheckCharacterMode.Validate, true, "123-455", "123-455")]
    [InlineData(CheckCharacterMode.Validate, false, "012345678903", "0123456789")]
    public void PrintsTheDataAndTheCheckCharacters(CheckCharacterMode checkCharacters, bool printCheckCharacters, string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Code11Symbology(checkCharacters, printCheckCharacters), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Every character has five elements. The characters 0, 9 and the dash have one wide element and the others two, as does the start and stop character, so
    /// with a wide element of three modules a character spans 7 or 9 modules, and a narrow gap separates the characters. 123-45 is the start character, five
    /// characters with two wide elements, the dash and the stop character, which is 7 × 9 + 7 = 70 modules and 7 gaps. 0123456789 is the start and stop
    /// characters, the digits 1 to 8 and the digits 0 and 9, which is 10 × 9 + 2 × 7 = 104 modules and 11 gaps.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The width in modules before the quiet zones.</param>
    [Theory]
    [InlineData("123-45", 77)]
    [InlineData("0123456789", 115)]
    public void SpansSevenOrNineModulesPerCharacter(string text, int expected)
        => Assert.Equal(expected, SymbolEncoder.Read(new Code11Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// The encoder rejects an empty text and any character other than a digit or the dash.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("12 34")]
    [InlineData("12.34")]
    [InlineData("12A4")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Code11Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
