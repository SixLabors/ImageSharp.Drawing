// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="FlattermarkenSymbology"/>.
/// </summary>
public class FlattermarkenSymbologyTests
{
    /// <summary>
    /// Every digit takes a window of nine modules, so the symbol is nine modules per digit wide whatever the digits, with no quiet zone.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("11099")]
    [InlineData("0123456789")]
    public void SpansNineModulesPerDigit(string text)
        => SymbolEncoder.Encode(new FlattermarkenSymbology(), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal(9 * text.Length, SymbolEncoder.RunSum(ref symbol));
            Assert.Equal(0, symbol.LeadingQuietZone);
            Assert.Equal(0, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// The TEC-IT Barcode Reference gives a "Symbol height between 5 and 10mm" and a module width of 2 to 3 mm. The bars are 10 mm high at any module width:
    /// 5 modules at 2 mm and 10 / 3 modules at 3 mm.
    /// </summary>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    [Theory]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void DrawsBarsTenMillimetresHigh(double xDimension)
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, XDimension = xDimension };

        SymbolEncoder.Encode(new FlattermarkenSymbology(), "11099", options, (ref symbol) =>
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal((float)(10 / xDimension), height, 1e-4F)));
    }

    /// <summary>
    /// The human readable text is the digits as the caller gives them.
    /// </summary>
    [Fact]
    public void PrintsTheDigitsAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new FlattermarkenSymbology(), "11099", options, SymbolEncoder.Texts);

        Assert.Equal("11099", Assert.Single(texts));
    }

    /// <summary>
    /// The encoder rejects an empty text and any character that is not an ASCII digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("12A")]
    [InlineData("1 2")]
    [InlineData("１２")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new FlattermarkenSymbology(), text, new BarcodeOptions()));
}
