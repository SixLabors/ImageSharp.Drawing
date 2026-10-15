// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Sscc18Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from an independent reference implementation through
/// its raw encoding API. An SSCC-18 is a GS1-128 symbol carrying the Serial Shipping Container Code of GS1
/// Application Identifier (00), so the runs carry the double character start pattern, the identifier, the
/// eighteen digits, the check character and the stop character.
/// </summary>
public class Sscc18SymbologyTests
{
    private const string Expected =
        "2112324111312122222213122214112313112313111122321311233311212411124111131311232331112";

    /// <summary>
    /// The check digit is optional, and the spaces a caller groups the number with are ignored, so all
    /// three forms encode to one symbol.
    /// </summary>
    [Theory]
    [InlineData("(00)10614141123456789")]
    [InlineData("(00)106141411234567897")]
    [InlineData("(00) 1 0614141 1234567 89")]
    public void MatchesReferenceRuns(string text)
        => Assert.Equal(Expected, Encode(text));

    /// <summary>
    /// A supplied check digit is verified against section 7.9 of the GS1 General Specifications.
    /// </summary>
    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("(00)106141411234567890"));

    /// <summary>
    /// Section 3.3.1 gives Application Identifier (00) an 18 digit Serial Shipping Container Code, and the
    /// input carries the element string syntax, so anything else is rejected.
    /// </summary>
    [Theory]
    [InlineData("106141411234567897")]
    [InlineData("(01)106141411234567897")]
    [InlineData("(00)1061414112345678")]
    [InlineData("(00)1061414112345678977")]
    [InlineData("(00)1061414112345678A")]
    public void RejectsMalformedInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// A computed check digit joins the printed number after a space, so the caller can see it was added.
    /// </summary>
    [Fact]
    public void PrintsAComputedCheckDigitAfterASpace()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Sscc18Symbology(), "(00)10614141123456789", options, SymbolEncoder.Texts);

        Assert.Equal("(00)10614141123456789 7", Assert.Single(texts));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Sscc18Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
