// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataMatrix, section 5.6 of the GS1 General Specifications, in a rectangular size. The symbol takes
/// the smallest of the six rectangular sizes of Table 5-33, 8 x 18 to 16 x 48, that holds the data, and
/// carries GS1 element strings as <see cref="Gs1DataMatrixSymbology"/> does.
/// <para>
/// The input is the element string syntax the standard prints: an Application Identifier in parentheses,
/// then its data, repeated. The symbol does not carry the parentheses.
/// </para>
/// <para>
/// When a font is set, the element strings print below the symbol, one on each line, with the Application
/// Identifiers in parentheses. An element string of Application Identifier (8200) does not print.
/// </para>
/// </summary>
public sealed class Gs1DataMatrixRectangularSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataMatrixRectangularSymbology"/> class.
    /// </summary>
    public Gs1DataMatrixRectangularSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(text, DataMatrixEncoder.MaximumLength);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Prepare(text, ref encoded);
            DataMatrixEncoder.BuildSymbol(encoded.AsSpan(), true, DataMatrixFormat.Rectangular, 0, 0, options.XDimension ?? PointXDimension, ref symbol);
            if (options.Font is not null)
            {
                Gs1Data.AddMatrixReadable(text, options.Font, ref symbol);
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
