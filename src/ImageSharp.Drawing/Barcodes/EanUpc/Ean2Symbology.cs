// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The two digit EAN/UPC add-on symbology, which the GS1 General Specifications specify. It carries the issue number on an ISSN periodical barcode. The symbol
/// starts with the 4 module add-on guard bar pattern 1011. Each digit takes a seven module character from number set A or B, with a 01 delineator between
/// them. The number set parity carries the two digit value modulo four.
/// <para>
/// The human readable interpretation prints above the bars. The quiet zone before the symbol is 7 modules, the smallest separation that Table 5-44 of the
/// GS1 General Specifications gives between an add-on and an EAN-13 or UPC-E symbol. A UPC-A symbol needs 9 modules.
/// </para>
/// </summary>
public sealed class Ean2Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Ean2Symbology"/> class.
    /// </summary>
    public Ean2Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length != 2)
        {
            throw new ArgumentException($"EAN-2 requires exactly 2 digits; got {text.Length} characters.", nameof(text));
        }

        BarcodeSymbology.ValidateDigits(text);

        // Table 5-8 of section 5.2.2.5.1 of the GS1 General Specifications: the two-digit value modulo four selects the parity pattern, where 0 = AA, 1 = AB, 2 = BA and 3 = BB. The remainder bits
        // map to the number sets directly: a set bit is B.
        int parity = ((10 * (text[0] - '0')) + (text[1] - '0')) % 4;
        AddOnLayout.Build(text, parity, options, ref symbol);
    }
}
