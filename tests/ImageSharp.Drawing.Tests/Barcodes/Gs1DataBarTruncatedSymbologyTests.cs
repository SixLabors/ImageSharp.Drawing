// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarTruncatedSymbology"/>. The expected sequences come from an
/// independent reference implementation through its raw encoding API, and are the same as those of GS1
/// DataBar Omnidirectional, because the two differ in height alone.
/// </summary>
public class Gs1DataBarTruncatedSymbologyTests
{
    [Theory]
    [InlineData("(01)09521234543213", "01151115111238114311112234112211114725121211311")]
    [InlineData("(01)00012345678905", "01111112181274113211214132111124117332224131111")]
    public void Gs1DataBarTruncated_MatchesReferenceWidths(string text, string expected)
        => Assert.Equal(expected, string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// Section 5.5.2.1.2 of the GS1 General Specifications: "Its dimensions are 96X wide by 13X high".
    /// </summary>
    [Fact]
    public void Gs1DataBarTruncated_IsNinetySixModulesWideAndThirteenHigh()
    {
        LinearBarcodeSymbol symbol = Encode("(01)00012345678905");
        Assert.Equal(96F, symbol.WidthInModules);
        Assert.Equal(13F, symbol.HeightInModules);
        Assert.Equal(0F, symbol.LeadingQuietZone);
        Assert.Equal(0F, symbol.TrailingQuietZone);
    }

    /// <summary>
    /// A bar height in millimetres sets the height of the bars.
    /// </summary>
    [Fact]
    public void Gs1DataBarTruncated_BarHeightOptionSetsTheHeight()
    {
        BarcodeOptions options = new() { BarHeight = 20F * DataBarEncoder.NominalXDimension };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Gs1DataBarTruncatedSymbology().Encode("(01)00012345678905", options);
        Assert.Equal(20F, symbol.HeightInModules, new ApproximateFloatComparer(1e-4F));
    }

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Gs1DataBarTruncatedSymbology().Encode(text, new BarcodeOptions());
}
