// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PlesseySymbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, from the
/// reference implementation's raw encoding API. The reference ends on the last bar of the reversed start code, so the strings compare directly.
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
        => Assert.Equal(reference, Encode(text));

    /// <summary>
    /// The check characters hold the eight bit remainder of the data bits divided by the generator polynomial x^8 + x^7 + x^6 + x^5 + x^3 + 1, low four bits
    /// first. The data bits of a single 0 are all zero, so the remainder is zero. A single F is the bits 1111, which the division reduces to the remainder
    /// whose low and high halves are 2 and 1. The expected lines are the reference implementation's for the same data.
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
        string[] texts = SymbolEncoder.Read(new PlesseySymbology(false, true), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Supplied check characters are validated against the data and carried once, so the symbol is the one the calculated check characters produce.
    /// </summary>
    [Fact]
    public void ValidatesSuppliedCheckCharacters()
    {
        string validated = SymbolEncoder.Read(new PlesseySymbology(true), "01234ABCDDC", new BarcodeOptions(), SymbolEncoder.RunString);

        Assert.Equal(Sample, validated);
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new PlesseySymbology(true), "01234ABCDCD", new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new PlesseySymbology(true), "DC", new BarcodeOptions()));
    }

    /// <summary>
    /// The printed line shows the data and, when asked, the check characters. By default it shows the data only, as the reference implementations print it. It
    /// never shows the start code or the reversed start code.
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
        string[] texts = SymbolEncoder.Read(new PlesseySymbology(validateCheckCharacters, printCheckCharacters), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// The start code is four bits of five modules, every character is four bits of five modules, the two check characters are eight bits, and the end is a
    /// five module termination bar and four more bits, so a symbol of N data characters spans 20N + 85 modules before its quiet zones.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("01234ABCD")]
    public void SpansTwentyModulesPerCharacter(string text)
        => Assert.Equal((20 * text.Length) + 85, SymbolEncoder.Read(new PlesseySymbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum));

    [Theory]
    [InlineData("")]
    [InlineData("12G4")]
    [InlineData("12a4")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new PlesseySymbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
