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
    /// Encodes a value into the alternating bar and space run widths the renderer draws, starting with
    /// the first bar of the finder pattern and ending on the bar of the last channel.
    /// </summary>
    /// <param name="value">The value, within the range of the channel count.</param>
    /// <param name="digits">The number of digits, which sets the channel count.</param>
    /// <param name="shortFinder">Whether the finder pattern is the short form of three bars.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(int value, int digits, bool shortFinder)
    {
        int channels = digits + ChannelsBeyondDigits;
        int finder = shortFinder ? ShortFinderElements : FinderElements;
        int[] runs = new int[finder + (channels * 2)];
        runs.AsSpan(0, finder).Fill(1);

        Enumeration enumeration = new(channels, value);
        enumeration.NextSpace(3, channels, channels);
        for (int channel = 0; channel < channels; channel++)
        {
            runs[finder + (channel * 2)] = enumeration.Spaces[channel + 3];
            runs[finder + (channel * 2) + 1] = enumeration.Bars[channel + 3];
        }

        return runs;
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. Channel Code carries no guard bars, so every bar runs
    /// the full height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in modules.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(int[] runs, string text, BarcodeOptions options)
    {
        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

        float xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        float nominalBarHeight = MathF.Max(NominalBarHeight, MinimumBarHeightMillimetres / xDimension);
        float barHeight = EanUpcEncoder.ResolveBarHeight(options, BarcodeSymbology.PointXDimension, nominalBarHeight);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            heights[i] = barHeight;
        }

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Walks the space and bar sequences of a channel count in the order of the standard and stops at
    /// the sequence whose position is the target value. The first three entries of each array are the
    /// end of the finder pattern, which the width rules look back at.
    /// </summary>
    private sealed class Enumeration
    {
        private readonly int channels;
        private readonly int target;
        private int value;
        private bool found;

        public Enumeration(int channels, int target)
        {
            this.channels = channels;
            this.target = target;
            this.Bars = [1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0];
            this.Spaces = [0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0];
        }

        public int[] Bars { get; }

        public int[] Spaces { get; }

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
                this.Spaces[i] = space;
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
            int first = this.Spaces[i] + this.Bars[i - 1] + this.Bars[i - 2] + this.Spaces[i - 1] > 4 ? 1 : 2;
            if (i < this.channels + 2)
            {
                for (int bar = first; bar <= maximumBar && !this.found; bar++)
                {
                    this.Bars[i] = bar;
                    this.NextSpace(i + 1, maximumBar + 1 - bar, maximumSpace);
                }
            }
            else if (first <= maximumBar)
            {
                this.Bars[i] = maximumBar;
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
