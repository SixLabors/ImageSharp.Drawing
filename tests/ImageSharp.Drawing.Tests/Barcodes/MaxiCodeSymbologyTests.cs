// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="MaxiCodeSymbology"/> against ISO/IEC 16023: the quiet zones and the size, the automatic mode, the X dimension and the data
/// that the symbology rejects.
/// </summary>
public class MaxiCodeSymbologyTests
{
    /// <summary>
    /// Section 4.11.5 of ISO/IEC 16023: "1W on the left and right sides" and "1Y on the top and bottom", and section 4.11.6: the symbol with its quiet zones
    /// "is 32X wide by (34Y + V) high".
    /// </summary>
    [Fact]
    public void QuietZonesAndSizeFollowSection411()
        => SymbolEncoder.Encode(new MaxiCodeSymbology(), "This is MaxiCode", new BarcodeOptions(), static (ref symbol) =>
        {
            double y = Math.Sqrt(3) / 2;
            double v = 2 / Math.Sqrt(3);

            Assert.Equal(32F, symbol.WidthInModules + symbol.LeadingQuietZone + symbol.TrailingQuietZone);
            Assert.Equal((34 * y) + v, symbol.HeightInModules + symbol.TopQuietZone + symbol.BottomQuietZone, 4);
        });

    /// <summary>
    /// A structured carrier message needs its postal code, country code and class of service, each followed by a group separator. A mode 2 postal code is 1 to
    /// 9 digits, a mode 3 postal code 1 to 6 characters, and the country code and the class of service are three digits.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(2, "152382802\u001D840\u001D001")]
    [InlineData(2, "1234567890\u001D840\u001D001\u001D")]
    [InlineData(2, "12A45\u001D840\u001D001\u001D")]
    [InlineData(3, "B10500X\u001D056\u001D999\u001D")]
    [InlineData(3, "b1050\u001D056\u001D999\u001D")]
    [InlineData(2, "152382802\u001D84\u001D001\u001D")]
    [InlineData(2, "152382802\u001D840\u001D01A\u001D")]
    public void RejectsMalformedStructuredCarrier(int mode, string text)
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new MaxiCodeSymbology((MaxiCodeMode)mode), text, new BarcodeOptions()));

    /// <summary>
    /// Without a mode the symbol takes mode 5 when the data fits and mode 4 otherwise, so it matches the symbol of that mode. The data ends in code set C,
    /// which has no pad codeword: 71 or 80 characters of code set A, the lock into code set C and four characters of code set C need 77 or 86 codewords. 77
    /// fill mode 5 with no latch back to code set A, and 86 take mode 4, whose pads follow a latch back to code set A.
    /// </summary>
    /// <param name="capitals">The number of capital letters before the four accented letters.</param>
    /// <param name="mode">The mode the data takes.</param>
    [Theory]
    [InlineData(71, MaxiCodeMode.FullErrorCorrection)]
    [InlineData(80, MaxiCodeMode.Standard)]
    public void AutomaticModeMatchesTheModeItTakes(int capitals, MaxiCodeMode mode)
    {
        string text = new string('A', capitals) + "ÀÀÀÀ";
        string expected = null;
        SymbolEncoder.Encode(new MaxiCodeSymbology(mode), text, new BarcodeOptions(), (ref symbol) => expected = SymbolEncoder.ModuleGrid(ref symbol));

        SymbolEncoder.Encode(new MaxiCodeSymbology(), text, new BarcodeOptions(), (ref symbol) => Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol)));
    }

    /// <summary>
    /// A mode 5 symbol holds 77 codewords of data. Data that needs more codewords throws.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondModeCapacity()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new MaxiCodeSymbology(MaxiCodeMode.FullErrorCorrection), new string('A', 78), new BarcodeOptions()));

    /// <summary>
    /// MaxiCode carries the 256 byte values, U+0000 to U+00FF. A character above U+00FF throws.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new MaxiCodeSymbology(), "Ā", new BarcodeOptions()));

    /// <summary>
    /// Table 7 gives W = X = L/29 = 0.88 mm at L = 25.50 mm, and section 4.11.1: "The acceptable range of L is 24.00 mm to 27.00 mm." The hexagonal modules
    /// draw at the exact X dimension, so an X dimension inside that range draws as given and one outside it draws at the nearest limit.
    /// </summary>
    /// <param name="xDimension">The X dimension of the caller in millimetres, or <see langword="null"/> for the default.</param>
    /// <param name="expected">The X dimension the symbol draws at in millimetres.</param>
    [Theory]
    [InlineData(null, 0.88)]
    [InlineData(0.9, 0.9)]
    [InlineData(0.5, 24.00 / 29)]
    [InlineData(2.0, 27.00 / 29)]
    public void ClampsTheXDimensionToTheRangeOfSectionFourElevenOne(double? xDimension, double expected)
        => SymbolEncoder.Encode(
            new MaxiCodeSymbology(),
            "This is MaxiCode",
            new BarcodeOptions { XDimension = xDimension },
            (ref symbol) => Assert.Equal(expected, symbol.XDimension, 1e-12));
}
