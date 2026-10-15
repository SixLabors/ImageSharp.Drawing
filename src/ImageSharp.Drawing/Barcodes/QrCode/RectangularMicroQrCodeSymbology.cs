// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Rectangular Micro QR Code symbology (rMQR), ISO/IEC 23941: a rectangular matrix of 7 to 17 module
/// rows and 27 to 139 module columns, versions R7x43 to R17x139, with a finder pattern at the left end and a
/// finder sub-pattern at the right end. It carries the characters U+0000 to U+00FF in Numeric, Alphanumeric
/// and Byte mode segments with Reed-Solomon error correction at level M or H.
/// </summary>
public sealed class RectangularMicroQrCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RectangularMicroQrCodeSymbology"/> class at error
    /// correction level M. The symbol takes the version of the smallest area that holds the data.
    /// </summary>
    public RectangularMicroQrCodeSymbology()
        : this(QrErrorCorrectionLevel.M)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangularMicroQrCodeSymbology"/> class. The symbol takes
    /// the version of the smallest area that holds the data at the level.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level, M or H.</param>
    /// <exception cref="ArgumentException">The error correction level is L or Q.</exception>
    public RectangularMicroQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo((int)errorCorrectionLevel, (int)QrErrorCorrectionLevel.L, (int)QrErrorCorrectionLevel.H, nameof(errorCorrectionLevel));
        if (errorCorrectionLevel is not (QrErrorCorrectionLevel.M or QrErrorCorrectionLevel.H))
        {
            throw new ArgumentException(
                $"Rectangular Micro QR Code has error correction levels M and H only; got {errorCorrectionLevel}.",
                nameof(errorCorrectionLevel));
        }

        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangularMicroQrCodeSymbology"/> class with a fixed
    /// version.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level, M or H.</param>
    /// <param name="version">The version.</param>
    /// <exception cref="ArgumentException">The error correction level is L or Q.</exception>
    public RectangularMicroQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel, RectangularMicroQrCodeVersion version)
        : this(errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo((int)version, (int)RectangularMicroQrCodeVersion.R7x43, (int)RectangularMicroQrCodeVersion.R17x139, nameof(version));
        this.Version = version;
    }

    /// <summary>
    /// Gets the error correction level.
    /// </summary>
    public QrErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>
    /// Gets the version, or <see langword="null"/> when the version follows from the data.
    /// </summary>
    public RectangularMicroQrCodeVersion? Version { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        QrEncoder.BuildSymbol(
            text,
            false,
            QrFormat.Rectangular,
            this.ErrorCorrectionLevel,
            (int?)this.Version ?? 0,
            options.XDimension ?? PointXDimension,
            ref symbol);
    }
}
