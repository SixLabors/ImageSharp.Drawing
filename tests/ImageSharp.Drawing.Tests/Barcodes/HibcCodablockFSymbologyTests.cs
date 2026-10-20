// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcCodablockFSymbology"/>. Each expected row is the alternating bar and space module widths of one row, starting with a bar,
/// taken from an independent reference implementation through its raw encoding API. The rows carry the flag character, the data and the modulo 43 check
/// character the symbology adds.
/// </summary>
public class HibcCodablockFSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="HibcCodablockF_MatchesReferenceRows"/>.
    /// </summary>
    public static TheoryData<string, string> HibcCodablockF_MatchesReferenceRowsData { get; } = new()
    {
        // A primary data structure.
        {
            "A123BJC5D6E71",
            "2114121141311211242312121113231232212232112211321311231121331313211421122331112|" +
            "2114121141312312122132121123132231121321133121311232212113131131411113232331112|" +
            "2114121141311122321131411141311131411141311131411141311222314112121122142331112"
        },

        // A primary and secondary data structure with the separator and the date and lot fields.
        {
            "A99912345/$$52001510X3",
            "2114121141311211242312121113231131411131414121213121311131231141312222212331112|" +
            "2114121141312312121132221213221213221131412133112122221132222213123121132331112|" +
            "2114121141311122323311212211322211321131411141311131411321311223123221122331112"
        },

        // A secondary data structure on its own.
        {
            "$A1234G",
            "2114121141311114222312121213221113231131411122321311231141312113131213222331112|" +
            "2114121141312312122131311131411141311131411141311131411223122132121311232331112"
        },
    };

    [Theory]
    [MemberData(nameof(HibcCodablockF_MatchesReferenceRowsData))]
    public void HibcCodablockF_MatchesReferenceRows(string text, string expected)
        => SymbolEncoder.Encode(new HibcCodablockFSymbology(), text, new BarcodeOptions(), (ref symbol) =>
        {
            string[] rows = expected.Split('|');
            Assert.Equal((rows.Length * 2) + 1, symbol.Rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.Equal(rows[i], SymbolEncoder.RunString(ref symbol, symbol.Rows[(i * 2) + 1]));
            }
        });

    /// <summary>
    /// The symbol is the Codablock F symbol of the flag character, the data and the check character, so an input that already carries its check character
    /// encodes the same way.
    /// </summary>
    [Fact]
    public void HibcCodablockF_ValidatesAGivenCheckCharacter()
    {
        string calculated = SymbolEncoder.Read(new HibcCodablockFSymbology(), "A123BJC5D6E71", new BarcodeOptions(), SymbolEncoder.RowRunLists);
        string given = SymbolEncoder.Read(new HibcCodablockFSymbology(8, true), "A123BJC5D6E71G", new BarcodeOptions(), SymbolEncoder.RowRunLists);
        string plain = SymbolEncoder.Read(new CodablockFSymbology(), "+A123BJC5D6E71G", new BarcodeOptions(), SymbolEncoder.RowRunLists);
        Assert.Equal(calculated, given);
        Assert.Equal(calculated, plain);

        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new HibcCodablockFSymbology(8, true), "A123BJC5D6E7Z", new BarcodeOptions()));
    }

    /// <summary>
    /// The column count is 4 to 62.
    /// </summary>
    [Fact]
    public void HibcCodablockF_RejectsInvalidColumns()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HibcCodablockFSymbology(3, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HibcCodablockFSymbology(63, false));
    }
}
