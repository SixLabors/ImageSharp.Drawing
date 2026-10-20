// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Compact Aztec Code, ISO/IEC 24778: an Aztec Code symbol in the compact format, 15 x 15 to 27 x 27 modules with a 2-ring bullseye and 1 to 4 data layers. By
/// default the symbol is the smallest compact symbol that holds the data with 23 percent of its capacity plus 3 codewords for error correction.
/// </summary>
public sealed class AztecCodeCompactSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AztecCodeCompactSymbology"/> class at the recommended error correction level of 23 percent.
    /// </summary>
    public AztecCodeCompactSymbology()
        : this(AztecEncoder.DefaultErrorCorrectionPercentage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AztecCodeCompactSymbology"/> class.
    /// </summary>
    /// <param name="errorCorrectionPercentage"> The minimum percentage of the symbol capacity that error correction takes, 5 to 95. Three more check codewords
    /// are always added. </param>
    public AztecCodeCompactSymbology(int errorCorrectionPercentage)
    {
        Guard.MustBeBetweenOrEqualTo(errorCorrectionPercentage, 5, 95, nameof(errorCorrectionPercentage));
        this.ErrorCorrectionPercentage = errorCorrectionPercentage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AztecCodeCompactSymbology"/> class with a fixed number of data layers.
    /// </summary>
    /// <param name="errorCorrectionPercentage"> The minimum percentage of the symbol capacity that error correction takes, 5 to 95. Three more check codewords
    /// are always added. </param>
    /// <param name="layers">The number of data layers, 1 to 4.</param>
    public AztecCodeCompactSymbology(int errorCorrectionPercentage, int layers)
        : this(errorCorrectionPercentage)
    {
        Guard.MustBeBetweenOrEqualTo(layers, 1, 4, nameof(layers));
        this.Layers = layers;
    }

    /// <summary>
    /// Gets the minimum error correction percentage.
    /// </summary>
    public int ErrorCorrectionPercentage { get; }

    /// <summary>
    /// Gets the number of data layers, or <see langword="null"/> when the symbol is the smallest compact symbol that holds the data.
    /// </summary>
    public int? Layers { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        AztecEncoder.BuildSymbol(
            text,
            AztecFormat.Compact,
            this.ErrorCorrectionPercentage,
            this.Layers ?? 0,
            options.ResolveXDimension(0, DefaultMatrixXDimension, double.PositiveInfinity),
            ref symbol);
    }
}
