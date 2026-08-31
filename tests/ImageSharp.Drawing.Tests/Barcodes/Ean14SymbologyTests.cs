// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Ean14Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from an independent reference implementation through
/// its raw encoding API. An EAN-14 is a GS1-128 symbol carrying the Global Trade Item Number of GS1
/// Application Identifier (01), so the runs carry the double character start pattern, the identifier, the fourteen
/// digits, the check character and the stop character.
/// </summary>
public class Ean14SymbologyTests
{
    private const string Expected =
        "2112324111312221222212132133111122321311233111232321211221321232212331112";

    /// <summary>
    /// The check digit is optional, and the spaces a caller groups the number with are ignored, so all
    /// three forms encode to one symbol.
    /// </summary>
    [Theory]
    [InlineData("(01)0952123454321")]
    [InlineData("(01)09521234543213")]
    [InlineData("(01) 09521234543213")]
    public void MatchesReferenceRuns(string text)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(Expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// A supplied check digit is verified against section 7.9 of the GS1 General Specifications.
    /// </summary>
    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("(01)09521234543214"));

    /// <summary>
    /// Section 3.3.2 gives Application Identifier (01) a 14 digit Global Trade Item Number, and the input
    /// carries the element string syntax, so anything else is rejected.
    /// </summary>
    [Theory]
    [InlineData("09521234543213")]
    [InlineData("(02)09521234543213")]
    [InlineData("(01)095212345432")]
    [InlineData("(01)095212345432134")]
    [InlineData("(01)0952123454321A")]
    public void RejectsMalformedInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// A computed check digit joins the printed number after a space, so the caller can see it was added.
    /// </summary>
    [Fact]
    public void PrintsAComputedCheckDigitAfterASpace()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Ean14Symbology().Encode("(01)0952123454321", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("(01)0952123454321 3", placement.Text);
    }

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Ean14Symbology().Encode(text, new BarcodeOptions());
}
