// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Expanded Stacked, GS1 DataBar Expanded in rows for a symbol area or a print mechanism that is
/// not wide enough for a single row. Section 5.5.2.3.2 of the GS1 General Specifications: "It can be printed
/// in widths of 2 to 20 segments and can have from 2 to 11 rows. Its structure includes a 3X high separator
/// pattern between rows." Each row is 34X high, and the bar height of the options sets each row. No quiet
/// zone is required.
/// <para>
/// The input is the element string syntax the standard prints, each Application Identifier in parentheses
/// followed by its data. When the first element string is Application Identifier (01), its check digit must
/// be correct. The human readable interpretation is the input.
/// </para>
/// </summary>
public sealed class Gs1DataBarExpandedStackedSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarExpandedStackedSymbology"/> class with four
    /// segments in a row.
    /// </summary>
    public Gs1DataBarExpandedStackedSymbology()
        : this(4)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarExpandedStackedSymbology"/> class.
    /// </summary>
    /// <param name="segments">The number of segments in a full row, an even number from 2 to 20. A segment is one symbol character.</param>
    public Gs1DataBarExpandedStackedSymbology(int segments)
    {
        Guard.MustBeBetweenOrEqualTo(segments, DataBarExpandedEncoder.MinimumSegments, DataBarExpandedEncoder.MaximumStackedSegments, nameof(segments));
        if ((segments & 1) == 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segments), segments, "The number of segments in a row is even.");
        }

        this.Segments = segments;
    }

    /// <summary>
    /// Gets the number of segments in a full row. The last row may hold fewer.
    /// </summary>
    public int Segments { get; }

    /// <inheritdoc/>
    public override float NominalXDimension => DataBarExpandedEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        Span<char> buffer = stackalloc char[Gs1Data.StackBufferLength];
        ValueStringBuilder encoded = new(buffer);
        try
        {
            Gs1Data.Prepare(text, ref encoded);
            return DataBarExpandedEncoder.BuildStacked(encoded.AsSpan(), this.Segments, text, BarcodeTextSide.BelowBars, options);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
