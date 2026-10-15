// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Channel Code symbology, which ANSI/AIM BC12-1998 defines. A symbol carries a
/// finder pattern of five one-module bars with four one-module spaces between them, then one space and
/// one bar per channel. A symbol of C channels is 4C + 7 modules wide whatever its value, and the value
/// is the position of its space and bar widths in the ordered list of sequences that keep that width
/// and the width rules of the standard.
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
    /// The bar height in modules when the caller sets none: the twenty modules of the symbols in the
    /// figures of the standard, whose minimum is 5.0 mm or 15 per cent of the symbol length, whichever
    /// is greater.
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
        Span<int> runs = symbol.BeginLinear(finder + (channels * 2), out Span<float> heights, out _);
        runs[..finder].Fill(1);

        Enumeration enumeration = new(channels, value);
        enumeration.NextSpace(3, channels, channels);
        for (int channel = 0; channel < channels; channel++)
        {
            runs[finder + (channel * 2)] = enumeration.Space(channel + 3);
            runs[finder + (channel * 2) + 1] = enumeration.Bar(channel + 3);
        }

        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

        double xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        float nominalBarHeight = MathF.Max(NominalBarHeight, (float)(MinimumBarHeightMillimetres / xDimension));
        float barHeight = options.ResolveBarHeight(BarcodeSymbology.PointXDimension, nominalBarHeight);
        heights.Fill(barHeight);

        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Walks the space and bar sequences of a channel count in the order of the standard and stops at
    /// the sequence whose position is the target value. The first three entries of each sequence are the
    /// end of the finder pattern, which the width rules look back at. The eight channels of a seven digit
    /// symbol and those three entries take eleven of the twelve inline places.
    /// </summary>
    private struct Enumeration
    {
        private readonly int channels;
        private readonly int target;
        private InlineArray12<int> bars;
        private InlineArray12<int> spaces;
        private int value;
        private bool found;

        /// <summary>
        /// Initializes a new instance of the <see cref="Enumeration"/> struct.
        /// </summary>
        /// <param name="channels">The number of channels.</param>
        /// <param name="target">The value whose sequence the walk stops at.</param>
        public Enumeration(int channels, int target)
        {
            this.channels = channels;
            this.target = target;

            // The end of the finder pattern: three narrow bars, and the two narrow spaces between them.
            this.bars[0] = 1;
            this.bars[1] = 1;
            this.bars[2] = 1;
            this.spaces[1] = 1;
            this.spaces[2] = 1;
        }

        /// <summary>
        /// Returns the width of a bar of the sequence the walk stopped at.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The width in modules.</returns>
        public readonly int Bar(int index) => this.bars[index];

        /// <summary>
        /// Returns the width of a space of the sequence the walk stopped at.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <returns>The width in modules.</returns>
        public readonly int Space(int index) => this.spaces[index];

        /// <summary>
        /// Tries every width of space i within the width left for spaces. The last space takes all the
        /// width left, so the symbol width stays constant.
        /// </summary>
        /// <param name="i">The element index.</param>
        /// <param name="maximumBar">The width left for the bars.</param>
        /// <param name="maximumSpace">The width left for the spaces.</param>
        public void NextSpace(int i, int maximumBar, int maximumSpace)
        {
            int first = i < this.channels + 2 ? 1 : maximumSpace;
            for (int space = first; space <= maximumSpace && !this.found; space++)
            {
                this.spaces[i] = space;
                this.NextBar(i, maximumSpace + 1 - space, maximumBar);
            }
        }

        /// <summary>
        /// Tries every width of bar i within the width left for bars. A bar starts at two modules when
        /// the two bars and two spaces before it add to four or less, so no four consecutive elements
        /// other than the finder pattern are all one module wide. The last bar takes all the width left.
        /// </summary>
        /// <param name="i">The element index.</param>
        /// <param name="maximumSpace">The width left for the spaces.</param>
        /// <param name="maximumBar">The width left for the bars.</param>
        private void NextBar(int i, int maximumSpace, int maximumBar)
        {
            int first = this.spaces[i] + this.bars[i - 1] + this.bars[i - 2] + this.spaces[i - 1] > 4 ? 1 : 2;
            if (i < this.channels + 2)
            {
                for (int bar = first; bar <= maximumBar && !this.found; bar++)
                {
                    this.bars[i] = bar;
                    this.NextSpace(i + 1, maximumBar + 1 - bar, maximumSpace);
                }
            }
            else if (first <= maximumBar)
            {
                this.bars[i] = maximumBar;
                if (this.value == this.target)
                {
                    this.found = true;
                    return;
                }

                this.value++;
            }
        }
    }
}
