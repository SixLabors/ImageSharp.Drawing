// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PharmacodeSymbology"/> and <see cref="TwoTrackPharmacodeSymbology"/>.
/// Each expected one-track run string is the alternating bar and space module widths, starting with a
/// bar, from the reference implementation's raw encoding API, joined with commas. The reference emits a
/// gap after the last bar, so the test drops it before it compares. The two-track bar kinds come from the
/// same API's bar heights and offsets.
/// </summary>
public class PharmacodeSymbologyTests
{
    [Theory]
    [InlineData("3", "1,2,1,2")]
    [InlineData("117480", "3,2,3,2,1,2,1,2,3,2,1,2,3,2,1,2,3,2,3,2,3,2,1,2,3,2,1,2,1,2,3,2")]
    [InlineData("131070", "3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference[..^2], string.Join(",", Encode(text).RunWidths));

    /// <summary>
    /// Section 4.3 of the guide values a thin bar in position n at 2 to the power n - 1 and a thick bar at
    /// twice that, from the right. So 3 is two thin bars, 4 is a thin bar and a thick bar, 5 is a thick
    /// bar and a thin bar, and 6 is two thick bars, and every symbol's bars add back to its value.
    /// </summary>
    /// <param name="text">The number to encode.</param>
    /// <param name="bars">The bars from the left, T for thick and t for thin.</param>
    [Theory]
    [InlineData("3", "tt")]
    [InlineData("4", "tT")]
    [InlineData("5", "Tt")]
    [InlineData("6", "TT")]
    [InlineData("7", "ttt")]
    [InlineData("131070", "TTTTTTTTTTTTTTTT")]
    public void FollowsTheValueTableOfSectionFourThree(string text, string bars)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        string actual = string.Empty;
        int value = 0;
        for (int i = 0; i < symbol.RunWidths.Length; i += 2)
        {
            bool thick = symbol.RunWidths[i] == 3;
            actual += thick ? 'T' : 't';
            int position = (symbol.RunWidths.Length + 1) / 2 - (i / 2);
            value += (thick ? 2 : 1) << (position - 1);
        }

        Assert.Equal(bars, actual);
        Assert.Equal(int.Parse(text), value);
    }

    /// <summary>
    /// The one-track dimensions of section 1.2 at the 0.5 mm module: bars of 1 and 3 modules, gaps of 2,
    /// a height of 16 and quiet zones of 12.
    /// </summary>
    [Fact]
    public void DrawsTheOneTrackDimensionsOfSectionOneTwo()
    {
        LinearBarcodeSymbol symbol = Encode("117480");

        Assert.All(symbol.BarHeights, height => Assert.Equal(16F, height));
        Assert.All(symbol.BarTops, top => Assert.Equal(0F, top));
        Assert.Equal(12, symbol.LeadingQuietZone);
        Assert.Equal(12, symbol.TrailingQuietZone);
    }

    /// <summary>
    /// Section 4.5 of the guide values a lower bar in position n at 3 to the power n - 1, an upper bar at
    /// twice that and a full bar at three times that. Its worked example reads 81 + 18 + 3 + 3 = 105 from
    /// a full bar, an upper bar, a lower bar and a full bar. The expected bars for 117480, the smallest
    /// value 4 and the largest value 64570080 are the reference implementation's.
    /// </summary>
    /// <param name="text">The number to encode.</param>
    /// <param name="bars">The bars from the left, L for lower, U for upper and F for full.</param>
    [Theory]
    [InlineData("105", "FULF")]
    [InlineData("4", "LL")]
    [InlineData("117480", "LUUULUFFUFF")]
    [InlineData("64570080", "FFFFFFFFFFFFFFFF")]
    public void FollowsTheValueTableOfSectionFourFive(string text, string bars)
    {
        LinearBarcodeSymbol symbol = EncodeTwoTrack(text);
        string actual = string.Empty;
        int value = 0;
        int count = symbol.BarHeights.Length;
        for (int i = 0; i < count; i++)
        {
            char kind = symbol.BarHeights[i] == 8F ? 'F' : symbol.BarTops[i] == 4F ? 'L' : 'U';
            actual += kind;
            int weight = kind == 'L' ? 1 : kind == 'U' ? 2 : 3;
            value += weight * (int)Math.Pow(3, count - 1 - i);
        }

        Assert.Equal(bars, actual);
        Assert.Equal(int.Parse(text), value);
        Assert.All(symbol.RunWidths, run => Assert.Equal(1, run));
        Assert.Equal(6, symbol.LeadingQuietZone);
        Assert.Equal(6, symbol.TrailingQuietZone);
    }

    /// <summary>
    /// The half bars are half of the height the caller sets, and the lower bar starts halfway down. At the
    /// 1 mm X dimension a 30 mm bar height is 30 modules.
    /// </summary>
    [Fact]
    public void ScalesTheHalfBarsWithTheBarHeight()
    {
        BarcodeOptions options = new() { BarHeight = 30F };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new TwoTrackPharmacodeSymbology().Encode("105", options);

        Assert.Equal([30F, 15F, 15F, 30F], symbol.BarHeights);
        Assert.Equal([0F, 0F, 15F, 0F], symbol.BarTops);
    }

    [Fact]
    public void PrintsTheNumberAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new PharmacodeSymbology().Encode("117480", options);
        Assert.Equal("117480", Assert.Single(symbol.Text).Text);

        LinearBarcodeSymbol twoTrack = (LinearBarcodeSymbol)new TwoTrackPharmacodeSymbology().Encode("117480", options);
        Assert.Equal("117480", Assert.Single(twoTrack.Text).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("131071")]
    [InlineData("1234567")]
    [InlineData("12A")]
    [InlineData("１２")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    [Theory]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("64570081")]
    [InlineData("123456789")]
    [InlineData("12A")]
    public void RejectsMalformedTwoTrackInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => EncodeTwoTrack(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new PharmacodeSymbology().Encode(text, new BarcodeOptions());

    private static LinearBarcodeSymbol EncodeTwoTrack(string text)
        => (LinearBarcodeSymbol)new TwoTrackPharmacodeSymbology().Encode(text, new BarcodeOptions());
}
