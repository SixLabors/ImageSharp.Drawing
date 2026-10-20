// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="ChannelCodeSymbology"/>.
/// </summary>
public class ChannelCodeSymbologyTests
{
    /// <summary>
    /// The short finder pattern is three bars and two spaces in place of five bars and four spaces. The channels after it do not change.
    /// </summary>
    [Fact]
    public void DrawsTheShortFinderPattern()
    {
        string full = Encode("3493", false);

        Assert.StartsWith("1,1,1,1,1,1,1,1,1,", full, StringComparison.Ordinal);
        Assert.Equal("1,1,1,1,1," + full["1,1,1,1,1,1,1,1,1,".Length..], Encode("3493", true));
    }

    /// <summary>
    /// The finder pattern is nine modules and every channel adds a space and a bar whose widths sum to a constant, so a symbol of C channels, one more than its
    /// digits, spans 4C + 7 modules whatever its value. The quiet zones are 1X before the symbol and 2X after it.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("00")]
    [InlineData("26")]
    [InlineData("3493")]
    [InlineData("7742862")]
    public void SpansFourModulesPerChannel(string text)
    {
        SymbolEncoder.Encode(new ChannelCodeSymbology(false), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((4 * (text.Length + 1)) + 7, SymbolEncoder.RunSum(ref symbol));
            Assert.Equal(1, symbol.LeadingQuietZone);
            Assert.Equal(2, symbol.TrailingQuietZone);
        });
    }

    /// <summary>
    /// The human readable text is the digits as the caller gives them, with the leading zeros.
    /// </summary>
    [Fact]
    public void PrintsTheDigitsAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new ChannelCodeSymbology(), "0099", options, SymbolEncoder.Texts);

        Assert.Equal("0099", Assert.Single(texts));
    }

    /// <summary>
    /// The encoder rejects a value beyond the range of its channel count. It does not move the value to a wider symbol, because the digit count is the
    /// caller's choice of symbol size. The encoder also rejects fewer than 2 or more than 7 digits, and characters that are not ASCII digits.
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

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="shortFinder">Whether the symbol uses the short finder pattern.</param>
    /// <returns>The run widths of the symbol, separated by commas.</returns>
    private static string Encode(string text, bool shortFinder)
        => SymbolEncoder.Read(
            new ChannelCodeSymbology(shortFinder), text, new BarcodeOptions(), static (ref symbol) => string.Join(",", symbol.Runs.ToArray()));
}
