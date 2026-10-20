// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// The GS1 data rules every GS1 symbology applies to its element strings, tested through GS1-128: the format of each Application Identifier, and the invalid
/// pairs of section 4.13 of the GS1 General Specifications.
/// </summary>
public class Gs1DataTests
{
    /// <summary>
    /// Element strings that follow every rule.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(01)09521234543213")]
    [InlineData("(01)09521234543213(10)ABC123(21)XYZ789")]
    [InlineData("(01)09521234543213(17)260200")]
    [InlineData("(01)09521234543213(17)280229")]
    [InlineData("(00)395123451234567895")]
    [InlineData("(90)ABC(91)xyz")]
    public void AcceptsValidElementStrings(string text)
        => Assert.Null(Record.Exception(() => Encode(text)));

    /// <summary>
    /// Section 7.9.1: the last digit of a GTIN is the check digit of the digits before it.
    /// </summary>
    [Fact]
    public void RejectsAnIncorrectCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("(01)09521234543214"));

    /// <summary>
    /// Section 3.4.7: a date is YYMMDD with a real month and day, and a day of 00 when the day is not needed. February 2026 has 28 days.
    /// </summary>
    /// <param name="date">The date.</param>
    [Theory]
    [InlineData("261301")]
    [InlineData("260229")]
    [InlineData("260431")]
    public void RejectsADateThatDoesNotExist(string date)
        => Assert.Throws<ArgumentException>(() => Encode("(01)09521234543213(17)" + date));

    /// <summary>
    /// Section 7.11, Table 7-20: the data of an alphanumeric Application Identifier uses GS1 AI encodable character set 82, which has no tilde or space.
    /// </summary>
    /// <param name="batch">The batch or lot number.</param>
    [Theory]
    [InlineData("ABC~1")]
    [InlineData("ABC 1")]
    public void RejectsCharactersOutsideCharacterSet82(string batch)
        => Assert.Throws<ArgumentException>(() => Encode("(01)09521234543213(10)" + batch));

    /// <summary>
    /// An Application Identifier the GS1 General Specifications do not allocate has no format, so it is rejected, and so is data longer than the format allows.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(04)12345678901234567")]
    [InlineData("(01)09521234543213(10)ABCDEFGHIJKLMNOPQRSTU")]
    public void RejectsUnknownIdentifiersAndOverlongData(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 4.13.1, Table 4-4: GTIN with the count of units contained (37), Serial Number (21) with the third party controlled serialised extension (235),
    /// and two GTINs of different values, may not appear together.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(01)09521234543213(37)5")]
    [InlineData("(01)09521234543213(21)ABC(235)XYZ")]
    [InlineData("(01)09521234543213(01)09506000134352")]
    public void RejectsInvalidPairs(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 4.13.2, Table 4-5: a batch or lot number, an expiration date and a serial number each require a GTIN or another key on the same physical
    /// entity. The note of section 4.13.2: "This does not necessarily mean that the element strings need to appear in the same data carrier", so a symbol
    /// without the key is accepted.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(10)ABC123")]
    [InlineData("(17)260101")]
    [InlineData("(21)ABC")]
    public void AcceptsElementStringsWhoseAssociationsAreInAnotherCarrier(string text)
        => Assert.Null(Record.Exception(() => Encode(text)));

    /// <summary>
    /// An element string repeated with the same value is one item of data, so it is accepted.
    /// </summary>
    [Fact]
    public void AcceptsARepeatedElementStringWithOneValue()
        => Assert.Null(Record.Exception(() => Encode("(01)09521234543213(01)09521234543213")));

    /// <summary>
    /// The country of origin (422) is an ISO 3166-1 numeric code, the ship to country (4307) an ISO 3166-1 alpha-2 code and the currency of an amount payable
    /// (3932) an ISO 4217 numeric code: 276 and DE are Germany and 978 is the euro.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(01)09521234543213(422)276")]
    [InlineData("(00)395123451234567895(4307)DE")]
    [InlineData("(01)09521234543213(3103)000123(3932)978123")]
    [InlineData("(00)395123451234567895(7041)8")]
    [InlineData("(00)395123451234567895(7041)BX")]
    [InlineData("(00)395123451234567895(7041)APE")]
    public void AcceptsCodesFromTheCodeLists(string text)
        => Assert.Null(Record.Exception(() => Encode(text)));

    /// <summary>
    /// The codes 999 and 000, and the letters XX, are not in ISO 3166-1 or ISO 4217, and AE, APX and the lower case bx are not package type codes.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(01)09521234543213(422)999")]
    [InlineData("(00)395123451234567895(4307)XX")]
    [InlineData("(01)09521234543213(3103)000123(3932)000123")]
    [InlineData("(00)395123451234567895(7041)AE")]
    [InlineData("(00)395123451234567895(7041)APX")]
    [InlineData("(00)395123451234567895(7041)bx")]
    public void RejectsCodesOutsideTheCodeLists(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 7.13 converts a longitude to Y = 10 000 000 x ((longitude + 360) mod 360). The remainder is below 360, so Y is at most 3 599 999 999. The
    /// example of the section converts latitude -62.0914152 and longitude -58.4702029 to 0279085848 and 3015297971.
    /// </summary>
    [Fact]
    public void LimitsTheLongitudeOfSectionSevenThirteen()
    {
        Assert.Null(Record.Exception(() => Encode("(00)395123451234567895(4309)02790858483015297971")));
        Assert.Null(Record.Exception(() => Encode("(00)395123451234567895(4309)02790858483599999999")));
        Assert.Throws<ArgumentException>(() => Encode("(00)395123451234567895(4309)02790858483600000000"));
    }

    /// <summary>
    /// Section 4.14 rule 2.a.iii: "The font size SHOULD be at least 2 mm (0.08 in) in height". A point is 1/72 inch, so a font of 2 mm is 2 * 72 / 25.4
    /// points, and every GS1 linear symbology prints its human readable interpretation at that size or larger.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("Gs1128", "(01)09521234543213")]
    [InlineData("DataBarOmnidirectional", "(01)09521234543213")]
    [InlineData("DataBarLimited", "(01)15012345678907")]
    [InlineData("DataBarExpanded", "(01)90614141000015(3202)000150")]
    [InlineData("DataBarExpandedStacked", "(01)90614141000015(3202)000150")]
    [InlineData("Itf14", "15400141288763")]
    public void PrintsTheTextAtTwoMillimetresOrLarger(string kind, string text)
    {
        BarcodeSymbology symbology = kind switch
        {
            "Gs1128" => new Gs1128Symbology(),
            "DataBarOmnidirectional" => new Gs1DataBarOmnidirectionalSymbology(),
            "DataBarLimited" => new Gs1DataBarLimitedSymbology(),
            "DataBarExpanded" => new Gs1DataBarExpandedSymbology(),
            "DataBarExpandedStacked" => new Gs1DataBarExpandedStackedSymbology(),
            _ => new Itf14Symbology(),
        };

        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(4F) };
        SymbolEncoder.Encode(symbology, text, options, static (ref symbol) => Assert.Equal(2F * 72F / 25.4F, symbol.TextMinimumSize, 1e-4F));
    }

    /// <summary>
    /// Encodes the text at an X dimension of 0.25 mm. Table 5-52 of the GS1 General Specifications allows GS1-128 from 0.170 mm, and 0.25 mm keeps the longest
    /// test data within the 165.10 mm of section 5.4.4.3.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    private static void Encode(string text)
        => SymbolEncoder.Encode(new Gs1128Symbology(), text, new BarcodeOptions { XDimension = 0.25 });
}
