// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="IdentcodeSymbology"/>. The expected run string is the alternating bar
/// and space module widths, starting with a bar, taken verbatim from an independent reference
/// implementation through its raw encoding API. That implementation draws a wide element two modules
/// wide and emits a narrow space after the stop pattern, so the test widens every wide element to the
/// three modules this library draws and drops the trailing space before it compares. The number is the
/// worked example the Deutsche Post documentation gives.
/// </summary>
public class IdentcodeSymbologyTests
{
    /// <summary>
    /// The reference emits the same symbol whether the check digit is supplied or calculated, so both
    /// forms of the documented example compare against one vector.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("56310243031")]
    [InlineData("563102430313")]
    public void MatchesReferenceRuns(string text)
    {
        const string reference = "11112112221111222111111211122121121212211121121221211122121111212111";

        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(reference[..^1].Replace('2', '3'), string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// The documentation weights the digits 4 and 9 alternately from the first, so 56310243031 gives
    /// 20 + 54 + 12 + 9 + 0 + 18 + 16 + 27 + 0 + 27 + 4 = 187, and "187 Modulo 10 = 7, Ergänzung zu 10
    /// = 3". The printed line is the grouping the reference implementations agree on for that example. A
    /// supplied check digit that disagrees is rejected.
    /// </summary>
    [Fact]
    public void CalculatesTheDeutschePostCheckDigit()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new IdentcodeSymbology().Encode("56310243031", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("56.310 243.031 3", placement.Text);
        Assert.Throws<ArgumentException>(() => Encode("563102430314"));
    }

    /// <summary>
    /// An Identcode is eleven data digits and an optional check digit, so anything else is rejected, as
    /// is a non-digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("5631024303")]
    [InlineData("5631024303131")]
    [InlineData("5631024303A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new IdentcodeSymbology().Encode(text, new BarcodeOptions());
}
