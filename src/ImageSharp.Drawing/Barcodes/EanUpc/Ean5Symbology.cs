// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The five digit EAN/UPC add-on symbology, which the GS1 General Specifications specify. It carries the
/// price on an ISBN book barcode. The symbol starts with the add-on guard pattern 01011. Each digit takes a
/// seven module character from number set A or B, with a 01 delineator between characters. The add-on
/// checksum has no character of its own. The number set parity carries it.
/// <para>
/// The human readable interpretation prints above the bars. The leading quiet zone absorbs the leading
/// space module of the guard pattern.
/// </para>
/// </summary>
public sealed class Ean5Symbology : BarcodeSymbology
{
    private const int Width = 47;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ean5Symbology"/> class.
    /// </summary>
    public Ean5Symbology()
    {
    }

    /// <inheritdoc/>
    public override float NominalXDimension => EanUpcEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        if (text.Length != 5)
        {
            throw new ArgumentException($"EAN-5 requires exactly 5 digits; got {text.Length} characters.", nameof(text));
        }

        EanUpcEncoder.ValidateDigits(text);

        // GS1 General Specifications: V = (3 x (d1 + d3 + d5) + 9 x (d2 + d4)) mod 10 selects the parity pattern.
        int checksum = ((3 * ((text[0] - '0') + (text[2] - '0') + (text[4] - '0')))
            + (9 * ((text[1] - '0') + (text[3] - '0')))) % 10;

        int parity = EanUpcEncoder.AddOnFiveParity[checksum];

        Span<byte> modules = stackalloc byte[Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, 0b1011, 4);
        for (int i = 0; i < 5; i++)
        {
            if (i > 0)
            {
                EanUpcEncoder.AppendPattern(modules, ref position, 0b01, 2);
            }

            ReadOnlySpan<byte> numberSet = ((parity >> (4 - i)) & 1) == 0 ? EanUpcEncoder.NumberSetA : EanUpcEncoder.NumberSetB;
            EanUpcEncoder.AppendPattern(modules, ref position, numberSet[text[i] - '0'], 7);
        }

        return AddOnLayout.Build(modules, text, options);
    }
}
