// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the five plain 2 of 5 symbologies. Each expected run string is the alternating bar
/// and space module widths, starting with a bar, taken verbatim from an independent reference
/// implementation through its raw encoding API. That implementation draws a wide element three modules
/// wide for these symbologies, as this library does, so the strings compare directly.
/// </summary>
public class Code2Of5SymbologyTests
{
    [Theory]
    [InlineData(typeof(Industrial2Of5Symbology), "1234", "313111311111113111311111313131111111111131113131113")]
    [InlineData(typeof(Industrial2Of5Symbology), "0123456789", "313111111131311131111111311131111131313111111111113111313111311111113131111111111131313111113111113111311131113")]
    [InlineData(typeof(Iata2Of5Symbology), "1234", "11113111111131113111113131311111111111311131311")]
    [InlineData(typeof(Iata2Of5Symbology), "0123456789", "11111111313111311111113111311111313131111111111131113131113111111131311111111111313131111131111131113111311")]
    [InlineData(typeof(Matrix2Of5Symbology), "1234", "31111131113113113133111111313131111")]
    [InlineData(typeof(Matrix2Of5Symbology), "0123456789", "31111111331131113113113133111111313131311113311111133131131113131131111")]
    [InlineData(typeof(Coop2Of5Symbology), "1234", "3131111331113131113311131131133")]
    [InlineData(typeof(Coop2Of5Symbology), "0123456789", "3131331111111331113131113311131131131311133111311131311311313111133")]
    [InlineData(typeof(Datalogic2Of5Symbology), "1234", "1111311131131131331111113131311")]
    [InlineData(typeof(Datalogic2Of5Symbology), "0123456789", "1111113311311131131131331111113131313111133111111331311311131311311")]
    public void MatchesReferenceRuns(Type symbologyType, string text, string reference)
    {
        LinearBarcodeSymbol symbol = Encode(symbologyType, CheckCharacterMode.None, true, text);
        Assert.Equal(reference, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Appendix C of AIM USS-I 2/5 weights the digits 3 and 1 alternately from the right, starting with 3,
    /// and the check digit lifts the sum to the next multiple of ten. For 1234 the sum is 12 + 3 + 6 + 1 =
    /// 22, so the check digit is 8. The reference vectors are the ones it emits with its check digit
    /// turned on.
    /// </summary>
    /// <param name="symbologyType">The symbology under test.</param>
    /// <param name="reference">The reference run string with the check digit carried.</param>
    [Theory]
    [InlineData(typeof(Industrial2Of5Symbology), "3131113111111131113111113131311111111111311131311111311131113")]
    [InlineData(typeof(Iata2Of5Symbology), "111131111111311131111131313111111111113111313111113111311")]
    [InlineData(typeof(Matrix2Of5Symbology), "31111131113113113133111111313131131131111")]
    [InlineData(typeof(Coop2Of5Symbology), "3131111331113131113311131131311311133")]
    [InlineData(typeof(Datalogic2Of5Symbology), "1111311131131131331111113131311311311")]
    public void CalculatesTheCheckDigitOfAppendixC(Type symbologyType, string reference)
    {
        LinearBarcodeSymbol symbol = Encode(symbologyType, CheckCharacterMode.Compute, true, "1234");
        Assert.Equal(reference, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// A supplied check digit is validated against the data and carried once, so the symbol is the one
    /// the calculated check digit produces.
    /// </summary>
    /// <param name="symbologyType">The symbology under test.</param>
    [Theory]
    [InlineData(typeof(Industrial2Of5Symbology))]
    [InlineData(typeof(Iata2Of5Symbology))]
    [InlineData(typeof(Matrix2Of5Symbology))]
    [InlineData(typeof(Coop2Of5Symbology))]
    [InlineData(typeof(Datalogic2Of5Symbology))]
    public void ValidatesASuppliedCheckDigit(Type symbologyType)
    {
        LinearBarcodeSymbol validated = Encode(symbologyType, CheckCharacterMode.Validate, true, "12348");
        LinearBarcodeSymbol computed = Encode(symbologyType, CheckCharacterMode.Compute, true, "1234");

        Assert.Equal(string.Concat(computed.RunWidths), string.Concat(validated.RunWidths));
        Assert.Throws<ArgumentException>(() => Encode(symbologyType, CheckCharacterMode.Validate, true, "12347"));
    }

    /// <summary>
    /// The printed line shows the digits the symbol carries. The check digit prints by default, and a
    /// caller can keep it off the printed line.
    /// </summary>
    /// <param name="checkDigit">
    /// Whether the symbol carries the check digit, and whether the encoder calculates it or validates it.
    /// </param>
    /// <param name="printCheckDigit">Whether the check digit is part of the printed line.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, true, "1234", "1234")]
    [InlineData(CheckCharacterMode.Compute, false, "1234", "1234")]
    [InlineData(CheckCharacterMode.Compute, true, "1234", "12348")]
    [InlineData(CheckCharacterMode.Validate, true, "12348", "12348")]
    public void PrintsTheDigitsTheSymbolCarries(CheckCharacterMode checkDigit, bool printCheckDigit, string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Industrial2Of5Symbology(checkDigit, printCheckDigit).Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    [Theory]
    [InlineData("12A4")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(typeof(Industrial2Of5Symbology), CheckCharacterMode.None, true, text));

    private static LinearBarcodeSymbol Encode(Type symbologyType, CheckCharacterMode checkDigit, bool printCheckDigit, string text)
    {
        BarcodeSymbology symbology = (BarcodeSymbology)Activator.CreateInstance(symbologyType, checkDigit, printCheckDigit)!;
        return (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());
    }
}
