// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="CodabarSymbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, from the reference implementation's raw encoding API. The
/// reference draws a wide element three modules wide and emits a narrow gap after the stop character, so
/// the test drops that trailing gap before it compares.
/// </summary>
public class CodabarSymbologyTests
{
    private const string Library = "11331311113113111111133111113311311113111311113113131131";

    /// <summary>
    /// The alternative names T, N, * and E of the start and stop characters encode as A, B, C and D.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run string.</param>
    [Theory]
    [InlineData("A40156B", Library)]
    [InlineData("T40156N", Library)]
    [InlineData("C1234567890D", "111313311111331111131131331111111131131131111311131111311311311113311111311311111111133111133311")]
    [InlineData("A-$:/.+B", "1133131111133111113311113111313131311131313131111131313113131131")]
    [InlineData("A1B", "113313111111331113131131")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference[..^1], string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// The check character lifts the sum of the values of the start character, the data and the stop
    /// character to the next multiple of sixteen. For A40156B the values are 16, 4, 0, 1, 5, 6 and 17,
    /// which sum to 49, so the check character has value 15, which is +. It sits before the stop
    /// character. The expected runs are the reference implementation's with its check character on.
    /// </summary>
    [Fact]
    public void CalculatesTheModuloSixteenCheckCharacter()
    {
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new CodabarSymbology(CheckCharacterMode.Compute).Encode("A40156B", new BarcodeOptions());
        Assert.Equal("1133131111311311111113311111331131111311131111311131313113131131"[..^1], string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// A supplied check character sits before the stop character. It is validated against the rest of the
    /// symbol and carried once, so the symbol is the one the calculated check character produces.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
    {
        LinearBarcodeSymbol validated = (LinearBarcodeSymbol)new CodabarSymbology(CheckCharacterMode.Validate).Encode("A40156+B", new BarcodeOptions());
        LinearBarcodeSymbol computed = (LinearBarcodeSymbol)new CodabarSymbology(CheckCharacterMode.Compute).Encode("A40156B", new BarcodeOptions());

        Assert.Equal(string.Concat(computed.RunWidths), string.Concat(validated.RunWidths));
        Assert.Throws<ArgumentException>(() => new CodabarSymbology(CheckCharacterMode.Validate).Encode("A40156-B", new BarcodeOptions()));
    }

    /// <summary>
    /// The printed line shows the input as given, the start and stop characters included. A check
    /// character prints before the stop character by default, and a caller can keep it off the line.
    /// </summary>
    /// <param name="checkCharacter">Whether the symbol carries the check character, and whether the encoder calculates it or validates it.</param>
    /// <param name="printCheckCharacter">Whether the check character is part of the printed line.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, true, "A40156B", "A40156B")]
    [InlineData(CheckCharacterMode.None, true, "T40156N", "T40156N")]
    [InlineData(CheckCharacterMode.Compute, true, "A40156B", "A40156+B")]
    [InlineData(CheckCharacterMode.Compute, false, "A40156B", "A40156B")]
    [InlineData(CheckCharacterMode.Validate, true, "A40156+B", "A40156+B")]
    [InlineData(CheckCharacterMode.Validate, false, "A40156+B", "A40156B")]
    public void PrintsTheInputAsGiven(CheckCharacterMode checkCharacter, bool printCheckCharacter, string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new CodabarSymbology(checkCharacter, printCheckCharacter).Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// Every character is seven elements of which two are wide, or three for the start and stop
    /// characters and the symbols : / . +, and a narrow gap separates the characters. With a wide element
    /// of three modules a symbol of C characters, D of them with three wide elements among the data,
    /// spans 12C + 2D + 3 modules before its quiet zones.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="wideDataCharacters">The number of data characters with three wide elements.</param>
    [Theory]
    [InlineData("A40156B", 0)]
    [InlineData("A-$:/.+B", 4)]
    [InlineData("A1B", 0)]
    public void SpansTwelveModulesPerCharacter(string text, int wideDataCharacters)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((12 * text.Length) + (2 * wideDataCharacters) + 3, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("A12")]
    [InlineData("12B")]
    [InlineData("A1B2C")]
    [InlineData("A1 2B")]
    [InlineData("a12b")]
    [InlineData("A１２B")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new CodabarSymbology().Encode(text, new BarcodeOptions());
}
