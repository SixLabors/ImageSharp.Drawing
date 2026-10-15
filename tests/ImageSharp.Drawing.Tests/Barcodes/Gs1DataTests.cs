// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// The GS1 data rules every GS1 symbology applies to its element strings, tested through GS1-128: the
/// format of each Application Identifier, and the invalid pairs and mandatory associations of section 4.13
/// of the GS1 General Specifications.
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
    /// Section 3.4.7: a date is YYMMDD with a real month and day, and a day of 00 when the day is not
    /// needed. February 2026 has 28 days.
    /// </summary>
    /// <param name="date">The date.</param>
    [Theory]
    [InlineData("261301")]
    [InlineData("260229")]
    [InlineData("260431")]
    public void RejectsADateThatDoesNotExist(string date)
        => Assert.Throws<ArgumentException>(() => Encode("(01)09521234543213(17)" + date));

    /// <summary>
    /// Section 7.11, Table 7-20: the data of an alphanumeric Application Identifier uses GS1 AI encodable
    /// character set 82, which has no tilde or space.
    /// </summary>
    /// <param name="batch">The batch or lot number.</param>
    [Theory]
    [InlineData("ABC~1")]
    [InlineData("ABC 1")]
    public void RejectsCharactersOutsideCharacterSet82(string batch)
        => Assert.Throws<ArgumentException>(() => Encode("(01)09521234543213(10)" + batch));

    /// <summary>
    /// An Application Identifier the GS1 General Specifications do not allocate has no format, so it is
    /// rejected, and so is data longer than the format allows.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(04)12345678901234567")]
    [InlineData("(01)09521234543213(10)ABCDEFGHIJKLMNOPQRSTU")]
    public void RejectsUnknownIdentifiersAndOverlongData(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 4.13.1, Table 4-4: GTIN with the count of units contained (37), Serial Number (21) with the
    /// third party controlled serialised extension (235), and two GTINs of different values, may not appear
    /// together.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(01)09521234543213(37)5")]
    [InlineData("(01)09521234543213(21)ABC(235)XYZ")]
    [InlineData("(01)09521234543213(01)09506000134352")]
    public void RejectsInvalidPairs(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 4.13.2, Table 4-5: a batch or lot number requires a GTIN, a GTIN of contained trade items, a
    /// Made-to-Order GTIN, an ITIP or an ITIP of contained trade items, and an expiration date requires the
    /// item it dates.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(10)ABC123")]
    [InlineData("(17)260101")]
    [InlineData("(21)ABC")]
    public void RejectsMissingMandatoryAssociations(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text));

    /// <summary>
    /// An element string repeated with the same value is one item of data, so it is accepted.
    /// </summary>
    [Fact]
    public void AcceptsARepeatedElementStringWithOneValue()
        => Assert.Null(Record.Exception(() => Encode("(01)09521234543213(01)09521234543213")));

    /// <summary>
    /// The country of origin (422) is an ISO 3166-1 numeric code, the ship to country (4307) an ISO 3166-1
    /// alpha-2 code and the currency of an amount payable (3932) an ISO 4217 numeric code: 276 and DE are
    /// Germany and 978 is the euro.
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
    /// The codes 999 and 000, and the letters XX, are not in ISO 3166-1 or ISO 4217, and AE, APX and the
    /// lower case bx are not package type codes.
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
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    private static void Encode(string text)
        => SymbolEncoder.Encode(new Gs1128Symbology(), text, new BarcodeOptions());
}
