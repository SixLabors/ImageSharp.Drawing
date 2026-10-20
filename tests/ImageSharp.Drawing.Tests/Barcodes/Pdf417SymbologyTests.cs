// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Pdf417Symbology"/> and <see cref="CompactPdf417Symbology"/>. The codeword tests carry the worked examples of ISO/IEC 15438.
/// Each expected row is the alternating bar and space module widths of one row, starting with a bar, taken from an independent reference implementation through
/// its raw encoding API, so the compaction modes, the error correction codewords, the matrix size and the row indicators all show up here.
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
    /// Gets the cases of <see cref="Pdf417_MatchesReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, int, int, string> Pdf417_MatchesReferenceRowsData { get; } = new()
    {
        // The text of Table 6 at the recommended level 2: seven rows of two columns.
        {
            "PDF417",
            0,
            -1,
            "8111111351111152211113263131222341111144711311121|" +
            "8111111351111323211433124113111541111216711311121|" +
            "8111111331111163112222432352121121111353711311121|" +
            "8111111321114251231312232421151111114243711311121|" +
            "8111111321113414322431112212432141113232711311121|" +
            "8111111341114114125124113123115141114411711311121|" +
            "8111111331123151122322141411313321123143711311121"
        },

        // Text over two columns, which the row count follows.
        {
            "This is PDF417",
            2,
            -1,
            "8111111351111152211115243411133141111144711311121|" +
            "8111111341111414421114131131226141111216711311121|" +
            "8111111331111163212311431531122211111444711311121|" +
            "8111111321114251414312112113314211114243711311121|" +
            "8111111321113513351321113111342241113232711311121|" +
            "8111111341114114215114121411134241115122711311121|" +
            "8111111331123151112422142514112121123143711311121|" +
            "8111111361121132532212111313411311116133711311121|" +
            "8111111321122162152111332153111351123113711311121"
        },

        // The text of Figure 1 in Text Compaction mode.
        {
            "PDF417 Symbology Standard",
            0,
            -1,
            "811111135111115231112243313122235121114251111152711311121|" +
            "811111136111133141131115221132153511231141111216711311121|" +
            "811111131111124611421314113114242224114131111361711311121|" +
            "811111132111425113123133141414112512122221114251711311121|" +
            "811111133111342242222113612311214111222441113232711311121|" +
            "811111135111412251143111115133215311321131115114711311121|" +
            "811111133112315121332213513111323124112331123151711311121|" +
            "811111135112112423214113421113142212321411116133711311121"
        },

        // Every sub-mode of Table 5: Alpha, Lower, Mixed and Punctuation with their latches.
        {
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()",
            0,
            -1,
            "81111113311112354112124241111144111232342115112431111235711311121|" +
            "81111113411114141114522112114215312142222122421351111224711311121|" +
            "81111113211112541224133122311431214211241511114311111444711311121|" +
            "81111113111151521322221422323221151115122111425111115152711311121|" +
            "81111113211135133112322311134511421114134212121421113315711311121|" +
            "81111113411142131221315211321621541131112141152141115122711311121|" +
            "81111113111232341316131113214132414121222233231111123234711311121|" +
            "81111113611211326111113321116141511312221114522121116141711311121|" +
            "81111113111222531122325115321221244111223411511151123113711311121|" +
            "81111113111325221321423121241214132222141323241111132522711311121|" +
            "81111113211234134212151122124222411211162115331121123215711311121|" +
            "81111113111314421241111612131144131411422251131261132211711311121"
        },

        // Seventy digits, all numeric: two groups of section 5.4.4.2, 44 digits and 26.
        {
            "1234567890123456789012345678901234567890123456789012345678901234567890",
            0,
            -1,
            "811111133111123521113342124141131313242151111152711311121|" +
            "811111134111141431122215311324123216211151111224711311121|" +
            "811111131111124663112112111224511111225411111444711311121|" +
            "811111131111515212114242122234211331311421114251711311121|" +
            "811111132111351354111221231132141152113321113315711311121|" +
            "811111135111412211112155113322322131153141115122711311121|" +
            "811111131112323411212514114241133331131231123151711311121|" +
            "811111136112113221123611251232114112212421116141711311121|" +
            "811111136112221223311331211312522352121151123113711311121|" +
            "811111131113252214232212251112233212123311132423711311121|" +
            "811111132112341332132213251331115111132321123215711311121|" +
            "811111132113135113421114162111322211125361132211711311121"
        },

        // Eight digits, all numeric: Numeric Compaction mode for data that is only digits.
        {
            "12345678",
            0,
            -1,
            "8111111351111152211113261241411341111144711311121|" +
            "8111111351111323311325113314211241111216711311121|" +
            "8111111331111163132115312352121121111353711311121|" +
            "8111111321114251141414112221232311114243711311121|" +
            "8111111321113414221334111151214241113232711311121|" +
            "8111111341114114221361111611114241114411711311121|" +
            "8111111331123151131331321421211521123143711311121"
        },

        // Thirteen digits between text: Numeric Compaction mode inside, Text Compaction mode around it.
        {
            "Code 1234567890123 then text",
            0,
            -1,
            "811111135111115221112334111322251221332351111152711311121|" +
            "811111134111141421133412116112234111222441111216711311121|" +
            "811111131111124611141144116131131342131211111444711311121|" +
            "811111132111425112123233142322121512212321114251711311121|" +
            "811111132111351341313311311322141143124141113232711311121|" +
            "811111135111412262112212114322222352121141115122711311121|" +
            "811111133112315114232212233321122311132431123151711311121|" +
            "811111136112113241151113112213614312222111116133711311121|" +
            "811111136112221225411121214114221254211151123113711311121"
        },

        // A byte value in text: the mode shift 913 of section 5.4.3.2, and a tab and a line of punctuation.
        {
            "Hello, World! ¢ tab\there",
            0,
            -1,
            "811111135111115221112334412212322115132251111152711311121|" +
            "811111134111141411221262161142113511231141111216711311121|" +
            "811111131111124612412322314112325114311111111444711311121|" +
            "811111132111425131241222424211122211233321114251711311121|" +
            "811111132111351311431241611111331611411241113232711311121|" +
            "811111135111412242124211132222412352121141115122711311121|" +
            "811111133112315114232212121231342314122231123151711311121|" +
            "811111136112113251312311633111113313312111116133711311121|" +
            "811111136112221212321125214511211542111251123113711311121"
        },

        // Six bytes: mode latch 924 and one base 256 to base 900 conversion, section 5.4.3.3.
        {
            "éèêëàâ",
            0,
            -1,
            "8111111351111152211114252333211241111144711311121|" +
            "8111111361111331112262211341124141111216711311121|" +
            "8111111331111163123413211111215531111361711311121|" +
            "8111111321114251123241221423221211114243711311121|" +
            "8111111331113422211332142122522141113232711311121|" +
            "8111111341114114225212121213144131115114711311121|" +
            "8111111331123151111333323213123221123143711311121|" +
            "8111111351121124121253212113341211116133711311121"
        },

        // Seven bytes: mode latch 901, one conversion and one byte on its own codeword.
        {
            "éèêëàâä",
            0,
            -1,
            "8111111351111152211114251514131141111144711311121|" +
            "8111111361111331112262211341124141111216711311121|" +
            "8111111331111163123413211111215531111361711311121|" +
            "8111111321114251123241221212414211114243711311121|" +
            "8111111331113422312142224212151141113232711311121|" +
            "8111111341114114231111531161311331115114711311121|" +
            "8111111331123151222422214141121321123143711311121|" +
            "8111111351121124622211215122131211116133711311121"
        },

        // Punctuation and lower case letters alternating: shifts to Punctuation, section 5.4.2.3.
        {
            "a;b<c>d@e[f\\g]h_i`j~k!l",
            0,
            -1,
            "811111135111115221112334151115121513212251111152711311121|" +
            "811111134111141411115323411132321152133141111216711311121|" +
            "811111131111124651143111111315411441121311111444711311121|" +
            "811111132111425131211225321121434241111321114251711311121|" +
            "811111132111351342113231421114133611211241113232711311121|" +
            "811111135111412223111351112414311532122141115122711311121|" +
            "811111133112315123111225222513112124151131123151711311121|" +
            "811111136112113221163112421223214421211211116133711311121|" +
            "811111136112221223521112151111432531113151123113711311121"
        },

        // Mixed case: shifts to Alpha from Lower, three columns.
        {
            "MiXeD cAsE 42 MiXeD",
            3,
            -1,
            "811111135111115231112243522111412213223251111152711311121|" +
            "811111136111133113211261251141213323311141111216711311121|" +
            "811111131111124612512213214214211211245131111361711311121|" +
            "811111132111425112333113111322252511122321114251711311121|" +
            "811111133111342222133114142151122113351141113232711311121|" +
            "811111135111412251143111112513311136122131115114711311121|" +
            "811111133112315113214231121232331224211431123151711311121|" +
            "811111135112112461311311411215123115231111116133711311121"
        },

        // One column at level 0: two error correction codewords, Table 9.
        {
            "PDF417",
            1,
            0,
            "81111113511111525111125131111136711311121|" +
            "81111113511111252125411141111216711311121|" +
            "81111113211111551121153321111155711311121|" +
            "81111113211142511114252121114152711311121|" +
            "81111113211132164211323141113232711311121|" +
            "81111113511134112321114351113411711311121|" +
            "81111113311231514231132111123135711311121"
        },

        // Five columns at level 4: 32 error correction codewords and pad codewords, section 5.9.2.
        {
            "PDF417",
            5,
            4,
            "8111111351111152211114253131222351211142111425213122132341111243711311121|" +
            "8111111341112125116111241161112411611124251141214313111341111216711311121|" +
            "8111111331111262311162211442212112431411111521511233142121112163711311121|" +
            "8111111321114251221214233221314111142323212314132123161151116111711311121|" +
            "8111111341114141115112331232115261112141532211122222421241113232711311121|" +
            "8111111351114221114131331321214311212442134115112311216121116114711311121|" +
            "8111111331123151211223332211415133141131311413311115111621123242711311121|" +
            "8111111351121322541211213113211534122311251133111213421311116133711311121"
        },

        // Four columns at level 3: 16 error correction codewords.
        {
            "The quick brown fox jumps over the lazy dog",
            4,
            3,
            "81111113311112351111323534111331112132253143211231111235711311121|" +
            "81111113511114221313512151121223341421112312311451111224711311121|" +
            "81111113211112542241223113411313213412311432112321111452711311121|" +
            "81111113111151522411314111313116114251212321222311115152711311121|" +
            "81111113311135211221511441313311211334121212451121113315711311121|" +
            "81111113411142131431132213411214113313232352121131115213711311121|" +
            "81111113111232343143221111141513241123311231321411123234711311121|" +
            "81111113411212153114241131253111411123231222511321116141711311121|" +
            "81111113111222533141123213322132123512211235122161123121711311121|" +
            "81111113111325221213333115131411122424112422111411132522711311121"
        },
    };

    /// <summary>
    /// Each row of the symbol matches the reference row.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="columns">The number of data columns, or 0 for the default.</param>
    /// <param name="errorCorrectionLevel">The error correction level, or -1 for the default.</param>
    /// <param name="expected">The run widths of each reference row, separated by "|".</param>
    [Theory]
    [MemberData(nameof(Pdf417_MatchesReferenceRowsData))]
    public void Pdf417_MatchesReferenceRows(string text, int columns, int errorCorrectionLevel, string expected)
        => AssertRows(expected, Create(columns, errorCorrectionLevel, false), text);

    /// <summary>
    /// Gets the cases of <see cref="CompactPdf417_MatchesReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, int, int, string> CompactPdf417_MatchesReferenceRowsData { get; } = new()
    {
        // The text of Table 6 at the recommended level 2: seven rows of two columns.
        {
            "PDF417",
            0,
            -1,
            "811111135111115221111326313122231|" +
            "811111135111132321143312411311151|" +
            "811111133111116311222243235212111|" +
            "811111132111425123131223242115111|" +
            "811111132111341432243111221243211|" +
            "811111134111411412512411312311511|" +
            "811111133112315112232214141131331"
        },

        // Text over two columns, which the row count follows.
        {
            "This is PDF417",
            2,
            -1,
            "811111135111115221111524341113311|" +
            "811111134111141442111413113122611|" +
            "811111133111116321231143153112221|" +
            "811111132111425141431211211331421|" +
            "811111132111351335132111311134221|" +
            "811111134111411421511412141113421|" +
            "811111133112315111242214251411211|" +
            "811111136112113253221211131341131|" +
            "811111132112216215211133215311131"
        },

        // The text of Figure 1 in Text Compaction mode.
        {
            "PDF417 Symbology Standard",
            0,
            -1,
            "81111113511111523111224331312223512111421|" +
            "81111113611113314113111522113215351123111|" +
            "81111113111112461142131411311424222411411|" +
            "81111113211142511312313314141411251212221|" +
            "81111113311134224222211361231121411122241|" +
            "81111113511141225114311111513321531132111|" +
            "81111113311231512133221351311132312411231|" +
            "81111113511211242321411342111314221232141"
        },

        // Every sub-mode of Table 5: Alpha, Lower, Mixed and Punctuation with their latches.
        {
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()",
            0,
            -1,
            "8111111331111235411212424111114411123234211511241|" +
            "8111111341111414111452211211421531214222212242131|" +
            "8111111321111254122413312231143121421124151111431|" +
            "8111111311115152132222142232322115111512211142511|" +
            "8111111321113513311232231113451142111413421212141|" +
            "8111111341114213122131521132162154113111214115211|" +
            "8111111311123234131613111321413241412122223323111|" +
            "8111111361121132611111332111614151131222111452211|" +
            "8111111311122253112232511532122124411122341151111|" +
            "8111111311132522132142312124121413222214132324111|" +
            "8111111321123413421215112212422241121116211533111|" +
            "8111111311131442124111161213114413141142225113121"
        },

        // Seventy digits, all numeric: two groups of section 5.4.4.2, 44 digits and 26.
        {
            "1234567890123456789012345678901234567890123456789012345678901234567890",
            0,
            -1,
            "81111113311112352111334212414113131324211|" +
            "81111113411114143112221531132412321621111|" +
            "81111113111112466311211211122451111122541|" +
            "81111113111151521211424212223421133131141|" +
            "81111113211135135411122123113214115211331|" +
            "81111113511141221111215511332232213115311|" +
            "81111113111232341121251411424113333113121|" +
            "81111113611211322112361125123211411221241|" +
            "81111113611222122331133121131252235212111|" +
            "81111113111325221423221225111223321212331|" +
            "81111113211234133213221325133111511113231|" +
            "81111113211313511342111416211132221112531"
        },

        // Eight digits, all numeric: Numeric Compaction mode for data that is only digits.
        {
            "12345678",
            0,
            -1,
            "811111135111115221111326124141131|" +
            "811111135111132331132511331421121|" +
            "811111133111116313211531235212111|" +
            "811111132111425114141411222123231|" +
            "811111132111341422133411115121421|" +
            "811111134111411422136111161111421|" +
            "811111133112315113133132142121151"
        },

        // Thirteen digits between text: Numeric Compaction mode inside, Text Compaction mode around it.
        {
            "Code 1234567890123 then text",
            0,
            -1,
            "81111113511111522111233411132225122133231|" +
            "81111113411114142113341211611223411122241|" +
            "81111113111112461114114411613113134213121|" +
            "81111113211142511212323314232212151221231|" +
            "81111113211135134131331131132214114312411|" +
            "81111113511141226211221211432222235212111|" +
            "81111113311231511423221223332112231113241|" +
            "81111113611211324115111311221361431222211|" +
            "81111113611222122541112121411422125421111"
        },

        // A byte value in text: the mode shift 913 of section 5.4.3.2, and a tab and a line of punctuation.
        {
            "Hello, World! ¢ tab\there",
            0,
            -1,
            "81111113511111522111233441221232211513221|" +
            "81111113411114141122126216114211351123111|" +
            "81111113111112461241232231411232511431111|" +
            "81111113211142513124122242421112221123331|" +
            "81111113211135131143124161111133161141121|" +
            "81111113511141224212421113222241235212111|" +
            "81111113311231511423221212123134231412221|" +
            "81111113611211325131231163311111331331211|" +
            "81111113611222121232112521451121154211121"
        },

        // Six bytes: mode latch 924 and one base 256 to base 900 conversion, section 5.4.3.3.
        {
            "éèêëàâ",
            0,
            -1,
            "811111135111115221111425233321121|" +
            "811111136111133111226221134112411|" +
            "811111133111116312341321111121551|" +
            "811111132111425112324122142322121|" +
            "811111133111342221133214212252211|" +
            "811111134111411422521212121314411|" +
            "811111133112315111133332321312321|" +
            "811111135112112412125321211334121"
        },

        // Seven bytes: mode latch 901, one conversion and one byte on its own codeword.
        {
            "éèêëàâä",
            0,
            -1,
            "811111135111115221111425151413111|" +
            "811111136111133111226221134112411|" +
            "811111133111116312341321111121551|" +
            "811111132111425112324122121241421|" +
            "811111133111342231214222421215111|" +
            "811111134111411423111153116131131|" +
            "811111133112315122242221414112131|" +
            "811111135112112462221121512213121"
        },

        // Punctuation and lower case letters alternating: shifts to Punctuation, section 5.4.2.3.
        {
            "a;b<c>d@e[f\\g]h_i`j~k!l",
            0,
            -1,
            "81111113511111522111233415111512151321221|" +
            "81111113411114141111532341113232115213311|" +
            "81111113111112465114311111131541144112131|" +
            "81111113211142513121122532112143424111131|" +
            "81111113211135134211323142111413361121121|" +
            "81111113511141222311135111241431153212211|" +
            "81111113311231512311122522251311212415111|" +
            "81111113611211322116311242122321442121121|" +
            "81111113611222122352111215111143253111311"
        },

        // Mixed case: shifts to Alpha from Lower, three columns.
        {
            "MiXeD cAsE 42 MiXeD",
            3,
            -1,
            "81111113511111523111224352211141221322321|" +
            "81111113611113311321126125114121332331111|" +
            "81111113111112461251221321421421121124511|" +
            "81111113211142511233311311132225251112231|" +
            "81111113311134222213311414215112211335111|" +
            "81111113511141225114311111251331113612211|" +
            "81111113311231511321423112123233122421141|" +
            "81111113511211246131131141121512311523111"
        },

        // One column at level 0: two error correction codewords, Table 9.
        {
            "PDF417",
            1,
            0,
            "8111111351111152511112511|" +
            "8111111351111125212541111|" +
            "8111111321111155112115331|" +
            "8111111321114251111425211|" +
            "8111111321113216421132311|" +
            "8111111351113411232111431|" +
            "8111111331123151423113211"
        },

        // Five columns at level 4: 32 error correction codewords and pad codewords, section 5.9.2.
        {
            "PDF417",
            5,
            4,
            "811111135111115221111425313122235121114211142521312213231|" +
            "811111134111212511611124116111241161112425114121431311131|" +
            "811111133111126231116221144221211243141111152151123314211|" +
            "811111132111425122121423322131411114232321231413212316111|" +
            "811111134111414111511233123211526111214153221112222242121|" +
            "811111135111422111413133132121431121244213411511231121611|" +
            "811111133112315121122333221141513314113131141331111511161|" +
            "811111135112132254121121311321153412231125113311121342131"
        },

        // Four columns at level 3: 16 error correction codewords.
        {
            "The quick brown fox jumps over the lazy dog",
            4,
            3,
            "8111111331111235111132353411133111213225314321121|" +
            "8111111351111422131351215112122334142111231231141|" +
            "8111111321111254224122311341131321341231143211231|" +
            "8111111311115152241131411131311611425121232122231|" +
            "8111111331113521122151144131331121133412121245111|" +
            "8111111341114213143113221341121411331323235212111|" +
            "8111111311123234314322111114151324112331123132141|" +
            "8111111341121215311424113125311141112323122251131|" +
            "8111111311122253314112321332213212351221123512211|" +
            "8111111311132522121333311513141112242411242211141"
        },
    };

    /// <summary>
    /// Each row of the Compact PDF417 symbol matches the reference row.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="columns">The number of data columns, or 0 for the default.</param>
    /// <param name="errorCorrectionLevel">The error correction level, or -1 for the default.</param>
    /// <param name="expected">The run widths of each reference row, separated by "|".</param>
    [Theory]
    [MemberData(nameof(CompactPdf417_MatchesReferenceRowsData))]
    public void CompactPdf417_MatchesReferenceRows(string text, int columns, int errorCorrectionLevel, string expected)
        => AssertRows(expected, Create(columns, errorCorrectionLevel, true), text);

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
    /// Creates the symbology of a reference case.
    /// </summary>
    /// <param name="columns">The number of data columns, or 0 for the default.</param>
    /// <param name="errorCorrectionLevel">The error correction level, or -1 for the default.</param>
    /// <param name="compact">Whether the symbology is Compact PDF417.</param>
    /// <returns>The symbology.</returns>
    private static BarcodeSymbology Create(int columns, int errorCorrectionLevel, bool compact)
    {
        if (compact)
        {
            return columns == 0
                ? new CompactPdf417Symbology()
                : errorCorrectionLevel < 0 ? new CompactPdf417Symbology(columns) : new CompactPdf417Symbology(columns, errorCorrectionLevel);
        }

        return columns == 0
            ? new Pdf417Symbology()
            : errorCorrectionLevel < 0 ? new Pdf417Symbology(columns) : new Pdf417Symbology(columns, errorCorrectionLevel);
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

    /// <summary>
    /// Encodes the text with the default options and compares the run widths of each row with the reference rows.
    /// </summary>
    /// <param name="expected">The reference rows, each a string of run widths, separated by a vertical bar.</param>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void AssertRows(string expected, BarcodeSymbology symbology, string text)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions(), (ref symbol) =>
        {
            string[] rows = expected.Split('|');

            Assert.Equal(rows.Length, symbol.Rows.Length);

            for (int i = 0; i < rows.Length; i++)
            {
                Assert.Equal(rows[i], SymbolEncoder.RunString(ref symbol, symbol.Rows[i]));
            }
        });
}
