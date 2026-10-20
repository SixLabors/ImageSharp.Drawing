// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="QrCodeSymbology"/>, <see cref="MicroQrCodeSymbology"/>, <see cref="D3AqrSymbology"/> and the GS1, GS1 Digital Link and HIBC QR
/// Code symbologies: the quiet zones, the input that each symbology rejects and the human readable interpretation.
/// </summary>
public class QrCodeSymbologyTests
{
    /// <summary>
    /// Section 5.3.7 of ISO/IEC 18004: the quiet zone is 4X wide around a QR Code symbol and 2X wide around a Micro QR Code symbol.
    /// </summary>
    [Fact]
    public void QuietZoneFollowsSectionFiveThreeSeven()
    {
        Assert.Equal(4F, SymbolEncoder.Read(new QrCodeSymbology(), "1234", new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));
        Assert.Equal(2F, SymbolEncoder.Read(new MicroQrCodeSymbology(), "1234", new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));
    }

    /// <summary>
    /// Table 7 of ISO/IEC 18004: a version 40-L symbol holds 7089 digits, the most any symbol holds. The symbology rejects 7090 digits.
    /// </summary>
    [Fact]
    public void QrCode_RejectsDataLargerThanTheLargestSymbol()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Encode(new QrCodeSymbology(QrErrorCorrectionLevel.L), Digits(7090)));

    /// <summary>
    /// Table 7: a version 1-H symbol holds 17 digits. A symbology fixed at that version rejects 18 digits.
    /// </summary>
    [Fact]
    public void QrCode_RejectsDataLargerThanTheFixedVersion()
        => Assert.Throws<ArgumentException>(() => Encode(new QrCodeSymbology(QrErrorCorrectionLevel.H, 1), Digits(18)));

    /// <summary>
    /// Section 6.3.4: Byte mode carries ISO/IEC 8859-1. The symbology rejects a character above U+00FF.
    /// </summary>
    [Fact]
    public void QrCode_RejectsCharactersAboveTheByteRange() => Assert.Throws<ArgumentException>(() => Encode(new QrCodeSymbology(), "Ā"));

    /// <summary>
    /// Sections 6.3.3 and 6.3.4: version M1 holds digits only, and M2 has no Byte mode. The symbology rejects a letter at M1 and a small letter at M2.
    /// </summary>
    [Fact]
    public void MicroQrCode_RejectsModesTheVersionDoesNotHave()
    {
        Assert.Throws<ArgumentException>(() => Encode(new MicroQrCodeSymbology(QrErrorCorrectionLevel.L, 1), "A"));
        Assert.Throws<ArgumentException>(() => Encode(new MicroQrCodeSymbology(QrErrorCorrectionLevel.L, 2), "a"));
    }

    /// <summary>
    /// Section 6.5.1 and Table 13: Micro QR Code has no level H, M1 has error detection only and only M4 has level Q. The symbology rejects level H, level M
    /// at M1 and level Q at M3.
    /// </summary>
    [Fact]
    public void MicroQrCode_RejectsLevelsTheVersionDoesNotHave()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MicroQrCodeSymbology(QrErrorCorrectionLevel.H));
        Assert.Throws<ArgumentException>(() => new MicroQrCodeSymbology(QrErrorCorrectionLevel.M, 1));
        Assert.Throws<ArgumentException>(() => new MicroQrCodeSymbology(QrErrorCorrectionLevel.Q, 3));
    }

    /// <summary>
    /// The two arms carry 14 bits, so the category is 0 to 16383. The symbology rejects -1 and 16384.
    /// </summary>
    [Fact]
    public void D3Aqr_RejectsCategoriesOutsideFourteenBits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new D3AqrSymbology(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new D3AqrSymbology(16384));
    }

    /// <summary>
    /// When the symbology validates the check character, it rejects input whose last character is not the check character of the data.
    /// </summary>
    [Fact]
    public void HibcQrCode_RejectsAnIncorrectCheckCharacter() => Assert.Throws<ArgumentException>(() => Encode(new HibcQrCodeSymbology(true), "A123BJC5D6E70"));

    /// <summary>
    /// GS1 QR Code takes element strings with each Application Identifier in parentheses. It rejects digits without the parentheses.
    /// </summary>
    [Fact]
    public void Gs1QrCode_RejectsDataOutsideElementStringSyntax()
        => Assert.Throws<ArgumentException>(() => Encode(new Gs1QrCodeSymbology(), "0109521234543213"));

    /// <summary>
    /// Section 5.7.4.7 of the GS1 General Specifications gives GS1 QR Code the human readable interpretation rules of section 4.14, the rules GS1 DataMatrix
    /// follows: one element string on each line, with the Application Identifiers in parentheses, and no URL of Application Identifier (8200).
    /// </summary>
    [Fact]
    public void Gs1QrCode_PrintsOneElementStringOnEachLine()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal(
            "(01)03453120000011\n(17)191125\n(10)ABCD1234",
            Assert.Single(SymbolEncoder.Read(new Gs1QrCodeSymbology(), "(01)03453120000011(17)191125(10)ABCD1234", options, SymbolEncoder.Texts)));

        Assert.Equal(
            "(01)09520123456788",
            Assert.Single(SymbolEncoder.Read(new Gs1QrCodeSymbology(), "(01)09520123456788(8200)http://www.abc.net", options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// Rule 1c of section 4.14 of the GS1 General Specifications: the human readable interpretation follows the order of the encoded data, which for a GS1
    /// Digital Link URI is the primary key and its qualifiers in the path, then the data attributes in the query string. The URI itself does not print.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkQrCode_PrintsTheElementStringsOfTheUri()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        string[] texts = SymbolEncoder.Read(
            new Gs1DigitalLinkQrCodeSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            options,
            SymbolEncoder.Texts);

        Assert.Equal("(01)09521234543213\n(22)ABC-123\n(99)XYZ-987", Assert.Single(texts));
    }

    /// <summary>
    /// Section 3.0 of ANSI/HIBC 2.6 gives every symbology the same interpretation between asterisks, and the figure of section 4.3.3 breaks concatenated data
    /// after the "/" that joins it.
    /// </summary>
    [Fact]
    public void HibcQrCode_PrintsTheDataBetweenDelimiters()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal("*+A123BJC5D6E71G*", Assert.Single(SymbolEncoder.Read(new HibcQrCodeSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts)));

        Assert.Equal(
            "*+A123BJC5D6E71/\n$$52ØØ151ØX3C*",
            Assert.Single(SymbolEncoder.Read(new HibcQrCodeSymbology(), "A123BJC5D6E71/$$52001510X3", options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// The human readable interpretation prints only when the caller sets a font.
    /// </summary>
    [Fact]
    public void Gs1AndHibcQrCode_PrintNoTextWithoutAFont()
    {
        BarcodeOptions options = new();

        Assert.Empty(SymbolEncoder.Read(new Gs1QrCodeSymbology(), "(01)03453120000011", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new Gs1DigitalLinkQrCodeSymbology(), "https://id.gs1.org/01/09521234543213", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new HibcQrCodeSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text) => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

    /// <summary>
    /// Returns the digits 0 to 9 repeated to a length.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <returns>The digits.</returns>
    private static string Digits(int length) => string.Create(length, 0, static (span, _) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = (char)('0' + (i % 10));
        }
    });
}
