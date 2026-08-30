// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1128Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from the BWIPP reference implementation through its raw
/// encoding API. The runs carry the double character start pattern of section 5.4.2, the code set switches
/// the encoder chose, any separator, the check character and the stop character.
/// </summary>
public class Gs1128SymbologyTests
{
    [Theory]

    // One element string with a predefined length from Table 7-6, so no separator is needed.
    [InlineData("(01)09521234543213", "2112324111312221222212132133111122321311233111232321211221321232212331112")]

    // Two predefined length element strings run together with no separator between them.
    [InlineData(
        "(01)09521234543213(3103)000123",
        "2112324111312221222212132133111122321311233111232321211221322123211212232122222221223121314131112331112")]

    // A variable length element string, which needs no separator because it is the last one.
    [InlineData("(10)ABC123", "2112324111312213121141311113231311231313211232212232112211322123212331112")]

    // A variable length element string followed by another, so a separator closes the first.
    [InlineData(
        "(10)ABC123(01)09521234543213",
        "2112324111312213121141311113231311231313211232211131413121314111312221222212132133111122321311233111232321211221322212132331112")]

    // The eighteen digit SSCC of Application Identifier 00, twenty characters in all.
    [InlineData("(00)395123451234567895", "2112324111312122222113132131133121311131231122321311233311212411121141131143112331112")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Section 4.14 rule 2c requires parentheses around the Application Identifiers in the human readable
    /// interpretation, and rule 2b keeps the separators out of it.
    /// </summary>
    [Fact]
    public void PrintsElementStringsWithParentheses()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Gs1128Symbology().Encode("(10)ABC123(01)09521234543213", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("(10)ABC123(01)09521234543213", placement.Text);
        Assert.DoesNotContain(Code128Encoder.Separator.ToString(), placement.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Table 7-6 fixes the total length of the element strings it lists, so a short one is rejected.
    /// </summary>
    [Fact]
    public void RejectsWrongPredefinedLength()
        => Assert.Throws<ArgumentException>(() => Encode("(01)0952123454321"));

    /// <summary>
    /// The input carries the element string syntax of section 4.14, so anything else is rejected.
    /// </summary>
    [Theory]
    [InlineData("0109521234543213")]
    [InlineData("(01")]
    [InlineData("(1)1")]
    [InlineData("(0A)1")]
    [InlineData("(10)")]
    public void RejectsMalformedInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 5.4.1 allows 48 data characters in one symbol.
    /// </summary>
    [Fact]
    public void RejectsOverlongData()
        => Assert.ThrowsAny<ArgumentException>(() => Encode("(10)" + new string('A', 48)));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Gs1128Symbology().Encode(text, new BarcodeOptions());
}
