// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Code 39, a Health Industry Bar Code carried by a Code 39 symbol. The symbol carries the flag
/// character, the data and the modulo 43 check character, and Code 39 adds no check character of its own
/// because the data already holds one. The human readable interpretation is that same string between
/// delimiters, with a check character that is a space shown as an underscore.
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
    /// <param name="validateCheckCharacter">
    /// Whether the check character at the end of the input is validated. When <see langword="false"/> the
    /// whole input is data.
    /// </param>
    public HibcCode39Symbology(bool validateCheckCharacter)
        => this.ValidateCheckCharacter = validateCheckCharacter;

    /// <summary>
    /// Gets a value indicating whether the check character at the end of the input is validated. When
    /// <see langword="false"/> the whole input is data.
    /// </summary>
    public bool ValidateCheckCharacter { get; }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        Span<char> buffer = stackalloc char[HibcData.StackBufferLength];
        ValueStringBuilder encoded = new(buffer);
        try
        {
            HibcData.Prepare(text, this.ValidateCheckCharacter, ref encoded);
            Code39Encoder.Validate(encoded.AsSpan());

            return Code39Encoder.BuildSymbol(
                Code39Encoder.Encode(encoded.AsSpan(), null),
                options.Font is null ? string.Empty : HibcData.BuildReadable(encoded.AsSpan()),
                options);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
