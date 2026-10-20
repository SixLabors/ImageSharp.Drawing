// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXingCpp;
using ZXingNet = ZXing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Round-trip tests: every rendered barcode must decode back to its input through an independent decoder. This proves the images are valid scannable symbols
/// without a human point of reference, which the module-sequence and golden image tests cannot do on their own. The decoder is zxing-cpp. MSI, Pharmacode and
/// Intelligent Mail, which zxing-cpp does not read, decode through ZXing.Net. The decoders return the transmitted data: a symbol that carries GS1 data reports
/// it through its symbology identifier, so the leading Function 1 Symbol Character is not in the data, and a later one is the &lt;GS&gt; separator. The
/// standalone add-on symbologies have no decode tests because the decoders read add-ons only as extensions of a main EAN/UPC symbol.
/// </summary>
public class BarcodeDecodeTests
{
    /// <summary>
    /// Example 3 of Annex A, Table 19, of the Swiss Implementation Guidelines for the QR-bill, with a message of characters that take two and three bytes in
    /// UTF-8.
    /// </summary>
    private const string SwissQrBillExample = "SPC\n0200\n1\nCH5204835012345671000\nS\nMuster Stiftung\nP.O. box\n\n3001\nBern\nCH\n\n\n\n\n\n\n\n\nCHF\n"
        + "\n\n\n\n\n\n\nNON\n\nSpende 100 € Ș ț ő ÿ\nEPD\n\n\n";

    /// <summary>
    /// Gets the resolutions at which a module of one point draws at 1 to 7 pixels. The module draws at a whole number of pixels, so these are all the
    /// module widths from 72 to 504 dots per inch.
    /// </summary>
    private static ReadOnlySpan<float> WholePixelModuleResolutions => [72F, 144F, 216F, 288F, 360F, 432F, 504F];

    /// <summary>
    /// Gets the resolutions from 72 to 504 dots per inch in steps of 18. A symbol of a fixed size draws its modules at a fraction of a pixel at most of them.
    /// </summary>
    private static ReadOnlySpan<float> FixedSizeResolutions =>
    [
        72F, 90F, 108F, 126F, 144F, 162F, 180F, 198F, 216F, 234F, 252F, 270F, 288F,
        306F, 324F, 342F, 360F, 378F, 396F, 414F, 432F, 450F, 468F, 486F, 504F,
    ];

    /// <summary>
    /// An EAN-13 symbol decodes to its thirteen digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("5901234123457")]
    [InlineData("4006381333931")]
    [InlineData("9780306406157")]
    public void Ean13_RoundTrips(string text)
        => AssertDecodes(new Ean13Symbology(), text, BarcodeFormat.EAN13, text);

    /// <summary>
    /// An EAN-8 symbol decodes to its eight digits.
    /// </summary>
    [Fact]
    public void Ean8_RoundTrips()
        => AssertDecodes(new Ean8Symbology(), "96385074", BarcodeFormat.EAN8, "96385074");

    /// <summary>
    /// The decoder transmits a UPC-A symbol as the thirteen digit GTIN-13 form, with a leading zero.
    /// </summary>
    [Fact]
    public void UpcA_RoundTrips()
        => AssertDecodes(new UpcASymbology(), "036000291452", BarcodeFormat.UPCA, "0036000291452");

    /// <summary>
    /// The decoder transmits a UPC-E symbol as the GTIN-13 form of the UPC-A number it stands for. The zero suppression rule of the last data digit gives that
    /// number: for 04252614 the last data digit 1 makes the manufacturer number 42100 and the item number 00526, and for 01234565 the last data digit 6 makes
    /// the manufacturer number 12345 and the item number 00006.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The GTIN-13 form.</param>
    [Theory]
    [InlineData("01234565", "0012345000065")]
    [InlineData("04252614", "0042100005264")]
    public void UpcE_RoundTrips(string text, string expected)
        => AssertDecodes(new UpcESymbology(), text, BarcodeFormat.UPCE, expected);

    /// <summary>
    /// An ISBN symbol is an EAN-13 symbol. It decodes to the thirteen digits of the ISBN without the hyphens.
    /// </summary>
    [Fact]
    public void Isbn_RoundTrips()
        => AssertDecodes(new IsbnSymbology(), "978-0-306-40615-7", BarcodeFormat.EAN13, "9780306406157");

    /// <summary>
    /// An ISMN symbol is an EAN-13 symbol. It decodes to the thirteen digits of the ISMN without the hyphens.
    /// </summary>
    [Fact]
    public void Ismn_RoundTrips()
        => AssertDecodes(new IsmnSymbology(), "979-0-2600-0043-8", BarcodeFormat.EAN13, "9790260000438");

    /// <summary>
    /// An ISSN symbol is an EAN-13 symbol. It decodes to 977, the seven data digits of the ISSN, the sequence variant 00 and the EAN-13 check digit.
    /// </summary>
    [Fact]
    public void Issn_RoundTrips()
        => AssertDecodes(new IssnSymbology(), "0317-8471", BarcodeFormat.EAN13, "9770317847001");

    /// <summary>
    /// An M&amp;S symbol is an EAN-8 symbol. A seven digit number decodes with its leading zero.
    /// </summary>
    [Fact]
    public void Mands_RoundTrips()
        => AssertDecodes(new MandsSymbology(), "0642118", BarcodeFormat.EAN8, "00642118");

    /// <summary>
    /// A Code 128 symbol decodes to its data.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("CODE128")]
    [InlineData("ABC-123")]
    public void Code128_RoundTrips(string text)
        => AssertDecodes(new Code128Symbology(), text, BarcodeFormat.Code128, text);

    /// <summary>
    /// A GS1-128 symbol starts with the Function 1 Symbol Character, which the decoder reports as the symbology identifier ]C1. The data is the element strings
    /// without the parentheses, which section 4.14 rule 2c keeps to the human readable interpretation.
    /// </summary>
    /// <param name="text">The element strings to encode.</param>
    /// <param name="expected">The transmitted data.</param>
    [Theory]
    [InlineData("(01)09521234543213", "0109521234543213")]
    [InlineData("(91)ABC123", "91ABC123")]
    public void Gs1_128_RoundTrips(string text, string expected)
        => AssertDecodes(new Gs1128Symbology(), text, BarcodeFormat.Code128, "]C1", expected);

    /// <summary>
    /// An EAN-14 decodes to its Application Identifier and the fourteen digit Global Trade Item Number, as GS1 data.
    /// </summary>
    [Fact]
    public void Ean14_RoundTrips()
        => AssertDecodes(new Ean14Symbology(), "(01)09521234543213", BarcodeFormat.Code128, "]C1", "0109521234543213");

    /// <summary>
    /// An SSCC-18 decodes to its Application Identifier and the eighteen digit Serial Shipping Container Code, as GS1 data.
    /// </summary>
    [Fact]
    public void Sscc18_RoundTrips()
        => AssertDecodes(new Sscc18Symbology(), "(00)106141411234567897", BarcodeFormat.Code128, "]C1", "00106141411234567897");

    /// <summary>
    /// An Intelligent Mail package barcode starts with the Function 1 Symbol Character, which the decoder reports as the symbology identifier ]C1, and section
    /// 2.2.1 of USPS2000508 places a second one after the ZIP Code, which the decoder reports as the &lt;GS&gt; separator before the PIC.
    /// </summary>
    [Fact]
    public void IntelligentMailPackage_RoundTrips()
        => AssertDecodes(
            new IntelligentMailPackageSymbology("USPS TRACKING #"),
            "(420)123456789(92)12391234567812345670",
            BarcodeFormat.Code128,
            "]C1",
            "420123456789\u001D9212391234567812345670",
            new BarcodeOptions
            {
                XDimension = BarcodeSymbology.DefaultXDimension,
                Font = BarcodeFonts.OcrB.CreateFont(10.75F),
            });

    /// <summary>
    /// A HIBC Code 128 decodes to the flag character, the data and the check character, which is what the symbol carries; the delimiters belong to the human
    /// readable interpretation alone.
    /// </summary>
    [Fact]
    public void HibcCode128_RoundTrips()
        => AssertDecodes(new HibcCode128Symbology(), "A123BJC5D6E71", BarcodeFormat.Code128, "+A123BJC5D6E71G");

    /// <summary>
    /// A Code 39 symbol decodes to its data. The start and stop character are symbol overhead, and the asterisks that stand for them belong to the human
    /// readable interpretation alone. The decoder reads standard Code 39, not the full ASCII mode that A.3 calls a non-standard transmission mode, so a pair
    /// such as <c>+A</c> reads back as the two characters the symbol carries rather than as one lowercase letter.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("CODE39")]
    [InlineData("ABC-123")]
    public void Code39_RoundTrips(string text)
        => AssertDecodes(new Code39Symbology(), text, BarcodeFormat.Code39Std, text);

    /// <summary>
    /// A Code 93 symbol decodes to its data. The two check characters are symbol overhead, so the decoder validates them and removes them from the result.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("CODE93")]
    [InlineData("ABC-123")]
    public void Code93_RoundTrips(string text)
        => AssertDecodes(new Code93Symbology(), text, BarcodeFormat.Code93, text);

    /// <summary>
    /// A Code 93 Extended symbol is an ordinary Code 93 symbol, and the decoder reverses the Table 3 substitution on its own, so the ASCII characters come back
    /// as they went in.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("Code93")]
    [InlineData("abc")]
    [InlineData("a@b")]
    [InlineData("Hello, World!")]
    public void Code93Extended_RoundTrips(string text)
        => AssertDecodes(new Code93ExtendedSymbology(), text, BarcodeFormat.Code93, text);

    /// <summary>
    /// An Interleaved 2 of 5 symbol decodes to the digits it carries: the leading zero that makes an odd count a pair, the data and the check digit when the
    /// symbol carries one. The decoder does not validate a check digit, because the symbology does not require one.
    /// </summary>
    /// <param name="checkDigit"> Whether the symbol carries the check digit, and whether the encoder calculates it or validates it. </param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The digits the symbol carries.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, "1234", "1234")]
    [InlineData(CheckCharacterMode.None, "123", "0123")]
    [InlineData(CheckCharacterMode.Compute, "12345", "123457")]
    [InlineData(CheckCharacterMode.Compute, "1234", "012348")]
    public void Interleaved2Of5_RoundTrips(CheckCharacterMode checkDigit, string text, string expected)
        => AssertDecodes(new Interleaved2Of5Symbology(checkDigit), text, BarcodeFormat.ITF, expected);

    /// <summary>
    /// A Leitcode decodes to the fourteen digits the symbol carries, which is the data and the check digit. The full stops and the space belong to the printed
    /// line alone.
    /// </summary>
    [Fact]
    public void Leitcode_RoundTrips()
        => AssertDecodes(new LeitcodeSymbology(), "2134807501640", BarcodeFormat.ITF, "21348075016401");

    /// <summary>
    /// An ITF-14 symbol decodes to the fourteen digits it carries. The bearer bar lies outside the quiet zones, so the decoder finds the clear space it needs
    /// on both sides of the bars.
    /// </summary>
    [Fact]
    public void Itf14_RoundTrips()
        => AssertDecodes(new Itf14Symbology(), "1540014128876", BarcodeFormat.ITF, "15400141288763");

    /// <summary>
    /// A Codabar symbol decodes to its start character, its data, the check character when the symbol carries one, and its stop character. The decoder does
    /// not validate a check character, because the symbology does not define one.
    /// </summary>
    /// <param name="checkCharacter">Whether the symbol carries the check character, and whether the encoder calculates it or validates it.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The characters the symbol carries.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, "A40156B", "A40156B")]
    [InlineData(CheckCharacterMode.Compute, "A40156B", "A40156+B")]
    public void Codabar_RoundTrips(CheckCharacterMode checkCharacter, string text, string expected)
        => AssertDecodes(new CodabarSymbology(checkCharacter), text, BarcodeFormat.Codabar, expected);

    /// <summary>
    /// An MSI symbol decodes to the digits it carries, the check digits included. The ZXing.Net decoder validates a modulo 10 check digit only when it is asked
    /// to assume one, so it returns every digit here.
    /// </summary>
    /// <param name="checkDigit">The check digits the symbol carries.</param>
    /// <param name="expected">The digits the symbol carries.</param>
    [Theory]
    [InlineData(MsiCheckDigit.None, "1234567")]
    [InlineData(MsiCheckDigit.Modulo10, "12345674")]
    [InlineData(MsiCheckDigit.NcrModulo1110, "123456790")]
    public void Msi_RoundTrips(MsiCheckDigit checkDigit, string expected)
        => AssertDecodesWithZXingNet(new MsiSymbology(checkDigit), "1234567", ZXingNet.BarcodeFormat.MSI, expected);

    /// <summary>
    /// A one-track Pharmacode symbol decodes to the number it carries.
    /// </summary>
    [Fact]
    public void Pharmacode_RoundTrips()
        => AssertDecodesWithZXingNet(new PharmacodeSymbology(), "117480", ZXingNet.BarcodeFormat.PHARMA_CODE, "117480");

    /// <summary>
    /// An Intelligent Mail barcode decodes to the 20-digit tracking code and the 11-digit routing code it carries, the fourth example of Appendix C of
    /// USPS-B-3200.
    /// </summary>
    [Fact]
    public void IntelligentMail_RoundTrips()
        => AssertDecodesWithZXingNet(
            new IntelligentMailSymbology(),
            "0123456709498765432101234567891",
            ZXingNet.BarcodeFormat.IMB,
            "0123456709498765432101234567891");

    /// <summary>
    /// An Identcode decodes to the twelve digits the symbol carries, which is the data and the check digit. The full stops and the spaces belong to the printed
    /// line alone.
    /// </summary>
    [Fact]
    public void Identcode_RoundTrips()
        => AssertDecodes(new IdentcodeSymbology(), "56310243031", BarcodeFormat.ITF, "563102430313");

    /// <summary>
    /// A PZN decodes to the identifier ISO/IEC 15418 gives it, the digits and the check digit, which is what the symbol carries; the term PZN and the spaces
    /// around the identifier are printed only.
    /// </summary>
    [Fact]
    public void Pzn8_RoundTrips()
        => AssertDecodes(new Pzn8Symbology(), "03752864", BarcodeFormat.PZN, "-03752864");

    /// <summary>
    /// A Code 32 symbol is a standard Code 39 symbol of the six base 32 characters that carry the nine digit AIC code. The letter A and the digits themselves
    /// belong to the printed line alone.
    /// </summary>
    [Fact]
    public void Code32_RoundTrips()
        => AssertDecodes(new Code32Symbology(), "012345676", BarcodeFormat.Code39Std, "0CSSBD");

    /// <summary>
    /// A PZN7 carries one digit fewer than a PZN8, and the same identifier in front of it, in a standard Code 39 symbol.
    /// </summary>
    [Fact]
    public void Pzn7_RoundTrips()
        => AssertDecodes(new Pzn7Symbology(), "1234562", BarcodeFormat.Code39Std, "-1234562");

    /// <summary>
    /// A HIBC Code 39 decodes to the flag character, the data and the check character, which is what the symbol carries; the delimiters belong to the human
    /// readable interpretation alone.
    /// </summary>
    [Fact]
    public void HibcCode39_RoundTrips()
        => AssertDecodes(new HibcCode39Symbology(), "A123BJC5D6E71", BarcodeFormat.Code39Std, "+A123BJC5D6E71G");

    /// <summary>
    /// A Code 39 Extended symbol is an ordinary Code 39 symbol, so A.3.1 needs the decoder put into full ASCII mode before the substituted pairs read back as
    /// the characters they stand for.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("Code39")]
    [InlineData("abc")]
    [InlineData("a@b")]
    [InlineData("Hello, World!")]
    public void Code39Extended_RoundTrips(string text)
        => AssertDecodes(new Code39ExtendedSymbology(), text, BarcodeFormat.Code39Ext, text);

    /// <summary>
    /// A Telepen symbol decodes to its ASCII data. The check character is symbol overhead, so the decoder validates it and removes it from the result.
    /// </summary>
    [Fact]
    public void Telepen_RoundTrips()
        => AssertDecodes(new TelepenSymbology(), "ABC123", BarcodeFormat.TelepenAlpha, "ABC123");

    /// <summary>
    /// A Telepen Numeric symbol carries a pair of digits in each character, and the decoder reads it as numeric data.
    /// </summary>
    [Fact]
    public void TelepenNumeric_RoundTrips()
        => AssertDecodes(new TelepenNumericSymbology(), "1234", BarcodeFormat.TelepenNumeric, "1234");

    /// <summary>
    /// An EAN-13 symbol with its human readable interpretation decodes to its digits. The text does not go into the bars or the quiet zones.
    /// </summary>
    [Fact]
    public void Ean13_RoundTrips_WithText()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(21.5F);

        AssertDecodes(new Ean13Symbology(), "5901234123457", BarcodeFormat.EAN13, "5901234123457", options);
    }

    /// <summary>
    /// Every GS1 DataBar symbol carries GS1 data, which the decoder reports through the symbology identifier ]e0. The data is the element strings without the
    /// parentheses of the human readable interpretation.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    [Theory]
    [InlineData("Omnidirectional")]
    [InlineData("Truncated")]
    [InlineData("Stacked")]
    [InlineData("StackedOmnidirectional")]
    [InlineData("Limited")]
    [InlineData("Expanded")]
    [InlineData("ExpandedStacked")]
    public void Gs1DataBar_RoundTrips(string kind)
    {
        (BarcodeSymbology symbology, BarcodeFormat format) = kind switch
        {
            "Omnidirectional" => ((BarcodeSymbology)new Gs1DataBarOmnidirectionalSymbology(), BarcodeFormat.DataBarOmni),
            "Truncated" => (new Gs1DataBarTruncatedSymbology(), BarcodeFormat.DataBarOmni),
            "Stacked" => (new Gs1DataBarStackedSymbology(), BarcodeFormat.DataBarStk),
            "StackedOmnidirectional" => (new Gs1DataBarStackedOmnidirectionalSymbology(), BarcodeFormat.DataBarStkOmni),
            "Limited" => (new Gs1DataBarLimitedSymbology(), BarcodeFormat.DataBarLtd),
            "Expanded" => (new Gs1DataBarExpandedSymbology(), BarcodeFormat.DataBarExp),
            _ => (new Gs1DataBarExpandedStackedSymbology(), BarcodeFormat.DataBarExpStk),
        };

        (string text, string expected) = kind switch
        {
            "Limited" => ("(01)15012345678907", "0115012345678907"),
            "Expanded" or "ExpandedStacked" => ("(01)90614141000015(3202)000150", "01906141410000153202000150"),
            _ => ("(01)09521234543213", "0109521234543213"),
        };

        AssertDecodes(symbology, text, format, "]e0", expected);
    }

    /// <summary>
    /// The linear component of a GS1 Composite symbol decodes to its data with its linkage flag set and the separator pattern above it. zxing-cpp reads the
    /// linear component only and has no decoder for the 2D component. The data is that of the figures of ISO/IEC 24723.
    /// </summary>
    /// <param name="kind">The composite symbology.</param>
    /// <param name="linear">The element strings of the linear component.</param>
    /// <param name="component">The element strings of the 2D component.</param>
    /// <param name="expected">The transmitted data.</param>
    [Theory]
    [InlineData("Omnidirectional", "(01)03612345678904", "(11)990102", "0103612345678904")]
    [InlineData("Truncated", "(01)03612345678904", "(11)990102", "0103612345678904")]
    [InlineData("Stacked", "(01)03412345678900", "(17)010200", "0103412345678900")]
    [InlineData("StackedOmnidirectional", "(01)03612345678904", "(11)990102", "0103612345678904")]
    [InlineData("Limited", "(01)13112345678906", "(17)010615(10)A123456", "0113112345678906")]
    [InlineData("Expanded", "(01)93712345678904(3103)001234", "(91)1A2B3C4D5E", "01937123456789043103001234")]
    [InlineData("ExpandedStacked", "(01)00012345678905(10)ABCDEF", "(21)12345678", "010001234567890510ABCDEF")]
    [InlineData("Gs1128", "(01)03212345678906", "(21)A1B2C3D4E5F6G7H8", "0103212345678906")]
    [InlineData("Gs1128CcC", "(01)93812345678901", "(10)ABCD123456(410)3898765432108", "0193812345678901")]
    public void Gs1Composite_LinearComponentRoundTrips(string kind, string linear, string component, string expected)
    {
        (CompositeBarcodeSymbology symbology, BarcodeFormat format) = kind switch
        {
            "Omnidirectional" => ((CompositeBarcodeSymbology)new Gs1DataBarOmnidirectionalCompositeSymbology(), BarcodeFormat.DataBarOmni),
            "Truncated" => (new Gs1DataBarTruncatedCompositeSymbology(), BarcodeFormat.DataBarOmni),
            "Stacked" => (new Gs1DataBarStackedCompositeSymbology(), BarcodeFormat.DataBarStk),
            "StackedOmnidirectional" => (new Gs1DataBarStackedOmnidirectionalCompositeSymbology(), BarcodeFormat.DataBarStkOmni),
            "Limited" => (new Gs1DataBarLimitedCompositeSymbology(), BarcodeFormat.DataBarLtd),
            "Expanded" => (new Gs1DataBarExpandedCompositeSymbology(), BarcodeFormat.DataBarExp),
            "ExpandedStacked" => (new Gs1DataBarExpandedStackedCompositeSymbology(), BarcodeFormat.DataBarExpStk),
            "Gs1128" => (new Gs1128CompositeSymbology(), BarcodeFormat.Code128),
            _ => (new Gs1128CompositeSymbology(Gs1CompositeComponent.CcC), BarcodeFormat.Code128),
        };

        PointF origin = new(20, 20);
        BarcodeOptions options = CreateOptions();
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, origin, linear, component);
        using Image<L8> image = new((int)MathF.Ceiling(bounds.Right) + 20, (int)MathF.Ceiling(bounds.Bottom) + 20, new L8(255));

        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, origin, linear, component)));

        byte[] pixels = new byte[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);

        using ReaderOptions reader = new() { Formats = format, TryHarder = true, TextMode = TextMode.Plain };
        Barcode[] barcodes = BarcodeReader.Read(new ImageView(pixels, image.Width, image.Height, ImageFormat.Lum, 0, 0), reader);
        try
        {
            Barcode barcode = Assert.Single(barcodes);
            Assert.Equal(expected, barcode.Text);
        }
        finally
        {
            foreach (Barcode barcode in barcodes)
            {
                barcode.Dispose();
            }
        }
    }

    /// <summary>
    /// The GS1 North American Coupon is a GS1 DataBar Expanded Stacked symbol of the element string (8110).
    /// </summary>
    [Fact]
    public void Gs1NorthAmericanCoupon_RoundTrips()
        => AssertDecodes(
            new Gs1NorthAmericanCouponSymbology(),
            "(8110)106141416543213500110000310123196000",
            BarcodeFormat.DataBarExpStk,
            "]e0",
            "8110106141416543213500110000310123196000");

    /// <summary>
    /// PDF417 decodes to its text in the full, compact and micro forms, and with the HIBC flag and check characters.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    /// <param name="text">The text.</param>
    /// <param name="expected">The decoded text.</param>
    [Theory]
    [InlineData("Full", "PDF417 Symbology Standard", "PDF417 Symbology Standard")]
    [InlineData("Compact", "PDF417 Symbology Standard", "PDF417 Symbology Standard")]
    [InlineData("Micro", "MicroPDF417", "MicroPDF417")]
    [InlineData("Hibc", "A123BJC5D6E71", "+A123BJC5D6E71G")]
    [InlineData("HibcMicro", "A123BJC5D6E71", "+A123BJC5D6E71G")]
    public void Pdf417_RoundTrips(string kind, string text, string expected)
    {
        (BarcodeSymbology symbology, BarcodeFormat format) = kind switch
        {
            "Full" => ((BarcodeSymbology)new Pdf417Symbology(), BarcodeFormat.PDF417),
            "Compact" => (new CompactPdf417Symbology(), BarcodeFormat.CompactPDF417),
            "Micro" => (new MicroPdf417Symbology(), BarcodeFormat.MicroPDF417),
            "Hibc" => (new HibcPdf417Symbology(), BarcodeFormat.PDF417),
            _ => (new HibcMicroPdf417Symbology(), BarcodeFormat.MicroPDF417),
        };

        AssertDecodes(symbology, text, format, expected);
    }

    /// <summary>
    /// A Data Matrix symbol decodes to the characters it carries, whichever encodation schemes hold them: ASCII digit pairs, C40, Text, X12, EDIFACT and Base
    /// 256. A message in format 05 or 06 decodes to its header and trailer, which the 05 or 06 Macro stands for.
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
        => AssertDecodes(new DataMatrixSymbology(), text, BarcodeFormat.DataMatrix, text);

    /// <summary>
    /// A rectangular Data Matrix symbol decodes to its text.
    /// </summary>
    [Fact]
    public void DataMatrixRectangular_RoundTrips()
        => AssertDecodes(new DataMatrixRectangularSymbology(), "This is Data Matrix!", BarcodeFormat.DataMatrix, "This is Data Matrix!");

    /// <summary>
    /// A Data Matrix rectangular extension symbol of ISO/IEC 21471 decodes as Data Matrix.
    /// </summary>
    [Fact]
    public void DataMatrixRectangularExtension_RoundTrips()
        => AssertDecodes(
            new DataMatrixRectangularExtensionSymbology(),
            "This is Data Matrix rectangular extension",
            BarcodeFormat.DataMatrix,
            "This is Data Matrix rectangular extension");

    /// <summary>
    /// The largest Data Matrix symbol, 144 x 144 with ten interleaved Reed-Solomon blocks, decodes to its 3116 digits.
    /// </summary>
    [Fact]
    public void DataMatrix_LargestSymbolRoundTrips()
    {
        string text = string.Concat(Enumerable.Repeat("0123456789", 311)) + "012345";

        AssertDecodes(new DataMatrixSymbology(), text, BarcodeFormat.DataMatrix, text);
    }

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the Function 1 Symbol Character at the start marks GS1 data, which the decoder reports as the
    /// symbology identifier ]d2 of section 5.6.3.3, and a separator after a variable length element string is transmitted as &lt;GS&gt;.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_RoundTrips()
        => AssertDecodes(
            new Gs1DataMatrixSymbology(),
            "(01)09521234543213(10)ABC123(21)XYZ789",
            BarcodeFormat.DataMatrix,
            "]d2",
            "0109521234543213" + "10ABC123\u001d" + "21XYZ789");

    /// <summary>
    /// A HIBC Data Matrix symbol decodes to the flag character, the data and the check character. The delimiters are only in the human readable
    /// interpretation.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_RoundTrips()
        => AssertDecodes(new HibcDataMatrixSymbology(), "A123BJC5D6E71", BarcodeFormat.DataMatrix, "+A123BJC5D6E71G");

    /// <summary>
    /// A 2D Mailmark barcode is a Data Matrix symbol of the Royal Mail fields and the customer content as written.
    /// </summary>
    [Fact]
    public void Mailmark2D_RoundTrips()
        => AssertDecodes(
            new Mailmark2DSymbology(Mailmark2DType.Type29),
            "JGB 012100123412345678AB19XY1A 0             www.xyz.com",
            BarcodeFormat.DataMatrix,
            "JGB 012100123412345678AB19XY1A 0             www.xyz.com");

    /// <summary>
    /// Section 5.9 of the GS1 General Specifications: the Data Matrix symbol carries the GS1 Digital Link URI as plain data, so it decodes to the URI as
    /// written.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkDataMatrix_RoundTrips()
        => AssertDecodes(
            new Gs1DigitalLinkDataMatrixSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            BarcodeFormat.DataMatrix,
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987");

    /// <summary>
    /// A QR Code symbol decodes to its text at each error correction level, with Numeric, Alphanumeric and Byte mode segments.
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
        => AssertDecodes(new QrCodeSymbology(level), text, BarcodeFormat.QRCode, text);

    /// <summary>
    /// Section 6.3.4 of ISO/IEC 18004: Byte mode carries ISO/IEC 8859-1. A QR Code symbol of ISO/IEC 8859-1 letters decodes to those letters.
    /// </summary>
    [Fact]
    public void QrCode_Iso88591RoundTrips()
        => AssertDecodes(new QrCodeSymbology(), "éèêëàâäöüß", BarcodeFormat.QRCode, "éèêëàâäöüß");

    /// <summary>
    /// Table 7 of ISO/IEC 18004: a version 40-L symbol holds 7089 digits in 25 interleaved Reed-Solomon blocks. That symbol decodes to its digits.
    /// </summary>
    [Fact]
    public void QrCode_LargestSymbolRoundTrips()
    {
        string text = string.Concat(Enumerable.Repeat("0123456789", 708)) + "012345678";

        AssertDecodes(new QrCodeSymbology(QrErrorCorrectionLevel.L), text, BarcodeFormat.QRCode, text);
    }

    /// <summary>
    /// Sections 6.4.8.1 and 13.4 of ISO/IEC 18004: in FNC1 first position mode, a separator is "%" in Alphanumeric mode or &lt;GS&gt; in Byte mode, both
    /// transmitted as &lt;GS&gt;, and "%%" is a "%" in the data. The decoder reports the mode as the symbology identifier ]Q3 of section 5.7.4.3 of the GS1
    /// General Specifications.
    /// </summary>
    /// <param name="text">The element strings to encode.</param>
    /// <param name="expected">The transmitted data.</param>
    [Theory]
    [InlineData("(01)09521234543213(10)ABC123(21)XYZ789", "0109521234543213" + "10ABC123\u001d" + "21XYZ789")]
    [InlineData("(01)09521234543213(10)AB%CD(21)12%34", "0109521234543213" + "10AB%CD\u001d" + "2112%34")]
    [InlineData("(01)09521234543213(21)abc-def/123(10)xyz", "0109521234543213" + "21abc-def/123\u001d" + "10xyz")]
    public void Gs1QrCode_RoundTrips(string text, string expected)
        => AssertDecodes(new Gs1QrCodeSymbology(), text, BarcodeFormat.QRCode, "]Q3", expected);

    /// <summary>
    /// Section 4.1.1 of the Swiss Implementation Guidelines for the QR-bill: the data is UTF-8. The symbol draws 46 mm wide with the Swiss cross over its
    /// centre, and the error correction recovers the modules the cross covers.
    /// </summary>
    [Fact]
    public void SwissQrCode_RoundTrips()
        => AssertDecodes(new SwissQrCodeSymbology(), SwissQrBillExample, BarcodeFormat.QRCode, SwissQrBillExample);

    /// <summary>
    /// Section 6.4 of the Swiss Implementation Guidelines for the QR-bill: the symbol "must always be 46 x 46 mm". The modules of that size are a fraction of a
    /// pixel at most resolutions. The symbol decodes at every resolution from 72 to 504 dots per inch in steps of 18.
    /// </summary>
    [Fact]
    public void SwissQrCode_RoundTripsAtEveryResolution()
        => AssertDecodesAtResolutions(
            new SwissQrCodeSymbology(),
            SwissQrBillExample,
            BarcodeFormat.QRCode,
            SwissQrBillExample,
            FixedSizeResolutions,
            null,
            null);

    /// <summary>
    /// The D3 component stands outside the host QR Code symbol, so the host decodes as a QR Code symbol at every host ratio.
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
            BarcodeFormat.QRCode,
            "HTTPS://ID.GS1.ORG/01/09521234543213/22/ABC%2D123?99=XYZ-987");

    /// <summary>
    /// A HIBC QR Code symbol decodes to the flag character, the data and the check character. The delimiters are only in the human readable interpretation.
    /// </summary>
    [Fact]
    public void HibcQrCode_RoundTrips()
        => AssertDecodes(new HibcQrCodeSymbology(), "A123BJC5D6E71", BarcodeFormat.QRCode, "+A123BJC5D6E71G");

    /// <summary>
    /// A Micro QR Code symbol decodes to its text.
    /// </summary>
    [Fact]
    public void MicroQrCode_RoundTrips()
        => AssertDecodes(new MicroQrCodeSymbology(), "Micro QR!", BarcodeFormat.MicroQRCode, "Micro QR!");

    /// <summary>
    /// A rectangular Micro QR Code symbol decodes to its text.
    /// </summary>
    [Fact]
    public void RectangularMicroQrCode_RoundTrips()
        => AssertDecodes(new RectangularMicroQrCodeSymbology(), "https://sixlabors.com/", BarcodeFormat.RMQRCode, "https://sixlabors.com/");

    /// <summary>
    /// Aztec Code symbols decode to their text at each format: compact, full-range and HIBC with its flag and check characters.
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

        AssertDecodes(symbology, text, BarcodeFormat.AztecCode, expected);
    }

    /// <summary>
    /// An Aztec Rune carries a value from 0 to 255, which the decoder returns as three decimal digits.
    /// </summary>
    /// <param name="text">The value.</param>
    /// <param name="expected">The decoded value.</param>
    [Theory]
    [InlineData("0", "000")]
    [InlineData("25", "025")]
    [InlineData("255", "255")]
    public void AztecRune_RoundTrips(string text, string expected)
        => AssertDecodes(new AztecRuneSymbology(), text, BarcodeFormat.AztecRune, expected);

    /// <summary>
    /// A MaxiCode symbol decodes to its text: the 93 character message of Figure 2 of ISO/IEC 16023 in mode 4.
    /// </summary>
    [Fact]
    public void MaxiCode_RoundTrips()
        => AssertDecodes(
            new MaxiCodeSymbology(),
            "THIS IS A 93 CHARACTER CODE SET A MESSAGE THAT FILLS A MODE 4, UNAPPENDED, MAXICODE SYMBOL...",
            BarcodeFormat.MaxiCode,
            "THIS IS A 93 CHARACTER CODE SET A MESSAGE THAT FILLS A MODE 4, UNAPPENDED, MAXICODE SYMBOL...");

    /// <summary>
    /// Section 5.9 of the GS1 General Specifications: the QR Code symbol carries the GS1 Digital Link URI as plain data, so it decodes to the URI as written.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkQrCode_RoundTrips()
        => AssertDecodes(
            new Gs1DigitalLinkQrCodeSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            BarcodeFormat.QRCode,
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987");

    /// <summary>
    /// The EAN/UPC symbols decode at every module width from 1 to 7 pixels. Their digits hold the characters 1, 2, 7 and 8, whose bars and spaces Table 5-10 of
    /// the GS1 General Specifications corrects by one thirteenth of a module. UPC-A and UPC-E transmit as GTIN-13: the UPC-E 01278129 has the last data digit
    /// 2, which makes the manufacturer number 12200 and the item number 00781.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    [Theory]
    [InlineData("Ean13")]
    [InlineData("Ean8")]
    [InlineData("UpcA")]
    [InlineData("UpcE")]
    public void EanUpc_RoundTripsAtEveryModuleWidth(string kind)
    {
        (BarcodeSymbology symbology, string text, BarcodeFormat format, string expected) = kind switch
        {
            "Ean13" => ((BarcodeSymbology)new Ean13Symbology(), "5901234123457", BarcodeFormat.EAN13, "5901234123457"),
            "Ean8" => (new Ean8Symbology(), "12781270", BarcodeFormat.EAN8, "12781270"),
            "UpcA" => (new UpcASymbology(), "012712871281", BarcodeFormat.UPCA, "0012712871281"),
            _ => (new UpcESymbology(), "01278129", BarcodeFormat.UPCE, "0012200007819"),
        };

        AssertDecodesAtResolutions(symbology, text, format, expected, WholePixelModuleResolutions, BarcodeSymbology.DefaultXDimension, null);
    }

    /// <summary>
    /// The symbologies of a narrow and a wide element decode at every module width from 1 to 7 pixels. ITF-14 draws the wide element at 2.5 modules, which
    /// rounds half up to a whole pixel at an odd number of pixels a module, and Code 39, PZN and Interleaved 2 of 5 at 3 modules.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    [Theory]
    [InlineData("Code39")]
    [InlineData("Pzn8")]
    [InlineData("Interleaved2Of5")]
    [InlineData("Itf14")]
    public void TwoWidthSymbologies_RoundTripAtEveryModuleWidth(string kind)
    {
        (BarcodeSymbology symbology, string text, BarcodeFormat format, string expected) = kind switch
        {
            "Code39" => ((BarcodeSymbology)new Code39Symbology(), "CODE39", BarcodeFormat.Code39Std, "CODE39"),
            "Pzn8" => (new Pzn8Symbology(), "03752864", BarcodeFormat.PZN, "-03752864"),
            "Interleaved2Of5" => (new Interleaved2Of5Symbology(), "123456", BarcodeFormat.ITF, "123456"),
            _ => (new Itf14Symbology(), "1540014128876", BarcodeFormat.ITF, "15400141288763"),
        };

        AssertDecodesAtResolutions(symbology, text, format, expected, WholePixelModuleResolutions, BarcodeSymbology.DefaultXDimension, null);
    }

    /// <summary>
    /// The stacked and matrix symbols decode at every module width from 1 to 7 pixels.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    [Theory]
    [InlineData("Pdf417")]
    [InlineData("QrCode")]
    [InlineData("DataMatrix")]
    public void TwoDimensionalSymbologies_RoundTripAtEveryModuleWidth(string kind)
    {
        const string text = "HELLO WORLD 12345";

        (BarcodeSymbology symbology, BarcodeFormat format) = kind switch
        {
            "Pdf417" => ((BarcodeSymbology)new Pdf417Symbology(), BarcodeFormat.PDF417),
            "QrCode" => (new QrCodeSymbology(), BarcodeFormat.QRCode),
            _ => (new DataMatrixSymbology(), BarcodeFormat.DataMatrix),
        };

        AssertDecodesAtResolutions(symbology, text, format, text, WholePixelModuleResolutions, BarcodeSymbology.DefaultXDimension, null);
    }

    /// <summary>
    /// A MaxiCode symbol decodes at every resolution from 72 to 504 dots per inch in steps of 18. Its hexagonal modules do not align with whole pixels, and
    /// the symbol keeps the nominal size of ISO/IEC 16023.
    /// </summary>
    [Fact]
    public void MaxiCode_RoundTripsAtEveryResolution()
    {
        const string text = "THIS IS A 93 CHARACTER CODE SET A MESSAGE THAT FILLS A MODE 4, UNAPPENDED, MAXICODE SYMBOL...";

        AssertDecodesAtResolutions(new MaxiCodeSymbology(), text, BarcodeFormat.MaxiCode, text, FixedSizeResolutions, null, null);
    }

    /// <summary>
    /// Draws a symbol at each resolution and checks that each drawing decodes. The symbol starts on the nearest whole pixel, so an origin between two pixels
    /// draws the same image one pixel along, and <see cref="BarcodeLayoutTests"/> checks those origins. Every failing case is collected, so a failure shows
    /// every resolution it happens at.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="format">The format the decoder reads.</param>
    /// <param name="expected">The decoded text.</param>
    /// <param name="resolutions">The resolutions in dots per inch.</param>
    /// <param name="xDimension">The X dimension in millimetres, or <see langword="null"/> for the nominal X dimension of the symbology.</param>
    /// <param name="identifier">The symbology identifier the decoder reports, or <see langword="null"/> where the test does not check it.</param>
    private static void AssertDecodesAtResolutions(
        BarcodeSymbology symbology,
        string text,
        BarcodeFormat format,
        string expected,
        ReadOnlySpan<float> resolutions,
        double? xDimension,
        string identifier)
    {
        List<float> failures = [];
        foreach (float dpi in resolutions)
        {
            BarcodeOptions options = new() { Dpi = dpi, XDimension = xDimension };

            if (Record.Exception(() => AssertDecodes(symbology, text, format, identifier, expected, options)) is not null)
            {
                failures.Add(dpi);
            }
        }

        Assert.True(failures.Count == 0, $"No decode at {string.Join(", ", failures)} dpi.");
    }

    /// <summary>
    /// Draws a symbol at the default options and checks that it decodes to the transmitted data. The test does not check the symbology identifier.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="format">The format the decoder reads.</param>
    /// <param name="expected">The transmitted data.</param>
    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected)
        => AssertDecodes(symbology, text, format, null, expected, CreateOptions());

    /// <summary>
    /// Draws a symbol with the options and checks that it decodes to the transmitted data. The test does not check the symbology identifier.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="format">The format the decoder reads.</param>
    /// <param name="expected">The transmitted data.</param>
    /// <param name="options">The options to draw with.</param>
    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected, BarcodeOptions options)
        => AssertDecodes(symbology, text, format, null, expected, options);

    /// <summary>
    /// Draws a symbol at the default options and checks that it decodes to the symbology identifier and the transmitted data.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="format">The format the decoder reads.</param>
    /// <param name="identifier">The symbology identifier the decoder reports.</param>
    /// <param name="expected">The transmitted data.</param>
    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string identifier, string expected)
        => AssertDecodes(symbology, text, format, identifier, expected, CreateOptions());

    /// <summary>
    /// Draws a symbol and decodes it with zxing-cpp, restricted to one format, and checks the format, the symbology identifier and the transmitted data.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="format">The format the decoder reads.</param>
    /// <param name="identifier">The symbology identifier the decoder reports, or <see langword="null"/> where the test does not check it.</param>
    /// <param name="expected">The transmitted data.</param>
    /// <param name="options">The options to draw with.</param>
    private static void AssertDecodes(
        BarcodeSymbology symbology,
        string text,
        BarcodeFormat format,
        string identifier,
        string expected,
        BarcodeOptions options)
    {
        byte[] pixels = Draw(symbology, text, options, out int width, out int height);

        // The plain text mode returns the data as the symbol transmits it, without the parentheses of a GS1 human readable interpretation.
        using ReaderOptions reader = new() { Formats = format, TryHarder = true, TextMode = TextMode.Plain };
        Barcode[] barcodes = BarcodeReader.Read(new ImageView(pixels, width, height, ImageFormat.Lum, 0, 0), reader);
        try
        {
            Assert.True(barcodes.Length > 0, $"No decode at {options.Dpi} dpi.");

            // The decoder reports the symbology family, such as Code 39 for a standard or full ASCII read, so the test compares families.
            Barcode barcode = Assert.Single(barcodes);
            Assert.Equal(format.Symbology(), barcode.Symbology);
            if (identifier is not null)
            {
                Assert.Equal(identifier, barcode.SymbologyIdentifier);
            }

            Assert.Equal(expected, barcode.Text);
        }
        finally
        {
            foreach (Barcode barcode in barcodes)
            {
                barcode.Dispose();
            }
        }
    }

    /// <summary>
    /// Draws a symbol and decodes it with ZXing.Net, for the formats zxing-cpp does not read.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="format">The format the decoder reads.</param>
    /// <param name="expected">The decoded text.</param>
    private static void AssertDecodesWithZXingNet(BarcodeSymbology symbology, string text, ZXingNet.BarcodeFormat format, string expected)
    {
        byte[] pixels = Draw(symbology, text, CreateOptions(), out int width, out int height);

        ZXingNet.BarcodeReaderGeneric reader = new();
        reader.Options.PossibleFormats = [format];
        reader.Options.TryHarder = true;

        ZXingNet.Result result = reader.Decode(new ZXingNet.RGBLuminanceSource(pixels, width, height, ZXingNet.RGBLuminanceSource.BitmapFormat.Gray8));

        Assert.True(result is not null, "No decode.");
        Assert.Equal(format, result.BarcodeFormat);
        Assert.Equal(expected, result.Text);
    }

    /// <summary>
    /// Draws a symbol on white with a margin on every side. The image size comes from the measured bounds, so the image does not clip a wide symbol. The image
    /// is 8-bit grey, the luminance the decoders read, so they take the pixels without a conversion.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options to draw with.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <returns>The grey pixels, row by row.</returns>
    private static byte[] Draw(BarcodeSymbology symbology, string text, BarcodeOptions options, out int width, out int height)
    {
        PointF origin = new(20, 20);
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, origin, text);

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Right) + 20, (int)MathF.Ceiling(bounds.Bottom) + 20, new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, origin, text)));

        byte[] pixels = new byte[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        width = image.Width;
        height = image.Height;

        return pixels;
    }

    /// <summary>
    /// Draws at the default options, so every decode test proves that a symbol drawn with no options reads.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateOptions()
        => new();
}
