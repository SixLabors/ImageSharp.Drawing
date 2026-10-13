// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarLimitedSymbology"/>. Each expected sequence is the alternating bar
/// and space module widths, with a zero width bar before the guard space and after the closing five module
/// space, taken from an independent reference implementation through its raw encoding API and confirmed by a
/// second one. A sequence carries the guard patterns, the two symbol characters and the check character
/// between them.
/// </summary>
public class Gs1DataBarLimitedSymbologyTests
{
    [Theory]
    [InlineData("(01)09521234543213", "0111141221111341311111121132111122221145112111150")]
    [InlineData("(01)15012345678907", "0113222321211112311211112112211212112321322221150")]
    [InlineData("(01)00312345678906", "0111111512131411321111212112111125111212112511150")]
    [InlineData("(01)00000000000000", "0111111111111616311131111212111111111111161631150")]
    [InlineData("(01)1999999999999", "0112422121112151111111211123111114113321114211150")]
    [InlineData("(01)0001234567890", "0111211122111516111111212113111111322231151121150")]
    public void Gs1DataBarLimited_MatchesReferenceWidths(string text, string expected)
        => Assert.Equal(expected, string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// Section 5.5.2.2 of the GS1 General Specifications: "79X wide, starting with a 1X space and ending with
    /// a 5X space, by 10X high", 47 elements, and no quiet zone.
    /// </summary>
    [Fact]
    public void Gs1DataBarLimited_IsSeventyNineModulesWideAndTenHigh()
    {
        LinearBarcodeSymbol symbol = Encode("(01)15012345678907");
        Assert.Equal(79F, symbol.WidthInModules);
        Assert.Equal(10F, symbol.HeightInModules);
        Assert.Equal(47, symbol.RunWidths.Length - 2);
        Assert.Equal(1, symbol.RunWidths[1]);
        Assert.Equal(5, symbol.RunWidths[^2]);
        Assert.Equal(0F, symbol.LeadingQuietZone);
        Assert.Equal(0F, symbol.TrailingQuietZone);
    }

    /// <summary>
    /// The human readable interpretation is the element string with the check digit, below the bars.
    /// </summary>
    [Fact]
    public void Gs1DataBarLimited_TextPrintsTheElementStringWithTheCheckDigit()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Gs1DataBarLimitedSymbology().Encode("(01)1501234567890", options);
        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("(01)15012345678907", placement.Text);
        Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
    }

    /// <summary>
    /// Section 5.5.2.2: a GTIN-14 must have the indicator digit 1, so a first digit above 1 is refused. The
    /// check digit is calculated for the 13 digit inputs, so only the first digit is wrong in them.
    /// </summary>
    [Theory]
    [InlineData("(01)2501234567890")]
    [InlineData("(01)9501234567890")]
    [InlineData("(01)15012345678908")]
    [InlineData("15012345678907")]
    public void Gs1DataBarLimited_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Gs1DataBarLimitedSymbology().Encode(text, new BarcodeOptions());
}
