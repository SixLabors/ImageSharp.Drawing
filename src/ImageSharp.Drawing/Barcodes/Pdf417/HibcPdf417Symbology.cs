// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC PDF417, a Health Industry Bar Code carried by a PDF417 symbol. The symbol carries the flag character, the data and the modulo 43 check character.
/// </summary>
public sealed class HibcPdf417Symbology : BarcodeSymbology
{
    /// <summary>
    /// The number of data columns when the caller sets none.
    /// </summary>
    private const int DefaultColumns = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcPdf417Symbology"/> class with two data columns, calculating the check character from the data.
    /// </summary>
    public HibcPdf417Symbology()
        : this(DefaultColumns, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcPdf417Symbology"/> class.
    /// </summary>
    /// <param name="columns">The number of data columns in a row, 1 to 30. The row count follows from the data.</param>
    /// <param name="validateCheckCharacter">Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to.</param>
    public HibcPdf417Symbology(int columns, bool validateCheckCharacter)
    {
        Guard.MustBeBetweenOrEqualTo(columns, Pdf417Encoder.MinimumColumns, Pdf417Encoder.MaximumColumns, nameof(columns));

        this.Columns = columns;
        this.ValidateCheckCharacter = validateCheckCharacter;
    }

    /// <summary>
    /// Gets the number of data columns in a row.
    /// </summary>
    public int Columns { get; }

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

            Pdf417Encoder.BuildSymbol(encoded.AsSpan(), this.Columns, null, false, options, ref symbol);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
