// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Aztec Code, a Health Industry Bar Code carried by an Aztec Code symbol. The symbol carries the flag character, the data and the modulo 43 check
/// character, in the smallest symbol that holds them.
/// The text follows a data structure of ANSI/HIBC 2.6: primary data, secondary data that ends with its link character, or primary and secondary data joined
/// with "/", each with any supplemental data fields.
/// <para>
/// When a font is set, that same string prints below the symbol between delimiters, with a space printed as an underscore and a zero as "Ø". Primary and
/// secondary data that the symbol joins with "/" print on two lines, broken after the "/". When the options set no X dimension, the X dimension
/// is 0.015 inch, from section 5.2 of ANSI/HIBC 2.6.
/// </para>
/// </summary>
public sealed class HibcAztecCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HibcAztecCodeSymbology"/> class at the recommended error correction level of 23 percent, calculating the
    /// check character from the data.
    /// </summary>
    public HibcAztecCodeSymbology()
        : this(AztecEncoder.DefaultErrorCorrectionPercentage, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcAztecCodeSymbology"/> class at the recommended error correction level of 23 percent.
    /// </summary>
    /// <param name="validateCheckCharacter"> Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to. </param>
    public HibcAztecCodeSymbology(bool validateCheckCharacter)
        : this(AztecEncoder.DefaultErrorCorrectionPercentage, validateCheckCharacter)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcAztecCodeSymbology"/> class.
    /// </summary>
    /// <param name="errorCorrectionPercentage"> The minimum percentage of the symbol capacity that error correction takes, 5 to 95. Three more check codewords
    /// are always added. </param>
    /// <param name="validateCheckCharacter"> Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to. </param>
    public HibcAztecCodeSymbology(int errorCorrectionPercentage, bool validateCheckCharacter)
    {
        Guard.MustBeBetweenOrEqualTo(errorCorrectionPercentage, 5, 95, nameof(errorCorrectionPercentage));
        this.ErrorCorrectionPercentage = errorCorrectionPercentage;
        this.ValidateCheckCharacter = validateCheckCharacter;
    }

    /// <summary>
    /// Gets the minimum error correction percentage.
    /// </summary>
    public int ErrorCorrectionPercentage { get; }

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
            AztecEncoder.BuildSymbol(
                encoded.AsSpan(),
                AztecFormat.Auto,
                this.ErrorCorrectionPercentage,
                0,
                options.ResolveXDimension(HibcData.MinimumMatrixXDimension, HibcData.NominalMatrixXDimension, double.PositiveInfinity),
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
