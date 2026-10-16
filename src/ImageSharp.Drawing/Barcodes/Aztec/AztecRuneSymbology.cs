// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Aztec Runes of ISO/IEC 24778: 256 small marks of 11 x 11 modules, the Core Symbol of a compact Aztec Code symbol whose mode message carries one value. The
/// text is the value, 0 to 255, in decimal digits.
/// </summary>
public sealed class AztecRuneSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AztecRuneSymbology"/> class.
    /// </summary>
    public AztecRuneSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length is < 1 or > 3 || text.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            throw new ArgumentException("An Aztec Rune carries a value of 0 to 255 in decimal digits.", nameof(text));
        }

        int value = int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        if (value > 255)
        {
            throw new ArgumentException($"An Aztec Rune carries a value of 0 to 255; got {value}.", nameof(text));
        }

        AztecEncoder.BuildRune(value, options.XDimension ?? PointXDimension, ref symbol);
    }
}
