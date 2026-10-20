// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarTruncatedSymbology"/>. The tests check the dimensions of the GS1 General Specifications and the bar height option.
/// </summary>
public class Gs1DataBarTruncatedSymbologyTests
{
    /// <summary>
    /// Section 5.5.2.1.2 of the GS1 General Specifications: "Its dimensions are 96X wide by 13X high".
    /// </summary>
    [Fact]
    public void Gs1DataBarTruncated_IsNinetySixModulesWideAndThirteenHigh()
        => SymbolEncoder.Encode(new Gs1DataBarTruncatedSymbology(), "(01)00012345678905", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(96F, symbol.WidthInModules);
            Assert.Equal(13F, symbol.HeightInModules);
            Assert.Equal(0F, symbol.LeadingQuietZone);
            Assert.Equal(0F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// A bar height in millimetres sets the height of the bars.
    /// </summary>
    [Fact]
    public void Gs1DataBarTruncated_BarHeightOptionSetsTheHeight()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 20F * DataBarEncoder.NominalXDimension };
        float height = SymbolEncoder.Read(new Gs1DataBarTruncatedSymbology(), "(01)00012345678905", options, static (ref symbol) => symbol.HeightInModules);

        Assert.Equal(20F, height, new ApproximateFloatComparer(1e-4F));
    }
}
