// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PharmacodeSymbology"/> and <see cref="TwoTrackPharmacodeSymbology"/>. Each expected one-track run string is the alternating bar
/// and space module widths, starting with a bar, from the reference implementation's raw encoding API, joined with commas. The reference emits a gap after the
/// last bar, so the test drops it before it compares. The two-track bar kinds come from the same API's bar heights and offsets.
/// </summary>
public class PharmacodeSymbologyTests
{
    [Theory]
    [InlineData("3", "1,2,1,2")]
    [InlineData("117480", "3,2,3,2,1,2,1,2,3,2,1,2,3,2,1,2,3,2,3,2,3,2,1,2,3,2,1,2,1,2,3,2")]
    [InlineData("131070", "3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2,3,2")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference[..^2], string.Join(",", Encode(text)));

    /// <summary>
    /// Section 4.3 of the guide values a thin bar in position n at 2 to the power n - 1 and a thick bar at twice that, from the right. So 3 is two thin bars, 4
    /// is a thin bar and a thick bar, 5 is a thick bar and a thin bar, and 6 is two thick bars, and every symbol's bars add back to its value.
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
        int[] runs = Encode(text);
        string actual = string.Empty;
        int value = 0;
        for (int i = 0; i < runs.Length; i += 2)
        {
            bool thick = runs[i] == 3;
            actual += thick ? 'T' : 't';
            int position = ((runs.Length + 1) / 2) - (i / 2);
            value += (thick ? 2 : 1) << (position - 1);
        }

        Assert.Equal(bars, actual);
        Assert.Equal(int.Parse(text, CultureInfo.InvariantCulture), value);
    }

    /// <summary>
    /// The one-track dimensions of section 1.2 at the 0.5 mm module: a height of 8.0 mm, 16 modules, and the 6 mm gap of section 2.2.4 to other print, 12
    /// modules. Both are in millimetres, so at a 0.35 mm module they are 8 / 0.35 and 6 / 0.35 modules.
    /// </summary>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.35)]
    public void DrawsTheOneTrackDimensionsOfSectionOneTwo(double xDimension)
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, XDimension = xDimension };
        SymbolEncoder.Encode(new PharmacodeSymbology(), "117480", options, (ref symbol) =>
        {
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal((float)(8.0 / xDimension), height, 1e-4F));
            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0F, top));
            Assert.Equal((float)(6 / xDimension), symbol.LeadingQuietZone, 1e-4F);
            Assert.Equal((float)(6 / xDimension), symbol.TrailingQuietZone, 1e-4F);
        });
    }

    /// <summary>
    /// Section 1.3 gives the thin bar of the miniature Pharmacode a minimum of 0.3 mm, and section 1.2 gives the thin bar of the standard Pharmacode a maximum
    /// of 0.7 mm. Section 1.4 gives the bars of the two-track Pharmacode 0.8 mm to 2 mm.
    /// </summary>
    /// <param name="twoTrack">Whether the symbol is a two-track Pharmacode.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="expected">The X dimension the symbol draws at in millimetres, the nearest limit of the range.</param>
    [Theory]
    [InlineData(false, 0.29, 0.3)]
    [InlineData(false, 0.71, 0.7)]
    [InlineData(true, 0.79, 0.8)]
    [InlineData(true, 2.01, 2.0)]
    public void ClampsAnXDimensionOutsideTheTables(bool twoTrack, double xDimension, double expected)
        => SymbolEncoder.Encode(
            twoTrack ? new TwoTrackPharmacodeSymbology() : new PharmacodeSymbology(),
            "117480",
            new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi, XDimension = xDimension },
            (ref symbol) => Assert.Equal(expected, symbol.XDimension, 1e-9));

    /// <summary>
    /// Section 4.5 of the guide values a lower bar in position n at 3 to the power n - 1, an upper bar at twice that and a full bar at three times that. Its
    /// worked example reads 81 + 18 + 3 + 3 = 105 from a full bar, an upper bar, a lower bar and a full bar. The expected bars for 117480, the smallest value 4
    /// and the largest value 64570080 are the reference implementation's.
    /// </summary>
    /// <param name="text">The number to encode.</param>
    /// <param name="bars">The bars from the left, L for lower, U for upper and F for full.</param>
    [Theory]
    [InlineData("105", "FULF")]
    [InlineData("4", "LL")]
    [InlineData("117480", "LUUULUFFUFF")]
    [InlineData("64570080", "FFFFFFFFFFFFFFFF")]
    public void FollowsTheValueTableOfSectionFourFive(string text, string bars)
        => SymbolEncoder.Encode(new TwoTrackPharmacodeSymbology(), text, new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi }, (ref symbol) =>
        {
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
            Assert.Equal(int.Parse(text, CultureInfo.InvariantCulture), value);
            Assert.All(symbol.Runs.ToArray(), run => Assert.Equal(1, run));
            Assert.Equal(6F, symbol.LeadingQuietZone);
            Assert.Equal(6F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// The half bars are half of the height the caller sets, and the lower bar starts halfway down. At the 1 mm X dimension a 30 mm bar height is 30 modules.
    /// </summary>
    [Fact]
    public void ScalesTheHalfBarsWithTheBarHeight()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 30F };
        SymbolEncoder.Encode(new TwoTrackPharmacodeSymbology(), "105", options, static (ref symbol) =>
        {
            Assert.Equal([30F, 15F, 15F, 30F], symbol.BarHeights.ToArray());
            Assert.Equal([0F, 0F, 15F, 0F], symbol.BarTops.ToArray());
        });
    }

    /// <summary>
    /// The Laetus Pharmacode Guide defines no text line, so a font prints none.
    /// </summary>
    [Fact]
    public void PrintsNoText()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Empty(SymbolEncoder.Read(new PharmacodeSymbology(), "117480", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new TwoTrackPharmacodeSymbology(), "117480", options, SymbolEncoder.Texts));
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
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new TwoTrackPharmacodeSymbology(), text, new BarcodeOptions()));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static int[] Encode(string text)
        => SymbolEncoder.Read(new PharmacodeSymbology(), text, new BarcodeOptions(), static (ref symbol) => symbol.Runs.ToArray());
}
