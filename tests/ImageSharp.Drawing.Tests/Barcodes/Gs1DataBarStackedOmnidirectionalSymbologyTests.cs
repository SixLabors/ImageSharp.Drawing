// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarStackedOmnidirectionalSymbology"/>. Each expected row is the 50 modules of the upper row, the three separator rows
/// and the lower row, 1 for dark, taken from an independent reference implementation through its raw encoding API and confirmed by a second one.
/// </summary>
public class Gs1DataBarStackedOmnidirectionalSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="Gs1DataBarStackedOmnidirectional_MatchesReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, string> Gs1DataBarStackedOmnidirectional_MatchesReferenceRowsData { get; } = new()
    {
        {
            "(01)09521234543213",
            "01000001010000010100111000000001011110001010110010|" +
            "00001110101111101010000101010100100001110101000000|" +
            "00000101010101010101010101010101010101010101010000|" +
            "00000111101001101010000101010100111110110110100000|" +
            "10111000010110010101111000000011000001001001011101"
        },
        {
            "(01)00012345678905",
            "01010100100000000100111111100001011100101101111010|" +
            "00001011011111111010000000010100100011010010000000|" +
            "00000101010101010101010101010101010101010101010000|" +
            "00000110101001111010000000101000110011000010000000|" +
            "10111001010110000101111111000111001100111101110101"
        },
        {
            "(01)00034567890125",
            "01010100100000000100111110000001010011100110011010|" +
            "00001011011111111010000001010100101100011001100000|" +
            "00000101010101010101010101010101010101010101010000|" +
            "00001000100010111010010101010000111101001101110000|" +
            "10110111011101000101100000000111000010110010001101"
        },
        {
            "(01)20012345678909",
            "01010001110100000100111111100001010011011011111010|" +
            "00001110001011111010000000010100101100100100000000|" +
            "00000101010101010101010101010101010101010101010000|" +
            "00001111101101011010010101010000111001001001110000|" +
            "10110000010010100101100000000111000110110110001101"
        },
        {
            "(01)99999999999997",
            "01001011101110000100011111111001011110110100111010|" +
            "00000100010001111010100000000100100001001011000000|" +
            "00000101010101010101010101010101010101010101010000|" +
            "00001100000010101010000000101010111000001010100000|" +
            "10100011111101010101111111000001000111110101011101"
        },

        // The right finder pattern has value 3, so the lower separator carries a single dark module over the start of the three module finder bar.
        {
            "(01)00000000000000",
            "01010100100000000100011111111001011111110010101010|" +
            "00001011011111111010100000000100100000001101010000|" +
            "00000101010101010101010101010101010101010101010000|" +
            "00000101001111111010000000000100100000000100100000|" +
            "10101010110000000101111111110111011111111011010101"
        },
    };

    /// <summary>
    /// The module rows of each symbol, separator rows included, match the reference implementation.
    /// </summary>
    /// <param name="text">The element string to encode.</param>
    /// <param name="expected">The reference module rows from the top, separated by vertical bars.</param>
    [Theory]
    [MemberData(nameof(Gs1DataBarStackedOmnidirectional_MatchesReferenceRowsData))]
    public void Gs1DataBarStackedOmnidirectional_MatchesReferenceRows(string text, string expected)
        => Assert.Equal(
            expected,
            SymbolEncoder.Read(new Gs1DataBarStackedOmnidirectionalSymbology(), text, new BarcodeOptions(), SymbolEncoder.RowModuleStrings));

    /// <summary>
    /// Section 5.5.2.1.4 of the GS1 General Specifications gives a width of 50X and "a 3X high separator pattern between two rows". The nominal rows are 46X
    /// each, the 95X of Table 5-44. No quiet zone is required.
    /// </summary>
    [Fact]
    public void Gs1DataBarStackedOmnidirectional_IsFiftyModulesWideAndNinetyFiveHigh()
        => SymbolEncoder.Encode(new Gs1DataBarStackedOmnidirectionalSymbology(), "(01)00034567890125", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(50F, symbol.WidthInModules);
            Assert.Equal(95F, symbol.HeightInModules);
            Assert.Equal([46F, 1F, 1F, 1F, 46F], SymbolEncoder.RowHeights(ref symbol));
            Assert.Equal(0F, symbol.LeadingQuietZone);
            Assert.Equal(0F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// Section 5.5.2.1.4: "69X is the minimum height of the symbol", two rows of 33X, so the encoder rejects a bar height under 33X. At the nominal 0.33 mm
    /// module 33X is 10.89 mm.
    /// </summary>
    [Fact]
    public void Gs1DataBarStackedOmnidirectional_RejectsARowUnderThirtyThreeModules()
    {
        Gs1DataBarStackedOmnidirectionalSymbology symbology = new();
        BarcodeOptions minimum = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 10.89 };
        BarcodeOptions under = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 10.88 };

        Assert.Null(Record.Exception(() => SymbolEncoder.Encode(symbology, "(01)00034567890125", minimum)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SymbolEncoder.Encode(symbology, "(01)00034567890125", under));
    }

    /// <summary>
    /// A bar height in millimetres sets each row. Section 5.5.2.1.4 gives the 69X minimum as two rows of 33X.
    /// </summary>
    [Fact]
    public void Gs1DataBarStackedOmnidirectional_BarHeightOptionSetsEachRow()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 33F * DataBarEncoder.NominalXDimension };

        float height = SymbolEncoder.Read(
            new Gs1DataBarStackedOmnidirectionalSymbology(),
            "(01)00034567890125",
            options,
            static (ref symbol) => symbol.HeightInModules);

        Assert.Equal(69F, height, new ApproximateFloatComparer(1e-4F));
    }
}
