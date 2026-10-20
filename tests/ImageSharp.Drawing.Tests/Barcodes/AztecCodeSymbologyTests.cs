// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the Aztec Code symbologies against ISO/IEC 24778: the values and the data that the symbologies reject, the quiet zone and the
/// capacity of a compact symbol.
/// </summary>
public class AztecCodeSymbologyTests
{
    /// <summary>
    /// A rune carries a value of 0 to 255 written in decimal digits.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("256")]
    [InlineData("1000")]
    [InlineData("-1")]
    [InlineData("A")]
    public void RuneRejectsValuesOutsideTheRange(string text)
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new AztecRuneSymbology(), text, new BarcodeOptions()));

    /// <summary>
    /// Section 4.1 a) of ISO/IEC 24778: the default interpretation covers the 256 byte values, U+0000 to U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new AztecCodeSymbology(), "Ā", new BarcodeOptions()));

    /// <summary>
    /// Section 4.1 c) of ISO/IEC 24778: "No quiet zones are required outside the bounds of the symbol."
    /// </summary>
    [Fact]
    public void HasNoQuietZone()
        => SymbolEncoder.Encode(new AztecCodeSymbology(), "This is Aztec Code", new BarcodeOptions(), static (ref symbol) =>
            Assert.Equal(0F, symbol.LeadingQuietZone));

    /// <summary>
    /// Table 1 of ISO/IEC 24778: a compact symbol holds at most 76 codewords, so data that needs more fills no compact symbol.
    /// </summary>
    [Fact]
    public void CompactRejectsDataBeyondFourLayers()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new AztecCodeCompactSymbology(), new string('a', 200), new BarcodeOptions()));

    /// <summary>
    /// Section 7.2.3 of ISO/IEC 24778: the message length of a compact symbol has 6 bits, "from one up to 64 codewords". Digits take a 5-bit D/L and 4 bits
    /// each, so 126 digits fill 509 bits, 64 codewords of 8 bits, and 127 digits fill 513 bits, 65 codewords. At 5 percent the 4-layer compact symbol of 76
    /// codewords has room for the 65 codewords and their 7 check codewords, but the length cannot say 65.
    /// </summary>
    [Fact]
    public void CompactHoldsAtMost64DataCodewords()
    {
        AztecCodeCompactSymbology symbology = new(5);

        SymbolEncoder.Encode(symbology, new string('0', 126), new BarcodeOptions(), static (ref symbol) =>
            Assert.Equal(27, symbol.Columns));

        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(symbology, new string('0', 127), new BarcodeOptions()));
    }
}
