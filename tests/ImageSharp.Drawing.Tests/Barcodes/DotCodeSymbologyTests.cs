// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="DotCodeSymbology"/>: the sizes, the data and the characters that the symbology rejects.
/// </summary>
public class DotCodeSymbologyTests
{
    /// <summary>
    /// A fixed size holds a fixed number of codewords, so data that needs more does not fit it.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondTheSize()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new DotCodeSymbology(5, 6), "This text needs more than 5 by 6 holds", new BarcodeOptions()));

    /// <summary>
    /// The columns and the rows of a DotCode symbol sum to an odd number.
    /// </summary>
    [Fact]
    public void RejectsSidesWithEvenSum()
        => Assert.Throws<ArgumentException>(() => new DotCodeSymbology(10, 10));

    /// <summary>
    /// DotCode carries the 256 byte values, U+0000 to U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new DotCodeSymbology(), "Ā", new BarcodeOptions()));
}
