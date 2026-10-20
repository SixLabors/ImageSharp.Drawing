// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcPdf417Symbology"/>. The symbol carries the flag character, the data and the modulo 43 check character, and the tests check
/// the check character and the HIBC character set.
/// </summary>
public class HibcPdf417SymbologyTests
{
    /// <summary>
    /// The check character of A123BJC5D6E71 is G: the flag character and the data sum to 145, and 145 mod 43 is 16, the value of G. With the check character
    /// given and validated, the symbol is the one the encoder builds when it appends that character itself.
    /// </summary>
    [Fact]
    public void HibcPdf417_ValidatesAGivenCheckCharacter()
        => Assert.Equal(Encode(new HibcPdf417Symbology(), "A123BJC5D6E71"), Encode(new HibcPdf417Symbology(2, true), "A123BJC5D6E71G"));

    /// <summary>
    /// A supplied check character that is not the modulo 43 check character of the data throws.
    /// </summary>
    [Fact]
    public void HibcPdf417_RejectsAWrongCheckCharacter()
        => Assert.Throws<ArgumentException>(() => Encode(new HibcPdf417Symbology(2, true), "A123BJC5D6E71H"));

    /// <summary>
    /// HIBC uses the Code 39 character set. A lowercase letter is outside that set and throws.
    /// </summary>
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
}
