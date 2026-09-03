// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Telepen symbology. It carries all 128 ASCII characters without shift characters, between the
/// start code _ and the stop code z, which the printed line does not show. Every symbol carries the
/// modulo 127 check character, which the printed line does not show either.
/// </summary>
public sealed class TelepenSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, TelepenEncoder.MaximumLength, nameof(text));

        Span<int> buffer = text.Length <= TelepenEncoder.StackBufferLength
            ? stackalloc int[TelepenEncoder.StackBufferLength]
            : new int[text.Length];
        int count = 0;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAscii)
            {
                throw new ArgumentException($"Telepen carries only ASCII characters; got {current.ToDisplayString()}.", nameof(text));
            }

            buffer[count++] = current.Value;
        }

        string readable = options.Font is null ? string.Empty : text;
        return TelepenEncoder.BuildSymbol(TelepenEncoder.Encode(buffer[..count]), readable, options);
    }
}
