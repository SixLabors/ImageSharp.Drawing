// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Round-trip tests: every rendered barcode must decode back to its input through ZXing.Net, an independent
/// decoder. This proves the images are valid scannable symbols without a human point of reference, which the
/// module-sequence and golden image tests cannot do on their own. The standalone add-on symbologies have no
/// decode tests because ZXing reads add-ons only as extensions of a main EAN/UPC symbol.
/// </summary>
public class BarcodeDecodeTests
{
    [Theory]
    [InlineData("5901234123457")]
    [InlineData("4006381333931")]
    [InlineData("9780306406157")]
    public void Ean13_RoundTrips(string text)
        => AssertDecodes(new Ean13Symbology(), text, BarcodeFormat.EAN_13, text);

    [Fact]
    public void Ean8_RoundTrips()
        => AssertDecodes(new Ean8Symbology(), "96385074", BarcodeFormat.EAN_8, "96385074");

    [Fact]
    public void UpcA_RoundTrips()
        => AssertDecodes(new UpcASymbology(), "036000291452", BarcodeFormat.UPC_A, "036000291452");

    [Theory]
    [InlineData("01234565")]
    [InlineData("04252614")]
    [InlineData("16543214")]
    public void UpcE_RoundTrips(string text)
        => AssertDecodes(new UpcESymbology(), text, BarcodeFormat.UPC_E, text);

    [Fact]
    public void Isbn_RoundTrips()
        => AssertDecodes(new IsbnSymbology(), "978-0-306-40615-7", BarcodeFormat.EAN_13, "9780306406157");

    [Fact]
    public void Ismn_RoundTrips()
        => AssertDecodes(new IsmnSymbology(), "979-0-2600-0043-8", BarcodeFormat.EAN_13, "9790260000438");

    [Fact]
    public void Issn_RoundTrips()
        => AssertDecodes(new IssnSymbology(), "0317-8471", BarcodeFormat.EAN_13, "9770317847001");

    [Fact]
    public void Mands_RoundTrips()
        => AssertDecodes(new MandsSymbology(), "0642118", BarcodeFormat.EAN_8, "00642118");

    [Theory]
    [InlineData("CODE128")]
    [InlineData("ABC-123")]
    public void Code128_RoundTrips(string text)
        => AssertDecodes(new Code128Symbology(), text, BarcodeFormat.CODE_128, text);

    /// <summary>
    /// A GS1-128 symbol decodes to its element strings without the parentheses, which section 4.14
    /// rule 2c keeps to the human readable interpretation, and without the Function 1 characters, which
    /// are symbol overhead rather than data.
    /// </summary>
    [Theory]
    [InlineData("(01)09521234543213", "0109521234543213")]
    [InlineData("(91)ABC123", "91ABC123")]
    public void Gs1_128_RoundTrips(string text, string expected)
        => AssertDecodes(new Gs1128Symbology(), text, BarcodeFormat.CODE_128, expected);

    /// <summary>
    /// An EAN-14 decodes to its Application Identifier and the fourteen digit Global Trade Item Number.
    /// </summary>
    [Fact]
    public void Ean14_RoundTrips()
        => AssertDecodes(new Ean14Symbology(), "(01)09521234543213", BarcodeFormat.CODE_128, "0109521234543213");

    /// <summary>
    /// An SSCC-18 decodes to its Application Identifier and the eighteen digit Serial Shipping Container
    /// Code.
    /// </summary>
    [Fact]
    public void Sscc18_RoundTrips()
        => AssertDecodes(new Sscc18Symbology(), "(00)106141411234567897", BarcodeFormat.CODE_128, "00106141411234567897");

    /// <summary>
    /// An Intelligent Mail package barcode decodes to the routing code, the Function 1 character that
    /// section 2.2.1 of USPS2000508 places after the ZIP Code, and the PIC. The decoder reports Function 1
    /// characters only when it reads the symbol as GS1: the first as the symbology identifier ]C1 and the
    /// second as the group separator.
    /// </summary>
    [Fact]
    public void IntelligentMailPackage_RoundTrips()
        => AssertDecodes(
            new IntelligentMailPackageSymbology("USPS TRACKING #"),
            "(420)123456789(92)12391234567812345670",
            BarcodeFormat.CODE_128,
            "]C1420123456789\u001D9212391234567812345670",
            CreateOptions(),
            static reader => reader.Options.AssumeGS1 = true);

    /// <summary>
    /// A HIBC Code 128 decodes to the flag character, the data and the check character, which is what the
    /// symbol carries; the delimiters belong to the human readable interpretation alone.
    /// </summary>
    [Fact]
    public void HibcCode128_RoundTrips()
        => AssertDecodes(new HibcCode128Symbology(), "A123BJC5D6E71", BarcodeFormat.CODE_128, "+A123BJC5D6E71G");

    /// <summary>
    /// A Code 39 symbol decodes to its data. The start and stop character are symbol overhead, and the
    /// asterisks that stand for them belong to the human readable interpretation alone. The decoder is
    /// taken out of full ASCII mode, which A.3 calls a non-standard transmission mode, so a pair such as
    /// <c>+A</c> reads back as the two characters the symbol carries rather than as one lowercase letter.
    /// </summary>
    [Theory]
    [InlineData("CODE39")]
    [InlineData("ABC-123")]
    public void Code39_RoundTrips(string text)
        => AssertDecodes(
            new Code39Symbology(),
            text,
            BarcodeFormat.CODE_39,
            text,
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A Code 93 symbol decodes to its data. The two check characters are symbol overhead, so the
    /// decoder validates them and removes them from the result.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("CODE93")]
    [InlineData("ABC-123")]
    public void Code93_RoundTrips(string text)
        => AssertDecodes(new Code93Symbology(), text, BarcodeFormat.CODE_93, text);

    /// <summary>
    /// A Code 93 Extended symbol is an ordinary Code 93 symbol, and the decoder reverses the Table 3
    /// substitution on its own, so the ASCII characters come back as they went in.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("Code93")]
    [InlineData("abc")]
    [InlineData("a@b")]
    [InlineData("Hello, World!")]
    public void Code93Extended_RoundTrips(string text)
        => AssertDecodes(new Code93ExtendedSymbology(), text, BarcodeFormat.CODE_93, text);

    /// <summary>
    /// An Interleaved 2 of 5 symbol decodes to the digits it carries: the leading zero that makes an odd
    /// count a pair, the data and the check digit when the symbol carries one. The decoder does not
    /// validate a check digit, because the symbology does not require one. It does apply a length list,
    /// which defaults to 6, 8, 10, 12 and 14 digits, so the four digit symbols name their length.
    /// </summary>
    /// <param name="checkDigit">
    /// Whether the symbol carries the check digit, and whether the encoder calculates it or validates it.
    /// </param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The digits the symbol carries.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, "1234", "1234")]
    [InlineData(CheckCharacterMode.None, "123", "0123")]
    [InlineData(CheckCharacterMode.Compute, "12345", "123457")]
    [InlineData(CheckCharacterMode.Compute, "1234", "012348")]
    public void Interleaved2Of5_RoundTrips(CheckCharacterMode checkDigit, string text, string expected)
        => AssertDecodes(
            new Interleaved2Of5Symbology(checkDigit),
            text,
            BarcodeFormat.ITF,
            expected,
            CreateOptions(),
            reader => reader.Options.AllowedLengths = [4, 6]);

    /// <summary>
    /// A Leitcode decodes to the fourteen digits the symbol carries, which is the data and the check
    /// digit. The full stops and the space belong to the printed line alone.
    /// </summary>
    [Fact]
    public void Leitcode_RoundTrips()
        => AssertDecodes(new LeitcodeSymbology(), "2134807501640", BarcodeFormat.ITF, "21348075016401");

    /// <summary>
    /// An ITF-14 symbol decodes to the fourteen digits it carries. The bearer bar lies outside the quiet
    /// zones, so the decoder finds the clear space it needs on both sides of the bars.
    /// </summary>
    [Fact]
    public void Itf14_RoundTrips()
        => AssertDecodes(new Itf14Symbology(), "1540014128876", BarcodeFormat.ITF, "15400141288763");

    /// <summary>
    /// A Codabar symbol decodes to its start character, its data, the check character when the symbol
    /// carries one, and its stop character. The decoder strips the start and stop characters unless it
    /// is asked to return them, and it does not validate a check character, because the symbology does
    /// not define one.
    /// </summary>
    /// <param name="checkCharacter">Whether the symbol carries the check character, and whether the encoder calculates it or validates it.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The characters the symbol carries.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, "A40156B", "A40156B")]
    [InlineData(CheckCharacterMode.Compute, "A40156B", "A40156+B")]
    public void Codabar_RoundTrips(CheckCharacterMode checkCharacter, string text, string expected)
        => AssertDecodes(
            new CodabarSymbology(checkCharacter),
            text,
            BarcodeFormat.CODABAR,
            expected,
            CreateOptions(),
            reader => reader.Options.ReturnCodabarStartEnd = true);

    /// <summary>
    /// An MSI symbol decodes to the digits it carries, the check digits included. The decoder validates
    /// a modulo 10 check digit only when it is asked to assume one, so it returns every digit here.
    /// </summary>
    /// <param name="checkDigit">The check digits the symbol carries.</param>
    /// <param name="expected">The digits the symbol carries.</param>
    [Theory]
    [InlineData(MsiCheckDigit.None, "1234567")]
    [InlineData(MsiCheckDigit.Modulo10, "12345674")]
    [InlineData(MsiCheckDigit.NcrModulo1110, "123456790")]
    public void Msi_RoundTrips(MsiCheckDigit checkDigit, string expected)
        => AssertDecodes(new MsiSymbology(checkDigit), "1234567", BarcodeFormat.MSI, expected);

    /// <summary>
    /// A one-track Pharmacode symbol decodes to the number it carries.
    /// </summary>
    [Fact]
    public void Pharmacode_RoundTrips()
        => AssertDecodes(new PharmacodeSymbology(), "117480", BarcodeFormat.PHARMA_CODE, "117480");

    /// <summary>
    /// An Identcode decodes to the twelve digits the symbol carries, which is the data and the check
    /// digit. The full stops and the spaces belong to the printed line alone.
    /// </summary>
    [Fact]
    public void Identcode_RoundTrips()
        => AssertDecodes(new IdentcodeSymbology(), "56310243031", BarcodeFormat.ITF, "563102430313");

    /// <summary>
    /// A PZN decodes to the identifier ISO/IEC 15418 gives it, the digits and the check digit, which is
    /// what the symbol carries; the term PZN and the spaces around the identifier are printed only.
    /// </summary>
    [Fact]
    public void Pzn8_RoundTrips()
        => AssertDecodes(
            new Pzn8Symbology(),
            "03752864",
            BarcodeFormat.CODE_39,
            "-03752864",
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A Code 32 symbol decodes to the six base 32 characters that carry the nine digit AIC code. The
    /// letter A and the digits themselves belong to the printed line alone.
    /// </summary>
    [Fact]
    public void Code32_RoundTrips()
        => AssertDecodes(
            new Code32Symbology(),
            "012345676",
            BarcodeFormat.CODE_39,
            "0CSSBD",
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A PZN7 carries one digit fewer than a PZN8, and the same identifier in front of it.
    /// </summary>
    [Fact]
    public void Pzn7_RoundTrips()
        => AssertDecodes(
            new Pzn7Symbology(),
            "1234562",
            BarcodeFormat.CODE_39,
            "-1234562",
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A HIBC Code 39 decodes to the flag character, the data and the check character, which is what the
    /// symbol carries; the delimiters belong to the human readable interpretation alone.
    /// </summary>
    [Fact]
    public void HibcCode39_RoundTrips()
        => AssertDecodes(
            new HibcCode39Symbology(),
            "A123BJC5D6E71",
            BarcodeFormat.CODE_39,
            "+A123BJC5D6E71G",
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A Code 39 Extended symbol is an ordinary Code 39 symbol, so A.3.1 needs the decoder put into full
    /// ASCII mode before the substituted pairs read back as the characters they stand for.
    /// </summary>
    [Theory]
    [InlineData("Code39")]
    [InlineData("abc")]
    [InlineData("a@b")]
    [InlineData("Hello, World!")]
    public void Code39Extended_RoundTrips(string text)
    {
        BarcodeOptions options = CreateOptions();
        AssertDecodes(
            new Code39ExtendedSymbology(),
            text,
            BarcodeFormat.CODE_39,
            text,
            options,
            reader => reader.Options.UseCode39ExtendedMode = true);
    }

    /// <summary>
    /// The decoder must also read the symbol with the human readable interpretation present, proving the
    /// text does not intrude into the bars or quiet zones.
    /// </summary>
    [Fact]
    public void Ean13_RoundTrips_WithText()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(21.5F);
        AssertDecodes(new Ean13Symbology(), "5901234123457", BarcodeFormat.EAN_13, "5901234123457", options);
    }

    /// <summary>
    /// A Data Matrix symbol decodes to the characters it carries, whichever encodation schemes hold them:
    /// ASCII digit pairs, C40, Text, X12, EDIFACT and Base 256. A message in format 05 or 06 decodes to
    /// its header and trailer, which the 05 or 06 Macro stands for.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("This is Data Matrix!")]
    [InlineData("0123456789012345678901234567890123456789")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("ABC*DEF>GHI\r123*XYZ>")]
    [InlineData("ABC.DEF/GHI-JKL@MNO^PQR")]
    [InlineData("éèêëàâäöüß")]
    [InlineData("[)>\u001e05\u001d0123456789\u001e\u0004")]
    public void DataMatrix_RoundTrips(string text)
        => AssertDecodes(new DataMatrixSymbology(), text, BarcodeFormat.DATA_MATRIX, text);

    [Fact]
    public void DataMatrixRectangular_RoundTrips()
        => AssertDecodes(new DataMatrixRectangularSymbology(), "This is Data Matrix!", BarcodeFormat.DATA_MATRIX, "This is Data Matrix!");

    /// <summary>
    /// The 144 x 144 symbol, the largest, with ten interleaved Reed-Solomon blocks.
    /// </summary>
    [Fact]
    public void DataMatrix_LargestSymbolRoundTrips()
    {
        string text = string.Concat(Enumerable.Repeat("0123456789", 311)) + "012345";
        AssertDecodes(new DataMatrixSymbology(), text, BarcodeFormat.DATA_MATRIX, text);
    }

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the Function 1 Symbol Character at the start marks
    /// GS1 data, and a separator after a variable length element string is transmitted as &lt;GS&gt;.
    /// This decoder returns every Function 1 Symbol Character as &lt;GS&gt;, the leading one included,
    /// rather than the symbology identifier ]d2 of section 5.6.3.3.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_RoundTrips()
        => AssertDecodes(
            new Gs1DataMatrixSymbology(),
            "(01)09521234543213(10)ABC123(21)XYZ789",
            BarcodeFormat.DATA_MATRIX,
            "\u001d0109521234543213" + "10ABC123\u001d" + "21XYZ789");

    [Fact]
    public void HibcDataMatrix_RoundTrips()
        => AssertDecodes(new HibcDataMatrixSymbology(), "A123BJC5D6E71", BarcodeFormat.DATA_MATRIX, "+A123BJC5D6E71G");

    /// <summary>
    /// Section 5.9 of the GS1 General Specifications: the Data Matrix symbol carries the GS1 Digital Link URI
    /// as plain data, so it decodes to the URI as written.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkDataMatrix_RoundTrips()
        => AssertDecodes(
            new Gs1DigitalLinkDataMatrixSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            BarcodeFormat.DATA_MATRIX,
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987");

    /// <summary>
    /// QR Code at each error correction level, in Numeric, Alphanumeric and Byte mode segments.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="level">The error correction level.</param>
    [Theory]
    [InlineData("QR Code Symbol", QrErrorCorrectionLevel.L)]
    [InlineData("Hello, World!", QrErrorCorrectionLevel.M)]
    [InlineData("https://sixlabors.com/products/imagesharp-drawing/", QrErrorCorrectionLevel.Q)]
    [InlineData("123456789012ABCDEFGH123456789012abcdefgh", QrErrorCorrectionLevel.H)]
    [InlineData("MiXeD cAsE 42 MiXeD $%*+-./:", QrErrorCorrectionLevel.M)]
    public void QrCode_RoundTrips(string text, QrErrorCorrectionLevel level)
        => AssertDecodes(new QrCodeSymbology(level), text, BarcodeFormat.QR_CODE, text);

    /// <summary>
    /// Section 6.3.4: Byte mode carries ISO/IEC 8859-1. The decoder guesses the character set of Byte mode
    /// data, so the test names it.
    /// </summary>
    [Fact]
    public void QrCode_Iso88591RoundTrips()
        => AssertDecodes(
            new QrCodeSymbology(),
            "éèêëàâäöüß",
            BarcodeFormat.QR_CODE,
            "éèêëàâäöüß",
            CreateOptions(),
            static reader => reader.Options.CharacterSet = "ISO-8859-1");

    /// <summary>
    /// Table 7: a version 40-L symbol holds 7089 digits in 25 interleaved Reed-Solomon blocks.
    /// </summary>
    [Fact]
    public void QrCode_LargestSymbolRoundTrips()
    {
        string text = string.Concat(Enumerable.Repeat("0123456789", 708)) + "012345678";
        AssertDecodes(new QrCodeSymbology(QrErrorCorrectionLevel.L), text, BarcodeFormat.QR_CODE, text);
    }

    /// <summary>
    /// Sections 6.4.8.1 and 13.4 of ISO/IEC 18004: in FNC1 first position mode, a separator is "%" in
    /// Alphanumeric mode or &lt;GS&gt; in Byte mode, both transmitted as &lt;GS&gt;, and "%%" is a "%" in the
    /// data. The decoder returns the data without the symbology identifier ]Q3 of section 5.7.4.3 of the GS1
    /// General Specifications.
    /// </summary>
    /// <param name="text">The element strings to encode.</param>
    /// <param name="expected">The transmitted data.</param>
    [Theory]
    [InlineData("(01)09521234543213(10)ABC123(21)XYZ789", "0109521234543213" + "10ABC123\u001d" + "21XYZ789")]
    [InlineData("(01)09521234543213(10)AB%CD(21)12%34", "0109521234543213" + "10AB%CD\u001d" + "2112%34")]
    [InlineData("(01)09521234543213(21)abc-def/123(10)xyz", "0109521234543213" + "21abc-def/123\u001d" + "10xyz")]
    public void Gs1QrCode_RoundTrips(string text, string expected)
        => AssertDecodes(new Gs1QrCodeSymbology(), text, BarcodeFormat.QR_CODE, expected);

    /// <summary>
    /// Section 4.1.1 of the Swiss Implementation Guidelines for the QR-bill: the data is UTF-8. The symbol
    /// draws 46 mm wide with the Swiss cross over its centre, and the error correction recovers the modules
    /// the cross covers. The decoder guesses the character set of Byte mode data, so the test names it.
    /// </summary>
    [Fact]
    public void SwissQrCode_RoundTrips()
    {
        // Example 3 of Annex A, Table 19, with a message of characters that take two and three bytes in UTF-8.
        const string text = "SPC\n0200\n1\nCH5204835012345671000\nS\nMuster Stiftung\nP.O. box\n\n3001\nBern\nCH\n\n\n\n\n\n\n\n\nCHF\n"
            + "\n\n\n\n\n\n\nNON\n\nSpende 100 € Ș ț ő ÿ\nEPD\n\n\n";
        AssertDecodes(
            new SwissQrCodeSymbology(),
            text,
            BarcodeFormat.QR_CODE,
            text,
            new BarcodeOptions { Dpi = 150F },
            static reader => reader.Options.CharacterSet = "UTF-8");
    }

    /// <summary>
    /// The D3 component stands outside the host QR Code symbol, so the host decodes as a QR Code symbol at
    /// every host ratio.
    /// </summary>
    /// <param name="hostRatio">The width of a D3 module in host modules.</param>
    [Theory]
    [InlineData(D3AqrHostRatio.Two)]
    [InlineData(D3AqrHostRatio.TwoAndAHalf)]
    [InlineData(D3AqrHostRatio.Three)]
    public void D3Aqr_RoundTrips(D3AqrHostRatio hostRatio)
        => AssertDecodes(
            new D3AqrSymbology(2842, QrErrorCorrectionLevel.M, hostRatio),
            "HTTPS://ID.GS1.ORG/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            BarcodeFormat.QR_CODE,
            "HTTPS://ID.GS1.ORG/01/09521234543213/22/ABC%2D123?99=XYZ-987");

    [Fact]
    public void HibcQrCode_RoundTrips()
        => AssertDecodes(new HibcQrCodeSymbology(), "A123BJC5D6E71", BarcodeFormat.QR_CODE, "+A123BJC5D6E71G");

    /// <summary>
    /// Aztec Code symbols decode to their text at each format: compact, full-range and HIBC with its flag and
    /// check characters.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    /// <param name="text">The text.</param>
    /// <param name="expected">The decoded text.</param>
    [Theory]
    [InlineData("Auto", "This is Aztec Code", "This is Aztec Code")]
    [InlineData("Auto", "Hello, World! a. b: c\r\nd", "Hello, World! a. b: c\r\nd")]
    [InlineData("Full", "This is Aztec Code", "This is Aztec Code")]
    [InlineData("Compact", "Compact", "Compact")]
    [InlineData("Hibc", "A123BJC5D6E71", "+A123BJC5D6E71G")]
    public void AztecCode_RoundTrips(string kind, string text, string expected)
    {
        BarcodeSymbology symbology = kind switch
        {
            "Auto" => new AztecCodeSymbology(),
            "Full" => new AztecCodeSymbology(23, 6),
            "Compact" => new AztecCodeCompactSymbology(),
            _ => new HibcAztecCodeSymbology(),
        };

        AssertDecodes(symbology, text, BarcodeFormat.AZTEC, expected);
    }

    /// <summary>
    /// A MaxiCode symbol decodes to its text: the 93 character message of Figure 2 of ISO/IEC 16023 in mode 4.
    /// </summary>
    [Fact]
    public void MaxiCode_RoundTrips()
        => AssertDecodes(
            new MaxiCodeSymbology(),
            "THIS IS A 93 CHARACTER CODE SET A MESSAGE THAT FILLS A MODE 4, UNAPPENDED, MAXICODE SYMBOL...",
            BarcodeFormat.MAXICODE,
            "THIS IS A 93 CHARACTER CODE SET A MESSAGE THAT FILLS A MODE 4, UNAPPENDED, MAXICODE SYMBOL...",
            new BarcodeOptions { Dpi = 300F },
            static reader => reader.Options.PureBarcode = true);

    /// <summary>
    /// Section 5.9 of the GS1 General Specifications: the QR Code symbol carries the GS1 Digital Link URI as
    /// plain data, so it decodes to the URI as written.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkQrCode_RoundTrips()
        => AssertDecodes(
            new Gs1DigitalLinkQrCodeSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            BarcodeFormat.QR_CODE,
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987");

    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected)
        => AssertDecodes(symbology, text, format, expected, CreateOptions(), null);

    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected, BarcodeOptions options)
        => AssertDecodes(symbology, text, format, expected, options, null);

    private static void AssertDecodes(
        BarcodeSymbology symbology,
        string text,
        BarcodeFormat format,
        string expected,
        BarcodeOptions options,
        Action<BarcodeReaderGeneric> configureReader)
    {
        // The image holds the symbol and a margin on every side, measured rather than assumed, so a
        // symbology whose symbol is wider than the next is not silently clipped.
        PointF origin = new(20, 20);
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, text, options, origin);

        using Image<Rgba32> image = new(
            (int)MathF.Ceiling(bounds.Right) + 20,
            (int)MathF.Ceiling(bounds.Bottom) + 20,
            Color.White.ToPixel<Rgba32>());

        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, text, options, origin)));

        byte[] pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        BarcodeReaderGeneric reader = new();
        reader.Options.PossibleFormats = [format];
        reader.Options.TryHarder = true;
        configureReader?.Invoke(reader);

        Result result = reader.Decode(new RGBLuminanceSource(pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32));

        Assert.NotNull(result);
        Assert.Equal(format, result.BarcodeFormat);
        Assert.Equal(expected, result.Text);
    }

    /// <summary>
    /// Draws at a one point X dimension and 216 dots per inch, 3 px per module, with a 40 point bar
    /// height, 120 px.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateOptions()
        => new()
        {
            Dpi = 216F,
            XDimension = BarcodeSymbology.PointXDimension,
            BarHeight = 40F * BarcodeSymbology.PointXDimension,
        };
}
