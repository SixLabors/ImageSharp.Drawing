// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code11Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, from the reference implementation's raw encoding API. The
/// reference draws a wide element three modules wide and emits a narrow gap after the stop character, so
/// the test drops that trailing gap before it compares.
/// </summary>
public class Code11SymbologyTests
{
    [Theory]
    [InlineData("123-45", "113311311131131131331111113111113131313111113311")]
    [InlineData("0123456789", "113311111131311131131131331111113131313111133111111331311311311111113311")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference[..^1], string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// C weights the data from the right, 1 to 10 and around again, and takes the sum modulo 11. For
    /// 123-45 the values are 1, 2, 3, 10, 4 and 5, so the sum is 5 + 8 + 30 + 12 + 10 + 6 = 71 and C is
    /// 5. Fewer than ten data characters carry C alone. K weights C and the data from the right, 1 to 9
    /// and around again, and takes the sum modulo 11. For 0123456789 C is 165 modulo 11, which is 0, and
    /// K is 0 + 18 + 24 + 28 + 30 + 30 + 28 + 24 + 18 + 1 + 0 = 201 modulo 11, which is 3. Ten or more
    /// data characters carry both. The expected runs are the reference implementation's with its check
    /// characters on.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run string with the check characters carried.</param>
    [Theory]
    [InlineData("123-45", "113311311131131131331111113111113131313111313111113311")]
    [InlineData("0123456789", "113311111131311131131131331111113131313111133111111331311311311111111131331111113311")]
    [InlineData("0123456789-", "113311111131311131131131331111113131313111133111111331311311311111113111111131133111113311")]
    public void CalculatesTheCheckCharacters(string text, string reference)
    {
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Code11Symbology(CheckCharacterMode.Compute).Encode(text, new BarcodeOptions());
        Assert.Equal(reference[..^1], string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Supplied check characters are validated against the data and carried once, so the symbol is the
    /// one the calculated check characters produce. Ten characters carry one check character and twelve
    /// carry two, so eleven cannot carry a valid check and are rejected.
    /// </summary>
    /// <param name="text">The data with its check characters.</param>
    /// <param name="data">The data alone.</param>
    [Theory]
    [InlineData("123-455", "123-45")]
    [InlineData("012345678903", "0123456789")]
    public void ValidatesSuppliedCheckCharacters(string text, string data)
    {
        LinearBarcodeSymbol validated = (LinearBarcodeSymbol)new Code11Symbology(CheckCharacterMode.Validate).Encode(text, new BarcodeOptions());
        LinearBarcodeSymbol computed = (LinearBarcodeSymbol)new Code11Symbology(CheckCharacterMode.Compute).Encode(data, new BarcodeOptions());

        Assert.Equal(string.Concat(computed.RunWidths), string.Concat(validated.RunWidths));
    }

    [Theory]
    [InlineData("123-456")]
    [InlineData("012345678904")]
    [InlineData("01234567890")]
    [InlineData("5")]
    public void RejectsWrongOrAmbiguousCheckCharacters(string text)
        => Assert.Throws<ArgumentException>(() => new Code11Symbology(CheckCharacterMode.Validate).Encode(text, new BarcodeOptions()));

    /// <summary>
    /// The printed line shows the data and, by default, the check characters. It never shows the start
    /// and stop character.
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
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Code11Symbology(checkCharacters, printCheckCharacters).Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// Every character has five elements. The characters 0, 9 and the dash have one wide element and the
    /// others two, as does the start and stop character, so with a wide element of three modules a
    /// character spans 7 or 9 modules, and a narrow gap separates the characters. 123-45 is the start
    /// character, five characters with two wide elements, the dash and the stop character, which is
    /// 7 × 9 + 7 = 70 modules and 7 gaps. 0123456789 is the start and stop characters, the digits 1 to 8
    /// and the digits 0 and 9, which is 10 × 9 + 2 × 7 = 104 modules and 11 gaps.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The width in modules before the quiet zones.</param>
    [Theory]
    [InlineData("123-45", 77)]
    [InlineData("0123456789", 115)]
    public void SpansSevenOrNineModulesPerCharacter(string text, int expected)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal(expected, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12 34")]
    [InlineData("12.34")]
    [InlineData("12A4")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Code11Symbology().Encode(text, new BarcodeOptions());
}
