// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="FacingIdentificationMarkSymbology"/>. Each expected run string is the
/// alternating bar and space widths in modules of 1/32 inch, starting with a bar. The values for A, C,
/// D and E are the reference implementation's raw encoding API output divided by its 2.25 point module.
/// Its pattern B has one space of 6.25 points where the nine bit pattern 101101101 and the 17/32 inch
/// width give 6.75, so the value for B comes from the pattern.
/// </summary>
public class FacingIdentificationMarkSymbologyTests
{
    [Theory]
    [InlineData("A", "1,1,1,5,1,5,1,1,1")]
    [InlineData("B", "1,3,1,1,1,3,1,1,1,3,1")]
    [InlineData("C", "1,1,1,3,1,3,1,3,1,1,1")]
    [InlineData("D", "1,1,1,1,1,3,1,3,1,1,1,1,1")]
    [InlineData("E", "1,3,1,7,1,3,1")]
    public void MatchesThePatterns(string text, string reference)
        => Assert.Equal(reference, string.Join(",", Encode(text).RunWidths));

    /// <summary>
    /// Every pattern is 17/32 inch wide, 17 modules, and its bars 5/8 inch high, 20 modules, with no
    /// quiet zone and no printed line.
    /// </summary>
    /// <param name="text">The pattern letter.</param>
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    [InlineData("E")]
    public void SpansSeventeenModules(string text)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new FacingIdentificationMarkSymbology().Encode(text, options);

        Assert.Equal(17F, symbol.WidthInModules);
        Assert.All(symbol.BarHeights, height => Assert.Equal(20F, height));
        Assert.Equal(0, symbol.LeadingQuietZone);
        Assert.Equal(0, symbol.TrailingQuietZone);
        Assert.Empty(symbol.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F")]
    [InlineData("a")]
    [InlineData("AB")]
    [InlineData("Ａ")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new FacingIdentificationMarkSymbology().Encode(text, new BarcodeOptions());
}
