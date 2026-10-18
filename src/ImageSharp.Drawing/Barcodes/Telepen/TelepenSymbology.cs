// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Telepen symbology. It carries all 128 ASCII characters without shift characters, between the start code _ and the stop code z, which the printed line
/// does not show. Every symbol carries the modulo 127 check character, which the printed line does not show either.
/// </summary>
public sealed class TelepenSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, TelepenEncoder.MaximumLength, nameof(text));

        // One value for each character, so the buffer never grows: short text works inline and longer text rents once.
        InlineArray64<int> bufferStorage = default;
        ValueListBuilder<int> buffer = text.Length <= TelepenEncoder.InlineBufferLength ? new(bufferStorage) : new(text.Length);
        try
        {
            SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
            while (codePoints.MoveNext())
            {
                CodePoint current = codePoints.Current;
                if (!current.IsAscii)
                {
                    throw new ArgumentException($"Telepen carries only ASCII characters; got {current.ToDisplayString()}.", nameof(text));
                }

                buffer.Append(current.Value);
            }

            TelepenEncoder.BuildSymbol(buffer.AsSpan(), text, options, ref symbol);
        }
        finally
        {
            buffer.Dispose();
        }
    }
}
