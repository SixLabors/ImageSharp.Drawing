// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Pzn8Symbology"/> and <see cref="Pzn7Symbology"/>. Each expected run string is the alternating bar and space module widths,
/// starting with a bar, taken from an independent reference implementation through its raw encoding API, less the gap it emits after the stop character. The
/// check digit and the printed line come from the IFA technical information documents on PZN coding.
/// </summary>
public class PznSymbologyTests
{
    [Theory]
    [InlineData("2758089", "1311313111131111313111331111311113113131311331111131131131111113313111311311311111331131111133113111131131311")]
    [InlineData("27580899", "1311313111131111313111331111311113113131311331111131131131111113313111311311311111331131111133113111131131311")]
    [InlineData("03752864", "1311313111131111313111133131113133111111111311313131133111111133111131311311311111333111111113311131131131311")]
    public void MatchesReferenceRuns(string text, string expected)
        => Assert.Equal(expected, Encode(new Pzn8Symbology(), text));

    [Theory]
    [InlineData("123456", "131131311113111131313113111131113311113131331111111113311131311331111111333111111133111131131131311")]
    [InlineData("1234562", "131131311113111131313113111131113311113131331111111113311131311331111111333111111133111131131131311")]
    public void MatchesReferenceRunsForTheSevenDigitForm(string text, string expected)
        => Assert.Equal(expected, Encode(new Pzn7Symbology(), text));

    /// <summary>
    /// The worked example in the IFA check digit document: the digits 2758089 weighted 1 to 7 give the products 2, 14, 15, 32, 0, 48 and 63, which sum to 174;
    /// 174 divided by 11 leaves 9, so the complete PZN is 27580899.
    /// </summary>
    [Fact]
    public void MatchesTheWorkedCheckDigitExample()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Pzn8Symbology(), "2758089", options, SymbolEncoder.Texts);

        Assert.Equal("PZN - 27580899", Assert.Single(texts));
    }

    /// <summary>
    /// The PZN of the worked PPN example in the same document is 03752864, whose check digit is therefore the trailing 4.
    /// </summary>
    [Fact]
    public void AcceptsTheCheckDigitOfTheWorkedPpnExample()
        => Assert.Equal(Encode(new Pzn8Symbology(), "0375286"), Encode(new Pzn8Symbology(), "03752864"));

    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode(new Pzn8Symbology(), "27580891"));

    /// <summary>
    /// IFA: "If the remainder is the number 10, this digit sequence is not used as PZN." The digits 0000003 weight the trailing 3 by seven, giving 21, and 21
    /// divided by 11 leaves 10.
    /// </summary>
    [Fact]
    public void RejectsADigitSequenceThatLeavesTen()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Encode(new Pzn8Symbology(), "0000003"));
        Assert.Contains("remainder of 10", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A PZN8 carries seven digits and a check digit, and a PZN7 six and a check digit, so anything else is rejected, as is a non-digit.
    /// </summary>
    [Theory]
    [InlineData("275808")]
    [InlineData("275808999")]
    [InlineData("275808A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Pzn8Symbology(), text));

    /// <summary>
    /// IFA prints the term PZN and the identifier separated with a space "for better readability", and says "the spaces and the term PZN are not represented in
    /// the barcode".
    /// </summary>
    [Fact]
    public void PrintsTheTermAndIdentifierWithoutEncodingThem()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Pzn8Symbology(), "03752864", options, SymbolEncoder.Texts);

        Assert.Equal("PZN - 03752864", Assert.Single(texts));
    }

    /// <summary>
    /// IFA gives a nominal code height of 10 mm at the nominal module width of 0.25 mm, which is 40 modules, and that height changes with the module width
    /// rather than with the symbol length.
    /// </summary>
    [Fact]
    public void DefaultsTheBarHeightToTheNominalCodeHeight()
        => Assert.Equal(40F, SymbolEncoder.Read(new Pzn8Symbology(), "03752864", new BarcodeOptions(), static (ref symbol) => symbol.BarHeights[0]));

    /// <summary>
    /// IFA gives a nominal module width of 0.25 mm, so a symbol without an X dimension of the caller draws its modules at 0.25 mm and its 40 module bars 10 mm
    /// high. An X dimension of the caller replaces it.
    /// </summary>
    [Fact]
    public void DefaultsTheModuleWidthToTheNominalModuleWidth()
    {
        BarcodeOptions options = new() { XDimension = 0.3 };

        Assert.Equal(0.25, SymbolEncoder.Read(new Pzn8Symbology(), "03752864", new BarcodeOptions(), static (ref symbol) => symbol.XDimension));
        Assert.Equal(0.3, SymbolEncoder.Read(new Pzn8Symbology(), "03752864", options, static (ref symbol) => symbol.XDimension));
    }

    /// <summary>
    /// The symbol carries the identifier, the digits and the check digit, and Code 39 adds no check character of its own.
    /// </summary>
    [Fact]
    public void AddsNoCode39CheckCharacter()
        => Assert.Equal(((1 + 8 + 2) * 16) - 1, SymbolEncoder.Read(new Pzn8Symbology(), "03752864", new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RunString);
}
