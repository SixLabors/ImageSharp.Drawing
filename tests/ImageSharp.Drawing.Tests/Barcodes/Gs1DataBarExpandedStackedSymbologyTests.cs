// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarExpandedStackedSymbology"/>. The tests check the dimensions of Figure 5-48 of the GS1 General Specifications, the
/// bar height option, the human readable interpretation and the segment counts that the symbology rejects.
/// </summary>
public class Gs1DataBarExpandedStackedSymbologyTests
{
    /// <summary>
    /// Section 5.5.2.3.2 of the GS1 General Specifications gives Figure 5-48, which encodes (01)90614141000015(3202)000150 in two rows of four segments, as
    /// 25.91 millimetres wide by 18.03 millimetres high at an X dimension of 0.254 millimetre: 102 modules by 71, two rows of 34X and a separator of 3X.
    /// </summary>
    [Fact]
    public void Gs1DataBarExpandedStacked_MatchesTheDimensionsOfFigure548()
        => SymbolEncoder.Encode(new Gs1DataBarExpandedStackedSymbology(), "(01)90614141000015(3202)000150", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(102F, symbol.WidthInModules);
            Assert.Equal(71F, symbol.HeightInModules);
            Assert.Equal([34F, 1F, 1F, 1F, 34F], SymbolEncoder.RowHeights(ref symbol));
            Assert.Equal(0F, symbol.LeadingQuietZone);
            Assert.Equal(0F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// A bar height in millimetres sets each row.
    /// </summary>
    [Fact]
    public void Gs1DataBarExpandedStacked_BarHeightOptionSetsEachRow()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 40F * DataBarExpandedEncoder.NominalXDimension };

        float height = SymbolEncoder.Read(
            new Gs1DataBarExpandedStackedSymbology(),
            "(01)90614141000015(3202)000150",
            options,
            static (ref symbol) => symbol.HeightInModules);

        Assert.Equal(83F, height, new ApproximateFloatComparer(1e-4F));
    }

    /// <summary>
    /// The human readable interpretation is the element strings as given, below the last row.
    /// </summary>
    [Fact]
    public void Gs1DataBarExpandedStacked_TextPrintsBelowTheLastRow()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new Gs1DataBarExpandedStackedSymbology(), "(01)90614141000015(3202)000150", options, static (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("(01)90614141000015(3202)000150", symbol.TextOf(placement).ToString());
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
        });
    }

    /// <summary>
    /// Section 5.5.2.3.2: "It can be printed in widths of 2 to 20 segments", and a row holds whole segment pairs.
    /// </summary>
    /// <param name="segments">The number of segments in a row.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(22)]
    public void Gs1DataBarExpandedStacked_RejectsInvalidSegments(int segments)
        => Assert.ThrowsAny<ArgumentException>(() => new Gs1DataBarExpandedStackedSymbology(segments));
}
