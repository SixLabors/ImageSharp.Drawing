// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcPdf417Symbology"/>. Each expected row is the alternating bar and space
/// module widths of one row, starting with a bar, taken from an independent reference implementation
/// through its raw encoding API. The symbol carries the flag character, the data and the modulo 43 check
/// character.
/// </summary>
public class HibcPdf417SymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="HibcPdf417_MatchesReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, string> HibcPdf417_MatchesReferenceRowsData { get; } = new()
    {
        {
            "A123BJC5D6E71",
            "8111111331111235411121521513121341111144711311121|" +
                "8111111361111331533121114513111151111224711311121|" +
                "8111111331111163111222531161331131111361711311121|" +
                "8111111311115152121324223331111411114243711311121|" +
                "8111111331113422433131113331411121113315711311121|" +
                "8111111341114114125231213162121131115114711311121|" +
                "8111111311123234111224243212214221123143711311121|" +
                "8111111351121124113311523511312121116141711311121|" +
                "8111111321122162134112142112631111122451711311121|" +
                "8111111311132522232222221511151221132332711311121|" +
                "8111111331123322112211632111442221123215711311121"
        },
        {
            "A99912345/$$52001510X3",
            "8111111331111235311122431513121341111144711311121|" +
                "8111111341111414533121112331511151111224711311121|" +
                "8111111331111163112413325111412211111444711311121|" +
                "8111111311115152111331342211324211114243711311121|" +
                "8111111321113513622211212113422221113315711311121|" +
                "8111111341114114211111554111431241115122711311121|" +
                "8111111311123234211141521422221321123143711311121|" +
                "8111111361121132433131111112613221116141711311121|" +
                "8111111321122162216123112411611151123113711311121|" +
                "8111111311132522111322251231331321132332711311121|" +
                "8111111321123413131143132413321121123215711311121|" +
                "8111111311131343112222431211225361132211711311121"
        },
        {
            "H123ABC01234567890",
            "8111111331111235411121521513121341111144711311121|" +
                "8111111361111331251331114513111151111224711311121|" +
                "8111111331111163111222531252231131111361711311121|" +
                "8111111311115152211142511421322211114243711311121|" +
                "8111111331113422411132323112322321113315711311121|" +
                "8111111341114114521141213212521131115114711311121|" +
                "8111111311123234221323311512313121123143711311121|" +
                "8111111351121124511222311223521121116141711311121|" +
                "8111111321122162421141131232112511122451711311121|" +
                "8111111311132522132331221133311421132332711311121|" +
                "8111111331123322126212212211513221123215711311121"
        },
        {
            "A1B2C3 D4-E5.F6%G7",
            "8111111341111243111124251513121341111144711311121|" +
                "8111111361111331533121114513111161111232711311121|" +
                "8111111331111163116133111434112131111361711311121|" +
                "8111111351116111142133211512313111114243711311121|" +
                "8111111331113422115121421112542131113323711311121|" +
                "8111111341114114221113521252312131115114711311121|" +
                "8111111321123242333111141314212321123143711311121|" +
                "8111111351121124211433122115412111116232711311121|" +
                "8111111321122162316211123162121111122451711311121|" +
                "8111111311133134142133211423221221132332711311121|" +
                "8111111331123322111144145111112531123223711311121|" +
                "8111111311131343321211612331123221132161711311121|" +
                "8111111311151116421311411215121411142521711311121|" +
                "8111111351131321311224131611411241131214711311121"
        },
    };

    [Theory]
    [MemberData(nameof(HibcPdf417_MatchesReferenceRowsData))]
    public void HibcPdf417_MatchesReferenceRows(string text, string expected)
        => AssertRows(expected, new HibcPdf417Symbology(), text);

    /// <summary>
    /// Gets the cases of <see cref="HibcPdf417_FourColumnsMatchReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, string> HibcPdf417_FourColumnsMatchReferenceRowsData { get; } = new()
    {
        {
            "A123BJC5D6E71",
            "81111113411111443111224315131213142132221512232131111235711311121|" +
                "81111113411114142111614145131111612113213422311161111133711311121|" +
                "81111113211112541343212131621112125231213162121111111444711311121|" +
                "81111113111142431112242432122142142322121423221211115152711311121|" +
                "81111113211135132121411523214311331331213116211231113224711311121|" +
                "81111113411142131213225121411125131311436211211341115122711311121"
        },
        {
            "A99912345/$$52001510X3",
            "81111113411111443111224315131213142132222422111431111235711311121|" +
                "81111113411114144121211541113232311232234114212261111133711311121|" +
                "81111113211112541142111652113212211111554111431211111444711311121|" +
                "81111113111142432111415214222213151231312114233111115152711311121|" +
                "81111113211135132421421143112321411224211411522131113224711311121|" +
                "81111113411142131412215111532311112222431211225341115122711311121"
        },
        {
            "H123ABC01234567890",
            "81111113411111443111224315131213333113121512232131111235711311121|" +
                "81111113411114142111614153312111411132325331211161111133711311121|" +
                "81111113211112545111412211131541521141213212521111111444711311121|" +
                "81111113111142432213233115123131142322121423221211115152711311121|" +
                "81111113211135134411222141122124112312613121311531113224711311121|" +
                "81111113411142133242113113411214113511231215124141115122711311121"
        },
        {
            "A1B2C3 D4-E5.F6%G7",
            "81111113511111521111242515131213142132221512232131111235711311121|" +
                "81111113511113234513111144222111442221114331311141111216711311121|" +
                "81111113211112541531112311142251221113521252312121111353711311121|" +
                "81111113211142513331111413142123512111421212141511115152711311121|" +
                "81111113211134143331411125133111442221111161112441113232711311121|" +
                "81111113411142133112116221111155321211612331123241114411711311121|" +
                "81111113311231514213114112151214311321423241221211123234711311121"
        },
    };

    [Theory]
    [MemberData(nameof(HibcPdf417_FourColumnsMatchReferenceRowsData))]
    public void HibcPdf417_FourColumnsMatchReferenceRows(string text, string expected)
        => AssertRows(expected, new HibcPdf417Symbology(4, false), text);

    /// <summary>
    /// The check character of A123BJC5D6E71 is G: the flag character and the data sum to 145, and 145 mod 43
    /// is 16, the value of G. With the check character given and validated, the symbol is the one the
    /// encoder builds when it appends that character itself.
    /// </summary>
    [Fact]
    public void HibcPdf417_ValidatesAGivenCheckCharacter()
        => Assert.Equal(Encode(new HibcPdf417Symbology(), "A123BJC5D6E71"), Encode(new HibcPdf417Symbology(2, true), "A123BJC5D6E71G"));

    [Fact]
    public void HibcPdf417_RejectsAWrongCheckCharacter()
        => Assert.Throws<ArgumentException>(() => Encode(new HibcPdf417Symbology(2, true), "A123BJC5D6E71H"));

    [Fact]
    public void HibcPdf417_RejectsCharactersOutsideTheHibcSet()
        => Assert.Throws<ArgumentException>(() => Encode(new HibcPdf417Symbology(), "a123"));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of each row, as <see cref="SymbolEncoder.RowRunLists"/> writes them.</returns>
    private static string Encode(HibcPdf417Symbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.RowRunLists);

    /// <summary>
    /// Encodes the text with the default options and compares the run widths of each row with the
    /// reference rows.
    /// </summary>
    /// <param name="expected">The reference rows, each a string of run widths, separated by a vertical bar.</param>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void AssertRows(string expected, HibcPdf417Symbology symbology, string text)
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
