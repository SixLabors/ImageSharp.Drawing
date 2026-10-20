// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="SwissQrCodeSymbology"/> against the Swiss Implementation Guidelines for the QR-bill version 2.4. The valid data is examples 1
/// to 4 of Annex A, Tables 17 to 20, with the names as the payment parts print them. Each rejected input changes one line of an example so that the line
/// breaks one rule of the guidelines.
/// </summary>
public class SwissQrCodeSymbologyTests
{
    /// <summary>
    /// Annex A, Table 17: a QR-IBAN with a QR reference, an amount and an ultimate debtor.
    /// </summary>
    private static readonly string[] Example1 =
    [
        "SPC", "0200", "1", "CH6431961000004421557", "S", "Max Muster & Söhne", "Musterstrasse", "123", "8000",
        "Seldwyla", "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "50.00", "CHF", "S", "Simon Muster", "Musterstrasse", "1", "8000", "Seldwyla", "CH", "QRR",
        "000008207791225857421286694", "Payment of travel", "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 18: a QR reference with billing information and an alternative procedure.
    /// </summary>
    private static readonly string[] Example2 =
    [
        "SPC", "0200", "1", "CH4431999123000889012", "S", "Max Muster & Söhne", "Musterstrasse", "123", "8000",
        "Seldwyla", "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "1949.75", "CHF", "S", "Simon Muster", "Musterstrasse", "1", "8000", "Seldwyla", "CH", "QRR",
        "210000000003139471430009017", "Order from 15.10.2020", "EPD",
        "//S1/10/1234/11/201021/30/102673386/32/7.7/40/0:30", "eBill/B/simon.muster@example.com", string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 19: an IBAN without a reference, an amount or an ultimate debtor.
    /// </summary>
    private static readonly string[] Example3 =
    [
        "SPC", "0200", "1", "CH5204835012345671000", "S", "Muster Stiftung", "P.O. box", string.Empty, "3001", "Bern",
        "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "CHF", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "NON", string.Empty, string.Empty, "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 20: an IBAN with a Creditor Reference. Table 20 prints the reference RF720191230100405JSH0438, whose check digits fail the modulo 97-10
    /// check section 2.12.2 requires: the remainder is 49, not 1. The check digits of that reference body are 24.
    /// </summary>
    private static readonly string[] Example4 =
    [
        "SPC", "0200", "1", "CH5800791123000889012", "S", "Muster Krankenkasse", "Musterstrasse", "12", "8000",
        "Seldwyla", "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "211.00", "CHF", "S", "Sarah Beispiel", "Musterstrasse", "1", "8000", "Seldwyla", "CH", "SCOR",
        "RF240191230100405JSH0438", string.Empty, "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Gets the cases of <see cref="RejectsALineOutsideItsDefinition"/>: an example, the number of a line of Table 8, and a value for that line that breaks its
    /// definition.
    /// </summary>
    public static TheoryData<string, int, string> RejectsALineOutsideItsDefinitionData { get; } = new()
    {
        // Lines 1 to 3, the header: fixed values.
        { nameof(Example1), 1, "SPD" },
        { nameof(Example1), 2, "0201" },
        { nameof(Example1), 3, "2" },

        // Line 4: a CH or LI IBAN of 21 characters, no spaces, with valid check digits.
        { nameof(Example1), 4, "CH6431961000004421558" },
        { nameof(Example1), 4, "ch6431961000004421557" },
        { nameof(Example1), 4, "CH64 31961000004421557" },
        { nameof(Example1), 4, "DE89370400440532013000" },

        // Lines 5 to 11, the creditor: a structured address.
        { nameof(Example1), 5, "K" },
        { nameof(Example1), 6, string.Empty },
        { nameof(Example1), 6, new string('A', 71) },
        { nameof(Example1), 7, new string('A', 71) },
        { nameof(Example1), 8, new string('1', 17) },
        { nameof(Example1), 9, string.Empty },
        { nameof(Example1), 9, new string('1', 17) },
        { nameof(Example1), 10, string.Empty },
        { nameof(Example1), 10, new string('A', 36) },
        { nameof(Example1), 11, string.Empty },
        { nameof(Example1), 11, "XX" },
        { nameof(Example1), 11, "ch" },

        // Lines 12 to 18, the ultimate creditor: must not be filled in.
        { nameof(Example1), 12, "S" },
        { nameof(Example1), 13, "Max Muster" },

        // Line 19, the amount: no leading zeros, a point and two decimals, at most 12 characters, and at least 0.01 outside a notification.
        { nameof(Example1), 19, "050.00" },
        { nameof(Example1), 19, "50.0" },
        { nameof(Example1), 19, "50,00" },
        { nameof(Example1), 19, ".50" },
        { nameof(Example1), 19, "1000000000.00" },
        { nameof(Example1), 19, "0.00" },

        // Line 20, the currency: CHF or EUR, and CHF with a QR reference.
        { nameof(Example1), 20, "USD" },
        { nameof(Example1), 20, "EUR" },

        // Lines 21 to 27, the ultimate debtor: when used, a structured address.
        { nameof(Example1), 21, "K" },
        { nameof(Example1), 22, string.Empty },
        { nameof(Example1), 25, string.Empty },
        { nameof(Example1), 26, string.Empty },
        { nameof(Example1), 27, string.Empty },
        { nameof(Example3), 22, "Simon Muster" },

        // Lines 28 and 29: QRR with a QR-IBAN and a QR reference, SCOR with an IBAN and a Creditor Reference, and NON with an IBAN and no reference.
        { nameof(Example1), 28, "XYZ" },
        { nameof(Example1), 28, "SCOR" },
        { nameof(Example1), 28, "NON" },
        { nameof(Example1), 29, "000008207791225857421286695" },
        { nameof(Example1), 29, "00000820779122585742128669" },
        { nameof(Example1), 29, "00000820779122585742128669A" },
        { nameof(Example1), 29, "000000000000000000000000000" },
        { nameof(Example3), 28, "QRR" },
        { nameof(Example3), 29, "RF18539007547034" },
        { nameof(Example4), 29, "RF720191230100405JSH0438" },
        { nameof(Example4), 29, "RF730191230100405JSH0438" },
        { nameof(Example4), 29, "RF18" },
        { nameof(Example4), 29, "RF1853900754703400000000000" },
        { nameof(Example4), 29, "XX18539007547034" },
        { nameof(Example4), 29, "RF18 5390 0754 7034" },

        // Section 4.1.3: no element is filled with blanks up to its maximum length.
        { nameof(Example1), 6, "Max Muster & Söhne".PadRight(70) },
        { nameof(Example1), 10, "Seldwyla".PadRight(35) },

        // Line 30, the unstructured message, at most 140 characters, and line 31, the trailer EPD.
        { nameof(Example1), 30, new string('A', 141) },
        { nameof(Example1), 31, "EPX" },

        // Line 32, the billing information: // and a name of two characters first, and at most 140 characters with the message. Lines 33 and 34, the
        // alternative procedures: at most 100 characters.
        { nameof(Example2), 32, "S1/10/1234" },
        { nameof(Example2), 32, "//S" },
        { nameof(Example2), 30, new string('A', 100) },
        { nameof(Example2), 33, new string('A', 101) },
    };

    /// <summary>
    /// Section 6.4: the symbol is 46 x 46 mm without its quiet zone, whatever its version. At 254 dots per inch a millimetre is 10 pixels.
    /// </summary>
    /// <param name="example">The example of Annex A.</param>
    [Theory]
    [InlineData(nameof(Example2))]
    [InlineData(nameof(Example3))]
    public void Is46MillimetresWide(string example)
    {
        BarcodeOptions options = new() { Dpi = 254F, IncludeQuietZones = false };

        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(new SwissQrCodeSymbology(), options, PointF.Empty, Payload(example, "\n"));

        Assert.Equal(new SizeF(460F, 460F), bounds.Size);
    }

    /// <summary>
    /// Section 6.4.1: the symbol has the quiet zone of ISO 18004, four modules.
    /// </summary>
    [Fact]
    public void QuietZoneIsFourModules()
    {
        string text = Payload(nameof(Example3), "\n");

        Assert.Equal(4F, SymbolEncoder.Read(new SwissQrCodeSymbology(), text, new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));
    }

    /// <summary>
    /// Section 6.4: "The measurements of the Swiss QR Code for printing must always be 46 x 46 mm (without surrounding quiet space) regardless of the Swiss QR
    /// Code version". The modules divide 46 mm, and the symbology rejects an X dimension from the caller.
    /// </summary>
    [Fact]
    public void IsAlwaysFortySixMillimetresWide()
    {
        string text = Payload(nameof(Example3), "\n");

        SymbolEncoder.Encode(new SwissQrCodeSymbology(), text, new BarcodeOptions(), static (ref symbol) =>
            Assert.Equal(46.0, symbol.XDimension * symbol.Columns, 1e-9));

        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new SwissQrCodeSymbology(), text, new BarcodeOptions { XDimension = 0.5 }));
    }

    /// <summary>
    /// Each line follows its definition in Table 8 and the rules of chapters 2 and 4. The symbology rejects a line that breaks its definition.
    /// </summary>
    /// <param name="example">The example of Annex A.</param>
    /// <param name="line">The number of the line in Table 8.</param>
    /// <param name="value">The value of the line.</param>
    [Theory]
    [MemberData(nameof(RejectsALineOutsideItsDefinitionData))]
    public void RejectsALineOutsideItsDefinition(string example, int line, string value)
        => Assert.Throws<ArgumentException>(() => Encode(With(Lines(example), line, value)));

    /// <summary>
    /// Section 4.1.4: the lines up to the trailer must be present, the billing information and the alternative procedures are the only further lines, and the
    /// lines are separated by CR LF or by LF, the same one throughout. The symbology rejects data that breaks one of these rules.
    /// </summary>
    [Fact]
    public void RejectsLinesOutsideTheStructure()
    {
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\n', Example1[..30])));
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\n', Example1) + "\nAV3"));
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\r', Example1)));
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\n', Example1[..20]) + "\r\n" + string.Join('\n', Example1[20..])));
    }

    /// <summary>
    /// Section 4.1.1 permits U+0020 to U+007E, U+00A0 to U+017F, U+0218 to U+021B and U+20AC, and section 6.2 permits at most 997 characters. The symbology
    /// rejects a tab, a Greek letter, an emoji and 998 characters.
    /// </summary>
    [Fact]
    public void RejectsCharactersOutsideSectionFourOneOneAndLongData()
    {
        Assert.Throws<ArgumentException>(() => Encode(With(Example1, 30, "Payment\tof travel")));
        Assert.Throws<ArgumentException>(() => Encode(With(Example1, 30, "Payment Ω")));
        Assert.Throws<ArgumentException>(() => Encode(With(Example1, 30, "Payment 😀")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(new string('A', 998)));
    }

    /// <summary>
    /// Data the guidelines permit beyond the examples: no billing information or alternative procedure lines, the amount 0.00 with a notification text of
    /// section 4.4, an amount below 1.00 and the largest amount, a Creditor Reference in small letters, which the banks read without regard to case, and a
    /// Creditor Reference in EUR. The symbology encodes each of them.
    /// </summary>
    [Fact]
    public void AcceptsDataTheGuidelinesPermit()
    {
        string[] notification = (string[])Example3.Clone();
        notification[18] = "0.00";
        notification[29] = "DO NOT USE FOR PAYMENT";
        Encode(string.Join('\n', notification));

        notification[29] = "NICHT ZUR ZAHLUNG VERWENDEN";
        Encode(string.Join('\n', notification));

        Encode(string.Join('\n', Example1[..31]));
        Encode(With(Example4, 19, "0.10"));
        Encode(With(Example4, 19, "999999999.99"));
        Encode(With(Example4, 29, "rf240191230100405jsh0438"));
        Encode(With(Example4, 20, "EUR"));
    }

    /// <summary>
    /// Annex B, Figure 22: the 26 digits 21000000000313947143000901 take the check digit 7, the QR reference of Example 2.
    /// </summary>
    [Fact]
    public void AcceptsTheCheckDigitOfFigureTwentyTwo()
    {
        Encode(With(Example2, 29, "210000000003139471430009017"));
        Assert.Throws<ArgumentException>(() => Encode(With(Example2, 29, "210000000003139471430009016")));
    }

    /// <summary>
    /// Returns the data of an example with its lines separated by the separator.
    /// </summary>
    /// <param name="example">The example of Annex A.</param>
    /// <param name="separator">The separator.</param>
    /// <returns>The data.</returns>
    private static string Payload(string example, string separator) => string.Join(separator, Lines(example));

    /// <summary>
    /// Returns the lines of an example.
    /// </summary>
    /// <param name="example">The name of the example.</param>
    /// <returns>The lines.</returns>
    private static string[] Lines(string example) => example switch
    {
        nameof(Example1) => Example1,
        nameof(Example2) => Example2,
        nameof(Example3) => Example3,
        _ => Example4,
    };

    /// <summary>
    /// Returns the data of an example with one line replaced, its lines separated by LF.
    /// </summary>
    /// <param name="lines">The lines of the example.</param>
    /// <param name="number">The number of the line in Table 8.</param>
    /// <param name="value">The value of the line.</param>
    /// <returns>The data.</returns>
    private static string With(string[] lines, int number, string value)
    {
        string[] copy = (string[])lines.Clone();
        copy[number - 1] = value;

        return string.Join('\n', copy);
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    private static void Encode(string text) => SymbolEncoder.Encode(new SwissQrCodeSymbology(), text, new BarcodeOptions());
}
