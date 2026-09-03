// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="ChannelCodeSymbology"/>. Each expected run string is the alternating bar
/// and space module widths, starting with a bar, from the reference implementation's raw encoding API,
/// joined with commas. The reference ends on the bar of the last channel, so the strings compare directly.
/// </summary>
public class ChannelCodeSymbologyTests
{
    /// <summary>
    /// One vector per channel count at the largest value it carries, the smallest values, and a value
    /// with a leading zero, which selects one more channel than the value alone would.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run widths, comma separated.</param>
    [Theory]
    [InlineData("00", "1,1,1,1,1,1,1,1,1,1,2,1,1,3,2")]
    [InlineData("01", "1,1,1,1,1,1,1,1,1,1,2,1,2,3,1")]
    [InlineData("26", "1,1,1,1,1,1,1,1,1,3,3,1,1,1,1")]
    [InlineData("099", "1,1,1,1,1,1,1,1,1,2,1,1,1,1,2,3,3")]
    [InlineData("292", "1,1,1,1,1,1,1,1,1,4,3,1,2,1,1,1,1")]
    [InlineData("1234", "1,1,1,1,1,1,1,1,1,2,1,1,1,2,1,2,4,2,2")]
    [InlineData("3493", "1,1,1,1,1,1,1,1,1,5,4,1,1,1,2,1,1,1,1")]
    [InlineData("00000", "1,1,1,1,1,1,1,1,1,1,2,1,1,1,1,1,2,1,1,6,4")]
    [InlineData("44072", "1,1,1,1,1,1,1,1,1,6,5,1,1,1,1,1,2,1,1,1,1")]
    [InlineData("576688", "1,1,1,1,1,1,1,1,1,7,5,1,2,1,1,1,1,1,2,1,1,1,1")]
    [InlineData("7742862", "1,1,1,1,1,1,1,1,1,8,6,1,1,1,2,1,1,1,1,1,2,1,1,1,1")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference, string.Join(",", Encode(text, false).RunWidths));

    [Fact]
    public void DrawsTheShortFinderPattern()
        => Assert.Equal("1,1,1,1,1,5,4,1,1,1,2,1,1,1,1", string.Join(",", Encode("3493", true).RunWidths));

    /// <summary>
    /// The finder pattern is nine modules and every channel adds a space and a bar whose widths sum to a
    /// constant, so a symbol of C channels, one more than its digits, spans 4C + 7 modules whatever its
    /// value. The quiet zones are 1X before the symbol and 2X after it.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("00")]
    [InlineData("26")]
    [InlineData("3493")]
    [InlineData("7742862")]
    public void SpansFourModulesPerChannel(string text)
    {
        LinearBarcodeSymbol symbol = Encode(text, false);
        int widthInModules = 0;
        foreach (int run in symbol.RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((4 * (text.Length + 1)) + 7, widthInModules);
        Assert.Equal(1, symbol.LeadingQuietZone);
        Assert.Equal(2, symbol.TrailingQuietZone);
    }

    [Fact]
    public void PrintsTheDigitsAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new ChannelCodeSymbology().Encode("0099", options);
        Assert.Equal("0099", Assert.Single(symbol.Text).Text);
    }

    /// <summary>
    /// A value beyond the range of its channel count is rejected rather than moved to a wider symbol,
    /// because the digit count is the caller's choice of symbol size.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("27")]
    [InlineData("293")]
    [InlineData("3494")]
    [InlineData("44073")]
    [InlineData("576689")]
    [InlineData("7742863")]
    [InlineData("12345678")]
    [InlineData("12A")]
    [InlineData("１２")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text, false));

    private static LinearBarcodeSymbol Encode(string text, bool shortFinder)
        => (LinearBarcodeSymbol)new ChannelCodeSymbology(shortFinder).Encode(text, new BarcodeOptions());
}
