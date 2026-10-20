// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Data Matrix, a Health Industry Bar Code carried by a Data Matrix symbol in a square size. The symbol carries the flag character, the data and the
/// modulo 43 check character.
/// The text follows a data structure of ANSI/HIBC 2.6: primary data, secondary data that ends with its link character, or primary and secondary data joined
/// with "/", each with any supplemental data fields.
/// <para>
/// When a font is set, that same string prints below the symbol between delimiters, with a space printed as an underscore and a zero as "Ø". Primary and
/// secondary data that the symbol joins with "/" print on two lines, broken after the "/". When the options set no X dimension, the symbol prints at
/// <see cref="BarcodeSymbology.DefaultMatrixXDimension"/>.
/// </para>
/// </summary>
public sealed class HibcDataMatrixSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HibcDataMatrixSymbology"/> class, calculating the check character from the data.
    /// </summary>
    public HibcDataMatrixSymbology()
        : this(false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcDataMatrixSymbology"/> class.
    /// </summary>
    /// <param name="validateCheckCharacter"> Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to. </param>
    public HibcDataMatrixSymbology(bool validateCheckCharacter)
        => this.ValidateCheckCharacter = validateCheckCharacter;

    /// <summary>
    /// Gets a value indicating whether the last character of the input is the check character.
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
            DataMatrixEncoder.BuildSymbol(
                encoded.AsSpan(),
                false,
                DataMatrixFormat.Square,
                0,
                0,
                options.ResolveXDimension(HibcData.MinimumMatrixXDimension, DefaultMatrixXDimension, double.PositiveInfinity),
                ref symbol);

            if (options.Font is not null)
            {
                HibcData.AddMatrixReadable(encoded.AsSpan(), ref symbol);
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
