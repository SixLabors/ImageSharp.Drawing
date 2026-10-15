// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="RawLinearSymbology"/>. The expected run string is the alternating bar
/// and space module widths, starting with a bar, from the reference implementation's raw encoding API.
/// It is the input itself.
/// </summary>
public class RawLinearSymbologyTests
{
    [Fact]
    public void MatchesReferenceRuns()
        => Assert.Equal(
            "3,3,1,1,3,2,1,3,1,3,1,3,4,1,1,1,2,2,1,3,1,3,1,3,1,2,1,3,1,2,0",
            Encode("331132131313411122131313121312"));

    /// <summary>
    /// An odd number of digits ends on a bar. An even number ends on a space, which stays behind a bar
    /// of zero width, so the symbol keeps its width and no quiet zone is drawn.
    /// </summary>
    [Fact]
    public void KeepsATrailingSpaceBehindAZeroWidthBar()
    {
        Assert.Equal("1,2,3", Encode("123"));
        Assert.Equal("1,2,3,4,0", Encode("1234"));

        SymbolEncoder.Encode(new RawLinearSymbology(), "1234", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(10F, symbol.WidthInModules);
            Assert.Equal(0, symbol.LeadingQuietZone);
            Assert.Equal(0, symbol.TrailingQuietZone);
        });
    }

    [Fact]
    public void PrintsNoLine()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        Assert.Empty(SymbolEncoder.Read(new RawLinearSymbology(), "123", options, SymbolEncoder.Texts));
    }

    [Theory]
    [InlineData("")]
    [InlineData("102")]
    [InlineData("12A")]
    [InlineData("１２")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol, separated by commas.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new RawLinearSymbology(), text, new BarcodeOptions(), static (ref symbol) => string.Join(",", symbol.Runs.ToArray()));
}
