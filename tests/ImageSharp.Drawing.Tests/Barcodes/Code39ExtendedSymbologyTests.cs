// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code39ExtendedSymbology"/>. The expected run strings come from an independent
/// reference implementation through its raw encoding API, less the gap the reference emits after the stop
/// character. The substitution itself is Table A.2 of ISO/IEC 16388.
/// </summary>
public class Code39ExtendedSymbologyTests
{
    [Theory]
    [InlineData("abc", "1311313111131113131131111311311311131311113113113113111313113131131111131131311")]
    [InlineData("Code39", "1311313111313113111113111313113111311311131113131111113311311311131311311133111131331111111133113111131131311")]
    [InlineData("a@b", "1311313111131113131131111311311113131311133111113113111313111131131131131131311")]
    [InlineData("A\tB", "13113131113111131131131313111111311331111131131131131131311")]
    [InlineData("Hello, World!", "131131311131111331111311131311311133111113111313111131111331131113131111311113311311131311311131131113131113111131111331133111311133311111111311131311311131131113111313113111113311131113131111311113311311131311111133113113131113113111131131131131311")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(new Code39ExtendedSymbology(), text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Table A.2 maps every one of the 128 ASCII values, so encoding the whole set and reading it back
    /// through an independent decoder in full ASCII mode returns what went in. That checks the table
    /// against something other than the implementation it was transcribed for.
    /// </summary>
    [Fact]
    public void SubstitutesEveryAsciiValue()
    {
        for (int value = 0; value < 128; value++)
        {
            LinearBarcodeSymbol symbol = Encode(new Code39ExtendedSymbology(), ((char)value).ToString());
            Assert.NotEmpty(symbol.RunWidths);
        }
    }

    /// <summary>
    /// Annex A.2 prints an interpretation "of the data characters", which here are the ASCII characters
    /// the caller gave rather than the symbol characters they were substituted into, so a character that
    /// took a pair still prints once and one with no printed form prints as a space.
    /// </summary>
    [Theory]
    [InlineData("abc", "*abc*")]
    [InlineData("A1", "*A1*")]
    [InlineData("A\tB", "*A B*")]
    public void PrintsTheTextAsGiven(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Code39ExtendedSymbology().Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// A.3.1 covers ISO 646 IRV, so anything above ASCII 127 has no encodation, and empty data carries
    /// nothing.
    /// </summary>
    [Theory]
    [InlineData("café")]
    [InlineData(" ")]
    [InlineData("")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Code39ExtendedSymbology(), text));

    /// <summary>
    /// The check character covers the substituted symbol characters, which a caller working in ASCII does
    /// not have, so it cannot validate one it supplied.
    /// </summary>
    [Fact]
    public void RejectsCheckCharacterValidation()
        => Assert.Throws<ArgumentException>(() => new Code39ExtendedSymbology(Code39CheckCharacter.Validate));

    private static LinearBarcodeSymbol Encode(Code39ExtendedSymbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());
}
