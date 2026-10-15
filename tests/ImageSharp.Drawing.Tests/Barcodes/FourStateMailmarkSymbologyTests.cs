// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="FourStateMailmarkSymbology"/>. Every expected value is from the worked
/// examples of section 2.3 of the Royal Mail Mailmark barcode C and barcode L encoding and decoding
/// instructions, release 1b. The bar strings are their bar identifiers: A for an ascender, D for a
/// descender, F for a full bar and T for a tracker.
/// </summary>
public class FourStateMailmarkSymbologyTests
{
    /// <summary>
    /// The two examples of each document: barcode C of 66 bars from a 22-character application string
    /// and barcode L of 78 bars from a 26-character one.
    /// </summary>
    /// <param name="text">The application string.</param>
    /// <param name="bars">The bar identifiers from the left.</param>
    [Theory]
    [InlineData("1100000000000XY11     ", "TTDTTATTDTAATTDTAATTDTAATTDTTDDAATAADDATAATDDFAFTDDTAADDDTAAFDFAFF")]
    [InlineData("21B2254800659JW5O9QA6Y", "DAATATTTADTAATTFADDDDTTFTFDDDDFFDFDAFTADDTFFTDDATADTTFATTDAFDTFDDA")]
    [InlineData("11000000000000000XY11     ", "TTDTTATDDTTATTDTAATTDTAATDDTTATTDTTDATFTAATDDTAATDDTATATFAADDAATAATDDTAADFTFTA")]
    [InlineData("41038422416563762EF61AH8T ", "DTTFATTDDTATTTATFTDFFFTFDFDAFTTTADTTFDTFDDDTDFDDFTFAADTFDTDTDTFAATAFDDTAATTDTT")]
    public void EncodesTheExamplesOfTheInstructions(string text, string bars)
        => Assert.Equal(bars, SymbolEncoder.Read(new FourStateMailmarkSymbology(), text, new BarcodeOptions(), States));

    /// <summary>
    /// Section 2.2.2 of barcode C, Table 12: the destination "JW5O9QA6Y" has the internal user field
    /// value 118,259,964,139, and Table 9 gives the international designation 0. Barcode L, Table 12:
    /// "EF61AH8T " with one trailing space.
    /// </summary>
    [Fact]
    public void ConvertsTheDestinationField()
    {
        Assert.Equal(0UL, FourStateMailmarkEncoder.DestinationValue("XY11     "));
        Assert.Equal(118_259_964_139UL, FourStateMailmarkEncoder.DestinationValue("JW5O9QA6Y"));
        Assert.Equal(1UL, FourStateMailmarkEncoder.DestinationValue("A0A0AA0A "));
        Assert.True(FourStateMailmarkEncoder.DestinationValue("EF61AH8T ") > 5_408_000_000UL);
    }

    /// <summary>
    /// Section 2.2.5 of barcode C, Table 10 of Example 1: the data numbers of the consolidated value 4
    /// give the check numbers 14, 7, 23, 3, 23 and 15. Barcode L, Table 10 of Example 1: 20, 1, 20, 7,
    /// 14, 11 and 18.
    /// </summary>
    [Fact]
    public void GeneratesTheCheckNumbersOfExampleOne()
    {
        byte[] numbers = new byte[16];
        FourStateMailmarkEncoder.DataNumbers(4, numbers, 9);
        Assert.Equal(4, numbers[15]);
        byte[] check = new byte[6];
        FourStateMailmarkEncoder.CheckNumbers(numbers, [1, 17, 26, 30, 27, 30, 24], check);
        Assert.Equal([14, 7, 23, 3, 23, 15], check);

        byte[] numbersL = new byte[19];
        FourStateMailmarkEncoder.DataNumbers(4, numbersL, 11);
        Assert.Equal(4, numbersL[18]);
        byte[] checkL = new byte[7];
        FourStateMailmarkEncoder.CheckNumbers(numbersL, [1, 5, 9, 5, 26, 17, 25, 22], checkL);
        Assert.Equal([20, 1, 20, 7, 14, 11, 18], checkL);
    }

    /// <summary>
    /// Section 2.2.4 of barcode C, Table 13 of Example 2: the consolidated data value
    /// 354,779,892,418,644,019,776,828 gives the data numbers 15, 22, 3, 25, 23, 26, 7, 3, 20, 14, 1, 4,
    /// 16, 3, 9 and 28.
    /// </summary>
    [Fact]
    public void ConvertsTheConsolidatedValueOfExampleTwoToDataNumbers()
    {
        UInt128 consolidated = UInt128.Parse("354779892418644019776828", CultureInfo.InvariantCulture);
        byte[] numbers = new byte[16];
        FourStateMailmarkEncoder.DataNumbers(consolidated, numbers, 9);
        Assert.Equal([15, 22, 3, 25, 23, 26, 7, 3, 20, 14, 1, 4, 16, 3, 9, 28], numbers);
    }

    /// <summary>
    /// The bars are the Royal Mail 4-state bars of Table 11 of the Mailmark barcode definition document.
    /// </summary>
    [Fact]
    public void DrawsTheRoyalMailDimensions()
        => SymbolEncoder.Encode(new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(1F / 54F, symbol.RunUnit);
            Assert.Equal(131, symbol.Runs.Length);
            Assert.Equal((float)(2 / 0.54), symbol.LeadingQuietZone);
            Assert.Equal((float)(2 / 0.54), symbol.TrailingQuietZone);
        });

    [Fact]
    public void PrintsTheApplicationStringAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y", options, SymbolEncoder.Texts);
        Assert.Equal("21B2254800659JW5O9QA6Y", Assert.Single(texts));
    }

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

    /// <summary>
    /// Reads the bar states of a symbol from the bar tops and heights: F for a full bar, A for an
    /// ascender, D for a descender and T for a tracker.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The bar states.</returns>
    private static string States(ref BarcodeSymbol symbol)
    {
        float ascender = (float)(1.90 / 0.54);
        float tracker = (float)(1.30 / 0.54);
        char[] states = new char[symbol.BarHeights.Length];
        for (int i = 0; i < states.Length; i++)
        {
            bool up = symbol.BarTops[i] == 0F;
            bool down = symbol.BarTops[i] + symbol.BarHeights[i] > ascender + tracker + 0.001F;
            states[i] = up && down ? 'F' : up ? 'A' : down ? 'D' : 'T';
        }

        return new string(states);
    }
}
