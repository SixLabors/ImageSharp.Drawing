// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The QR Code symbology, ISO/IEC 18004: a square matrix of 21 x 21 to 177 x 177 modules, versions 1 to 40,
/// with finder patterns at three corners. It carries the characters U+0000 to U+00FF in Numeric,
/// Alphanumeric and Byte mode segments with Reed-Solomon error correction at one of four levels.
/// </summary>
public sealed class QrCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QrCodeSymbology"/> class at error correction level M. The
    /// symbol takes the smallest version that holds the data.
    /// </summary>
    public QrCodeSymbology()
        : this(QrErrorCorrectionLevel.M)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QrCodeSymbology"/> class. The symbol takes the smallest
    /// version that holds the data.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    public QrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo((int)errorCorrectionLevel, (int)QrErrorCorrectionLevel.L, (int)QrErrorCorrectionLevel.H, nameof(errorCorrectionLevel));
        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QrCodeSymbology"/> class with a fixed version.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="version">The version, 1 to 40.</param>
    public QrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel, int version)
        : this(errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo(version, 1, 40, nameof(version));
        this.Version = version;
    }

    /// <summary>
    /// Gets the error correction level.
    /// </summary>
    public QrErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>
    /// Gets the version, or <see langword="null"/> when the version follows from the data.
    /// </summary>
    public int? Version { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        QrEncoder.BuildSymbol(text, false, QrFormat.Full, this.ErrorCorrectionLevel, this.Version ?? 0, options.XDimension ?? PointXDimension, ref symbol);
    }
}
