// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HanXinSymbology"/>: the data and the characters that the symbology rejects.
/// </summary>
public class HanXinSymbologyTests
{
    /// <summary>
    /// A fixed version holds a fixed number of data codewords. Data that needs more codewords throws.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondTheVersion()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(
                new HanXinSymbology(HanXinErrorCorrectionLevel.L4, 1),
                "This text needs more than version 1 holds",
                new BarcodeOptions()));

    /// <summary>
    /// Han Xin Code carries the 256 byte values, U+0000 to U+00FF. A character above U+00FF throws.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new HanXinSymbology(), "Ā", new BarcodeOptions()));
}
