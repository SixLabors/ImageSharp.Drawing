// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="RectangularMicroQrCodeSymbology"/>: the version it takes for the data, the quiet zone, and the levels and input that the
/// symbology rejects.
/// </summary>
public class RectangularMicroQrCodeSymbologyTests
{
    /// <summary>
    /// The symbol has a quiet zone of two modules.
    /// </summary>
    [Fact]
    public void QuietZoneIsTwoModules()
        => Assert.Equal(
            2F,
            SymbolEncoder.Read(new RectangularMicroQrCodeSymbology(), "1234", new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));

    /// <summary>
    /// Rectangular Micro QR Code has the error correction levels M and H only. The symbology rejects the levels L and Q, with and without a fixed version.
    /// </summary>
    [Fact]
    public void RejectsLevelsLAndQ()
    {
        Assert.Throws<ArgumentException>(() => new RectangularMicroQrCodeSymbology(QrErrorCorrectionLevel.L));
        Assert.Throws<ArgumentException>(() => new RectangularMicroQrCodeSymbology(QrErrorCorrectionLevel.Q, RectangularMicroQrCodeVersion.R7x43));
    }

    /// <summary>
    /// The symbol takes the version of the smallest area that holds the data at the level. The expected versions follow from the numeric and byte capacities
    /// of Table 6 of ISO/IEC 23941, with the versions in order of area. For example, 15 digits at level M exceed R11x27 (area 297, 14 digits) and R7x43
    /// (area 301, 12 digits), so they take R13x27 (area 351, 26 digits). 48 digits exceed R9x59 (area 531, 47 digits) and R7x77 (area 539, 45 digits), so
    /// they take R13x43 (area 559, 62 digits).
    /// </summary>
    /// <param name="level">The error correction level.</param>
    /// <param name="text">The data.</param>
    /// <param name="rows">The module rows of the version.</param>
    /// <param name="columns">The module columns of the version.</param>
    [Theory]
    [InlineData(QrErrorCorrectionLevel.M, "012345678901", 11, 27)]
    [InlineData(QrErrorCorrectionLevel.M, "012345678901234", 13, 27)]
    [InlineData(QrErrorCorrectionLevel.M, "012345678901234567890123456", 11, 43)]
    [InlineData(QrErrorCorrectionLevel.M, "0123456789012345678901234567890123456789012345", 9, 59)]
    [InlineData(QrErrorCorrectionLevel.M, "012345678901234567890123456789012345678901234567", 13, 43)]
    [InlineData(QrErrorCorrectionLevel.H, "0123456789", 13, 27)]
    [InlineData(QrErrorCorrectionLevel.M, "abcdef", 11, 27)]
    [InlineData(QrErrorCorrectionLevel.M, "abcdefg", 13, 27)]
    public void TakesTheVersionOfTheSmallestAreaThatHoldsTheData(QrErrorCorrectionLevel level, string text, int rows, int columns)
    {
        (int matrixRows, int matrixColumns) = SymbolEncoder.Read(
            new RectangularMicroQrCodeSymbology(level),
            text,
            new BarcodeOptions(),
            static (ref symbol) => (symbol.MatrixRows, symbol.Columns));

        Assert.Equal((rows, columns), (matrixRows, matrixColumns));
    }

    /// <summary>
    /// R17x139 at level M, the largest version, holds 361 digits and no more. Table 6 of ISO/IEC 23941 gives that capacity, and 361 digits take that version.
    /// </summary>
    [Fact]
    public void HoldsTheCapacityOfTheLargestVersion()
    {
        (int rows, int columns) = SymbolEncoder.Read(
            new RectangularMicroQrCodeSymbology(),
            Digits(361),
            new BarcodeOptions(),
            static (ref symbol) => (symbol.MatrixRows, symbol.Columns));

        Assert.Equal((17, 139), (rows, columns));
    }

    /// <summary>
    /// R17x139 at level M, the largest version, holds 361 digits and no more. The symbology rejects 362 digits.
    /// </summary>
    [Fact]
    public void RejectsDataLargerThanTheLargestVersion() => Assert.Throws<ArgumentException>(() => Encode(new RectangularMicroQrCodeSymbology(), Digits(362)));

    /// <summary>
    /// A symbology fixed at version R7x43 and level H rejects 6 digits, which that version does not hold.
    /// </summary>
    [Fact]
    public void RejectsDataLargerThanTheFixedVersion()
    {
        RectangularMicroQrCodeSymbology symbology = new(QrErrorCorrectionLevel.H, RectangularMicroQrCodeVersion.R7x43);

        Assert.Throws<ArgumentException>(() => Encode(symbology, Digits(6)));
    }

    /// <summary>
    /// Byte mode carries the 256 byte values. The symbology rejects a character above U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveTheByteRange() => Assert.Throws<ArgumentException>(() => Encode(new RectangularMicroQrCodeSymbology(), "Ā"));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text) => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

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
}
