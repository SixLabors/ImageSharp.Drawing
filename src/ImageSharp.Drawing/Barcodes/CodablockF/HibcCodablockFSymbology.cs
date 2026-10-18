// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC Codablock F, a Health Industry Bar Code carried by a Codablock F symbol. The symbol carries the flag character, the data and the modulo 43 check
/// character, and prints that same string below the symbol when a font is set.
/// </summary>
public sealed class HibcCodablockFSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HibcCodablockFSymbology"/> class with eight data columns, calculating the check character from the data.
    /// </summary>
    public HibcCodablockFSymbology()
        : this(CodablockFEncoder.DefaultColumns, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcCodablockFSymbology"/> class.
    /// </summary>
    /// <param name="columns">The number of data columns in a row, 4 to 62. The row count follows from the data.</param>
    /// <param name="validateCheckCharacter">Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to.</param>
    public HibcCodablockFSymbology(int columns, bool validateCheckCharacter)
    {
        Guard.MustBeBetweenOrEqualTo(columns, CodablockFEncoder.MinimumColumns, CodablockFEncoder.MaximumColumns, nameof(columns));

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

            ReadOnlySpan<char> data = encoded.AsSpan();
            CodablockFEncoder.BuildSymbol(
                data,
                this.Columns,
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
