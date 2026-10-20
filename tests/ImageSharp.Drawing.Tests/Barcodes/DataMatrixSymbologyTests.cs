// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="DataMatrixSymbology"/>, <see cref="DataMatrixRectangularSymbology"/>, <see cref="DataMatrixRectangularExtensionSymbology"/>
/// and the GS1, GS1 Digital Link, HIBC and Mailmark 2D symbologies over them. The tests check the codewords, the symbol sizes, the finder pattern, the quiet
/// zones, the human readable interpretation and the input rules against ISO/IEC 16022, the GS1 General Specifications, ANSI/HIBC 2.6 and the Mailmark barcode
/// definition document.
/// </summary>
public class DataMatrixSymbologyTests
{
    /// <summary>
    /// The 45 characters of the Royal Mail fields of the example in Table 5 of the Mailmark barcode definition document.
    /// </summary>
    private const string Mailmark2DFields = "JGB 012100123412345678AB19XY1A 0             ";

    /// <summary>
    /// ISO/IEC 16022 Table 2: an ASCII character is its value plus 1, and a pair of digits is 130 plus the value of the pair.
    /// </summary>
    [Fact]
    public void DataMatrix_AsciiEncodationMatchesTableTwo()
    {
        Assert.Equal(['A' + 1], Codewords("A", [3]));
        Assert.Equal([130 + 12, 130 + 34], Codewords("1234", [3]));
    }

    /// <summary>
    /// ISO/IEC 16022 Table 2: a character above 127 takes the Upper Shift, 235, then its value less 128, plus 1.
    /// </summary>
    [Fact]
    public void DataMatrix_ExtendedAsciiTakesTheUpperShift()
        => Assert.Equal([235, 0xE9 - 128 + 1], Codewords("é", [3]));

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the Function 1 Symbol Character is encoded at the beginning of the data, and ISO/IEC 16022 Table 2
    /// gives it the codeword 232.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_StartsWithFnc1()
        => Assert.Equal([232, 130 + 1], Codewords([-1, '0', '1'], [3], 0));

    /// <summary>
    /// ISO/IEC 16022 Table 2 gives the 05 Macro and 06 Macro only as the ASCII codewords 236 and 237. A Macro stands for the message header, so it is the first
    /// codeword even when the data after it suits Base 256 or C40 encodation.
    /// </summary>
    /// <param name="macro">The Macro message value.</param>
    /// <param name="codeword">The codeword of the Macro.</param>
    /// <param name="data">The data after the Macro.</param>
    [Theory]
    [InlineData(-3, 236, "ÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ")]
    [InlineData(-4, 237, "ABCDEFGHIJKLMNO")]
    public void DataMatrix_MacroIsTheFirstCodeword(int macro, int codeword, string data)
    {
        int[] message = [macro, .. data];

        int[] codewords = Codewords(message, [62], 0);

        Assert.Equal(codeword, codewords[0]);
        Assert.DoesNotContain(codewords, c => c < 0);
    }

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the finder pattern is a solid L one module wide on the left and bottom edges, and the opposite edges
    /// alternate, with the upper right corner module light. Section 5.6.3.2: alignment patterns divide a symbol of 32 x 32 or more into data regions.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="rows">The expected number of module rows.</param>
    /// <param name="columns">The expected number of module columns.</param>
    [Theory]
    [InlineData("1234", 10, 10)]
    [InlineData("01234567890123456789012345678901234567890123456789", 22, 22)]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789", 32, 32)]
    public void DataMatrix_FinderPatternFollowsSectionFiveSixTwo(string text, int rows, int columns)
        => SymbolEncoder.Encode(new DataMatrixSymbology(), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal(rows, symbol.MatrixRows);
            Assert.Equal(columns, symbol.Columns);

            ReadOnlySpan<bool> modules = symbol.Modules;
            for (int r = 0; r < rows; r++)
            {
                Assert.True(modules[r * columns], $"Left edge row {r}");
                Assert.Equal(r % 2 == 1, modules[(r * columns) + columns - 1]);
            }

            for (int c = 0; c < columns; c++)
            {
                Assert.True(modules[((rows - 1) * columns) + c], $"Bottom edge column {c}");
                Assert.Equal(c % 2 == 0, modules[c]);
            }

            Assert.False(modules[columns - 1]);
        });

    /// <summary>
    /// Tables 5-32 and 5-33 of the GS1 General Specifications: the data codeword capacity of each size. Data of exactly that many codewords takes that size,
    /// and one codeword more takes the next size.
    /// </summary>
    /// <param name="capacity">The data codeword capacity of the size.</param>
    /// <param name="rows">The number of module rows of the size.</param>
    /// <param name="columns">The number of module columns of the size.</param>
    /// <param name="nextRows">The number of module rows of the next size.</param>
    /// <param name="nextColumns">The number of module columns of the next size.</param>
    [Theory]
    [InlineData(3, 10, 10, 12, 12)]
    [InlineData(5, 12, 12, 14, 14)]
    [InlineData(44, 26, 26, 32, 32)]
    [InlineData(204, 52, 52, 64, 64)]
    public void DataMatrix_SquareCapacityMatchesTableFiveThirtyTwo(int capacity, int rows, int columns, int nextRows, int nextColumns)
    {
        Assert.Equal((rows, columns), Size(new DataMatrixSymbology(), Digits(2 * capacity)));
        Assert.Equal((nextRows, nextColumns), Size(new DataMatrixSymbology(), Digits(2 * (capacity + 1))));
    }

    /// <summary>
    /// Table 5-33 of the GS1 General Specifications: the rectangular sizes hold 5, 10, 16, 22, 32 and 49 data codewords.
    /// </summary>
    /// <param name="capacity">The data codeword capacity of the size.</param>
    /// <param name="rows">The number of module rows of the size.</param>
    /// <param name="columns">The number of module columns of the size.</param>
    [Theory]
    [InlineData(5, 8, 18)]
    [InlineData(10, 8, 32)]
    [InlineData(16, 12, 26)]
    [InlineData(22, 12, 36)]
    [InlineData(32, 16, 36)]
    [InlineData(49, 16, 48)]
    public void DataMatrixRectangular_CapacityMatchesTableFiveThirtyThree(int capacity, int rows, int columns)
        => Assert.Equal((rows, columns), Size(new DataMatrixRectangularSymbology(), Digits(2 * capacity)));

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the largest symbol carries 3116 digits.
    /// </summary>
    [Fact]
    public void DataMatrix_RejectsDataLargerThanTheLargestSymbol()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new DataMatrixSymbology(), Digits(3117)));

    /// <summary>
    /// Table 5-32 of the GS1 General Specifications: a 10 x 10 symbol holds 3 data codewords, which is 6 digits. A symbology fixed at that size rejects 8
    /// digits.
    /// </summary>
    [Fact]
    public void DataMatrix_RejectsDataLargerThanTheFixedSize()
        => Assert.Throws<ArgumentException>(() => Encode(new DataMatrixSymbology(10, 10), Digits(8)));

    /// <summary>
    /// ISO/IEC 16022 Table 2 encodes byte values only. The symbology rejects a character above 255.
    /// </summary>
    [Fact]
    public void DataMatrix_RejectsCharactersAboveTheByteRange()
        => Assert.Throws<ArgumentException>(() => Encode(new DataMatrixSymbology(), "DMĀ"));

    /// <summary>
    /// Empty text carries no data. The symbology rejects it.
    /// </summary>
    [Fact]
    public void DataMatrix_RejectsEmptyData()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new DataMatrixSymbology(), string.Empty));

    /// <summary>
    /// Each symbology accepts only the sizes of its own table. The square symbology rejects 11 x 11 and 8 x 48. The rectangular extension symbology rejects
    /// 10 x 10.
    /// </summary>
    [Fact]
    public void DataMatrix_RejectsSizesOutsideTheTables()
    {
        Assert.Throws<ArgumentException>(() => new DataMatrixSymbology(11, 11));
        Assert.Throws<ArgumentException>(() => new DataMatrixSymbology(8, 48));
        Assert.Throws<ArgumentException>(() => new DataMatrixRectangularExtensionSymbology(10, 10));
    }

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the quiet zone is one module wide on all four sides.
    /// </summary>
    [Fact]
    public void DataMatrix_QuietZoneIsOneModule()
        => SymbolEncoder.Encode(new DataMatrixSymbology(), "1234", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(1, symbol.LeadingQuietZone);
            Assert.Equal(1, symbol.TrailingQuietZone);
            Assert.Equal(1, symbol.TopQuietZone);
            Assert.Equal(1, symbol.BottomQuietZone);
        });

    /// <summary>
    /// When the symbology validates the check character, it rejects input whose last character is not the check character of the data.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_RejectsAnIncorrectCheckCharacter()
    {
        Assert.Throws<ArgumentException>(() => Encode(new HibcDataMatrixSymbology(true), "A123BJC5D6E70"));
        Assert.Throws<ArgumentException>(() => Encode(new HibcDataMatrixRectangularSymbology(true), "A123BJC5D6E70"));
    }

    /// <summary>
    /// GS1 DataMatrix takes element strings with each Application Identifier in parentheses. It rejects digits without the parentheses.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_RejectsDataOutsideElementStringSyntax()
        => Assert.Throws<ArgumentException>(() => Encode(new Gs1DataMatrixSymbology(), "0109521234543213"));

    /// <summary>
    /// Example 1 of section 2.3 of the GS1 DataMatrix Guideline prints the element strings below the symbol, one on each line, with the Application Identifiers
    /// in parentheses, as Figure 4-6 of the GS1 General Specifications prefers.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_PrintsOneElementStringOnEachLine()
    {
        const string text = "(01)03453120000011(17)191125(10)ABCD1234";
        const string expected = "(01)03453120000011\n(17)191125\n(10)ABCD1234";
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new Gs1DataMatrixSymbology(), text, options, SymbolEncoder.Texts)));
        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new Gs1DataMatrixRectangularSymbology(), text, options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// Rule 3b of section 4.14 of the GS1 General Specifications: when Application Identifier (8200) appears, its URL does not appear in the human readable
    /// interpretation.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_LeavesTheUrlOfApplicationIdentifier8200OutOfTheText()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Gs1DataMatrixSymbology(), "(01)09520123456788(8200)http://www.abc.net", options, SymbolEncoder.Texts);

        Assert.Equal("(01)09520123456788", Assert.Single(texts));
    }

    /// <summary>
    /// Rule 1c of section 4.14 of the GS1 General Specifications: the human readable interpretation follows the order of the encoded data, which for a GS1
    /// Digital Link URI is the primary key and its qualifiers in the path, then the data attributes in the query string. The values print decoded, and the URI
    /// itself does not print.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkDataMatrix_PrintsTheElementStringsOfTheUri()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(
            new Gs1DigitalLinkDataMatrixSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            options,
            SymbolEncoder.Texts);

        Assert.Equal("(01)09521234543213\n(22)ABC-123\n(99)XYZ-987", Assert.Single(texts));
    }

    /// <summary>
    /// Figure 3 of ANSI/HIBC 2.6 prints the same string below a Data Matrix symbol as below the linear symbols, between asterisks.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_PrintsTheDataBetweenDelimiters()
    {
        const string expected = "*+A123BJC5D6E71G*";
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new HibcDataMatrixSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts)));
        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new HibcDataMatrixRectangularSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// The figure of section 4.3.3 of ANSI/HIBC 2.6 prints concatenated primary and secondary data on two lines, broken after the "/" that joins them, with the
    /// check character of the whole string.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_BreaksConcatenatedDataAfterTheJoin()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new HibcDataMatrixSymbology(), "A123BJC5D6E71/$$52001510X3", options, SymbolEncoder.Texts);

        Assert.Equal("*+A123BJC5D6E71/\n$$52ØØ151ØX3C*", Assert.Single(texts));
    }

    /// <summary>
    /// Only a "/" inside the data joins primary and secondary data. The modulo 43 check character of "+A99950" is "/": the values 41, 10, 9, 9, 9, 5 and 0
    /// sum to 83, and 83 modulo 43 is 40. That check character does not break the line.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_PrintsACheckCharacterSlashOnTheDataLine()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new HibcDataMatrixSymbology(), "A99950", options, SymbolEncoder.Texts);

        Assert.Equal("*+A9995Ø/*", Assert.Single(texts));
    }

    /// <summary>
    /// When the symbology validates the check character, the input is at least one data character and then the check character. The symbology rejects a
    /// single character.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_RejectsACheckCharacterWithoutData()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcDataMatrixSymbology(true), "A"));

    /// <summary>
    /// Section 4.14 rule 2a.iii of the GS1 General Specifications: the font is at least 2 mm high. A point is 1/72 inch, so a smaller font prints as the font
    /// of 2 mm and measures the same. ANSI/HIBC 2.6 states no size, so HIBC text keeps the size of the caller.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_TextIsAtLeastTwoMillimetresHigh()
    {
        const string gs1 = "(01)03453120000011(17)191125(10)ABCD1234";
        BarcodeOptions small = new() { Dpi = 300F, Font = BarcodeFonts.OcrB.CreateFont(3F) };
        BarcodeOptions minimum = new() { Dpi = 300F, Font = BarcodeFonts.OcrB.CreateFont(2F * 72F / 25.4F) };

        Assert.Equal(
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DataMatrixSymbology(), minimum, PointF.Empty, gs1),
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DataMatrixSymbology(), small, PointF.Empty, gs1));

        Assert.Equal(
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DigitalLinkDataMatrixSymbology(), minimum, PointF.Empty, "https://id.gs1.org/01/09521234543213"),
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DigitalLinkDataMatrixSymbology(), small, PointF.Empty, "https://id.gs1.org/01/09521234543213"));

        Assert.NotEqual(
            BarcodeMeasurer.MeasureRenderableBounds(new HibcDataMatrixSymbology(), minimum, PointF.Empty, "A123BJC5D6E71"),
            BarcodeMeasurer.MeasureRenderableBounds(new HibcDataMatrixSymbology(), small, PointF.Empty, "A123BJC5D6E71"));
    }

    /// <summary>
    /// The human readable interpretation prints only when the caller sets a font.
    /// </summary>
    [Fact]
    public void Gs1AndHibcDataMatrix_PrintNoTextWithoutAFont()
    {
        BarcodeOptions options = new();

        Assert.Empty(SymbolEncoder.Read(new Gs1DataMatrixSymbology(), "(01)03453120000011", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new Gs1DigitalLinkDataMatrixSymbology(), "https://id.gs1.org/01/09521234543213", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new HibcDataMatrixSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts));
    }

    /// <summary>
    /// Section 2.3 and Table 4 of the Mailmark barcode definition document: the data starts with the latch to C40 encodation, codeword 230, and the Royal Mail
    /// fields encode in C40 encodation.
    /// </summary>
    [Fact]
    public void Mailmark2D_StartsWithTheC40Latch()
    {
        int[] codewords = Codewords(Mailmark2DFields + "ABC123", [36], 45);

        Assert.Equal(230, codewords[0]);

        // C40 encodation packs three values as 1600 x C1 + 40 x C2 + C3 + 1, and the basic set gives the capital letters A to Z the values 14 to 39, so "JGB"
        // is 23, 20 and 15.
        int packed = (1600 * 23) + (40 * 20) + 15 + 1;
        Assert.Equal(packed >> 8, codewords[1]);
        Assert.Equal(packed & 0xFF, codewords[2]);
    }

    /// <summary>
    /// Section 2.4 of the Mailmark barcode definition document: a clear zone of four modules, and the recommended module size of 0.5 mm, 50 pixels at 100
    /// pixels a millimetre.
    /// </summary>
    [Fact]
    public void Mailmark2D_ClearZoneAndModuleSizeFollowSectionTwoFour()
    {
        Mailmark2DSymbology symbology = new(Mailmark2DType.Type7);

        SymbolEncoder.Encode(symbology, Mailmark2DFields, new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi }, static (ref symbol) =>
        {
            Assert.Equal(0.5, symbol.XDimension);
            Assert.Equal(4, symbol.LeadingQuietZone);
            Assert.Equal(4, symbol.TrailingQuietZone);
            Assert.Equal(4, symbol.TopQuietZone);
            Assert.Equal(4, symbol.BottomQuietZone);
        });
    }

    /// <summary>
    /// Section 2.4 of the Mailmark barcode definition document: "The module size of 2D Mailmark barcodes must be in the range of 0.5mm - 0.7mm."
    /// </summary>
    /// <param name="xDimension">The module size in millimetres.</param>
    /// <param name="expected">The module size the symbol draws at in millimetres, the nearest limit of the range.</param>
    [Theory]
    [InlineData(0.49, 0.5)]
    [InlineData(0.71, 0.7)]
    public void Mailmark2D_ClampsAModuleSizeOutsideSectionTwoFour(double xDimension, double expected)
        => SymbolEncoder.Encode(
            new Mailmark2DSymbology(Mailmark2DType.Type7),
            Mailmark2DFields,
            new BarcodeOptions { Dpi = SymbolEncoder.MillimetreDpi, XDimension = xDimension },
            (ref symbol) => Assert.Equal(expected, symbol.XDimension, 1e-9));

    /// <summary>
    /// Table 2 and Appendix A of the Mailmark barcode definition document: each Royal Mail field takes its legitimate values, padded with spaces to its fixed
    /// length.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("JGB 012100123412345678AB19XY1A 0")]
    [InlineData("XGB 012100123412345678AB19XY1A 0             ")]
    [InlineData("JGB 012100123412345678AB19XY1A 0           X ")]
    [InlineData("JGB 0 2100123412345678AB19XY1A 0             ")]
    [InlineData("JGB 312100123412345678AB19XY1A 0             ")]
    [InlineData("JGB D12100123412345678AB19XY1A 0             ")]
    [InlineData("JGB 022100123412345678AB19XY1A 0             ")]
    [InlineData("JGB 01210012341234567AAB19XY1A 0             ")]
    [InlineData("JGB 01210012341234567aAB19XY1A 0             ")]
    [InlineData("JGB 012100123412345678AB1 XY1A 0             ")]
    [InlineData("JGB 012100123412345678AB19XY   0             ")]
    [InlineData("JGB 012100123412345678AB19XY1A 7             ")]
    [InlineData("JGB 012100123412345678AB19XY1A 0SW1A         ")]
    public void Mailmark2D_RejectsInvalidRoyalMailFields(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new Mailmark2DSymbology(Mailmark2DType.Type9), text));

    /// <summary>
    /// Appendix A, Table 14 of the Mailmark barcode definition document: the destination post code plus DPS may be spaces, an outward code alone or the full
    /// post code with its delivery point, and the return to sender post code may be spaces or the outward and inward codes.
    /// </summary>
    /// <param name="destination">The destination post code plus DPS field.</param>
    /// <param name="returnToSender">The return to sender post code field.</param>
    [Theory]
    [InlineData("         ", "       ")]
    [InlineData("XY11     ", "       ")]
    [InlineData("N1      ", "W1A1AA ")]
    [InlineData("NE11AA9Z ", "SW1A1AA")]
    [InlineData("EC1A1BB1A", "M11AE  ")]
    public void Mailmark2D_AcceptsThePostcodeFormatsOfAppendixA(string destination, string returnToSender)
    {
        string text = "JGB 01210012341234567" + "8" + destination.PadRight(9) + "0" + returnToSender + "      ";

        Assert.Equal(24, Size(new Mailmark2DSymbology(Mailmark2DType.Type7), text).Rows);
    }

    /// <summary>
    /// Each Mailmark 2D type has a fixed size, which fixes the space for customer content. The symbology rejects customer content larger than that space.
    /// </summary>
    [Fact]
    public void Mailmark2D_RejectsCustomerContentLargerThanTheType()
        => Assert.Throws<ArgumentException>(() => Encode(new Mailmark2DSymbology(Mailmark2DType.Type7), Mailmark2DFields + "ABCDEFGHIJ"));

    /// <summary>
    /// URIs the GS1 Digital Link Standard: URI Syntax does not allow: a scheme other than http or https (section 4.11), a GTIN of fewer than 14 digits (section
    /// 4.1), key qualifiers out of order (section 4.9), a key qualifier in the query string, no primary key in the path, and data an Application Identifier
    /// does not allow. Section 4.2 percent-encodes "+", "(" and "#" in data and section 4.11 gives "%" two hexadecimal digits, and the URI of section 4.11
    /// has no fragment.
    /// </summary>
    /// <param name="text">The URI.</param>
    [Theory]
    [InlineData("ftp://example.com/01/09520123456788")]
    [InlineData("https://example.com/01/9520123456788")]
    [InlineData("https://example.com/01/09520123456788/21/ABC/10/XYZ")]
    [InlineData("https://example.com/01/09520123456788?10=ABC")]
    [InlineData("https://example.com/01/09520123456788?17=281301")]
    [InlineData("https://example.com/10/ABC")]
    [InlineData("https://example.com/01/09520123456788/")]
    [InlineData("https://example.com/01/09520123456788 ")]
    [InlineData("https://example.com/01/09520123456788/10/AB+C")]
    [InlineData("https://example.com/01/09520123456788/10/AB(C")]
    [InlineData("https://example.com/01/09520123456788/10/AB%C")]
    [InlineData("https://example.com/01/09520123456788/10/AB%2")]
    [InlineData("https://example.com/01/09520123456788/10/ABC#frag")]
    [InlineData("https://example.com/01/09520123456788?99=A+B")]
    public void Gs1DigitalLinkDataMatrix_RejectsInvalidUris(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new Gs1DigitalLinkDataMatrixSymbology(), text));

    /// <summary>
    /// Encodes the characters of the text into data codewords with no C40 prefix.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="capacities">The data codeword capacities of the sizes the data may take, smallest first.</param>
    /// <returns>The data codewords.</returns>
    private static int[] Codewords(string text, int[] capacities)
        => Codewords(text, capacities, 0);

    /// <summary>
    /// Encodes the characters of the text into data codewords.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="capacities">The data codeword capacities of the sizes the data may take, smallest first.</param>
    /// <param name="c40Prefix">The number of leading characters that encode in C40 encodation.</param>
    /// <returns>The data codewords.</returns>
    private static int[] Codewords(string text, int[] capacities, int c40Prefix)
    {
        int[] message = new int[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            message[i] = text[i];
        }

        return Codewords(message, capacities, c40Prefix);
    }

    /// <summary>
    /// Encodes a message into data codewords.
    /// </summary>
    /// <param name="message">The message values, where -1 stands for the Function 1 Symbol Character.</param>
    /// <param name="capacities">The data codeword capacities of the sizes the data may take, smallest first.</param>
    /// <param name="c40Prefix">The number of leading values that encode in C40 encodation.</param>
    /// <returns>The data codewords.</returns>
    private static int[] Codewords(int[] message, int[] capacities, int c40Prefix)
    {
        int[] codewords = new int[DataMatrixCodewords.MaximumCodewords];
        int count = DataMatrixCodewords.Encode(message, capacities, c40Prefix, codewords, new int[DataMatrixCodewords.ScratchLength(message.Length)]);

        return codewords[..count];
    }

    /// <summary>
    /// Returns a string of digits that repeats 0 to 9. Each pair of digits is one data codeword of ASCII encodation.
    /// </summary>
    /// <param name="count">The number of digits.</param>
    /// <returns>The digits.</returns>
    private static string Digits(int count)
    {
        StringBuilder builder = new(count);
        for (int i = 0; i < count; i++)
        {
            builder.Append((char)('0' + (i % 10)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

    /// <summary>
    /// Encodes the text with the default options and returns the size of the symbol.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The number of module rows and module columns.</returns>
    private static (int Rows, int Columns) Size(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), static (ref symbol) => (symbol.MatrixRows, symbol.Columns));
}
