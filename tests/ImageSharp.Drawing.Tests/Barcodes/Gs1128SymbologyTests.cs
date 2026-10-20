// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1128Symbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, taken from an
/// independent reference implementation through its raw encoding API. The runs carry the double character start pattern of section 5.4.2, the code set switches
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
    [InlineData("(91)ABC123", "2112324111314121211141311113231311231313211232212232112211322141212331112")]

    // A variable length element string followed by another, so a separator closes the first.
    [InlineData(
        "(10)ABC123(01)09521234543213",
        "2112324111312213121141311113231311231313211232211131413121314111312221222212132133111122321311233111232321211221322212132331112")]

    // The eighteen digit SSCC of Application Identifier 00, twenty characters in all.
    [InlineData("(00)395123451234567895", "2112324111312122222113132131133121311131231122321311233311212411121141131143112331112")]
    public void MatchesReferenceRuns(string text, string expected)
        => Assert.Equal(expected, Encode(text));

    /// <summary>
    /// Section 4.14 rule 2c requires parentheses around the Application Identifiers in the human readable interpretation, and rule 2b keeps the separators out
    /// of it.
    /// </summary>
    [Fact]
    public void PrintsElementStringsWithParentheses()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Gs1128Symbology(), "(10)ABC123(01)09521234543213", options, SymbolEncoder.Texts);

        string text = Assert.Single(texts);
        Assert.Equal("(10)ABC123(01)09521234543213", text);
        Assert.DoesNotContain(Gs1Data.Separator.ToString(), text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Section 4.14 rule 3b: when Application Identifier (8200) appears, its URL does not appear in the human readable interpretation. Table 5-52 allows
    /// GS1-128 from an X dimension of 0.170 mm, and 0.25 mm keeps the symbol within the 165.10 mm of section 5.4.4.3.
    /// </summary>
    [Fact]
    public void LeavesTheUrlOfApplicationIdentifier8200OutOfTheText()
    {
        BarcodeOptions options = new() { XDimension = 0.25, Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Gs1128Symbology(), "(01)09520123456788(8200)http://www.abc.net", options, SymbolEncoder.Texts);
        Assert.Equal("(01)09520123456788", Assert.Single(texts));
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
    /// Section 5.4.4.3: "The number of encoded data characters SHALL NOT exceed 48."
    /// </summary>
    [Fact]
    public void RejectsOverlongData()
        => Assert.ThrowsAny<ArgumentException>(() => Encode("(10)" + new string('A', 48)));

    /// <summary>
    /// Section 5.4.4.3: "The length, including Quiet Zones, cannot exceed 165.10 millimetres (6.500 inches)." The symbol of (01)09521234543213 is 134 modules:
    /// the start character, FNC1, eight Code Set C characters, the check character and the 13 module stop pattern. With its two 10 module quiet zones it is 154
    /// modules, 156.464 mm at the largest X dimension of the symbol specification tables, 1.016 mm. (01)09521234543213(10)12 adds two Code Set C characters,
    /// so it is 176 modules, 178.816 mm.
    /// </summary>
    [Fact]
    public void RejectsASymbolLongerThanTheMaximumLength()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 1.016 };

        Assert.Null(Record.Exception(() => SymbolEncoder.Encode(new Gs1128Symbology(), "(01)09521234543213", options)));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Gs1128Symbology(), "(01)09521234543213(10)12", options));
    }

    /// <summary>
    /// Footnote (****) of Table 5-47: "For GS1-128 and ITF-14 symbols the minimum symbol height for General distribution scanning is always 31.75 millimetres
    /// (1.250 inch)", so the bars are 31.75 / 0.25 modules high at an X dimension of 0.25 mm.
    /// </summary>
    [Fact]
    public void DrawsBarsOfTheGeneralDistributionHeightAtAnyXDimension()
        => SymbolEncoder.Encode(
            new Gs1128Symbology(),
            "(01)09521234543213",
            new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi, XDimension = 0.25 },
            static (ref symbol) => Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal((float)(31.75 / 0.25), height, 1e-3F)));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Gs1128Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
