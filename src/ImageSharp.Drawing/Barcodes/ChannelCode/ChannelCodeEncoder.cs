// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Channel Code symbology, which ANSI/AIM BC12-1998 defines. A symbol carries a
/// finder pattern of five one-module bars with four one-module spaces between them, then one space and
/// one bar per channel. A symbol of C channels is 4C + 7 modules wide whatever its value, and the value
/// is the position of its space and bar widths in the ordered list of sequences that keep that width
/// and the width rules of the symbology.
/// </summary>
internal static class ChannelCodeEncoder
{
    /// <summary>
    /// The smallest number of digits a symbol carries, which selects three channels.
    /// </summary>
    public const int MinimumLength = 2;

    /// <summary>
    /// The largest number of digits a symbol carries, which selects eight channels.
    /// </summary>
    public const int MaximumLength = 7;

    /// <summary>
    /// The quiet zone in modules before the symbol: "at least 1X wide".
    /// </summary>
    public const int LeadingQuietZone = 1;

    /// <summary>
    /// The quiet zone in modules after the symbol: "at least 2X wide".
    /// </summary>
    public const int TrailingQuietZone = 2;

    /// <summary>
    /// The number of channels beyond the number of digits.
    /// </summary>
    private const int ChannelsBeyondDigits = 1;

    /// <summary>
    /// The number of elements in the full finder pattern: five bars and four spaces.
    /// </summary>
    private const int FinderElements = 9;

    /// <summary>
    /// The number of elements in the short finder pattern: three bars and two spaces.
    /// </summary>
    private const int ShortFinderElements = 5;

    /// <summary>
    /// The bar height in modules when the caller sets none: twenty modules, and at least
    /// <see cref="MinimumBarHeightMillimetres"/>. The minimum height is 5.0 mm or 15 per cent of the symbol
    /// length, whichever is greater, and the longest symbol is 39 modules, so twenty modules always exceed
    /// 15 per cent.
    /// </summary>
    private const float NominalBarHeight = 20F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 5F;

    /// <summary>
    /// Gets the largest value each channel count carries, from three to eight channels.
    /// </summary>
    private static ReadOnlySpan<int> MaximumValues => [26, 292, 3493, 44072, 576688, 7742862];

    /// <summary>
    /// Returns the largest value a number of digits carries. Two to seven digits select three to eight
    /// channels.
    /// </summary>
    /// <param name="digits">The number of digits.</param>
    /// <returns>The largest value.</returns>
    public static int MaximumValue(int digits) => MaximumValues[digits - MinimumLength];

    /// <summary>
    /// Encodes a value and builds the symbol. The runs start with the first bar of the finder pattern and
    /// end on the bar of the last channel. Channel Code carries no guard bars, so every bar runs the full
    /// height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="value">The value, within the range of the channel count.</param>
    /// <param name="digits">The number of digits, which sets the channel count.</param>
    /// <param name="shortFinder">Whether the finder pattern is the short form of three bars.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        int value,
        int digits,
        bool shortFinder,
        ReadOnlySpan<char> readable,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        int channels = digits + ChannelsBeyondDigits;
        int finder = shortFinder ? ShortFinderElements : FinderElements;
        double xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        Span<int> runs = symbol.BeginLinear(finder + (channels * 2), xDimension, out Span<float> heights, out _);
        runs[..finder].Fill(1);

        // Eight channels, the most, fit inline.
        InlineArray8<int> spaceStorage = default;
        InlineArray8<int> barStorage = default;
        Span<int> spaces = ((Span<int>)spaceStorage)[..channels];
        Span<int> bars = ((Span<int>)barStorage)[..channels];
        Unrank(channels, value, spaces, bars);
        for (int channel = 0; channel < channels; channel++)
        {
            runs[finder + (channel * 2)] = spaces[channel];
            runs[finder + (channel * 2) + 1] = bars[channel];
        }

        // A finder element is one module, and the spaces and bars of C channels add to 4C - 2 modules.
        int widthInModules = finder + (channels * 4) - 2;

        float nominalBarHeight = MathF.Max(NominalBarHeight, (float)(MinimumBarHeightMillimetres / xDimension));
        float barHeight = options.ResolveBarHeight(xDimension, nominalBarHeight);
        heights.Fill(barHeight);

        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Writes the space and bar widths of the sequence at a position in the order of the symbology. The order
    /// takes each space width from the narrowest, then each bar width from the narrowest, channel by channel,
    /// so the sequences under one choice form one block of positions. Each choice counts the sequences under
    /// it and skips that block whole until the position falls inside one, so the work grows with the choices,
    /// not with the value.
    /// </summary>
    /// <param name="channels">The number of channels.</param>
    /// <param name="value">The position of the sequence, within the range of the channel count.</param>
    /// <param name="spaces">The buffer that receives the space widths, one a channel.</param>
    /// <param name="bars">The buffer that receives the bar widths, one a channel.</param>
    private static void Unrank(int channels, int value, Span<int> spaces, Span<int> bars)
    {
        int stride = channels + 1;
        using PooledBuffer<int> countBuffer = new(channels * stride * stride * 12);
        Span<int> counts = countBuffer.Span;
        counts.Fill(-1);

        // The end of the finder pattern stands before the first channel: three narrow bars and the narrow
        // spaces between them, so the previous bar is 1 and the bar and space before it add to 2.
        int barWidth = channels;
        int spaceWidth = channels;
        int previousBar = 1;
        int pair = 2;
        int remaining = value;
        for (int channel = 0; channel < channels - 1; channel++)
        {
            bool chosen = false;
            for (int space = 1; space <= spaceWidth && !chosen; space++)
            {
                for (int bar = FirstBar(space, previousBar, pair); bar <= barWidth; bar++)
                {
                    int nextPrevious = Math.Min(bar, 4);
                    int nextPair = Math.Min(previousBar + space, 4);
                    int count = Count(counts, channels, channel + 1, barWidth + 1 - bar, spaceWidth + 1 - space, nextPrevious, nextPair);
                    if (remaining < count)
                    {
                        spaces[channel] = space;
                        bars[channel] = bar;
                        barWidth += 1 - bar;
                        spaceWidth += 1 - space;
                        previousBar = nextPrevious;
                        pair = nextPair;
                        chosen = true;
                        break;
                    }

                    remaining -= count;
                }
            }
        }

        // The last space and the last bar take all the width left, so the symbol width stays constant.
        spaces[channels - 1] = spaceWidth;
        bars[channels - 1] = barWidth;
    }

    /// <summary>
    /// Counts the sequences that complete the channels from a channel on. The counts depend only on the
    /// channel, the widths left and the two sums the bar rule reads, so each is counted once.
    /// </summary>
    /// <param name="counts">The counts found so far, -1 where none is.</param>
    /// <param name="channels">The number of channels.</param>
    /// <param name="channel">The first channel to complete.</param>
    /// <param name="barWidth">The width left for the bars.</param>
    /// <param name="spaceWidth">The width left for the spaces.</param>
    /// <param name="previousBar">The width of the bar before the channel, at most 4.</param>
    /// <param name="pair">The width of the bar and the space before that bar, at most 4.</param>
    /// <returns>The number of sequences.</returns>
    private static int Count(Span<int> counts, int channels, int channel, int barWidth, int spaceWidth, int previousBar, int pair)
    {
        // The last channel takes all the width left, so it completes one sequence when its bar may take
        // that width.
        if (channel == channels - 1)
        {
            return FirstBar(spaceWidth, previousBar, pair) <= barWidth ? 1 : 0;
        }

        int stride = channels + 1;
        int index = (((((channel * stride) + barWidth) * stride) + spaceWidth) * 4) + previousBar - 1;
        index = (index * 3) + pair - 2;
        if (counts[index] >= 0)
        {
            return counts[index];
        }

        int total = 0;
        for (int space = 1; space <= spaceWidth; space++)
        {
            for (int bar = FirstBar(space, previousBar, pair); bar <= barWidth; bar++)
            {
                total += Count(counts, channels, channel + 1, barWidth + 1 - bar, spaceWidth + 1 - space, Math.Min(bar, 4), Math.Min(previousBar + space, 4));
            }
        }

        counts[index] = total;
        return total;
    }

    /// <summary>
    /// Returns the narrowest width a bar may take. A bar starts at two modules when the space before it, the
    /// bar before that space and the bar and space before that bar add to four or less, so no four
    /// consecutive elements other than the finder pattern are all one module wide. The previous bar and the
    /// pair are at most 4, because a larger width cannot change the sum's test against 4.
    /// </summary>
    /// <param name="space">The width of the space before the bar.</param>
    /// <param name="previousBar">The width of the bar before that space, at most 4.</param>
    /// <param name="pair">The width of the bar and the space before that bar, at most 4.</param>
    /// <returns>The narrowest bar width, 1 or 2.</returns>
    private static int FirstBar(int space, int previousBar, int pair) => space + previousBar + pair > 4 ? 1 : 2;
}
