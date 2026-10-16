// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 DotCode symbology: DotCode carrying GS1 element strings, section 5.8 of the GS1 General Specifications. The text is the element strings with their
/// Application Identifiers in parentheses, for example <c>(01)09521234543213(10)ABC123</c>. The human readable interpretation is drawn below the symbol when
/// <see cref="BarcodeOptions.Font"/> is set.
/// </summary>
public sealed class Gs1DotCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DotCodeSymbology"/> class. The symbol takes the smallest size near a width to height ratio of 3:2 that
    /// holds the data.
    /// </summary>
    public Gs1DotCodeSymbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DotCodeSymbology"/> class with a fixed number of columns. The symbol takes the fewest rows that hold the
    /// data.
    /// </summary>
    /// <param name="columns">The number of columns, 5 to 200.</param>
    public Gs1DotCodeSymbology(int columns)
    {
        Guard.MustBeBetweenOrEqualTo(columns, DotCodeEncoder.MinimumSide, DotCodeEncoder.MaximumSide, nameof(columns));
        this.Columns = columns;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DotCodeSymbology"/> class with a fixed size.
    /// </summary>
    /// <param name="columns">The number of columns, 5 to 200.</param>
    /// <param name="rows">The number of rows, 5 to 200. The columns and the rows sum to an odd number.</param>
    public Gs1DotCodeSymbology(int columns, int rows)
        : this(columns)
    {
        Guard.MustBeBetweenOrEqualTo(rows, DotCodeEncoder.MinimumSide, DotCodeEncoder.MaximumSide, nameof(rows));
        if (((columns + rows) & 1) == 0)
        {
            throw new ArgumentException("The columns and the rows of a DotCode symbol must sum to an odd number.", nameof(rows));
        }

        this.Rows = rows;
    }

    /// <summary>
    /// Gets the number of columns, or <see langword="null"/> when the size follows from the data.
    /// </summary>
    public int? Columns { get; }

    /// <summary>
    /// Gets the number of rows, or <see langword="null"/> when the rows follow from the data.
    /// </summary>
    public int? Rows { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        // The message is an FNC1 and the prepared element strings, so it holds one less than the encoder's maximum.
        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(text, DotCodeEncoder.MaximumLength - 1);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Prepare(text, ref encoded);
            DotCodeEncoder.BuildSymbol(encoded.AsSpan(), true, this.Columns ?? 0, this.Rows ?? 0, options.XDimension ?? PointXDimension, ref symbol);

            if (options.Font is not null)
            {
                Gs1Data.AddMatrixReadable(text, options.Font, ref symbol);
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
