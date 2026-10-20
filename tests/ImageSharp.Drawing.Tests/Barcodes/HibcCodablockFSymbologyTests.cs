// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcCodablockFSymbology"/>. The symbol carries the flag character, the data and the modulo 43 check character the symbology
/// adds, and the tests check the check character and the column range.
/// </summary>
public class HibcCodablockFSymbologyTests
{
    /// <summary>
    /// The symbol is the Codablock F symbol of the flag character, the data and the check character. An input that already carries its check character
    /// encodes the same way. A wrong check character throws.
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
    /// The constructor rejects a column count outside 4 to 62.
    /// </summary>
    [Fact]
    public void HibcCodablockF_RejectsInvalidColumns()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HibcCodablockFSymbology(3, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HibcCodablockFSymbology(63, false));
    }
}
