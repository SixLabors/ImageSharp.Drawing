// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Pdf417Symbology"/> and <see cref="CompactPdf417Symbology"/>. The codeword tests carry the worked examples of ISO/IEC 15438,
/// the pattern tests the bar-space sequences of Annex A, and the row tests the row structure, the quiet zones and the row height of the standard.
/// </summary>
public class Pdf417SymbologyTests
{
    /// <summary>
    /// Section 5.8.2 of ISO/IEC 15438 requires the row height "Y &gt;= 3X". At a 0.3 mm module 3X is 0.9 mm. A bar height of 3X encodes, and a bar height
    /// under 3X throws.
    /// </summary>
    [Fact]
    public void Pdf417_RejectsARowUnderThreeModules()
    {
        Pdf417Symbology symbology = new();
        BarcodeOptions minimum = new() { Dpi = SymbolEncoder.MillimetreDpi, XDimension = 0.3, BarHeight = 0.9 };
        BarcodeOptions under = new() { Dpi = SymbolEncoder.MillimetreDpi, XDimension = 0.3, BarHeight = 0.89 };

        Assert.Null(Record.Exception(() => SymbolEncoder.Encode(symbology, "PDF417", minimum)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SymbolEncoder.Encode(symbology, "PDF417", under));
    }

    /// <summary>
    /// Table 6 of section 5.4.2.2: the data PDF417 is the codewords 453, 178, 121 and 239.
    /// </summary>
    [Fact]
    public void Pdf417_TextCompactionMatchesTableSix()
        => Assert.Equal([453, 178, 121, 239], Codewords("PDF417"));

    /// <summary>
    /// Annex C: the bytes 231, 101, 11, 97, 205 and 2 are the codewords 387, 700, 208, 213 and 302. Six bytes take mode latch 924, section 5.4.3.1.
    /// </summary>
    [Fact]
    public void Pdf417_ByteCompactionMatchesAnnexC()
        => Assert.Equal([924, 387, 700, 208, 213, 302], Codewords("çe\u000BaÍ\u0002"));

    /// <summary>
    /// Annex D: the digits 000213298174000 are the codewords 1, 624, 434, 632, 282 and 200, after mode latch 902.
    /// </summary>
    [Fact]
    public void Pdf417_NumericCompactionMatchesAnnexD()
        => Assert.Equal([902, 1, 624, 434, 632, 282, 200], Codewords("000213298174000"));

    /// <summary>
    /// Section 5.4.4.2: a group of shorter than 44 digits takes INT(digits / 3) + 1 codewords, 10 for 28 digits.
    /// </summary>
    [Fact]
    public void Pdf417_NumericGroupTakesTheCodewordsOfSectionFiveFourFourTwo()
        => Assert.Equal(1 + 10, Codewords("1234567890123456789012345678").Length);

    /// <summary>
    /// Annex Q: the codewords 5, 453, 178, 121 and 239 at error correction level 1 take the error correction codewords 452, 327, 657 and 619. The level 1
    /// coefficients of Annex P, 522, 568, 723 and 809, are the ones the example divides by.
    /// </summary>
    [Fact]
    public void Pdf417_ErrorCorrectionMatchesAnnexQ()
    {
        int[] codewords = [5, 453, 178, 121, 239, 0, 0, 0, 0];
        Pdf417Codewords.AppendErrorCorrection(codewords, 4, new int[5]);

        Assert.Equal([5, 453, 178, 121, 239, 452, 327, 657, 619], codewords);
    }

    /// <summary>
    /// Annex A: the bar-space sequences of codewords 0 to 51 in the three clusters, as the first page of the table prints them.
    /// </summary>
    /// <param name="codeword">The codeword.</param>
    /// <param name="cluster0">The sequence in cluster 0.</param>
    /// <param name="cluster3">The sequence in cluster 3.</param>
    /// <param name="cluster6">The sequence in cluster 6.</param>
    [Theory]
    [InlineData(0, "31111136", "51111125", "21111155")]
    [InlineData(1, "41111144", "61111133", "31111163")]
    [InlineData(2, "51111152", "41111216", "11111246")]
    [InlineData(3, "31111235", "51111224", "21111254")]
    [InlineData(4, "41111243", "61111232", "31111262")]
    [InlineData(5, "51111251", "41111315", "11111345")]
    [InlineData(6, "21111326", "51111323", "21111353")]
    [InlineData(7, "31111334", "61111331", "31111361")]
    [InlineData(8, "21111425", "41111414", "11111444")]
    [InlineData(9, "11111516", "51111422", "21111452")]
    [InlineData(10, "21111524", "41111513", "11111543")]
    [InlineData(11, "11111615", "51111521", "61112114")]
    [InlineData(12, "21112136", "41111612", "11112155")]
    [InlineData(13, "31112144", "41112125", "21112163")]
    [InlineData(14, "41112152", "51112133", "61112213")]
    [InlineData(15, "21112235", "61112141", "11112254")]
    [InlineData(16, "31112243", "31112216", "21112262")]
    [InlineData(17, "41112251", "41112224", "61112312")]
    [InlineData(18, "11112326", "51112232", "11112353")]
    [InlineData(19, "21112334", "31112315", "21112361")]
    [InlineData(20, "11112425", "41112323", "61112411")]
    [InlineData(21, "11113136", "51112331", "11112452")]
    [InlineData(22, "21113144", "31112414", "51113114")]
    [InlineData(23, "31113152", "41112422", "61113122")]
    [InlineData(24, "11113235", "31112513", "11113163")]
    [InlineData(25, "21113243", "41112521", "51113213")]
    [InlineData(26, "31113251", "31112612", "61113221")]
    [InlineData(27, "11113334", "31113125", "11113262")]
    [InlineData(28, "21113342", "41113133", "51113312")]
    [InlineData(29, "11114144", "51113141", "11113361")]
    [InlineData(30, "21114152", "21113216", "51113411")]
    [InlineData(31, "11114243", "31113224", "41114114")]
    [InlineData(32, "21114251", "41113232", "51114122")]
    [InlineData(33, "11115152", "21113315", "41114213")]
    [InlineData(34, "51116111", "31113323", "51114221")]
    [InlineData(35, "31121135", "41113331", "41114312")]
    [InlineData(36, "41121143", "21113414", "41114411")]
    [InlineData(37, "51121151", "31113422", "31115114")]
    [InlineData(38, "21121226", "21113513", "41115122")]
    [InlineData(39, "31121234", "31113521", "31115213")]
    [InlineData(40, "41121242", "21113612", "41115221")]
    [InlineData(41, "21121325", "21114125", "31115312")]
    [InlineData(42, "31121333", "31114133", "31115411")]
    [InlineData(43, "11121416", "41114141", "21116114")]
    [InlineData(44, "21121424", "11114216", "31116122")]
    [InlineData(45, "31121432", "21114224", "21116213")]
    [InlineData(46, "11121515", "31114232", "31116221")]
    [InlineData(47, "21121523", "11114315", "21116312")]
    [InlineData(48, "11121614", "21114323", "11121146")]
    [InlineData(49, "21122135", "31114331", "21121154")]
    [InlineData(50, "31122143", "11114414", "31121162")]
    [InlineData(51, "41122151", "21114422", "11121245")]
    public void Pdf417_PatternsMatchAnnexA(int codeword, string cluster0, string cluster3, string cluster6)
    {
        Assert.Equal(cluster0, Sequence(Pdf417Patterns.Modules[codeword]));
        Assert.Equal(cluster3, Sequence(Pdf417Patterns.Modules[Pdf417Patterns.CodewordCount + codeword]));
        Assert.Equal(cluster6, Sequence(Pdf417Patterns.Modules[(2 * Pdf417Patterns.CodewordCount) + codeword]));
    }

    /// <summary>
    /// Section 5.3.1: every symbol character is four bars and four spaces of one to six modules, 17 modules in all, and its cluster number K = (b1 - b2 + b3 -
    /// b4 + 9) mod 9 is 0, 3 or 6 for the three clusters.
    /// </summary>
    [Fact]
    public void Pdf417_EveryPatternFollowsSectionFiveThreeOne()
    {
        for (int cluster = 0; cluster < 3; cluster++)
        {
            for (int codeword = 0; codeword < Pdf417Patterns.CodewordCount; codeword++)
            {
                string sequence = Sequence(Pdf417Patterns.Modules[(cluster * Pdf417Patterns.CodewordCount) + codeword]);

                Assert.Equal(8, sequence.Length);

                int[] widths = [.. sequence.Select(c => c - '0')];

                Assert.All(widths, width => Assert.InRange(width, 1, 6));
                Assert.Equal(17, widths.Sum());
                Assert.Equal(cluster * 3, (widths[0] - widths[2] + widths[4] - widths[6] + 9) % 9);
            }
        }
    }

    /// <summary>
    /// Section 5.2.2 and Table 1: a row is the start character, the left row indicator, the data columns, the right row indicator and the stop character, 17c +
    /// 69 modules. Section 5.8.3 gives a quiet zone of 2X on all four sides, and section 5.8.2 a row height of at least 3X.
    /// </summary>
    [Fact]
    public void Pdf417_RowsFollowSectionFiveTwoTwo()
        => SymbolEncoder.Encode(new Pdf417Symbology(4), "PDF417 rows and columns", new BarcodeOptions(), static (ref symbol) =>
        {
            foreach (BarcodeRow row in symbol.Rows)
            {
                int[] runs = SymbolEncoder.RowRuns(ref symbol, row);

                Assert.Equal((17 * 4) + 69, runs.Sum());
                Assert.Equal("81111113", string.Concat(runs.Take(8)));
                Assert.Equal("711311121", string.Concat(runs.TakeLast(9)));
                Assert.All(SymbolEncoder.RowBarHeights(ref symbol, row), height => Assert.Equal(3F, height));
            }

            Assert.Equal(2F, symbol.LeadingQuietZone);
            Assert.Equal(2F, symbol.TrailingQuietZone);
            Assert.Equal(2F, symbol.TopQuietZone);
            Assert.Equal(2F, symbol.BottomQuietZone);
        });

    /// <summary>
    /// Annex G: a Compact PDF417 row drops the right row indicator and ends with a single module bar, 17c + 35 modules.
    /// </summary>
    [Fact]
    public void CompactPdf417_RowsFollowAnnexG()
        => SymbolEncoder.Encode(new CompactPdf417Symbology(4), "PDF417 rows and columns", new BarcodeOptions(), static (ref symbol) =>
        {
            foreach (BarcodeRow row in symbol.Rows)
            {
                int[] runs = SymbolEncoder.RowRuns(ref symbol, row);

                Assert.Equal((17 * 4) + 35, runs.Sum());
                Assert.Equal(1, runs[^1]);
            }
        });

    /// <summary>
    /// A bar height in millimetres sets the height of every row.
    /// </summary>
    [Fact]
    public void Pdf417_BarHeightOptionSetsTheRowHeight()
    {
        BarcodeOptions options = new() { BarHeight = 5F * BarcodeSymbology.DefaultXDimension };
        ApproximateFloatComparer comparer = new(1e-4F);

        SymbolEncoder.Encode(new Pdf417Symbology(), "PDF417", options, (ref symbol) =>
        {
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal(5F, height, comparer));
            Assert.Equal(5F * symbol.Rows.Length, symbol.HeightInModules, comparer);
        });
    }

    /// <summary>
    /// Annex B and section 5.4.3: the data is byte values, so a character above U+00FF has no encodation.
    /// </summary>
    [Fact]
    public void Pdf417_RejectsCharactersAboveTheByteRange()
        => Assert.Throws<ArgumentException>(() => Encode(new Pdf417Symbology(), "PDF\u0100"));

    /// <summary>
    /// Empty data throws.
    /// </summary>
    [Fact]
    public void Pdf417_RejectsEmptyData()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Pdf417Symbology(), string.Empty));

    /// <summary>
    /// Section 5.2.1: a symbol has at most 90 rows, so one column holds at most 90 codewords.
    /// </summary>
    [Fact]
    public void Pdf417_RejectsDataThatNeedsMoreThanNinetyRows()
        => Assert.Throws<ArgumentException>(() => Encode(new Pdf417Symbology(1, 2), new string('A', 200)));

    /// <summary>
    /// Section 5.1.1 c) of ISO/IEC 15438 gives the maximum data of a symbol at error correction level 0: 925 data codewords, which encode 1 850 Text
    /// Compaction characters, 1 108 Byte Compaction characters or 2 710 Numeric Compaction characters. A symbology that chooses its own columns and level
    /// encodes each maximum.
    /// </summary>
    /// <param name="character">The character the data repeats: an upper case letter for Text Compaction, é for Byte Compaction, or the digits.</param>
    /// <param name="length">The number of characters.</param>
    [Theory]
    [InlineData('A', 1850)]
    [InlineData('é', 1108)]
    [InlineData('0', 2710)]
    public void Pdf417_HoldsTheMaximumDataOfSectionFiveOneOne(char character, int length)
        => Assert.Null(Record.Exception(() => SymbolEncoder.Encode(new Pdf417Symbology(), new string(character, length), new BarcodeOptions())));

    /// <summary>
    /// Section 5.2.1 and Table 9: 1 to 30 columns and error correction levels 0 to 8.
    /// </summary>
    [Fact]
    public void Pdf417_RejectsColumnsAndLevelsOutsideTheirRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pdf417Symbology(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pdf417Symbology(31));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pdf417Symbology(2, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pdf417Symbology(2, 9));
    }

    /// <summary>
    /// Encodes the text into data codewords.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The data codewords.</returns>
    private static int[] Codewords(string text)
    {
        ValueListBuilder<int> codewords = new(16);

        try
        {
            Pdf417Codewords.Encode(text, ref codewords);
            return codewords.AsSpan().ToArray();
        }
        finally
        {
            codewords.Dispose();
        }
    }

    /// <summary>
    /// Returns the bar and space widths of a symbol character, starting with a bar.
    /// </summary>
    /// <param name="modules">The 17 modules of the symbol character, the first module in bit 16 and 1 for a dark module.</param>
    /// <returns>The widths in modules, one digit for each bar and space.</returns>
    private static string Sequence(int modules)
    {
        System.Text.StringBuilder builder = new();
        int bit = 16;

        while (bit >= 0)
        {
            int dark = (modules >> bit) & 1;
            int width = 0;

            while (bit >= 0 && ((modules >> bit) & 1) == dark)
            {
                width++;
                bit--;
            }

            builder.Append(width);
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
}
