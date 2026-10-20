// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarStackedSymbology"/>. The tests check the dimensions of the GS1 General Specifications, the bar height option and the
/// human readable interpretation.
/// </summary>
public class Gs1DataBarStackedSymbologyTests
{
    /// <summary>
    /// Section 5.5.2.1.3 of the GS1 General Specifications: "Its dimensions are 50X wide by 13X high" with "a 1X high separator pattern between the two rows",
    /// an upper row of 5X and a lower row of 7X. No quiet zone is required.
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
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 14F * DataBarEncoder.NominalXDimension };
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
