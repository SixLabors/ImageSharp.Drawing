// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HanXinSymbology"/>: the capacity of each version, the version it takes for the data, and the characters that the symbology
/// rejects. The capacities come from Table C.1 of ISO/IEC 20830.
/// </summary>
public class HanXinSymbologyTests
{
    /// <summary>
    /// A fixed version holds the Numeric mode capacity of Table C.1 of ISO/IEC 20830 at the level, and the symbology rejects one more digit.
    /// </summary>
    /// <param name="level">The error correction level.</param>
    /// <param name="version">The version.</param>
    /// <param name="capacity">The number of digits that the version holds.</param>
    [Theory]
    [InlineData(HanXinErrorCorrectionLevel.L1, 1, 45)]
    [InlineData(HanXinErrorCorrectionLevel.L2, 1, 36)]
    [InlineData(HanXinErrorCorrectionLevel.L3, 1, 27)]
    [InlineData(HanXinErrorCorrectionLevel.L4, 1, 15)]
    [InlineData(HanXinErrorCorrectionLevel.L4, 3, 42)]
    [InlineData(HanXinErrorCorrectionLevel.L1, 84, 7827)]
    [InlineData(HanXinErrorCorrectionLevel.L4, 84, 3723)]
    public void HoldsTheNumericCapacityOfTableC1(HanXinErrorCorrectionLevel level, int version, int capacity)
    {
        HanXinSymbology symbology = new(level, version);

        Encode(symbology, Digits(capacity));
        Assert.ThrowsAny<ArgumentException>(() => Encode(symbology, Digits(capacity + 1)));
    }

    /// <summary>
    /// A fixed version holds the Binary byte mode capacity of Table C.1 of ISO/IEC 20830 at the level, and the symbology rejects one more byte.
    /// </summary>
    /// <param name="level">The error correction level.</param>
    /// <param name="version">The version.</param>
    /// <param name="capacity">The number of bytes that the version holds.</param>
    [Theory]
    [InlineData(HanXinErrorCorrectionLevel.L1, 1, 18)]
    [InlineData(HanXinErrorCorrectionLevel.L2, 1, 14)]
    [InlineData(HanXinErrorCorrectionLevel.L3, 1, 10)]
    [InlineData(HanXinErrorCorrectionLevel.L4, 1, 6)]
    [InlineData(HanXinErrorCorrectionLevel.L4, 3, 17)]
    [InlineData(HanXinErrorCorrectionLevel.L1, 84, 3261)]
    [InlineData(HanXinErrorCorrectionLevel.L4, 84, 1551)]
    public void HoldsTheBinaryByteCapacityOfTableC1(HanXinErrorCorrectionLevel level, int version, int capacity)
    {
        HanXinSymbology symbology = new(level, version);

        Encode(symbology, Letters(capacity));
        Assert.ThrowsAny<ArgumentException>(() => Encode(symbology, Letters(capacity + 1)));
    }

    /// <summary>
    /// Without a fixed version, the symbol takes the smallest version that holds the data. Table C.1 of ISO/IEC 20830 gives version 1 at level L1 a capacity of
    /// 45 digits or 18 bytes, and version 2 a capacity of 69 digits or 28 bytes. So 45 digits and 18 bytes take version 1, and 46 digits and 19 bytes take
    /// version 2.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="version">The version that holds the data.</param>
    [Theory]
    [InlineData("012345678901234567890123456789012345678901234", 1)]
    [InlineData("0123456789012345678901234567890123456789012345", 2)]
    [InlineData("ABCDEFGHIJKLMNOPQR", 1)]
    [InlineData("ABCDEFGHIJKLMNOPQRS", 2)]
    public void TakesTheSmallestVersionThatHoldsTheData(string text, int version)
        => Assert.Equal(
            Grid(new HanXinSymbology(HanXinErrorCorrectionLevel.L1, version), text),
            Grid(new HanXinSymbology(HanXinErrorCorrectionLevel.L1), text));

    /// <summary>
    /// Han Xin Code carries the 256 byte values, U+0000 to U+00FF. A character above U+00FF throws.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => Encode(new HanXinSymbology(), "Ā"));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(HanXinSymbology symbology, string text) => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The module grid of the symbol, as <see cref="SymbolEncoder.ModuleGrid"/> writes it.</returns>
    private static string Grid(HanXinSymbology symbology, string text) => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.ModuleGrid);

    /// <summary>
    /// Returns the digits 0 to 9 repeated to a length.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <returns>The digits.</returns>
    private static string Digits(int length) => string.Create(length, 0, static (span, _) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = (char)('0' + (i % 10));
        }
    });

    /// <summary>
    /// Returns the letters A to Z repeated to a length. The letters have no digits, so the encoder writes them as one Byte mode segment.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <returns>The letters.</returns>
    private static string Letters(int length) => string.Create(length, 0, static (span, _) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = (char)('A' + (i % 26));
        }
    });
}
