// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarOmnidirectionalSymbology"/>. Each expected sequence is the
/// alternating bar and space module widths, starting with a zero width bar before the guard space, taken
/// from an independent reference implementation through its raw encoding API and confirmed by a second
/// one. A sequence carries the guard patterns, the four symbol characters and the two finder patterns that
/// encode the check value.
/// </summary>
public class Gs1DataBarOmnidirectionalSymbologyTests
{
    [Theory]
    [InlineData("(01)09521234543213", "01151115111238114311112234112211114725121211311")]
    [InlineData("(01)00012345678905", "01111112181274113211214132111124117332224131111")]
    [InlineData("(01)00034567890125", "01111112181256111232222121313113112834112213211")]
    [InlineData("(01)20012345678909", "01111331151274111221215125121112112833212123211")]
    [InlineData("(01)99999999999997", "01121131341382114121123113611111117513511111311")]
    [InlineData("(01)00000000000000", "01111112181382117211111111111127119131812111111")]
    public void Gs1DataBarOmnidirectional_MatchesReferenceWidths(string text, string expected)
        => Assert.Equal(expected, string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// The check digit is calculated when 13 digits are given, so both forms encode the same symbol.
    /// </summary>
    [Fact]
    public void Gs1DataBarOmnidirectional_CalculatesTheCheckDigit()
        => Assert.Equal(Encode("(01)00012345678905").RunWidths, Encode("(01)0001234567890").RunWidths);

    /// <summary>
    /// Section 5.5.2.1.1 of the GS1 General Specifications: "Its dimensions are 96X wide, starting with a 1X
    /// space and ending with a 1X bar". No quiet zone is required, and the nominal height is the 46X of
    /// Table 5-44.
    /// </summary>
    [Fact]
    public void Gs1DataBarOmnidirectional_IsNinetySixModulesWideAndFortySixHigh()
    {
        LinearBarcodeSymbol symbol = Encode("(01)09521234543213");
        Assert.Equal(96F, symbol.WidthInModules);
        Assert.Equal(0, symbol.RunWidths[0]);
        Assert.Equal(1, symbol.RunWidths[1]);
        Assert.Equal(1, symbol.RunWidths[^1]);
        Assert.Equal(0F, symbol.LeadingQuietZone);
        Assert.Equal(0F, symbol.TrailingQuietZone);
        Assert.Equal(46F, symbol.HeightInModules);
        Assert.Equal(0.33, new Gs1DataBarOmnidirectionalSymbology().NominalXDimension);
    }

    /// <summary>
    /// The human readable interpretation is the element string with the check digit, below the bars.
    /// </summary>
    [Fact]
    public void Gs1DataBarOmnidirectional_TextPrintsTheElementStringWithTheCheckDigit()
    {
        Assert.Empty(Encode("(01)0952123454321").Text);

        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Gs1DataBarOmnidirectionalSymbology().Encode("(01)0952123454321", options);
        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("(01)09521234543213", placement.Text);
        Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
        Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
    }

    /// <summary>
    /// The symbol carries Application Identifier (01) and its 14 digit GTIN, so anything else is refused,
    /// and a given check digit must be the one section 7.9 calculates.
    /// </summary>
    [Theory]
    [InlineData("0952123454321")]
    [InlineData("(02)09521234543213")]
    [InlineData("(01)095212345432")]
    [InlineData("(01)095212345432130")]
    [InlineData("(01)0952123454321A")]
    [InlineData("(01)09521234543214")]
    public void Gs1DataBarOmnidirectional_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Gs1DataBarOmnidirectionalSymbology().Encode(text, new BarcodeOptions());
}
