// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="IntelligentMailSymbology"/>. Every expected value is from the four worked examples of Appendix C of USPS-B-3200 revision H,
/// Tables 13 to 16, which share the tracking code 01234567094987654321 and add a routing code of 0, 5, 9 or 11 digits.
/// </summary>
public class IntelligentMailSymbologyTests
{
    /// <summary>
    /// The millimetres in an inch.
    /// </summary>
    private const double Inch = 25.4;

    /// <summary>
    /// Step 1 of each example, the binary data as 13 bytes.
    /// </summary>
    /// <param name="text">The tracking code followed by the routing code.</param>
    /// <param name="hex">The binary data.</param>
    [Theory]
    [InlineData("01234567094987654321", "00000000001122103B5C2004B1")]
    [InlineData("0123456709498765432101234", "0000000D138A87BAB5CF3804B1")]
    [InlineData("01234567094987654321012345678", "000202BDC097711204D21804B1")]
    [InlineData("0123456709498765432101234567891", "016907B2A24ABC16A2E5C004B1")]
    public void ConvertsTheDigitsToBinary(string text, string hex)
    {
        UInt128 binary = IntelligentMailEncoder.Binary(text.AsSpan(0, 20), text.AsSpan(20));

        Assert.Equal(hex, binary.ToString("X26", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Step 2 of each example, the frame check sequence.
    /// </summary>
    /// <param name="text">The tracking code followed by the routing code.</param>
    /// <param name="fcs">The frame check sequence.</param>
    [Theory]
    [InlineData("01234567094987654321", 0x051)]
    [InlineData("0123456709498765432101234", 0x065)]
    [InlineData("01234567094987654321012345678", 0x606)]
    [InlineData("0123456709498765432101234567891", 0x751)]
    public void GeneratesTheFrameCheckSequence(string text, int fcs)
    {
        UInt128 binary = IntelligentMailEncoder.Binary(text.AsSpan(0, 20), text.AsSpan(20));

        Assert.Equal(fcs, IntelligentMailEncoder.FrameCheckSequence(binary));
    }

    /// <summary>
    /// Steps 3 to 5 of Example 4, Table 16: the codewords after the orientation and frame check sequence changes, and the characters with the frame check
    /// sequence bits applied.
    /// </summary>
    [Fact]
    public void ConvertsExampleFourToCodewordsAndCharacters()
    {
        UInt128 binary = IntelligentMailEncoder.Binary("01234567094987654321", "01234567891");
        int fcs = IntelligentMailEncoder.FrameCheckSequence(binary);

        int[] codewords = new int[10];
        IntelligentMailEncoder.Codewords(binary, fcs, codewords);

        Assert.Equal([673, 787, 607, 1022, 861, 19, 816, 1294, 35, 602], codewords);

        int[] characters = new int[10];
        IntelligentMailEncoder.Characters(codewords, fcs, characters);

        Assert.Equal([0x0DCB, 0x085C, 0x08E4, 0x0B06, 0x06DD, 0x1740, 0x17C6, 0x1200, 0x123F, 0x1B2B], characters);
    }

    /// <summary>
    /// Step 5 of Example 1, Table 13, whose codewords below 1287 all select Table 19 and whose frame check sequence bits 0, 4 and 6 negate characters A, E and
    /// G.
    /// </summary>
    [Fact]
    public void ConvertsExampleOneToCharacters()
    {
        int[] characters = new int[10];
        IntelligentMailEncoder.Characters([0, 0, 0, 0, 559, 202, 508, 451, 124, 34], 0x051, characters);

        Assert.Equal([0x1FE0, 0x001F, 0x001F, 0x001F, 0x0ADB, 0x01A3, 0x1BC3, 0x1838, 0x012B, 0x0076], characters);
    }

    /// <summary>
    /// Step 6 of each example, the 65 bars.
    /// </summary>
    /// <param name="text">The tracking code followed by the routing code.</param>
    /// <param name="bars">The bar states from the left.</param>
    [Theory]
    [InlineData("01234567094987654321", "ATTFATTDTTADTAATTDTDTATTDAFDDFADFDFTFFFFFTATFAAAATDFFTDAADFTFDTDT")]
    [InlineData("0123456709498765432101234", "DTTAFADDTTFTDTFTFDTDDADADAFADFATDDFTAAAFDTTADFAAATDFDTDFADDDTDFFT")]
    [InlineData("01234567094987654321012345678", "ADFTTAFDTTTTFATTADTAAATFTFTATDAAAFDDADATATDTDTTDFDTDATADADTDFFTFA")]
    [InlineData("0123456709498765432101234567891", "AADTFFDFTDADTAADAATFDTDDAAADDTDTTDAFADADDDTFFFDDTTTADFAAADFTDAADA")]
    public void EncodesTheBars(string text, string bars)
        => Assert.Equal(bars, SymbolEncoder.Read(new IntelligentMailSymbology(), text, new BarcodeOptions(), SymbolEncoder.FourStates));

    /// <summary>
    /// Section 2.3.1: "an overall barcode height of 0.125 inch to 0.165 inch", "an overall barcode pitch of 22±2 bars per inch", "a width of 0.020±0.005 inch",
    /// "spacing between bars of 0.012 inch to 0.040 inch", a distance from the lead edge of the 1st bar to the lead edge of the 65th that "shall never be less
    /// than 2.667 inches", and from the lead edge of the 1st to the trail edge of the 65th that "shall never be greater than 3.225 inches". Section 2.3.2: "A
    /// minimum clear zone shall be 0.028 inch above and below the barcode and 0.125 inch on each end of the barcode."
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfFigureSix()
        => SymbolEncoder.Encode(new IntelligentMailSymbology(), "0123456709498765432101234567891", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(129, symbol.Runs.Length);

            double unit = symbol.RunUnit * symbol.XDimension / Inch;
            int leadToLead = 0;

            for (int i = 0; i < symbol.Runs.Length - 1; i++)
            {
                Assert.InRange(symbol.Runs[i] * unit, (i & 1) == 0 ? 0.015 : 0.012, (i & 1) == 0 ? 0.025 : 0.040);
                leadToLead += symbol.Runs[i];
            }

            Assert.InRange(symbol.Runs[^1] * unit, 0.015, 0.025);
            Assert.InRange(64 / (leadToLead * unit), 20, 24);
            Assert.True(leadToLead * unit >= 2.667);
            Assert.True((leadToLead + symbol.Runs[^1]) * unit <= 3.225);

            Assert.InRange(SymbolEncoder.BarBottom(ref symbol) * symbol.XDimension / Inch, 0.125, 0.165);
            Assert.True(symbol.TopQuietZone * symbol.XDimension / Inch >= 0.028 - 1e-6);
            Assert.True(symbol.BottomQuietZone * symbol.XDimension / Inch >= 0.028 - 1e-6);
            Assert.True(symbol.LeadingQuietZone * symbol.XDimension / Inch >= 0.125 - 1e-6);
            Assert.True(symbol.TrailingQuietZone * symbol.XDimension / Inch >= 0.125 - 1e-6);
        });

    /// <summary>
    /// Section 2.4.3: the fields of the tracking code and the groups of the routing code, separated by spaces, with the example "01 234 567094 987654321 01234
    /// 5678 91" for the fourth example of Table 5. A mailer ID that starts with 9 is 9 digits and leaves a 6-digit serial number. Section 2.4.2: "the left edge
    /// of the leftmost digit aligns with the leftmost bar of the IMb". Section 2.4.1: the top of the line "shall be at least 0.028 inch below the bottom of the
    /// IMb but shall not be more than 0.5 inch below the bottom of the IMb".
    /// </summary>
    /// <param name="text">The digits.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("0123456709498765432101234567891", "01 234 567094 987654321 01234 5678 91")]
    [InlineData("01234567094987654321012345678", "01 234 567094 987654321 01234 5678")]
    [InlineData("0123456709498765432101234", "01 234 567094 987654321 01234")]
    [InlineData("01234567094987654321", "01 234 567094 987654321")]
    [InlineData("01234901234567891234", "01 234 901234567 891234")]
    public void PrintsTheFieldsOfSectionTwoFourThree(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new IntelligentMailSymbology(), text, options, (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());

            Assert.Equal(expected, symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
            Assert.Equal(BarcodeTextAlignment.Left, placement.Alignment);
            Assert.Equal(0F, placement.Left);
            Assert.InRange((placement.TextEdge - SymbolEncoder.BarBottom(ref symbol)) * symbol.XDimension / Inch, 0.028 - 1e-6, 0.5);
        });
    }

    /// <summary>
    /// The input is a 20 digit tracking code and a routing code of 0, 5, 9 or 11 digits, and the second digit of the barcode identifier is 0 to 4. The
    /// symbology rejects other lengths, a second digit above 4, a letter and full width digits.
    /// </summary>
    /// <param name="text">The tracking code followed by the routing code.</param>
    [Theory]
    [InlineData("")]
    [InlineData("0123456709498765432")]
    [InlineData("012345670949876543210")]
    [InlineData("0123456709498765432101234567891012")]
    [InlineData("0123456709498765432A")]
    [InlineData("05234567094987654321")]
    [InlineData("０1234567094987654321")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new IntelligentMailSymbology(), text, new BarcodeOptions()));
}
