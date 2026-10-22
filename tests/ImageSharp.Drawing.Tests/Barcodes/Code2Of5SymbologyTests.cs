// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the five plain 2 of 5 symbologies.
/// </summary>
public class Code2Of5SymbologyTests
{
    /// <summary>
    /// The encoder validates a supplied check digit against the data and carries it once. The symbol is then the one that the calculated check digit
    /// produces. The encoder rejects a wrong check digit and a check digit without data.
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
        string validated = Encode(symbologyType, CheckCharacterMode.Validate, true, "12348");
        string computed = Encode(symbologyType, CheckCharacterMode.Compute, true, "1234");

        Assert.Equal(computed, validated);
        Assert.Throws<ArgumentException>(() => Encode(symbologyType, CheckCharacterMode.Validate, true, "12347"));

        // A check digit alone has no data to check.
        Assert.Throws<ArgumentException>(() => Encode(symbologyType, CheckCharacterMode.Validate, true, "0"));
    }

    /// <summary>
    /// The printed line shows the digits the symbol carries. The check digit prints by default, and a caller can keep it off the printed line.
    /// </summary>
    /// <param name="checkDigit">Whether the symbol carries the check digit, and whether the encoder calculates it or validates it.</param>
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
        string[] texts = SymbolEncoder.Read(new Industrial2Of5Symbology(checkDigit, printCheckDigit), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// The encoder rejects any character that is not an ASCII digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("12A4")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(typeof(Industrial2Of5Symbology), CheckCharacterMode.None, true, text));

    /// <summary>
    /// Encodes the text with a symbology of the given type and the default options.
    /// </summary>
    /// <param name="symbologyType">The type of the symbology.</param>
    /// <param name="checkDigit">Whether the symbol carries a check digit, and whether the encoder calculates it or validates it.</param>
    /// <param name="printCheckDigit">Whether the check digit is part of the printed line.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(Type symbologyType, CheckCharacterMode checkDigit, bool printCheckDigit, string text)
    {
        BarcodeSymbology symbology = symbologyType.Name switch
        {
            nameof(Industrial2Of5Symbology) => new Industrial2Of5Symbology(checkDigit, printCheckDigit),
            nameof(Iata2Of5Symbology) => new Iata2Of5Symbology(checkDigit, printCheckDigit),
            nameof(Matrix2Of5Symbology) => new Matrix2Of5Symbology(checkDigit, printCheckDigit),
            nameof(Coop2Of5Symbology) => new Coop2Of5Symbology(checkDigit, printCheckDigit),
            _ => new Datalogic2Of5Symbology(checkDigit, printCheckDigit),
        };

        return SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RunString);
    }
}
