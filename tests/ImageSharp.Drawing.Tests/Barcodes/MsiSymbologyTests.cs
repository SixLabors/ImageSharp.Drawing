// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="MsiSymbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, from the reference implementation's raw encoding API. The
/// reference ends on the last bar of the stop character, so the strings compare directly.
/// </summary>
public class MsiSymbologyTests
{
    [Theory]
    [InlineData("1234567", "2112121221121221121212212112211212122112211221211212212121121")]
    [InlineData("0", "2112121212121")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference, string.Concat(Encode(text, MsiCheckDigit.None).RunWidths));

    /// <summary>
    /// The expected runs are the reference implementation's for each check digit calculation. For 1234567 the
    /// modulo 10 check digit is 4, the IBM modulo 11 check digit is 4 as well, because the weighted sum
    /// 106 leaves 7, and the NCR modulo 11 check digit is 9, because the weighted sum 112 leaves 2.
    /// </summary>
    /// <param name="checkDigit">The check digits the symbol carries.</param>
    /// <param name="reference">The reference run string with the check digits carried.</param>
    [Theory]
    [InlineData(MsiCheckDigit.Modulo10, "211212122112122112121221211221121212211221122121121221212112211212121")]
    [InlineData(MsiCheckDigit.Modulo1010, "21121212211212211212122121122112121221122112212112122121211221121212121221121")]
    [InlineData(MsiCheckDigit.Modulo11, "211212122112122112121221211221121212211221122121121221212112211212121")]
    [InlineData(MsiCheckDigit.Modulo1110, "21121212211212211212122121122112121221122112212112122121211221121212121221121")]
    [InlineData(MsiCheckDigit.NcrModulo11, "211212122112122112121221211221121212211221122121121221212121121221121")]
    [InlineData(MsiCheckDigit.NcrModulo1110, "21121212211212211212122121122112121221122112212112122121212112122112121212121")]
    public void CalculatesTheCheckDigits(MsiCheckDigit checkDigit, string reference)
        => Assert.Equal(reference, string.Concat(Encode("1234567", checkDigit).RunWidths));

    /// <summary>
    /// The printed line shows the data and, by default, the check digits. For 1234567 the modulo 10 check
    /// digit is 4: the digits 7, 5, 3 and 1 double to 14, 10, 6 and 2, whose digits add to 1 + 4 + 1 + 0 +
    /// 6 + 2 = 14, the digits 6, 4 and 2 add 12, and 26 lifts to 30 with 4. The second modulo 10 digit
    /// over 12345674 is 1, and over 12345679, the NCR result, it is 0, because 9, 6, 4 and 2 double to
    /// 18, 12, 8 and 4, whose digits add 24, the digits 7, 5, 3 and 1 add 16, and 40 is a multiple of ten.
    /// </summary>
    /// <param name="checkDigit">The check digits the symbol carries.</param>
    /// <param name="printCheckDigits">Whether the check digits are part of the printed line.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData(MsiCheckDigit.None, true, "1234567")]
    [InlineData(MsiCheckDigit.Modulo10, true, "12345674")]
    [InlineData(MsiCheckDigit.Modulo10, false, "1234567")]
    [InlineData(MsiCheckDigit.Modulo1010, true, "123456741")]
    [InlineData(MsiCheckDigit.Modulo11, true, "12345674")]
    [InlineData(MsiCheckDigit.Modulo1110, true, "123456741")]
    [InlineData(MsiCheckDigit.NcrModulo11, true, "12345679")]
    [InlineData(MsiCheckDigit.NcrModulo1110, true, "123456790")]
    public void PrintsTheDataAndTheCheckDigits(MsiCheckDigit checkDigit, bool printCheckDigits, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new MsiSymbology(checkDigit, printCheckDigits).Encode("1234567", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// The digit 6 weighs 12 in either modulo 11 calculation, which leaves 1, so its check value is 10 and
    /// no single digit can carry it.
    /// </summary>
    /// <param name="checkDigit">The modulo 11 calculation.</param>
    [Theory]
    [InlineData(MsiCheckDigit.Modulo11)]
    [InlineData(MsiCheckDigit.Modulo1110)]
    [InlineData(MsiCheckDigit.NcrModulo11)]
    [InlineData(MsiCheckDigit.NcrModulo1110)]
    public void RejectsAModuloElevenCheckValueOfTen(MsiCheckDigit checkDigit)
        => Assert.Throws<ArgumentException>(() => Encode("6", checkDigit));

    /// <summary>
    /// The start character is three modules, every digit is four bits of three modules, and the stop
    /// character is four modules, so a symbol of N digits spans 12N + 7 modules before its quiet zones.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("1234567")]
    [InlineData("0")]
    public void SpansTwelveModulesPerDigit(string text)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text, MsiCheckDigit.None).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((12 * text.Length) + 7, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12A4")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text, MsiCheckDigit.None));

    private static LinearBarcodeSymbol Encode(string text, MsiCheckDigit checkDigit)
        => (LinearBarcodeSymbol)new MsiSymbology(checkDigit).Encode(text, new BarcodeOptions());
}
