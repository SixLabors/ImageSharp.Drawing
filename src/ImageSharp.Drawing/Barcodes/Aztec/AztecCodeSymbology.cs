// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Aztec Code symbology, ISO/IEC 24778: a square matrix of 15 x 15 to 151 x 151 modules around a bullseye finder, with no quiet zone. It carries the
/// characters U+0000 to U+00FF with Reed-Solomon error correction. By default the symbol is the smallest that holds the data with 23 percent of its capacity
/// plus 3 codewords for error correction: a compact symbol of 1 to 4 layers, or a full-range symbol of 4 to 32 layers.
/// </summary>
public sealed class AztecCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AztecCodeSymbology"/> class at the recommended error correction level of 23 percent.
    /// </summary>
    public AztecCodeSymbology()
        : this(AztecEncoder.DefaultErrorCorrectionPercentage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AztecCodeSymbology"/> class.
    /// </summary>
    /// <param name="errorCorrectionPercentage"> The minimum percentage of the symbol capacity that error correction takes, 5 to 95. Three more check codewords
    /// are always added. </param>
    public AztecCodeSymbology(int errorCorrectionPercentage)
    {
        Guard.MustBeBetweenOrEqualTo(errorCorrectionPercentage, 5, 95, nameof(errorCorrectionPercentage));
        this.ErrorCorrectionPercentage = errorCorrectionPercentage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AztecCodeSymbology"/> class with a full-range symbol of a fixed number of data layers.
    /// </summary>
    /// <param name="errorCorrectionPercentage"> The minimum percentage of the symbol capacity that error correction takes, 5 to 95. Three more check codewords
    /// are always added. </param>
    /// <param name="layers">The number of data layers, 1 to 32.</param>
    public AztecCodeSymbology(int errorCorrectionPercentage, int layers)
        : this(errorCorrectionPercentage)
    {
        Guard.MustBeBetweenOrEqualTo(layers, 1, 32, nameof(layers));
        this.Layers = layers;
    }

    /// <summary>
    /// Gets the minimum error correction percentage.
    /// </summary>
    public int ErrorCorrectionPercentage { get; }

    /// <summary>
    /// Gets the number of data layers of the full-range symbol, or <see langword="null"/> when the symbol is the smallest that holds the data.
    /// </summary>
    public int? Layers { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        AztecEncoder.BuildSymbol(
            text,
            this.Layers is null ? AztecFormat.Auto : AztecFormat.Full,
            this.ErrorCorrectionPercentage,
            this.Layers ?? 0,
            options.ResolveXDimension(DefaultMatrixXDimension),
            ref symbol);
    }
}
