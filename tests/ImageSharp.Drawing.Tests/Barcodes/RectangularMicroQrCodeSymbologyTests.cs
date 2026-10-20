// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="RectangularMicroQrCodeSymbology"/>: the quiet zone and the levels and input that the symbology rejects.
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
