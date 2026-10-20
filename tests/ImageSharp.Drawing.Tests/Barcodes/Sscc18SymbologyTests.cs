// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Sscc18Symbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, taken from an
/// independent reference implementation through its raw encoding API. An SSCC-18 is a GS1-128 symbol carrying the Serial Shipping Container Code of GS1
/// Application Identifier (00), so the runs carry the double character start pattern, the identifier, the eighteen digits, the check character and the stop
/// character.
/// </summary>
public class Sscc18SymbologyTests
{
    /// <summary>
    /// The reference runs of the SSCC 106141411234567897.
    /// </summary>
    private const string Expected = "2112324111312122222213122214112313112313111122321311233311212411124111131311232331112";

    /// <summary>
    /// The check digit is optional, and the encoder ignores the spaces a caller groups the number with, so all three forms encode to one symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("(00)10614141123456789")]
    [InlineData("(00)106141411234567897")]
    [InlineData("(00) 1 0614141 1234567 89")]
    public void MatchesReferenceRuns(string text)
        => Assert.Equal(Expected, Encode(text));

    /// <summary>
    /// The encoder verifies a supplied check digit against section 7.9 of the GS1 General Specifications. A wrong check digit throws.
    /// </summary>
    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("(00)106141411234567890"));

    /// <summary>
    /// Section 3.3.1 gives Application Identifier (00) an 18 digit Serial Shipping Container Code, and the input uses the element string syntax. The
    /// symbology rejects input without the identifier, another identifier, the wrong number of digits and a non-digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("106141411234567897")]
    [InlineData("(01)106141411234567897")]
    [InlineData("(00)1061414112345678")]
    [InlineData("(00)1061414112345678977")]
    [InlineData("(00)1061414112345678A")]
    public void RejectsMalformedInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 4.14 rule 2.a.v of the GS1 General Specifications: "Spaces may be used in the HRI itself to ease manual data input". The printed number keeps
    /// the grouping of the input, and a computed check digit follows a space only when the input groups its digits with spaces.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("(00)10614141123456789", "(00)106141411234567897")]
    [InlineData("(00) 1 0614141 123456789", "(00) 1 0614141 123456789 7")]
    public void PrintsAComputedCheckDigitInTheGroupingOfTheInput(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Sscc18Symbology(), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Sscc18Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
