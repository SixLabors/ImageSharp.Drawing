// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Pzn8Symbology"/> and <see cref="Pzn7Symbology"/>. Each expected run string
/// is the alternating bar and space module widths, starting with a bar, taken from an independent reference
/// implementation through its raw encoding API, less the gap it emits after the stop character. The check
/// digit and the printed line come from the IFA technical information documents on PZN coding.
/// </summary>
public class PznSymbologyTests
{
    [Theory]
    [InlineData("2758089", "1311313111131111313111331111311113113131311331111131131131111113313111311311311111331131111133113111131131311")]
    [InlineData("27580899", "1311313111131111313111331111311113113131311331111131131131111113313111311311311111331131111133113111131131311")]
    [InlineData("03752864", "1311313111131111313111133131113133111111111311313131133111111133111131311311311111333111111113311131131131311")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(new Pzn8Symbology(), text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    [Theory]
    [InlineData("123456", "131131311113111131313113111131113311113131331111111113311131311331111111333111111133111131131131311")]
    [InlineData("1234562", "131131311113111131313113111131113311113131331111111113311131311331111111333111111133111131131131311")]
    public void MatchesReferenceRunsForTheSevenDigitForm(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(new Pzn7Symbology(), text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// The worked example in the IFA check digit document: the digits 2758089 weighted 1 to 7 give the
    /// products 2, 14, 15, 32, 0, 48 and 63, which sum to 174; 174 divided by 11 leaves 9, so the complete
    /// PZN is 27580899.
    /// </summary>
    [Fact]
    public void MatchesTheWorkedCheckDigitExample()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Pzn8Symbology().Encode("2758089", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("PZN - 27580899", placement.Text);
    }

    /// <summary>
    /// The PZN of the worked PPN example in the same document is 03752864, whose check digit is therefore
    /// the trailing 4.
    /// </summary>
    [Fact]
    public void AcceptsTheCheckDigitOfTheWorkedPpnExample()
        => Assert.Equal(
            string.Concat(Encode(new Pzn8Symbology(), "0375286").RunWidths),
            string.Concat(Encode(new Pzn8Symbology(), "03752864").RunWidths));

    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode(new Pzn8Symbology(), "27580891"));

    /// <summary>
    /// IFA: "If the remainder is the number 10, this digit sequence is not used as PZN." The digits
    /// 0000003 weight the trailing 3 by seven, giving 21, and 21 divided by 11 leaves 10.
    /// </summary>
    [Fact]
    public void RejectsADigitSequenceThatLeavesTen()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Encode(new Pzn8Symbology(), "0000003"));
        Assert.Contains("remainder of 10", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A PZN8 carries seven digits and a check digit, and a PZN7 six and a check digit, so anything else
    /// is rejected, as is a non-digit.
    /// </summary>
    [Theory]
    [InlineData("275808")]
    [InlineData("275808999")]
    [InlineData("275808A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Pzn8Symbology(), text));

    /// <summary>
    /// IFA prints the term PZN and the identifier separated with a space "for better readability", and
    /// says "the spaces and the term PZN are not represented in the barcode".
    /// </summary>
    [Fact]
    public void PrintsTheTermAndIdentifierWithoutEncodingThem()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Pzn8Symbology().Encode("03752864", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("PZN - 03752864", placement.Text);
    }

    /// <summary>
    /// IFA gives a nominal code height of 10 mm at the nominal module width of 0.25 mm, which is 40
    /// modules, and that height changes with the module width rather than with the symbol length.
    /// </summary>
    [Fact]
    public void DefaultsTheBarHeightToTheNominalCodeHeight()
    {
        LinearBarcodeSymbol symbol = Encode(new Pzn8Symbology(), "03752864");

        Assert.Equal(40F, symbol.BarHeights[0]);
    }

    /// <summary>
    /// The symbol carries the identifier, the digits and the check digit, and Code 39 adds no check
    /// character of its own.
    /// </summary>
    [Fact]
    public void AddsNoCode39CheckCharacter()
    {
        LinearBarcodeSymbol symbol = Encode(new Pzn8Symbology(), "03752864");
        int widthInModules = 0;
        foreach (int run in symbol.RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal(((1 + 8 + 2) * 16) - 1, widthInModules);
    }

    private static LinearBarcodeSymbol Encode(BarcodeSymbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());
}
