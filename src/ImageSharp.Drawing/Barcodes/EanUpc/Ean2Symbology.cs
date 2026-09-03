// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The two digit EAN/UPC add-on symbology, which the GS1 General Specifications specify. It carries the
/// issue number on an ISSN periodical barcode. The symbol starts with the add-on guard pattern 01011. Each
/// digit takes a seven module character from number set A or B, with a 01 delineator between them. The
/// number set parity carries the two digit value modulo four.
/// <para>
/// The human readable interpretation prints above the bars. The leading quiet zone absorbs the leading
/// space module of the guard pattern.
/// </para>
/// </summary>
public sealed class Ean2Symbology : BarcodeSymbology
{
    private const int Width = 20;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ean2Symbology"/> class.
    /// </summary>
    public Ean2Symbology()
    {
    }

    /// <inheritdoc/>
    public override float NominalXDimension => EanUpcEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        if (text.Length != 2)
        {
            throw new ArgumentException($"EAN-2 requires exactly 2 digits; got {text.Length} characters.", nameof(text));
        }

        EanUpcEncoder.ValidateDigits(text);

        // GS1 General Specifications: the two-digit value modulo four selects the parity pattern, where
        // 0 = AA, 1 = AB, 2 = BA and 3 = BB. The remainder bits map to the number sets directly: a set bit is B.
        int parity = ((10 * (text[0] - '0')) + (text[1] - '0')) % 4;

        Span<byte> modules = stackalloc byte[Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, 0b1011, 4);
        for (int i = 0; i < 2; i++)
        {
            if (i > 0)
            {
                EanUpcEncoder.AppendPattern(modules, ref position, 0b01, 2);
            }

            ReadOnlySpan<byte> numberSet = ((parity >> (1 - i)) & 1) == 0 ? EanUpcEncoder.NumberSetA : EanUpcEncoder.NumberSetB;
            EanUpcEncoder.AppendPattern(modules, ref position, numberSet[text[i] - '0'], 7);
        }

        return AddOnLayout.Build(modules, text, options);
    }
}
