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
