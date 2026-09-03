// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Channel Code symbology, which ANSI/AIM BC12-1998 defines. It carries a number of two to seven
/// digits. The number of digits, leading zeros included,
/// selects the channel count, one more than the digits, and each channel count carries a range of
/// values: 0 to 26 for three channels, then 292, 3493, 44072, 576688 and 7742862 for four to eight. The
/// printed line shows the digits as given.
/// </summary>
public sealed class ChannelCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelCodeSymbology"/> class.
    /// </summary>
    public ChannelCodeSymbology()
        : this(false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelCodeSymbology"/> class.
    /// </summary>
    /// <param name="shortFinder">
    /// Whether the finder pattern is the short form of three bars and two spaces rather than the full
    /// form of five bars and four spaces.
    /// </param>
    public ChannelCodeSymbology(bool shortFinder)
        => this.ShortFinder = shortFinder;

    /// <summary>
    /// Gets a value indicating whether the finder pattern is the short form of three bars and two
    /// spaces rather than the full form of five bars and four spaces.
    /// </summary>
    public bool ShortFinder { get; }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length < ChannelCodeEncoder.MinimumLength || text.Length > ChannelCodeEncoder.MaximumLength)
        {
            throw new ArgumentException($"Channel Code carries {ChannelCodeEncoder.MinimumLength} to {ChannelCodeEncoder.MaximumLength} digits; got {text.Length} characters.", nameof(text));
        }

        int value = 0;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"Channel Code carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }

            value = (value * 10) + (current.Value - '0');
        }

        int maximum = ChannelCodeEncoder.MaximumValue(text.Length);
        if (value > maximum)
        {
            throw new ArgumentException($"Channel Code carries values up to {maximum} in {text.Length} digits; got {value}.", nameof(text));
        }

        string readable = options.Font is null ? string.Empty : text;
        return ChannelCodeEncoder.BuildSymbol(ChannelCodeEncoder.Encode(value, text.Length, this.ShortFinder), readable, options);
    }
}
