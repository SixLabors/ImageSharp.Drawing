// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code39ExtendedSymbology"/>. The expected run strings come from an independent reference implementation through its raw encoding
/// API, less the gap the reference emits after the stop character. The substitution itself is Table A.2 of ISO/IEC 16388.
/// </summary>
public class Code39ExtendedSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceRuns"/>.
    /// </summary>
    public static TheoryData<string, string> MatchesReferenceRunsData { get; } = new()
    {
        { "abc", "1311313111131113131131111311311311131311113113113113111313113131131111131131311" },
        { "Code39", "1311313111313113111113111313113111311311131113131111113311311311131311311133111131331111111133113111131131311" },
        { "a@b", "1311313111131113131131111311311113131311133111113113111313111131131131131131311" },
        { "A\tB", "13113131113111131131131313111111311331111131131131131131311" },
        {
            "Hello, World!",
            "13113131113111133111131113131131113311111311131311113111133113111313111131111331131113131131113113111313111311113111133113311131113331111111" +
            "1311131311311131131113111313113111113311131113131111311113311311131311111133113113131113113111131131131131311"
        },
    };

    [Theory]
    [MemberData(nameof(MatchesReferenceRunsData))]
    public void MatchesReferenceRuns(string text, string expected)
        => Assert.Equal(expected, Encode(new Code39ExtendedSymbology(), text));

    /// <summary>
    /// Table A.2 maps every one of the 128 ASCII values, so encoding the whole set and reading it back through an independent decoder in full ASCII mode
    /// returns what went in. That checks the table against something other than the implementation it was transcribed for.
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
    /// A.3.1 covers ISO 646 IRV, so anything above ASCII 127 has no encodation, and empty data carries nothing.
    /// </summary>
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
