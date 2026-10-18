// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The PosiCode symbology, which AIM ITS/02-001 defines. The data lies in the positions of bars of one width. Versions A and B carry all 256 values of ISO/IEC
/// 8859-1, and the Limited versions carry the digits, the capital letters, the hyphen and the full stop. Every symbol carries a cyclic redundancy check
/// character, which the printed line does not show, between a start character and a stop character. The printed line shows the text as given.
/// </summary>
public sealed class PosiCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PosiCodeSymbology"/> class.
    /// </summary>
    public PosiCodeSymbology()
        : this(PosiCodeVersion.A)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PosiCodeSymbology"/> class.
    /// </summary>
    /// <param name="version">The version, which sets the character set and the bar positions.</param>
    public PosiCodeSymbology(PosiCodeVersion version)
        => this.Version = version;

    /// <summary>
    /// Gets the version, which sets the character set and the bar positions.
    /// </summary>
    public PosiCodeVersion Version { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, PosiCodeEncoder.MaximumLength, nameof(text));

        bool limited = PosiCodeEncoder.IsLimited(this.Version);

        // One value for each character, so the builder never grows: short text works inline and longer text rents once.
        InlineArray64<int> valueStorage = default;
        ValueListBuilder<int> values = text.Length <= PosiCodeEncoder.InlineBufferLength ? new(valueStorage) : new(text.Length);
        PooledBuffer<int> rentedCodewords = default;
        try
        {
            SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
            while (codePoints.MoveNext())
            {
                CodePoint current = codePoints.Current;
                if (limited)
                {
                    int codeword = PosiCodeEncoder.LimitedValue(current.Value);
                    if (codeword < 0)
                    {
                        throw new ArgumentException(
                            $"Limited PosiCode carries only digits, capital letters, the hyphen and the full stop; got {current.ToDisplayString()}.",
                            nameof(text));
                    }

                    values.Append(codeword);
                }
                else
                {
                    if (current.Value > 255)
                    {
                        throw new ArgumentException($"PosiCode carries only the characters of ISO/IEC 8859-1; got {current.ToDisplayString()}.", nameof(text));
                    }

                    values.Append(current.Value);
                }
            }

            if (limited)
            {
                PosiCodeEncoder.BuildSymbol(values.AsSpan(), this.Version, text, options, ref symbol);
            }
            else
            {
                // ToCodewords writes at most four codewords for each value.
                InlineArray128<int> codewordStorage = default;
                int capacity = values.Length * 4;
                Span<int> codewords = capacity <= 128
                    ? codewordStorage
                    : (rentedCodewords = new PooledBuffer<int>(capacity)).Span;
                int written = PosiCodeEncoder.ToCodewords(values.AsSpan(), codewords);
                PosiCodeEncoder.BuildSymbol(codewords[..written], this.Version, text, options, ref symbol);
            }
        }
        finally
        {
            values.Dispose();
            rentedCodewords.Dispose();
        }
    }
}
