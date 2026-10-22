// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code39ExtendedSymbology"/>. The substitution is Table A.2 of ISO/IEC 16388.
/// </summary>
public class Code39ExtendedSymbologyTests
{
    /// <summary>
    /// Table A.2 maps every one of the 128 ASCII values, so the encoder encodes each value as a text of one character.
    /// </summary>
    [Fact]
    public void SubstitutesEveryAsciiValue()
    {
        for (int value = 0; value < 128; value++)
        {
            Assert.NotEmpty(Encode(new Code39ExtendedSymbology(), ((char)value).ToString()));
        }
    }

    /// <summary>
    /// Annex A.2 prints an interpretation "of the data characters", which here are the ASCII characters the caller gave rather than the symbol characters they
    /// were substituted into, so a character that took a pair still prints once and one with no printed form prints as a space.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("abc", "*abc*")]
    [InlineData("A1", "*A1*")]
    [InlineData("A\tB", "*A B*")]
    public void PrintsTheTextAsGiven(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Code39ExtendedSymbology(), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// A.3.1 covers ISO 646 IRV, so anything above ASCII 127 has no encodation, and empty data carries nothing. The second case is the no-break space
    /// U+00A0, not the ASCII space.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("café")]
    [InlineData(" ")]
    [InlineData("")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Code39ExtendedSymbology(), text));

    /// <summary>
    /// The check character covers the substituted symbol characters, which a caller working in ASCII does not have, so it cannot validate one it supplied.
    /// </summary>
    [Fact]
    public void RejectsCheckCharacterValidation()
        => Assert.Throws<ArgumentException>(() => new Code39ExtendedSymbology(CheckCharacterMode.Validate));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(Code39ExtendedSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RunString);
}
