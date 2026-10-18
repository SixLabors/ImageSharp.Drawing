// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Code 128, a Health Industry Bar Code. A Code 128 symbol carries it. The symbol carries the flag character, the data and the check character. The human
/// readable interpretation shows that same string between delimiters. A check character that is a space prints as an underscore.
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
            Code128Encoder.BuildSymbol(
                data,
                false,
                HibcData.ReadableOpening,
                HibcData.ReadableBody(data),
                [HibcData.PrintedCheck(data), HibcData.ReadableClosing],
                options,
                ref symbol);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
