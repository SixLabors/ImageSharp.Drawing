// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Code 128, a Health Industry Bar Code. A Code 128 symbol carries it. The symbol carries the flag character, the data and the check character. The human
/// readable interpretation shows that same string between delimiters, with a space printed as an underscore and a zero as "Ø". When the options set no X
/// dimension, the symbol prints at <see cref="BarcodeSymbology.DefaultXDimension"/>, and when they set no bar height, the bars are 15% of the symbol width.
/// The text follows a data structure of ANSI/HIBC 2.6: primary data, secondary data that ends with its link character, or primary and secondary data joined
/// with "/", each with any supplemental data fields.
/// </summary>
public sealed class HibcCode128Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HibcCode128Symbology"/> class.
    /// </summary>
    public HibcCode128Symbology()
        : this(false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcCode128Symbology"/> class.
    /// </summary>
    /// <param name="validateCheckCharacter"> Whether the check character at the end of the input is validated. When <see langword="false"/> the whole input is
    /// data. </param>
    public HibcCode128Symbology(bool validateCheckCharacter) => this.ValidateCheckCharacter = validateCheckCharacter;

    /// <summary>
    /// Gets a value indicating whether the check character at the end of the input is validated. When <see langword="false"/> the whole input is data.
    /// </summary>
    public bool ValidateCheckCharacter { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray64<char> bufferStorage = default;
        int capacity = HibcData.PreparedCapacity(text.Length);
        ValueStringBuilder encoded = capacity <= HibcData.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            HibcData.Prepare(text, this.ValidateCheckCharacter, ref encoded);

            ReadOnlySpan<char> data = encoded.AsSpan();

            // The interpretation is the encoded data between two delimiters, so a buffer three characters longer holds it, and longer data rents it once.
            InlineArray64<char> readableStorage = default;
            int readableCapacity = HibcData.ReadableCapacity(data.Length);
            using PooledBuffer<char> rented = readableCapacity <= HibcData.InlineBufferLength ? default : new(readableCapacity);
            Span<char> readable = readableCapacity <= HibcData.InlineBufferLength ? ((Span<char>)readableStorage)[..readableCapacity] : rented.Span;
            int written = HibcData.WriteReadable(data, readable);

            // Section 5.1 of ANSI/HIBC 2.6: "The height of the bars should be at least 15% of the symbol length." That is a recommendation, so it sets the
            // nominal bar height and does not clamp the bar height.
            Code128Layout layout = new()
            {
                XDimension = options.ResolveXDimension(HibcData.MinimumLinearXDimension, DefaultXDimension, double.PositiveInfinity),
                MinimumHeightFraction = HibcData.MinimumBarHeightFraction,
                MinimumBarHeight = 0F,
                MaximumBarHeight = float.PositiveInfinity,
                TextClearance = BarcodeTextPlacement.Clearance,
                HorizontalQuietZone = Code128Encoder.QuietZone,
            };

            Code128Encoder.BuildSymbol(data, false, [], readable[..written], [], [], Code128Linkage.None, in layout, options, ref symbol);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
