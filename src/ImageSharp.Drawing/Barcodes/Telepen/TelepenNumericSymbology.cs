// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Telepen Numeric symbology, the double density numeric mode of Telepen. It packs "two digits into
/// one character" and a single digit followed by X into one of the remaining characters, so a symbol
/// carries an even number of characters in which X can stand only in the second position of a pair. The
/// symbol is a Telepen symbol of the packed characters, and the check character covers the packed
/// characters. The printed line shows the digits as given.
/// </summary>
public sealed class TelepenNumericSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, TelepenEncoder.MaximumLength, nameof(text));

        if ((text.Length & 1) == 1)
        {
            throw new ArgumentException($"Telepen Numeric carries an even number of characters; got {text.Length}.", nameof(text));
        }

        Span<int> buffer = text.Length <= TelepenEncoder.StackBufferLength
            ? stackalloc int[TelepenEncoder.StackBufferLength / 2]
            : new int[text.Length / 2];
        int count = 0;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        int index = 0;
        int first = 0;
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            bool second = (index & 1) == 1;
            if (current.IsAsciiDigit())
            {
                if (second)
                {
                    buffer[count++] = TelepenEncoder.NumericPairOffset + (first * 10) + (current.Value - '0');
                }
                else
                {
                    first = current.Value - '0';
                }
            }
            else if (current.Value == 'X' && second)
            {
                buffer[count++] = TelepenEncoder.NumericSingleOffset + first;
            }
            else
            {
                throw new ArgumentException($"Telepen Numeric carries only digits, and X in the second position of a pair; got {current.ToDisplayString()}.", nameof(text));
            }

            index += current.Utf16SequenceLength;
        }

        string readable = options.Font is null ? string.Empty : text;
        return TelepenEncoder.BuildSymbol(TelepenEncoder.Encode(buffer[..count]), readable, options);
    }
}
