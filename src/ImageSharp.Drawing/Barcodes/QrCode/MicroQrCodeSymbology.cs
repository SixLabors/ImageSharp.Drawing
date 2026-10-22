// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Micro QR Code symbology, ISO/IEC 18004: a square matrix of 11 x 11 to 17 x 17 modules, versions M1 to M4, with one finder pattern. Version M1 carries
/// digits only, M2 digits and the Alphanumeric characters, and M3 and M4 also the characters U+0000 to U+00FF in Byte mode. Error correction level H does not
/// exist, level Q exists in M4 only, and M1 detects errors without correcting them.
/// </summary>
public sealed class MicroQrCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MicroQrCodeSymbology"/> class at error correction level L. The symbol takes the smallest version that holds
    /// the data.
    /// </summary>
    public MicroQrCodeSymbology()
        : this(QrErrorCorrectionLevel.L)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MicroQrCodeSymbology"/> class. The symbol takes the smallest version that holds the data at the level.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level, L, M or Q.</param>
    public MicroQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo((int)errorCorrectionLevel, (int)QrErrorCorrectionLevel.L, (int)QrErrorCorrectionLevel.Q, nameof(errorCorrectionLevel));
        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MicroQrCodeSymbology"/> class with a fixed version.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level, L, M or Q.</param>
    /// <param name="version">The version, 1 to 4 for M1 to M4.</param>
    /// <exception cref="ArgumentException">The version does not have the error correction level.</exception>
    public MicroQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel, int version)
        : this(errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo(version, 1, 4, nameof(version));

        // Table 13: M1 has error detection only, which counts as level L, and only M4 has level Q.
        if ((version == 1 && errorCorrectionLevel != QrErrorCorrectionLevel.L) || (errorCorrectionLevel == QrErrorCorrectionLevel.Q && version != 4))
        {
            throw new ArgumentException($"Micro QR Code version M{version} does not have error correction level {errorCorrectionLevel}.", nameof(version));
        }

        this.Version = version;
    }

    /// <summary>
    /// Gets the error correction level.
    /// </summary>
    public QrErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>
    /// Gets the version, 1 to 4 for M1 to M4, or <see langword="null"/> when the version follows from the data.
    /// </summary>
    public int? Version { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // Section 8.1 of ISO/IEC 18004: "the width of a module shall be specified by the application", so the symbology gives no limit.
        QrEncoder.BuildSymbol(
            text,
            false,
            QrFormat.Micro,
            this.ErrorCorrectionLevel,
            this.Version ?? 0,
            options.ResolveXDimension(0, DefaultMatrixXDimension, double.PositiveInfinity),
            ref symbol);
    }
}
