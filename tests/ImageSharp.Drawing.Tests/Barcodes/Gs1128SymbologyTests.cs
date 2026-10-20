// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1128Symbology"/>.
/// </summary>
public class Gs1128SymbologyTests
{
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
    /// Table 7-6 fixes the total length of the element strings it lists, so the encoder rejects a short one.
    /// </summary>
    [Fact]
    public void RejectsWrongPredefinedLength()
        => Assert.Throws<ArgumentException>(() => Encode("(01)0952123454321"));

    /// <summary>
    /// The input carries the element string syntax of section 4.14. The encoder rejects text without parentheses, an open Application Identifier, an
    /// identifier that is too short or not digits, and an identifier without data.
    /// </summary>
    /// <param name="text">The text to encode.</param>
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
    /// The note of Table 5-50 of the GS1 General Specifications lets a small item truncate the bars of a GS1-128 symbol, but "In no case SHALL the bar height
    /// be less than 5.08 millimetres". A bar height of 5.08 mm encodes, and 5.07 mm throws.
    /// </summary>
    [Fact]
    public void RejectsABarHeightUnderTheTruncationMinimum()
    {
        Assert.Null(Record.Exception(() => SymbolEncoder.Encode(new Gs1128Symbology(), "(01)09521234543213", new BarcodeOptions { BarHeight = 5.08 })));
        Assert.Throws<ArgumentOutOfRangeException>(() => SymbolEncoder.Encode(new Gs1128Symbology(), "(01)09521234543213", new BarcodeOptions { BarHeight = 5.07 }));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Gs1128Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
