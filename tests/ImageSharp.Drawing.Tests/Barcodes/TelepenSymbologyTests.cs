// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="TelepenSymbology"/> and <see cref="TelepenNumericSymbology"/>.
/// </summary>
public class TelepenSymbologyTests
{
    /// <summary>
    /// The worked example of "Telepen - Barcode Symbology Information and History": the values of Telepen add to 717, which leaves 82 after division by 127,
    /// and 127 minus 82 is 45, the minus sign. A sum that divides exactly gives 0, and NUL characters alone give 127.
    /// </summary>
    /// <param name="values">The character values.</param>
    /// <param name="expected">The value of the check character.</param>
    [Theory]
    [InlineData(new[] { 84, 101, 108, 101, 112, 101, 110 }, 45)]
    [InlineData(new[] { 127 }, 0)]
    [InlineData(new[] { 0, 0 }, 127)]
    public void CalculatesTheCheckCharacterOfTheDocument(int[] values, int expected)
        => Assert.Equal(expected, TelepenEncoder.CheckCharacter(values));

    /// <summary>
    /// The document sends each character as 8 bits with even parity, least significant bit first. Binary 1 is the first pattern, and binary 010 is the third
    /// pattern, three times as wide. With a 3:1 ratio, the first pattern can only be a narrow bar and a narrow space, and the third pattern can only be a wide
    /// bar and a wide space. The start code, binary 01011111, sends 1, 1, 1, 1, 1 and then 010. DEL, value 127, has 7 bits set and a parity bit of 1, so it
    /// sends eight 1 bits. The stop code, binary 11111010, sends 010 and then 1, 1, 1, 1, 1. The runs end on the last bar.
    /// </summary>
    [Fact]
    public void SendsTheStartCodeDataAndStopCodeAsTheBitPatternsOfTheDocument()
    {
        string runs = Encode(((char)127).ToString());

        Assert.StartsWith("1111111111" + "33" + "1111111111111111", runs);
        Assert.EndsWith("33" + "111111111", runs);
    }

    /// <summary>
    /// The printed line shows the text as given. It never shows the start code, the check character or the stop code.
    /// </summary>
    [Fact]
    public void PrintsTheTextAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new TelepenSymbology(), "ABC123", options, SymbolEncoder.Texts);
        string[] numeric = SymbolEncoder.Read(new TelepenNumericSymbology(), "1X23", options, SymbolEncoder.Texts);

        Assert.Equal("ABC123", Assert.Single(texts));
        Assert.Equal("1X23", Assert.Single(numeric));
    }

    /// <summary>
    /// Every character is sixteen modules, the start code, the check character and the stop code are three more, and the runs end on the last bar of the stop
    /// code, one module before its end.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("ABC123")]
    [InlineData("A")]
    public void SpansSixteenModulesPerCharacter(string text)
        => Assert.Equal((16 * (text.Length + 3)) - 1, SymbolEncoder.Read(new TelepenSymbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// The Telepen history of SB Electronic Systems: "For open systems the minimum value of x is 0.0075 inch or 0.19 mm." At 1000 pixels a millimetre the
    /// smallest whole number of pixels at or over 0.1905 mm is 191 pixels, so an X dimension of 0.1 mm draws at 0.191 mm.
    /// </summary>
    [Fact]
    public void ClampsAnXDimensionUnderTheOpenSystemMinimum()
        => SymbolEncoder.Encode(
            new TelepenSymbology(),
            "ABC123",
            new BarcodeOptions { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 0.1 },
            static (ref symbol) => Assert.Equal(0.191, symbol.XDimension, 1e-9));

    /// <summary>
    /// Telepen carries the ASCII characters 0 to 127 only. The symbology rejects empty input and characters above 127.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("é")]
    [InlineData("１")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Numeric mode carries pairs of digits, and X can replace the second digit of a pair. The symbology rejects empty input, an odd number of characters,
    /// an X in the first place of a pair, a letter other than X and full width digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("X1")]
    [InlineData("12X4")]
    [InlineData("12A4")]
    [InlineData("１２")]
    public void RejectsMalformedNumericInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => EncodeNumeric(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new TelepenSymbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);

    /// <summary>
    /// Encodes the text in numeric mode with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string EncodeNumeric(string text)
        => SymbolEncoder.Read(new TelepenNumericSymbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
