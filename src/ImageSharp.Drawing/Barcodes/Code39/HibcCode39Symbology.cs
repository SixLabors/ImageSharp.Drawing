// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Code 39, a Health Industry Bar Code. A Code 39 symbol carries it. The symbol carries the flag character, the data and the modulo 43 check character.
/// Code 39 adds no check character of its own. The human readable interpretation shows that same string between delimiters, with a space printed as an
/// underscore and a zero as "Ø". When the options set no X dimension, the symbol prints at <see cref="BarcodeSymbology.DefaultXDimension"/>.
/// The text follows a data structure of ANSI/HIBC 2.6: primary data, secondary data that ends with its link character, or primary and secondary data joined
/// with "/", each with any supplemental data fields.
/// </summary>
public sealed class HibcCode39Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HibcCode39Symbology"/> class.
    /// </summary>
    public HibcCode39Symbology()
        : this(false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcCode39Symbology"/> class.
    /// </summary>
    /// <param name="validateCheckCharacter"> Whether the check character at the end of the input is validated. When <see langword="false"/> the whole input is
    /// data. </param>
    public HibcCode39Symbology(bool validateCheckCharacter) => this.ValidateCheckCharacter = validateCheckCharacter;

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
            // The prepared data is in the Code 39 character set and at most Code39Encoder.MaximumLength long.
            HibcData.Prepare(text, this.ValidateCheckCharacter, ref encoded);

            ReadOnlySpan<char> data = encoded.AsSpan();

            // The interpretation is the encoded data between two delimiters, so a buffer three characters longer holds it, and longer data rents it once.
            InlineArray64<char> readableStorage = default;
            int readableCapacity = HibcData.ReadableCapacity(data.Length);
            using PooledBuffer<char> rented = readableCapacity <= HibcData.InlineBufferLength ? default : new(readableCapacity);
            Span<char> readable = readableCapacity <= HibcData.InlineBufferLength ? ((Span<char>)readableStorage)[..readableCapacity] : rented.Span;
            int written = HibcData.WriteReadable(data, readable);
            double xDimension = options.ResolveXDimension(HibcData.MinimumLinearXDimension, DefaultXDimension, double.PositiveInfinity);
            Code39Encoder.BuildSymbol(data, null, [], readable[..written], [], options, xDimension, ref symbol);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
