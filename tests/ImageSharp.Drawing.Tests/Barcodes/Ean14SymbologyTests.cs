// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Ean14Symbology"/>. An EAN-14 is a GS1-128 symbol carrying the Global Trade Item Number of GS1 Application Identifier (01).
/// </summary>
public class Ean14SymbologyTests
{
    /// <summary>
    /// A supplied check digit is verified against section 7.9 of the GS1 General Specifications.
    /// </summary>
    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("(01)09521234543214"));

    /// <summary>
    /// Section 3.3.2 gives Application Identifier (01) a 14 digit Global Trade Item Number, and the input carries the element string syntax. The encoder
    /// rejects text without the (01) identifier, a different identifier, the wrong number of digits and a letter.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("09521234543213")]
    [InlineData("(02)09521234543213")]
    [InlineData("(01)095212345432")]
    [InlineData("(01)095212345432134")]
    [InlineData("(01)0952123454321A")]
    public void RejectsMalformedInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 4.14 rule 2.a.v of the GS1 General Specifications: "Spaces may be used in the HRI itself to ease manual data input". The printed number keeps
    /// the grouping of the input, and a computed check digit follows a space only when the input groups its digits with spaces.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("(01)0952123454321", "(01)09521234543213")]
    [InlineData("(01) 0952123454321", "(01) 0952123454321 3")]
    public void PrintsAComputedCheckDigitInTheGroupingOfTheInput(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Ean14Symbology(), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Ean14Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
