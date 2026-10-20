// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code128Symbology"/>.
/// </summary>
public class Code128SymbologyTests
{
    /// <summary>
    /// Every symbol starts and ends on a bar, so the run count is odd, and the stop character contributes the final four bars and three spaces of section
    /// 5.4.1.
    /// </summary>
    [Fact]
    public void Code128_StartsAndEndsOnABar()
        => Encode("CODE128", (ref symbol) =>
        {
            Assert.Equal(1, symbol.Runs.Length % 2);
            Assert.Equal("2331112", string.Concat(symbol.Runs[^7..].ToArray()));
        });

    /// <summary>
    /// Section 5.4.4.2 requires a quiet zone of ten times the X-dimension on both sides.
    /// </summary>
    [Fact]
    public void Code128_ReservesTheQuietZones()
        => Encode("CODE128", (ref symbol) =>
        {
            Assert.Equal(10, symbol.LeadingQuietZone);
            Assert.Equal(10, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// Code 128 carries no guard bars, so every bar runs the full height from the symbol top.
    /// </summary>
    [Fact]
    public void Code128_DrawsEveryBarAtOneHeight()
        => Encode("CODE128", (ref symbol) =>
        {
            float first = symbol.BarHeights[0];

            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0, top));
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal(first, height));
        });

    /// <summary>
    /// The symbology encodes ASCII 0 to 127. The encoder rejects a character above that range and does not replace it.
    /// </summary>
    [Fact]
    public void Code128_RejectsNonAscii()
        => Assert.Throws<ArgumentException>(() => Encode("caf\u00e9"));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    private static void Encode(string text)
        => SymbolEncoder.Encode(new Code128Symbology(), text, new BarcodeOptions());

    /// <summary>
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new Code128Symbology(), text, new BarcodeOptions(), check);
}
