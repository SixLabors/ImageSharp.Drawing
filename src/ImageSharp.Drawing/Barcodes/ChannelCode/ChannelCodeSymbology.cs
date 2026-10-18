// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Channel Code symbology, which ANSI/AIM BC12-1998 defines. It carries a number of two to seven digits. The number of digits, leading zeros included,
/// selects the channel count, one more than the digits, and each channel count carries a range of values: 0 to 26 for three channels, then 292, 3493, 44072,
/// 576688 and 7742862 for four to eight. The printed line shows the digits as given.
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
    /// <param name="shortFinder"> Whether the finder pattern is the short form of three bars and two spaces rather than the full form of five bars and four
    /// spaces. </param>
    public ChannelCodeSymbology(bool shortFinder)
        => this.ShortFinder = shortFinder;

    /// <summary>
    /// Gets a value indicating whether the finder pattern is the short form of three bars and two spaces rather than the full form of five bars and four
    /// spaces.
    /// </summary>
    public bool ShortFinder { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length < ChannelCodeEncoder.MinimumLength || text.Length > ChannelCodeEncoder.MaximumLength)
        {
            throw new ArgumentException(
                $"Channel Code carries {ChannelCodeEncoder.MinimumLength} to {ChannelCodeEncoder.MaximumLength} digits; got {text.Length} characters.",
                nameof(text));
        }

        ValidateDigits(text);
        int value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

        int maximum = ChannelCodeEncoder.MaximumValue(text.Length);
        if (value > maximum)
        {
            throw new ArgumentException($"Channel Code carries values up to {maximum} in {text.Length} digits; got {value}.", nameof(text));
        }

        ChannelCodeEncoder.BuildSymbol(value, text.Length, this.ShortFinder, text, options, ref symbol);
    }
}
