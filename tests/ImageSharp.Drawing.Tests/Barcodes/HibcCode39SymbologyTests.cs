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
        { "$A1234G", "1311313111131113131113131311113111131131311311113111331111313133111111111331113111111331313311111131131131311" },
        {
            "$$52001510X3",
            "13113131111311131311131313111113131311113113311111113311113111133131111113313111311311113131133111113113111131111331311113113111313133111111" +
            "1313111311131131311"
        },
        {
            "A99912345/$$52001510X3/16D20111212/S77DEFG45",
            "13113131111311131311311113113111331131111133113111113311311131131111311133111131313311111111133111313113311111131311131113131311111313131111" +
            "31133111111133111131111331311111133131113113111131311331111131131111311113313111131131113131331111111313111311311311113111333111111111331131" +
            "11331111311113313111311311113131131111313113111131113311113131131111311133111131131311131111311133111113113131111311313111113311313111331111" +
            "11313311111111133131111331113131133111111113113131131131311"
        },
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

    /// <summary>
    /// ANSI/HIBC 2.6 rejects data outside its data structures. Note 1 of section 2.2.1: primary data starts with a letter and secondary data with a digit or
    /// "$". Table 1: a labeler code, a product number of "A through Z and 0 through 9" and a unit of measure digit. Table 2: secondary data in its own
    /// symbol ends with a link character. Appendix E1.1: the date format digit is 0 through 7. Section 2.3.2.4: the quantity is the last supplemental field.
    /// </summary>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData("+A123BJC5D6E71")]
    [InlineData("12345")]
    [InlineData("A12-BJC5D6E71")]
    [InlineData("A123BJC5D6E7X")]
    [InlineData("1234")]
    [InlineData("$$8051C001G")]
    [InlineData("$$51340010X3G")]
    [InlineData("A99912349/$10X3/Q500/16D20111231")]
    [InlineData("A99912345/$$52001510X3/T123")]
    public void RejectsDataOutsideTheDataStructures(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode39Symbology(), text));

    /// <summary>
    /// Table 1 of ANSI/HIBC 2.6 gives the product number "1-18" characters.
    /// </summary>
    [Fact]
    public void RejectsAProductNumberLongerThan18Characters()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode39Symbology(), "A123" + new string('B', 19) + "1"));

    /// <summary>
    /// Section 4.1 of ANSI/HIBC 2.6: "The Check Character or Link Character in the symbol will sometimes be a space character. In this case, the
    /// human-readable interpretation shall use an "underscore" to represent the space character." The check character of A123XYZ8 is a space, and so is the
    /// link character of $A1234 followed by a space.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="expected">The interpretation.</param>
    [Theory]
    [InlineData("A123XYZ8", "*+A123XYZ8_*")]
    [InlineData("$A1234 ", "*+$A1234_9*")]
    public void PrintsASpaceAsAnUnderscore(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new HibcCode39Symbology(), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Section 4.1 of ANSI/HIBC 2.6: "It is the recommendation of HIBCC that the human-readable interpretation of zero be represented as "Ø"." The data is the
    /// example of section 2.2.1.1, whose check character is 3.
    /// </summary>
    [Fact]
    public void PrintsZeroAsASlashedZero()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new HibcCode39Symbology(), "A99912345/$$52001510X3", options, SymbolEncoder.Texts);

        Assert.Equal("*+A99912345/$$52ØØ151ØX33*", Assert.Single(texts));
    }

    /// <summary>
    /// The interpretation is the encoded data between delimiters, so it shows the flag character and the check character the caller did not type.
    /// </summary>
    [Theory]
    [InlineData("A123BJC5D6E71", "*+A123BJC5D6E71G*")]
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
        => Assert.Equal(((1 + 7 + 1 + 2) * 16) - 1, SymbolEncoder.Read(new HibcCode39Symbology(), "$A1234G", new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(HibcCode39Symbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RunString);
}
