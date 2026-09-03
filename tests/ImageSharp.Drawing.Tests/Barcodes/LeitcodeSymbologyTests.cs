// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="LeitcodeSymbology"/>. The expected run string is the alternating bar and
/// space module widths, starting with a bar, taken verbatim from an independent reference implementation
/// through its raw encoding API. That implementation draws a wide element two modules wide and emits a
/// narrow space after the stop pattern, so the test widens every wide element to the three modules this
/// library draws and drops the trailing space before it compares. The number is the worked example the
/// Deutsche Post documentation gives.
/// </summary>
public class LeitcodeSymbologyTests
{
    /// <summary>
    /// The reference emits the same symbol whether the check digit is supplied or calculated, so both
    /// forms of the documented example compare against one vector.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("2134807501640")]
    [InlineData("21348075016401")]
    public void MatchesReferenceRuns(string text)
    {
        const string reference = "111112211111222121121112211112221112111221211211212112112122111212112121122111";

        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(reference[..^1].Replace('2', '3'), string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// The documentation weights the digits 4 and 9 alternately from the first, so 2134807501640 gives
    /// 8 + 9 + 12 + 36 + 32 + 0 + 28 + 45 + 0 + 9 + 24 + 36 + 0 = 239, and "239 Modulo 10 = 9,
    /// Ergänzung zu 10 = 1". A supplied check digit that disagrees is rejected.
    /// </summary>
    [Fact]
    public void CalculatesTheDeutschePostCheckDigit()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new LeitcodeSymbology().Encode("2134807501640", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("21348.075.016.40 1", placement.Text);
        Assert.Throws<ArgumentException>(() => Encode("21348075016402"));
    }

    /// <summary>
    /// A Leitcode is thirteen data digits and an optional check digit, so anything else is rejected, as is
    /// a non-digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("213480750164")]
    [InlineData("213480750164012")]
    [InlineData("213480750164A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new LeitcodeSymbology().Encode(text, new BarcodeOptions());
}
