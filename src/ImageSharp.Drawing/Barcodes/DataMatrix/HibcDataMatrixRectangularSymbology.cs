// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Data Matrix in a rectangular size, a Health Industry Bar Code carried by a Data Matrix symbol of
/// one of the six rectangular sizes. The symbol carries the flag character, the data and the modulo 43
/// check character.
/// <para>
/// When a font is set, that same string prints below the symbol between delimiters, and a check character
/// that is a space prints as an underscore. Primary and secondary data that the symbol joins with "/" print
/// on two lines, broken after the "/".
/// </para>
/// </summary>
public sealed class HibcDataMatrixRectangularSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HibcDataMatrixRectangularSymbology"/> class, calculating
    /// the check character from the data.
    /// </summary>
    public HibcDataMatrixRectangularSymbology()
        : this(false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcDataMatrixRectangularSymbology"/> class.
    /// </summary>
    /// <param name="validateCheckCharacter">
    /// Whether the last character of the input is the check character, which the encoder validates
    /// against the data, rather than data the encoder appends a check character to.
    /// </param>
    public HibcDataMatrixRectangularSymbology(bool validateCheckCharacter)
        => this.ValidateCheckCharacter = validateCheckCharacter;

    /// <summary>
    /// Gets a value indicating whether the last character of the input is the check character.
    /// </summary>
    public bool ValidateCheckCharacter { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        InlineArray64<char> bufferStorage = default;
        int capacity = HibcData.PreparedCapacity(text.Length);
        ValueStringBuilder encoded = capacity <= HibcData.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            HibcData.Prepare(text, this.ValidateCheckCharacter, ref encoded);
            DataMatrixEncoder.BuildSymbol(encoded.AsSpan(), false, DataMatrixFormat.Rectangular, 0, 0, ref symbol);
            if (options.Font is not null)
            {
                DataMatrixEncoder.AddHibcReadable(encoded.AsSpan(), ref symbol);
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
