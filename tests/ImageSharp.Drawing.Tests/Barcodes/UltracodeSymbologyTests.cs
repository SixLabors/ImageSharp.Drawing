// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="UltracodeSymbology"/>: the data and the characters that the symbology rejects.
/// </summary>
public class UltracodeSymbologyTests
{
    /// <summary>
    /// The largest symbol holds 282 tiles. At level EC5, 203 characters make 206 message codewords and 77 check codewords, 283 tiles. The symbology rejects
    /// them.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondTheLargestSymbol()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new UltracodeSymbology(UltracodeErrorCorrectionLevel.EC5), new string('A', 203), new BarcodeOptions()));

    /// <summary>
    /// Ultracode carries the 256 byte values, U+0000 to U+00FF. The symbology rejects a character above U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new UltracodeSymbology(), "Ā", new BarcodeOptions()));
}
