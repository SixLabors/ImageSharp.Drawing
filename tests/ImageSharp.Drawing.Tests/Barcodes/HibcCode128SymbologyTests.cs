// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcCode128Symbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, taken
/// from an independent reference implementation through its raw encoding API. A HIBC Code 128 is a Code 128 symbol carrying the HIBC flag character, the data
/// and the modulo 43 check character.
/// </summary>
public class HibcCode128SymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceRuns"/>: the text and the run widths of the reference implementation.
    /// </summary>
    public static TheoryData<string, string> MatchesReferenceRunsData { get; } = new()
    {
        { "A123BJC5D6E71", "2112142312121113231232212232112211321311231121331313212132121123132231121321133121311232212113132321212331112" },
        { "$$52001510X3G", "2112142312121213221213221131412133112122221132222213121141313311212211322113131123131341112331112" },
        { "$A1234G", "2112142312121213221113231131411122321311231141312113132131312222212331112" },
        {
            "A99912345/$$52001510X3",
            "2112142312121113231131411131414121213121311131231141311132221213221213221131412133112122221132222213121141313311212211322211322131312331112"
        },
        {
            "A99912345/$$52001510X3/16D20111212/S77DEFG45",
            "21121423121211132311314111314141212131213111312311413111322212132212132211314121331121222211322222131211413133112122113211322212322122311211" +
            "23131131412212312312121122321122321141311132222131133121313121311123131321131323112113132212312132123121311133212331112"
        },
    };

    /// <summary>
    /// The symbol runs match the reference runs.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="expected">The run widths of the reference symbol.</param>
    [Theory]
    [MemberData(nameof(MatchesReferenceRunsData))]
    public void MatchesReferenceRuns(string text, string expected)
        => Assert.Equal(expected, Encode(new HibcCode128Symbology(), text));

    /// <summary>
    /// A caller can supply the check character. The symbol carries it once, so the runs match those of the same data without the check character.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
    {
        HibcCode128Symbology symbology = new(true);

        Assert.Equal(Encode(new HibcCode128Symbology(), "A123BJC5D6E71"), Encode(symbology, "A123BJC5D6E71G"));
    }

    /// <summary>
    /// A supplied check character that is not the modulo 43 check character of the data throws.
    /// </summary>
    [Fact]
    public void RejectsWrongCheckCharacter()
    {
        HibcCode128Symbology symbology = new(true);

        Assert.Throws<ArgumentException>(() => Encode(symbology, "A123BJC5D6E71H"));
    }

    /// <summary>
    /// HIBC uses the Code 39 character set. The symbology rejects empty data and characters outside that set.
    /// </summary>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData("")]
    [InlineData("a123")]
    [InlineData("A*123")]
    [InlineData("A,123")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode128Symbology(), text));

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
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode128Symbology(), text));

    /// <summary>
    /// Table 1 of ANSI/HIBC 2.6 gives the product number "1-18" characters.
    /// </summary>
    [Fact]
    public void RejectsAProductNumberLongerThan18Characters()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode128Symbology(), "A123" + new string('B', 19) + "1"));

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
        string[] texts = SymbolEncoder.Read(new HibcCode128Symbology(), text, options, SymbolEncoder.Texts);

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
        string[] texts = SymbolEncoder.Read(new HibcCode128Symbology(), "A99912345/$$52001510X3", options, SymbolEncoder.Texts);

        Assert.Equal("*+A99912345/$$52ØØ151ØX33*", Assert.Single(texts));
    }

    /// <summary>
    /// The human readable interpretation is the encoded data between delimiters, so it shows the flag character and the check character the caller did not
    /// type.
    /// </summary>
    [Fact]
    public void PrintsTheFlagAndCheckCharacters()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new HibcCode128Symbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts);

        Assert.Equal("*+A123BJC5D6E71G*", Assert.Single(texts));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(HibcCode128Symbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RunString);
}
