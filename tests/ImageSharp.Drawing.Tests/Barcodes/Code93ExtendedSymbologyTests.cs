// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code93ExtendedSymbology"/>. The expected run strings come from an
/// independent reference implementation through its raw encoding API with the check characters turned
/// on, which the reference leaves off by default and ANSI/AIM BC5-1995 requires. The substitution itself
/// is Table 3, and the two reference implementations consulted agree on it entry for entry.
/// </summary>
public class Code93ExtendedSymbologyTests
{
    [Theory]
    [InlineData("Code93", "1111412113111222111211221222112211121222112212111411111114112111133111211111411")]
    [InlineData("abc", "1111411222112111131222112112121222112113111211311312111111411")]
    [InlineData("a@b", "1111411222112111133121112221111222112112121121311122211111411")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// A character the base symbology carries takes one symbol character, and any other takes a shift
    /// character and one more. Section 2.6 measures the symbol as <c>(9 * (C + 4) + 1) * X</c>, so the
    /// module count counts the symbol characters the substitution produced rather than the input.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="characters">The number of symbol characters the text substitutes to.</param>
    [Theory]
    [InlineData("CODE93", 6)]
    [InlineData("abc", 6)]
    [InlineData("a@b", 6)]
    [InlineData("A", 1)]
    public void SubstitutesToSymbolCharacters(string text, int characters)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((9 * (characters + 4)) + 1, widthInModules);
    }

    /// <summary>
    /// A symbol whose text substitutes to nothing but base characters is the same symbol the base
    /// symbology produces, because the substitution leaves those characters alone.
    /// </summary>
    [Fact]
    public void MatchesTheBaseSymbologyForBaseCharacters()
    {
        LinearBarcodeSymbol extended = Encode("CODE93");
        LinearBarcodeSymbol plain = (LinearBarcodeSymbol)new Code93Symbology().Encode("CODE93", new BarcodeOptions());

        Assert.Equal(string.Concat(plain.RunWidths), string.Concat(extended.RunWidths));
    }

    /// <summary>
    /// The printed line shows the characters the caller gave rather than the symbol characters that stand
    /// for them, and a control character has no printed form, so it shows as a space.
    /// </summary>
    [Theory]
    [InlineData("a@b", "a@b")]
    [InlineData("Hello, World!", "Hello, World!")]
    [InlineData("A\tB", "A B")]
    public void PrintsTheTextAsGiven(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Code93ExtendedSymbology().Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// Table 3 covers ASCII 0 to 127 and nothing beyond it.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("CAFÉ")]
    [InlineData("©")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Code93ExtendedSymbology().Encode(text, new BarcodeOptions());
}
