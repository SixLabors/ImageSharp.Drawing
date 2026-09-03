// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The PosiCode symbology, which AIM ITS/02-001 defines. The data lies in the positions of bars of one
/// width. Versions A and B carry all 256 values of ISO/IEC 8859-1, and the Limited versions carry the
/// digits, the capital letters, the hyphen and the full stop. Every symbol carries a cyclic redundancy
/// check character, which the printed line does not show, between a start character and a stop
/// character. The printed line shows the text as given.
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
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, PosiCodeEncoder.MaximumLength, nameof(text));

        bool limited = PosiCodeEncoder.IsLimited(this.Version);
        Span<int> values = text.Length <= PosiCodeEncoder.StackBufferLength
            ? stackalloc int[PosiCodeEncoder.StackBufferLength]
            : new int[text.Length];
        int count = 0;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (limited)
            {
                int codeword = PosiCodeEncoder.LimitedValue(current.Value);
                if (codeword < 0)
                {
                    throw new ArgumentException($"Limited PosiCode carries only digits, capital letters, the hyphen and the full stop; got {current.ToDisplayString()}.", nameof(text));
                }

                values[count++] = codeword;
            }
            else
            {
                if (current.Value > 255)
                {
                    throw new ArgumentException($"PosiCode carries only the characters of ISO/IEC 8859-1; got {current.ToDisplayString()}.", nameof(text));
                }

                values[count++] = current.Value;
            }
        }

        int[] runs;
        if (limited)
        {
            runs = PosiCodeEncoder.Encode(values[..count], this.Version);
        }
        else
        {
            Span<int> codewords = count * 4 <= 256 ? stackalloc int[count * 4] : new int[count * 4];
            int written = PosiCodeEncoder.ToCodewords(values[..count], codewords);
            runs = PosiCodeEncoder.Encode(codewords[..written], this.Version);
        }

        string readable = options.Font is null ? string.Empty : text;
        return PosiCodeEncoder.BuildSymbol(runs, readable, options, this.Version);
    }
}
