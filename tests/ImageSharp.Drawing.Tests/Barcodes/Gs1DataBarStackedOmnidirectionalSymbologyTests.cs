// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarStackedOmnidirectionalSymbology"/>. The tests check the dimensions and the minimum row height of the GS1 General
/// Specifications and the bar height option.
/// </summary>
public class Gs1DataBarStackedOmnidirectionalSymbologyTests
{
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
