// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PlesseySymbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, from the reference implementation's raw encoding API. The
/// reference ends on the last bar of the reversed start code, so the strings compare directly.
/// </summary>
public class PlesseySymbologyTests
{
    private const string Sample = "323214321414141432141414143214143232141414143214143214323232143214143232321432323214323214143232541412323";

    [Theory]
    [InlineData("01234ABCD", Sample)]
    [InlineData("0", "32321432141414141414141414141414541412323")]
    [InlineData("A", "32321432143214323214143232323232541412323")]
    [InlineData("F", "32321432323232321432141432141414541412323")]
    [InlineData("1234567", "32321432321414141432141432321414141432143214321414323214323232141414141414321432541412323")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference, string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// The check characters hold the eight bit remainder of the data bits divided by the generator
    /// polynomial x^8 + x^7 + x^6 + x^5 + x^3 + 1, low four bits first. The data bits of a single 0 are
    /// all zero, so the remainder is zero. A single F is the bits 1111, which the division reduces to the
    /// remainder whose low and high halves are 2 and 1. The expected lines are the reference
    /// implementation's for the same data.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line with the check characters.</param>
    [Theory]
    [InlineData("0", "000")]
    [InlineData("F", "F21")]
    [InlineData("A", "A9F")]
    [InlineData("1234567", "12345670A")]
    [InlineData("01234ABCD", "01234ABCDDC")]
    public void CalculatesTheCheckCharacters(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new PlesseySymbology().Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// Supplied check characters are validated against the data and carried once, so the symbol is the
    /// one the calculated check characters produce.
    /// </summary>
    [Fact]
    public void ValidatesSuppliedCheckCharacters()
    {
        LinearBarcodeSymbol validated = (LinearBarcodeSymbol)new PlesseySymbology(true).Encode("01234ABCDDC", new BarcodeOptions());

        Assert.Equal(Sample, string.Concat(validated.RunWidths));
        Assert.Throws<ArgumentException>(() => new PlesseySymbology(true).Encode("01234ABCDCD", new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => new PlesseySymbology(true).Encode("DC", new BarcodeOptions()));
    }

    /// <summary>
    /// The printed line shows the data and, by default, the check characters. It never shows the start
    /// code or the reversed start code.
    /// </summary>
    /// <param name="validateCheckCharacters">Whether the input ends with the check characters.</param>
    /// <param name="printCheckCharacters">Whether the check characters are part of the printed line.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData(false, true, "01234ABCD", "01234ABCDDC")]
    [InlineData(false, false, "01234ABCD", "01234ABCD")]
    [InlineData(true, true, "01234ABCDDC", "01234ABCDDC")]
    [InlineData(true, false, "01234ABCDDC", "01234ABCD")]
    public void PrintsTheDataAndTheCheckCharacters(bool validateCheckCharacters, bool printCheckCharacters, string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new PlesseySymbology(validateCheckCharacters, printCheckCharacters).Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// The start code is four bits of five modules, every character is four bits of five modules, the two
    /// check characters are eight bits, and the end is a five module termination bar and four more bits,
    /// so a symbol of N data characters spans 20N + 85 modules before its quiet zones.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("01234ABCD")]
    public void SpansTwentyModulesPerCharacter(string text)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((20 * text.Length) + 85, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12G4")]
    [InlineData("12a4")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new PlesseySymbology().Encode(text, new BarcodeOptions());
}
