// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Han Xin Code symbology: a square matrix of 23 x 23 to 189 x 189 modules, versions 1 to 84, with finder patterns at the four corners and a quiet zone of
/// three modules on every side. It carries the characters U+0000 to U+00FF in Numeric and Byte mode segments with Reed-Solomon error correction at one of four
/// levels.
/// </summary>
public sealed class HanXinSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HanXinSymbology"/> class at error correction level L2. The symbol takes the smallest version that holds the
    /// data.
    /// </summary>
    public HanXinSymbology()
        : this(HanXinErrorCorrectionLevel.L2)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HanXinSymbology"/> class. The symbol takes the smallest version that holds the data.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    public HanXinSymbology(HanXinErrorCorrectionLevel errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo(
            (int)errorCorrectionLevel,
            (int)HanXinErrorCorrectionLevel.L1,
            (int)HanXinErrorCorrectionLevel.L4,
            nameof(errorCorrectionLevel));

        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HanXinSymbology"/> class with a fixed version.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="version">The version, 1 to 84.</param>
    public HanXinSymbology(HanXinErrorCorrectionLevel errorCorrectionLevel, int version)
        : this(errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo(version, 1, 84, nameof(version));
        this.Version = version;
    }

    /// <summary>
    /// Gets the error correction level.
    /// </summary>
    public HanXinErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>
    /// Gets the version, or <see langword="null"/> when the version follows from the data.
    /// </summary>
    public int? Version { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        HanXinEncoder.BuildSymbol(text, this.ErrorCorrectionLevel, this.Version ?? 0, options.XDimension ?? PointXDimension, ref symbol);
    }
}
