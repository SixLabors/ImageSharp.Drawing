// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataMatrix, section 5.6 of the GS1 General Specifications: a Data Matrix ECC 200 symbol in a square size that carries GS1 element strings. The data
/// starts with the Function 1 Symbol Character, and the same character separates an element string of variable length from the next one.
/// <para>
/// The input is the element string syntax the standard prints: an Application Identifier in parentheses, then its data, repeated. For example,
/// <c>(01)09521234543213(17)120508(10)ABCD1234</c>. The symbol does not carry the parentheses.
/// </para>
/// <para>
/// When a font is set, the element strings print below the symbol, one on each line, with the Application Identifiers in parentheses. An element string of
/// Application Identifier (8200) does not print.
/// </para>
/// <para>
/// The X dimension is the target of 0.495 millimetre of Table 5-45, symbol specification table 1 addendum 1. The symbol specification tables of section
/// 5.12.3 give GS1 DataMatrix X dimensions from 0.100 to 3.50 millimetres. An X dimension outside that range draws at the nearest limit.
/// </para>
/// </summary>
public sealed class Gs1DataMatrixSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataMatrixSymbology"/> class. The symbol takes the smallest square size that holds the data.
    /// </summary>
    public Gs1DataMatrixSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(text, DataMatrixEncoder.MaximumLength);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Prepare(text, ref encoded);
            DataMatrixEncoder.BuildSymbol(
                encoded.AsSpan(),
                true,
                DataMatrixFormat.Square,
                0,
                0,
                options.ResolveXDimension(Gs1Data.Gs1DataMatrixMinimumXDimension, Gs1Data.Gs1MatrixXDimension, Gs1Data.Gs1MatrixMaximumXDimension),
                ref symbol);

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
