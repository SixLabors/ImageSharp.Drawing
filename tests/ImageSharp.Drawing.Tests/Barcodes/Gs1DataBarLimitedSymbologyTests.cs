// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarLimitedSymbology"/>. The tests check the dimensions and the minimum height of the GS1 General Specifications, the
/// human readable interpretation and the input that the symbology rejects.
/// </summary>
public class Gs1DataBarLimitedSymbologyTests
{
    /// <summary>
    /// Section 5.5.2.2 of the GS1 General Specifications: "The minimum height SHALL be 10X", so a bar height under 10X draws at 10X. At the nominal 0.33 mm
    /// module 10X is 3.3 mm.
    /// </summary>
    [Fact]
    public void Gs1DataBarLimited_DrawsABarHeightUnderTenModulesAtTenModules()
    {
        BarcodeOptions under = new() { Dpi = SymbolEncoder.MillimetreDpi, BarHeight = 3.29 };

        float height = SymbolEncoder.Read(new Gs1DataBarLimitedSymbology(), "(01)15012345678907", under, static (ref symbol) => symbol.HeightInModules);

        Assert.Equal(10F, height);
    }

    /// <summary>
    /// Section 5.5.2.2 of the GS1 General Specifications: "79X wide, starting with a 1X space and ending with a 5X space, by 10X high", 47 elements, and no
    /// quiet zone.
    /// </summary>
    [Fact]
    public void Gs1DataBarLimited_IsSeventyNineModulesWideAndTenHigh()
        => Encode("(01)15012345678907", static (ref symbol) =>
        {
            Assert.Equal(79F, symbol.WidthInModules);
            Assert.Equal(10F, symbol.HeightInModules);
            Assert.Equal(47, symbol.Runs.Length - 2);
            Assert.Equal(1, symbol.Runs[1]);
            Assert.Equal(5, symbol.Runs[^2]);
            Assert.Equal(0F, symbol.LeadingQuietZone);
            Assert.Equal(0F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// The human readable interpretation is the element string with the check digit, below the bars.
    /// </summary>
    [Fact]
    public void Gs1DataBarLimited_TextPrintsTheElementStringWithTheCheckDigit()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new Gs1DataBarLimitedSymbology(), "(01)1501234567890", options, static (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("(01)15012345678907", symbol.TextOf(placement).ToString());
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
        });
    }

    /// <summary>
    /// Section 5.5.2.2: a GTIN-14 must have the indicator digit 1, so the encoder rejects a first digit above 1. The encoder calculates the check digit of the
    /// 13 digit inputs, so only the first digit is wrong in them. The encoder also rejects a wrong check digit and text without the element string syntax.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("(01)2501234567890")]
    [InlineData("(01)9501234567890")]
    [InlineData("(01)15012345678908")]
    [InlineData("15012345678907")]
    public void Gs1DataBarLimited_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Gs1DataBarLimitedSymbology(), text, new BarcodeOptions()));

    /// <summary>
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new Gs1DataBarLimitedSymbology(), text, new BarcodeOptions(), check);
}
