// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code93ExtendedSymbology"/>. The substitution is Table 3 of ANSI/AIM BC5-1995.
/// </summary>
public class Code93ExtendedSymbologyTests
{
    /// <summary>
    /// A character the base symbology carries takes one symbol character, and any other takes a shift character and one more. Section 2.6 measures the symbol
    /// as <c>(9 * (C + 4) + 1) * X</c>, so the module count counts the symbol characters the substitution produced rather than the input.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="characters">The number of symbol characters the text substitutes to.</param>
    [Theory]
    [InlineData("CODE93", 6)]
    [InlineData("abc", 6)]
    [InlineData("a@b", 6)]
    [InlineData("A", 1)]
    public void SubstitutesToSymbolCharacters(string text, int characters)
        => Assert.Equal((9 * (characters + 4)) + 1, SymbolEncoder.Read(new Code93ExtendedSymbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// A symbol whose text substitutes to nothing but base characters is the same symbol the base symbology produces, because the substitution leaves those
    /// characters alone.
    /// </summary>
    [Fact]
    public void MatchesTheBaseSymbologyForBaseCharacters()
        => Assert.Equal(SymbolEncoder.Read(new Code93Symbology(), "CODE93", new BarcodeOptions(), SymbolEncoder.RunString), Encode("CODE93"));

    /// <summary>
    /// The printed line shows the characters the caller gave rather than the symbol characters that stand for them, and a control character has no printed
    /// form, so it shows as a space.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("a@b", "a@b")]
    [InlineData("Hello, World!", "Hello, World!")]
    [InlineData("A\tB", "A B")]
    public void PrintsTheTextAsGiven(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Code93ExtendedSymbology(), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
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

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Code93ExtendedSymbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
