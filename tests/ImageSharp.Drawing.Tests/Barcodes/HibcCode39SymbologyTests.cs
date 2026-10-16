// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcCode39Symbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, taken
/// from an independent reference implementation through its raw encoding API, less the gap it emits after the stop character. A HIBC Code 39 carries the same
/// data a HIBC Code 128 does, drawn by Code 39, so these also prove the shared data layer.
/// </summary>
public class HibcCode39SymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceRuns"/>.
    /// </summary>
    public static TheoryData<string, string> MatchesReferenceRunsData { get; } = new()
    {
        {
            "A123BJC5D6E71",
            "13113131111311131311311113113131131111311133111131313311111111311311311111333111313113111131133111111111331131113331111131113311111113113131" +
            "31131111311111133131131131311"
        },
        { "12345", "13113131111311131311311311113111331111313133111111111331113131133111111111331131131131311" },
        {
            "$$52001510X3",
            "13113131111311131311131313111113131311113113311111113311113111133131111113313111311311113131133111113113111131111331311113113111313133111111" +
            "1313111311131131311"
        },
        { "/", "1311313111131113131113131113111331113111131131311" },
    };

    [Theory]
    [MemberData(nameof(MatchesReferenceRunsData))]
    public void MatchesReferenceRuns(string text, string expected)
        => Assert.Equal(expected, Encode(new HibcCode39Symbology(), text));

    /// <summary>
    /// A caller who has already calculated the check character supplies it, and the symbol carries it once, so the runs match those of the same data with the
    /// check character left off.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
        => Assert.Equal(Encode(new HibcCode39Symbology(), "A123BJC5D6E71"), Encode(new HibcCode39Symbology(true), "A123BJC5D6E71G"));

    [Fact]
    public void RejectsWrongCheckCharacter()
        => Assert.Throws<ArgumentException>(() => Encode(new HibcCode39Symbology(true), "A123BJC5D6E71H"));

    /// <summary>
    /// HIBC carries the Code 39 character set, so anything outside it is rejected, as is empty data and data beyond the length the symbology allows.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("a123")]
    [InlineData("A*123")]
    [InlineData("A,123")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode39Symbology(), text));

    [Fact]
    public void RejectsDataBeyondTheMaximumLength()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode39Symbology(), new string('A', 501)));

    /// <summary>
    /// The interpretation is the encoded data between delimiters, so it shows the flag character and the check character the caller did not type, and a check
    /// character that is a space shows as an underscore.
    /// </summary>
    [Theory]
    [InlineData("A123BJC5D6E71", "*+A123BJC5D6E71G*")]
    [InlineData("/", "*+/_*")]
    public void PrintsTheFlagAndCheckCharacters(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new HibcCode39Symbology(), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// The data already carries the HIBC check character, so Code 39 adds none of its own: the symbol is the flag character, the data and the one check
    /// character between the start and stop characters.
    /// </summary>
    [Fact]
    public void AddsNoCode39CheckCharacter()
        => Assert.Equal(((1 + 5 + 1 + 2) * 16) - 1, SymbolEncoder.Read(new HibcCode39Symbology(), "12345", new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(HibcCode39Symbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RunString);
}
