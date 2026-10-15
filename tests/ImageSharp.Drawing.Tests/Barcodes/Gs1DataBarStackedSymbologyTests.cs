// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarStackedSymbology"/>. Each expected row is the 50 modules of the
/// upper row, the separator and the lower row, 1 for dark, taken from an independent reference
/// implementation through its raw encoding API and confirmed by a second one. The separator follows the
/// rows above and below it, so it shows up here too.
/// </summary>
public class Gs1DataBarStackedSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="Gs1DataBarStacked_MatchesReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, string> Gs1DataBarStacked_MatchesReferenceRowsData { get; } = new()
    {
        {
            "(01)09521234543213",
            "01000001010000010100111000000001011110001010110010|" +
            "00000110101011101010000111111100101010110101000000|" +
            "10111000010110010101111000000011000001001001011101"
        },
        {
            "(01)00012345678905",
            "01010100100000000100111111100001011100101101111010|" +
            "00001010101011111010000000111010100011010010000000|" +
            "10111001010110000101111111000111001100111101110101"
        },
        {
            "(01)00034567890125",
            "01010100100000000100111110000001010011100110011010|" +
            "00001010101010111010010101111010101101011001100000|" +
            "10110111011101000101100000000111000010110010001101"
        },
        {
            "(01)20012345678909",
            "01010001110100000100111111100001010011011011111010|" +
            "00001110101011011010010101011010101001001001010000|" +
            "10110000010010100101100000000111000110110110001101"
        },
        {
            "(01)99999999999997",
            "01001011101110000100011111111001011110110100111010|" +
            "00000100010010101010100000101110101001001010100000|" +
            "10100011111101010101111111000001000111110101011101"
        },
        {
            "(01)00000000000000",
            "01010100100000000100011111111001011111110010101010|" +
            "00000101011111111010100000001010100000001101010000|" +
            "10101010110000000101111111110111011111111011010101"
        },
    };

    [Theory]
    [MemberData(nameof(Gs1DataBarStacked_MatchesReferenceRowsData))]
    public void Gs1DataBarStacked_MatchesReferenceRows(string text, string expected)
        => Assert.Equal(expected, SymbolEncoder.Read(new Gs1DataBarStackedSymbology(), text, new BarcodeOptions(), SymbolEncoder.RowModuleStrings));

    /// <summary>
    /// Section 5.5.2.1.3 of the GS1 General Specifications: "Its dimensions are 50X wide by 13X high" with
    /// "a 1X high separator pattern between the two rows", an upper row of 5X and a lower row of 7X. No
    /// quiet zone is required.
    /// </summary>
    [Fact]
    public void Gs1DataBarStacked_IsFiftyModulesWideAndThirteenHigh()
        => SymbolEncoder.Encode(new Gs1DataBarStackedSymbology(), "(01)09521234543213", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(50F, symbol.WidthInModules);
            Assert.Equal(13F, symbol.HeightInModules);
            Assert.Equal([5F, 1F, 7F], SymbolEncoder.RowHeights(ref symbol));
            Assert.Equal(0F, symbol.FrameThickness);
            Assert.Equal(0F, symbol.LeadingQuietZone);
            Assert.Equal(0F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// A bar height in millimetres sets the lower row, and the upper row keeps its ratio of five to seven.
    /// </summary>
    [Fact]
    public void Gs1DataBarStacked_BarHeightOptionSetsTheLowerRow()
    {
        BarcodeOptions options = new() { BarHeight = 14F * DataBarEncoder.NominalXDimension };
        ApproximateFloatComparer comparer = new(1e-4F);
        SymbolEncoder.Encode(new Gs1DataBarStackedSymbology(), "(01)09521234543213", options, (ref symbol) =>
        {
            Assert.Equal(10F, symbol.Rows[0].Height, comparer);
            Assert.Equal(14F, symbol.Rows[2].Height, comparer);
            Assert.Equal(25F, symbol.HeightInModules, comparer);
        });
    }

    /// <summary>
    /// The human readable interpretation is the element string with the check digit, below the lower row.
    /// </summary>
    [Fact]
    public void Gs1DataBarStacked_TextPrintsBelowTheLowerRow()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        SymbolEncoder.Encode(new Gs1DataBarStackedSymbology(), "(01)0952123454321", options, static (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("(01)09521234543213", symbol.TextOf(placement).ToString());
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
            Assert.Equal(50F, placement.Right);
        });
    }
}
