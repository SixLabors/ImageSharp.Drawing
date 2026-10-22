// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="LeitcodeSymbology"/>. The number is the worked example the Deutsche Post documentation gives.
/// </summary>
public class LeitcodeSymbologyTests
{
    /// <summary>
    /// The documentation weights the digits 4 and 9 alternately from the first, so 2134807501640 gives 8 + 9 + 12 + 36 + 32 + 0 + 28 + 45 + 0 + 9 + 24 + 36 + 0
    /// = 239, and "239 Modulo 10 = 9, Ergänzung zu 10 = 1". A supplied check digit that disagrees throws.
    /// </summary>
    [Fact]
    public void CalculatesTheDeutschePostCheckDigit()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new LeitcodeSymbology(), "2134807501640", options, SymbolEncoder.Texts);

        Assert.Equal("21348.075.016.40 1", Assert.Single(texts));

        Assert.Throws<ArgumentException>(() => Encode("21348075016402"));
    }

    /// <summary>
    /// A Leitcode is thirteen data digits and an optional check digit. The symbology rejects other lengths and non-digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("213480750164")]
    [InlineData("213480750164012")]
    [InlineData("213480750164A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// When the options set no X dimension, the narrow module is 0.495 mm. When the options set no bar height, the bars are 25 mm high. At 1000 pixels a
    /// millimetre, the module is 495 pixels.
    /// </summary>
    [Fact]
    public void DefaultsToTheNominalModuleAndTheLeastHeight()
        => SymbolEncoder.Encode(new LeitcodeSymbology(), "2134807501640", new BarcodeOptions { Dpi = SymbolEncoder.MicrometreDpi }, static (ref symbol) =>
        {
            Assert.Equal(0.495, symbol.XDimension, 1e-9);
            Assert.Equal(25.0, symbol.BarHeights[0] * symbol.XDimension, 1e-4);
        });

    /// <summary>
    /// Deutsche Post gives a narrow module of 0.375 mm to 0.5 mm and bars at least 25 mm high. An X dimension or a bar height outside that range draws at
    /// the nearest limit.
    /// </summary>
    /// <param name="xDimension">The X dimension the caller sets, in millimetres.</param>
    /// <param name="expected">The X dimension the symbol draws at, in millimetres.</param>
    [Theory]
    [InlineData(0.2, 0.375)]
    [InlineData(0.8, 0.5)]
    public void ClampsToTheDeutschePostRange(double xDimension, double expected)
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MicrometreDpi, XDimension = xDimension, BarHeight = 10 };
        SymbolEncoder.Encode(new LeitcodeSymbology(), "2134807501640", options, (ref symbol) =>
        {
            Assert.Equal(expected, symbol.XDimension, 1e-9);
            Assert.Equal(25.0, symbol.BarHeights[0] * symbol.XDimension, 1e-4);
        });
    }

    /// <summary>
    /// Encodes the text with the default X dimension at 100 pixels a millimetre.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new LeitcodeSymbology(), text, new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi }, SymbolEncoder.RunString);
}
