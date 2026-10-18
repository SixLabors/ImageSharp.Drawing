// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Expanded, a single row symbol of variable width that carries GS1 element strings and can be read in segments by an omnidirectional scanner.
/// Section 5.5.2.3.1 of the GS1 General Specifications gives "from 4 to 22 symbol characters, or a minimum of 102X wide and a maximum of 534X wide" and "34X
/// high". No quiet zone is required.
/// <para>
/// The input is the element string syntax the standard prints, each Application Identifier in parentheses followed by its data. A symbol carries up to 74
/// digits or 41 letters. When the first element string is Application Identifier (01), its check digit must be correct. The human readable interpretation
/// prints the element strings on one line, except an element string of Application Identifier (8200), which does not print.
/// </para>
/// </summary>
public sealed class Gs1DataBarExpandedSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarExpandedSymbology"/> class.
    /// </summary>
    public Gs1DataBarExpandedSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(text, DataBarExpandedEncoder.MaximumDataCharacters);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Prepare(text, ref encoded);

            // The interpretation is never longer than the text, which PreparedCapacity bounds to 122 characters, so the inline buffer always holds it.
            InlineArray128<char> readableStorage = default;
            Span<char> readable = readableStorage;
            int readableLength = options.Font is null ? 0 : Gs1Data.WriteReadable(text, false, readable);
            DataBarExpandedEncoder.BuildLinear(encoded.AsSpan(), readable[..readableLength], options, ref symbol);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
