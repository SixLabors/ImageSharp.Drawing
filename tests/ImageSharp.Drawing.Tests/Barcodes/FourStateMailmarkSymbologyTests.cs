// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="FourStateMailmarkSymbology"/>. Every expected value is from the worked examples of section 2.3 of the Royal Mail Mailmark
/// barcode C and barcode L encoding and decoding instructions, release 1b. The bar strings are their bar identifiers: A for an ascender, D for a descender, F
/// for a full bar and T for a tracker.
/// </summary>
public class FourStateMailmarkSymbologyTests
{
    /// <summary>
    /// The two examples of each document: barcode C of 66 bars from a 22-character application string and barcode L of 78 bars from a 26-character one.
    /// </summary>
    /// <param name="text">The application string.</param>
    /// <param name="bars">The bar identifiers from the left.</param>
    [Theory]
    [InlineData("1100000000000XY11     ", "TTDTTATTDTAATTDTAATTDTAATTDTTDDAATAADDATAATDDFAFTDDTAADDDTAAFDFAFF")]
    [InlineData("21B2254800659JW5O9QA6Y", "DAATATTTADTAATTFADDDDTTFTFDDDDFFDFDAFTADDTFFTDDATADTTFATTDAFDTFDDA")]
    [InlineData("11000000000000000XY11     ", "TTDTTATDDTTATTDTAATTDTAATDDTTATTDTTDATFTAATDDTAATDDTATATFAADDAATAATDDTAADFTFTA")]
    [InlineData("41038422416563762EF61AH8T ", "DTTFATTDDTATTTATFTDFFFTFDFDAFTTTADTTFDTFDDDTDFDDFTFAADTFDTDTDTFAATAFDDTAATTDTT")]
    public void EncodesTheExamplesOfTheInstructions(string text, string bars)
        => Assert.Equal(bars, SymbolEncoder.Read(new FourStateMailmarkSymbology(), text, new BarcodeOptions(), SymbolEncoder.FourStates));

    /// <summary>
    /// Section 2.2.2 of barcode C, Table 12: the destination "JW5O9QA6Y" has the internal user field value 118,259,964,139, and Table 9 gives the international
    /// designation "XY11" with five spaces the value 0. Barcode L, Table 12: "EF61AH8T " with one trailing space.
    /// </summary>
    [Fact]
    public void ConvertsTheDestinationField()
    {
        Assert.Equal(0UL, FourStateMailmarkEncoder.DestinationValue("XY11     ", 0));
        Assert.Equal(118_259_964_139UL, FourStateMailmarkEncoder.DestinationValue("JW5O9QA6Y", 0));
        Assert.Equal(1UL, FourStateMailmarkEncoder.DestinationValue("A0A0AA0A ", 0));
        Assert.True(FourStateMailmarkEncoder.DestinationValue("EF61AH8T ", 0) > 5_408_000_000UL);
    }

    /// <summary>
    /// Section 2.2.5 of barcode C, Table 10 of Example 1: the data numbers of the consolidated value 4 give the check numbers 14, 7, 23, 3, 23 and 15. Barcode
    /// L, Table 10 of Example 1: 20, 1, 20, 7, 14, 11 and 18.
    /// </summary>
    [Fact]
    public void GeneratesTheCheckNumbersOfExampleOne()
    {
        int[] numbers = new int[16];
        FourStateMailmarkEncoder.DataNumbers(4, numbers, 9);

        Assert.Equal(4, numbers[15]);

        int[] check = new int[6];
        FourStateMailmarkEncoder.CheckNumbers(numbers, [1, 17, 26, 30, 27, 30, 24], check);

        Assert.Equal([14, 7, 23, 3, 23, 15], check);

        int[] numbersL = new int[19];
        FourStateMailmarkEncoder.DataNumbers(4, numbersL, 11);

        Assert.Equal(4, numbersL[18]);

        int[] checkL = new int[7];
        FourStateMailmarkEncoder.CheckNumbers(numbersL, [1, 5, 9, 5, 26, 17, 25, 22], checkL);

        Assert.Equal([20, 1, 20, 7, 14, 11, 18], checkL);
    }

    /// <summary>
    /// Section 2.2.4 of barcode C, Table 13 of Example 2: the consolidated data value 354,779,892,418,644,019,776,828 gives the data numbers 15, 22, 3, 25, 23,
    /// 26, 7, 3, 20, 14, 1, 4, 16, 3, 9 and 28.
    /// </summary>
    [Fact]
    public void ConvertsTheConsolidatedValueOfExampleTwoToDataNumbers()
    {
        UInt128 consolidated = UInt128.Parse("354779892418644019776828", CultureInfo.InvariantCulture);
        int[] numbers = new int[16];

        FourStateMailmarkEncoder.DataNumbers(consolidated, numbers, 9);

        Assert.Equal([15, 22, 3, 25, 23, 26, 7, 3, 20, 14, 1, 4, 16, 3, 9, 28], numbers);
    }

    /// <summary>
    /// The bars are the Royal Mail 4-state bars of Table 11 of the Mailmark barcode definition document, 66 of them.
    /// </summary>
    [Fact]
    public void DrawsTheRoyalMailDimensions()
        => SymbolEncoder.Encode(new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(131, symbol.Runs.Length);
            AssertTableEleven(ref symbol);
        });

    /// <summary>
    /// Asserts the acceptable sizes of Table 11 of the Mailmark barcode definition document: an ascender and a descender of 1.60 to 2.16 mm, a tracker of 1.02
    /// to 1.52 mm, a full bar of 4.22 to 5.84 mm and a bar width of 0.38 to 0.63 mm. The text below Table 11: "It is required that there are between 20 and 24
    /// bars per inch (25.4mm) and they must be equally spaced." Section 3.5.2: "There must be a clear zone of at least 2mm around all edges".
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    internal static void AssertTableEleven(ref BarcodeSymbol symbol)
    {
        double unit = symbol.RunUnit * symbol.XDimension;
        int pitch = symbol.Runs[0] + symbol.Runs[1];
        for (int i = 0; i < symbol.Runs.Length; i += 2)
        {
            Assert.InRange(symbol.Runs[i] * unit, 0.38, 0.63);
        }

        for (int i = 1; i < symbol.Runs.Length; i += 2)
        {
            Assert.Equal(pitch, symbol.Runs[i - 1] + symbol.Runs[i]);
        }

        Assert.InRange(25.4 / (pitch * unit), 20, 24);

        // A tracker stands below the ascender part and above the descender part, and the full bar spans all three.
        string states = SymbolEncoder.FourStates(ref symbol);
        int tracker = states.IndexOf('T');
        float trackerTop = symbol.BarTops[tracker];
        float trackerBottom = trackerTop + symbol.BarHeights[tracker];
        float bottom = SymbolEncoder.BarBottom(ref symbol);

        Assert.InRange(trackerTop * symbol.XDimension, 1.60, 2.16);
        Assert.InRange(symbol.BarHeights[tracker] * symbol.XDimension, 1.02, 1.52);
        Assert.InRange((bottom - trackerBottom) * symbol.XDimension, 1.60, 2.16);
        Assert.InRange(bottom * symbol.XDimension, 4.22, 5.84);

        Assert.True(symbol.LeadingQuietZone * symbol.XDimension >= 2 - 1e-4);
        Assert.True(symbol.TrailingQuietZone * symbol.XDimension >= 2 - 1e-4);
        Assert.True(symbol.TopQuietZone * symbol.XDimension >= 2 - 1e-4);
        Assert.True(symbol.BottomQuietZone * symbol.XDimension >= 2 - 1e-4);
    }

    /// <summary>
    /// The Mailmark barcode definition document and the barcode C and L instructions define no text line, so a font prints none.
    /// </summary>
    [Fact]
    public void PrintsNoText()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Empty(SymbolEncoder.Read(new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y", options, SymbolEncoder.Texts));
    }

    /// <summary>
    /// The encoder rejects an application string that is not 22 or 26 characters. It also rejects a format above 4, a Version ID other than 1, a class
    /// outside its character set, a letter in a numeric field, a destination that is not a valid post code plus DPS, and small letters.
    /// </summary>
    /// <param name="text">The application string.</param>
    [Theory]
    [InlineData("")]
    [InlineData("21B2254800659JW5O9QA6")]
    [InlineData("21B2254800659JW5O9QA6YA")]
    [InlineData("51B2254800659JW5O9QA6Y")]
    [InlineData("22B2254800659JW5O9QA6Y")]
    [InlineData("21F2254800659JW5O9QA6Y")]
    [InlineData("21B2A54800659JW5O9QA6Y")]
    [InlineData("21B2254800659JW5O9QC6Y")]
    [InlineData("21B2254800659XY11    ")]
    [InlineData("21B2254800659jw5o9qa6y")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new FourStateMailmarkSymbology(), text, new BarcodeOptions()));
}
