// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="CodeOneSymbology"/>: the capacity of the versions and the data and the characters that the symbology rejects.
/// </summary>
public class CodeOneSymbologyTests
{
    /// <summary>
    /// The S versions carry digits only, and each holds a fixed number of them: 6, 12 and 18.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(CodeOneVersion.S10, "12345A")]
    [InlineData(CodeOneVersion.S10, "1234567")]
    [InlineData(CodeOneVersion.S10, "1000000")]
    [InlineData(CodeOneVersion.S20, "1000000000000")]
    [InlineData(CodeOneVersion.S20, "1234567890123")]
    [InlineData(CodeOneVersion.S30, "1234567890123456789")]
    public void NumericVersionRejectsTextItCannotHold(CodeOneVersion version, string text)
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodeOneSymbology(version), text, new BarcodeOptions()));

    /// <summary>
    /// Data that fills the 1480 data codewords of version H fits it. Two ASCII codewords and a Decimal run of 3546 digits, 4 + 1182 x 10 bits, fill the version
    /// exactly. Zint encodes the same text as version H.
    /// </summary>
    [Fact]
    public void FillsVersionHExactly()
    {
        string text = "AB" + new string('1', 3546);

        SymbolEncoder.Encode(new CodeOneSymbology(), text, new BarcodeOptions(), (ref symbol) =>
            Assert.Equal((148, 134), (symbol.MatrixRows, symbol.Columns)));
    }

    /// <summary>
    /// A fixed version holds a fixed number of data codewords, so data that needs more does not fit it.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondTheVersion()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new CodeOneSymbology(CodeOneVersion.A), "This text needs more than ten codewords", new BarcodeOptions()));

    /// <summary>
    /// Versions A to H carry the 256 byte values, U+0000 to U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodeOneSymbology(), "Ā", new BarcodeOptions()));
}
