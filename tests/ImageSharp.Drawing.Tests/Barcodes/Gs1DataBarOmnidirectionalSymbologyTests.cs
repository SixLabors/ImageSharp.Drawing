// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarOmnidirectionalSymbology"/>. Each expected sequence is the alternating bar and space module widths, starting with a
/// zero width bar before the guard space, taken from an independent reference implementation through its raw encoding API and confirmed by a second one. A
/// sequence carries the guard patterns, the four symbol characters and the two finder patterns that encode the check value.
/// </summary>
public class Gs1DataBarOmnidirectionalSymbologyTests
{
    /// <summary>
    /// The bar and space widths match the reference implementation.
    /// </summary>
    /// <param name="text">The element string to encode.</param>
    /// <param name="expected">The reference run string.</param>
    [Theory]
    [InlineData("(01)09521234543213", "01151115111238114311112234112211114725121211311")]
    [InlineData("(01)00012345678905", "01111112181274113211214132111124117332224131111")]
    [InlineData("(01)00034567890125", "01111112181256111232222121313113112834112213211")]
    [InlineData("(01)20012345678909", "01111331151274111221215125121112112833212123211")]
    [InlineData("(01)99999999999997", "01121131341382114121123113611111117513511111311")]
    [InlineData("(01)00000000000000", "01111112181382117211111111111127119131812111111")]
    public void Gs1DataBarOmnidirectional_MatchesReferenceWidths(string text, string expected)
        => Assert.Equal(expected, string.Concat(Runs(text)));

    /// <summary>
    /// The encoder calculates the check digit of 13 digits, so both forms encode the same symbol.
    /// </summary>
    [Fact]
    public void Gs1DataBarOmnidirectional_CalculatesTheCheckDigit()
        => Assert.Equal(Runs("(01)00012345678905"), Runs("(01)0001234567890"));

    /// <summary>
    /// Section 5.5.2.1.1 of the GS1 General Specifications: "33X is the minimum height of the symbol", so the encoder rejects a bar height under 33X. At the
    /// nominal 0.33 mm module 33X is 10.89 mm.
    /// </summary>
    [Fact]
    public void Gs1DataBarOmnidirectional_RejectsABarHeightUnderThirtyThreeModules()
    {
        Gs1DataBarOmnidirectionalSymbology symbology = new();
        BarcodeOptions minimum = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 10.89 };
        BarcodeOptions under = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 10.88 };

        Assert.Null(Record.Exception(() => SymbolEncoder.Encode(symbology, "(01)09521234543213", minimum)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SymbolEncoder.Encode(symbology, "(01)09521234543213", under));
    }

    /// <summary>
    /// Section 5.5.2.1.1 of the GS1 General Specifications: "Its dimensions are 96X wide, starting with a 1X space and ending with a 1X bar". No quiet zone is
    /// required, and the nominal height is the 46X of Table 5-44. The nominal X dimension of 0.33 mm is 33 pixels at 100 pixels a millimetre.
    /// </summary>
    [Fact]
    public void Gs1DataBarOmnidirectional_IsNinetySixModulesWideAndFortySixHigh()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi };

        SymbolEncoder.Encode(new Gs1DataBarOmnidirectionalSymbology(), "(01)09521234543213", options, static (ref symbol) =>
        {
            Assert.Equal(96F, symbol.WidthInModules);
            Assert.Equal(0, symbol.Runs[0]);
            Assert.Equal(1, symbol.Runs[1]);
            Assert.Equal(1, symbol.Runs[^1]);
            Assert.Equal(0F, symbol.LeadingQuietZone);
            Assert.Equal(0F, symbol.TrailingQuietZone);
            Assert.Equal(46F, symbol.HeightInModules);
            Assert.Equal(0.33, symbol.XDimension);
        });
    }

    /// <summary>
    /// The human readable interpretation is the element string with the check digit, below the bars. Without a font there is no text.
    /// </summary>
    [Fact]
    public void Gs1DataBarOmnidirectional_TextPrintsTheElementStringWithTheCheckDigit()
    {
        SymbolEncoder.Encode(
            new Gs1DataBarOmnidirectionalSymbology(),
            "(01)0952123454321",
            new BarcodeOptions(),
            static (ref symbol) => Assert.True(symbol.Text.IsEmpty));

        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new Gs1DataBarOmnidirectionalSymbology(), "(01)0952123454321", options, static (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("(01)09521234543213", symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
        });
    }

    /// <summary>
    /// The symbol carries Application Identifier (01) and its 14 digit GTIN, so the encoder rejects any other text. A given check digit must be the one that
    /// section 7.9 calculates.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("0952123454321")]
    [InlineData("(02)09521234543213")]
    [InlineData("(01)095212345432")]
    [InlineData("(01)095212345432130")]
    [InlineData("(01)0952123454321A")]
    [InlineData("(01)09521234543214")]
    public void Gs1DataBarOmnidirectional_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Runs(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static int[] Runs(string text)
        => SymbolEncoder.Read(new Gs1DataBarOmnidirectionalSymbology(), text, new BarcodeOptions(), static (ref symbol) => symbol.Runs.ToArray());
}
