// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Ultracode symbology: a colour matrix of two to five rows of tiles between black and white timing patterns, with a quiet zone of one module on every
/// side. Each tile is five modules in cyan, magenta, yellow and green. It carries the characters U+0000 to U+00FF with Reed-Solomon error correction. The
/// black modules take <see cref="BarcodeOptions.BarBrush"/> and the white modules show <see cref="BarcodeOptions.Background"/>; the tile colours are fixed.
/// </summary>
public sealed class UltracodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UltracodeSymbology"/> class at error correction level EC2.
    /// </summary>
    public UltracodeSymbology()
        : this(UltracodeErrorCorrectionLevel.EC2)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UltracodeSymbology"/> class.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    public UltracodeSymbology(UltracodeErrorCorrectionLevel errorCorrectionLevel)
    {
        int level = (int)errorCorrectionLevel;
        Guard.MustBeBetweenOrEqualTo(level, (int)UltracodeErrorCorrectionLevel.EC1, (int)UltracodeErrorCorrectionLevel.EC5, nameof(errorCorrectionLevel));
        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Gets the error correction level.
    /// </summary>
    public UltracodeErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        double xDimension = options.ResolveXDimension(0, DefaultMatrixXDimension, double.PositiveInfinity);
        UltracodeEncoder.BuildSymbol(text, this.ErrorCorrectionLevel, xDimension, ref symbol);
    }
}
