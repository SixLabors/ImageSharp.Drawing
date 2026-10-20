// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="IdentcodeSymbology"/>. The expected run string is the alternating bar and space module widths, starting with a bar, taken
/// verbatim from an independent reference implementation through its raw encoding API. That implementation draws a wide element two modules wide and emits a
/// narrow space after the stop pattern, so the test widens every wide element to the three modules this library draws and drops the trailing space before it
/// compares. The number is the worked example the Deutsche Post documentation gives.
/// </summary>
public class IdentcodeSymbologyTests
{
    /// <summary>
    /// The reference emits the same symbol whether the check digit is supplied or calculated, so both forms of the documented example compare against one
    /// vector.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("56310243031")]
    [InlineData("563102430313")]
    public void MatchesReferenceRuns(string text)
    {
        const string reference = "11112112221111222111111211122121121212211121121221211122121111212111";

        Assert.Equal(reference[..^1].Replace('2', '3'), Encode(text));
    }

    /// <summary>
    /// The documentation weights the digits 4 and 9 alternately from the first, so 56310243031 gives 20 + 54 + 12 + 9 + 0 + 18 + 16 + 27 + 0 + 27 + 4 = 187,
    /// and "187 Modulo 10 = 7, Ergänzung zu 10 = 3". The printed line is the grouping the reference implementations agree on for that example. A supplied check
    /// digit that disagrees throws.
    /// </summary>
    [Fact]
    public void CalculatesTheDeutschePostCheckDigit()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new IdentcodeSymbology(), "56310243031", options, SymbolEncoder.Texts);

        Assert.Equal("56.310 243.031 3", Assert.Single(texts));

        Assert.Throws<ArgumentException>(() => Encode("563102430314"));
    }

    /// <summary>
    /// An Identcode is eleven data digits and an optional check digit. The symbology rejects other lengths and non-digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("5631024303")]
    [InlineData("5631024303131")]
    [InlineData("5631024303A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// A symbol without an X dimension or a bar height of the caller draws at 0.5 mm, 50 pixels at 100 pixels a millimetre, with bars of 50 modules, 25 mm.
    /// </summary>
    [Fact]
    public void DefaultsToTheWidestModuleAndTheLeastHeight()
        => SymbolEncoder.Encode(new IdentcodeSymbology(), "56310243031", new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi }, static (ref symbol) =>
        {
            Assert.Equal(0.5, symbol.XDimension);
            Assert.Equal(25.0, symbol.BarHeights[0] * symbol.XDimension);
        });

    /// <summary>
    /// The default bar height of 25 mm and the quiet zone of 5 mm are in millimetres, so they hold at any narrow module. At 0.375 mm the 25 mm are 25 / 0.375
    /// modules and the 5 mm are 5 / 0.375 modules, more than the ten modules of Interleaved 2 of 5.
    /// </summary>
    [Fact]
    public void KeepsTheMillimetreDimensionsAtTheNarrowestModule()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 0.375 };
        SymbolEncoder.Encode(new IdentcodeSymbology(), "56310243031", options, static (ref symbol) =>
        {
            Assert.Equal(0.375, symbol.XDimension);
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal((float)(25 / 0.375), height, 1e-4F));
            Assert.Equal((float)(5 / 0.375), symbol.LeadingQuietZone, 1e-4F);
            Assert.Equal((float)(5 / 0.375), symbol.TrailingQuietZone, 1e-4F);
        });
    }

    /// <summary>
    /// Encodes the text at the nominal 0.5 mm module, 50 pixels at 100 pixels a millimetre.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new IdentcodeSymbology(), text, new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi }, SymbolEncoder.RunString);
}
