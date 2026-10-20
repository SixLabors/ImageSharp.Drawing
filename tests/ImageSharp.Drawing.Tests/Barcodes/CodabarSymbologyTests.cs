// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="CodabarSymbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, from the
/// reference implementation's raw encoding API. The reference draws a wide element three modules wide and emits a narrow gap after the stop character, so the
/// test drops that trailing gap before it compares.
/// </summary>
public class CodabarSymbologyTests
{
    private const string Library = "11331311113113111111133111113311311113111311113113131131";

    /// <summary>
    /// The bar and space widths match the reference implementation for the digits, the symbols and the start and stop characters. The alternative names T, N,
    /// * and E of the start and stop characters encode as A, B, C and D, so T40156N gives the same symbol as A40156B.
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
        => Assert.Equal(reference[..^1], Encode(text));

    /// <summary>
    /// The check character lifts the sum of the values of the start character, the data and the stop character to the next multiple of sixteen. For A40156B the
    /// values are 16, 4, 0, 1, 5, 6 and 17, which sum to 49, so the check character has value 15, which is +. It sits before the stop character. The expected
    /// runs are the reference implementation's with its check character on.
    /// </summary>
    [Fact]
    public void CalculatesTheModuloSixteenCheckCharacter()
    {
        string runs = SymbolEncoder.Read(new CodabarSymbology(CheckCharacterMode.Compute), "A40156B", new BarcodeOptions(), SymbolEncoder.RunString);

        Assert.Equal("1133131111311311111113311111331131111311131111311131313113131131"[..^1], runs);
    }

    /// <summary>
    /// A supplied check character sits before the stop character. It is validated against the rest of the symbol and carried once, so the symbol is the one the
    /// calculated check character produces.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
    {
        string validated = SymbolEncoder.Read(new CodabarSymbology(CheckCharacterMode.Validate), "A40156+B", new BarcodeOptions(), SymbolEncoder.RunString);
        string computed = SymbolEncoder.Read(new CodabarSymbology(CheckCharacterMode.Compute), "A40156B", new BarcodeOptions(), SymbolEncoder.RunString);

        Assert.Equal(computed, validated);
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodabarSymbology(CheckCharacterMode.Validate), "A40156-B", new BarcodeOptions()));
    }

    /// <summary>
    /// The printed line shows the input as given, the start and stop characters included. A check character prints before the stop character by default, and a
    /// caller can keep it off the line.
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
        string[] texts = SymbolEncoder.Read(new CodabarSymbology(checkCharacter, printCheckCharacter), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Every character is seven elements of which two are wide, or three for the start and stop characters and the symbols : / . +, and a narrow gap separates
    /// the characters. With a wide element of three modules a symbol of C characters, D of them with three wide elements among the data, spans 12C + 2D + 3
    /// modules before its quiet zones.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="wideDataCharacters">The number of data characters with three wide elements.</param>
    [Theory]
    [InlineData("A40156B", 0)]
    [InlineData("A-$:/.+B", 4)]
    [InlineData("A1B", 0)]
    public void SpansTwelveModulesPerCharacter(string text, int wideDataCharacters)
    {
        int widthInModules = SymbolEncoder.Read(new CodabarSymbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum);

        Assert.Equal((12 * text.Length) + (2 * wideDataCharacters) + 3, widthInModules);
    }

    /// <summary>
    /// Section 4.4.1 (d) of BS EN 798: "minimum width of quiet zone: 10X or 2,54 mm, whichever is greater". At an X dimension of 0.2 mm, 10X is 2 mm, so each
    /// quiet zone is 2.54 mm. At 0.5 mm, 10X is 5 mm and wider than 2.54 mm.
    /// </summary>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="expected">The width of each quiet zone in millimetres.</param>
    [Theory]
    [InlineData(0.2, 2.54)]
    [InlineData(0.5, 5.0)]
    public void KeepsTheQuietZoneOfSectionFourFourOne(double xDimension, double expected)
        => SymbolEncoder.Encode(
            new CodabarSymbology(),
            "A40156B",
            new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi, XDimension = xDimension },
            (ref symbol) =>
            {
                Assert.Equal(expected, symbol.LeadingQuietZone * symbol.XDimension, 1e-4);
                Assert.Equal(expected, symbol.TrailingQuietZone * symbol.XDimension, 1e-4);
            });

    /// <summary>
    /// Section 4.4.1 (a) of BS EN 798: "minimum width of narrow element (X): 0,191 mm", so a smaller X dimension draws at that minimum.
    /// </summary>
    [Fact]
    public void ClampsTheXDimensionToTheMinimumOfSectionFourFourOne()
        => SymbolEncoder.Encode(
            new CodabarSymbology(),
            "A40156B",
            new BarcodeOptions { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 0.1 },
            static (ref symbol) => Assert.Equal(0.191, symbol.XDimension, 1e-9));

    /// <summary>
    /// The encoder rejects text without data between a start character and a stop character. It also rejects a start or stop character inside the data,
    /// a space, a small letter and a character that is not ASCII.
    /// </summary>
    /// <param name="text">The text to encode.</param>
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

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new CodabarSymbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
