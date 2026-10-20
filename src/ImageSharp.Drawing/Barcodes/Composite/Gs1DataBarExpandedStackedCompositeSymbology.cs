// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 DataBar Expanded Stacked Composite symbology of ISO/IEC 24723: a GS1 DataBar Expanded Stacked linear component with its linkage bit set, a 1X
/// separator pattern, and a 4-column CC-A or CC-B 2D component above it. The first text is the element strings the linear component carries, and the second
/// those of the 2D component. Section 7.3 requires at least four symbol characters in the top row of the linear component.
/// </summary>
public sealed class Gs1DataBarExpandedStackedCompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of the 2D component, Table 1 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 4;

    /// <summary>
    /// The fewest segments in a row of the linear component. Section 7.3: "at least four symbol characters in its top row".
    /// </summary>
    private const int MinimumSegments = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarExpandedStackedCompositeSymbology"/> class with four segments in a row.
    /// </summary>
    public Gs1DataBarExpandedStackedCompositeSymbology()
        : this(MinimumSegments)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarExpandedStackedCompositeSymbology"/> class.
    /// </summary>
    /// <param name="segments">The number of segments in a full row of the linear component, an even number from 4 to 20. A segment is one symbol
    /// character.</param>
    public Gs1DataBarExpandedStackedCompositeSymbology(int segments)
        : base(new Gs1DataBarExpandedStackedSymbology(segments), new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcA, Columns))
    {
        Guard.MustBeBetweenOrEqualTo(segments, MinimumSegments, DataBarExpandedEncoder.MaximumStackedSegments, nameof(segments));
        this.Segments = segments;
    }

    /// <summary>
    /// Gets the number of segments in a full row of the linear component. The last row can hold fewer.
    /// </summary>
    public int Segments { get; }

    /// <inheritdoc/>
    internal override void EncodeParts(
        Span<BarcodeSymbol> parts,
        BarcodeOptions options,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
    {
        Gs1CompositeLayout.Validate(first, second);
        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(first, DataBarExpandedEncoder.MaximumDataCharacters);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Encode(first, ref encoded);
            DataBarExpandedEncoder.ValidateGtinFirst(first);

            // The interpretation is never longer than the text, which PreparedCapacity bounds to 122 characters, so the inline buffer always holds it.
            InlineArray128<char> readableStorage = default;
            Span<char> readable = readableStorage;
            int readableLength = options.Font is null ? 0 : Gs1Data.WriteReadable(first, false, readable);
            DataBarExpandedEncoder.BuildStacked(
                encoded.AsSpan(),
                this.Segments,
                true,
                [],
                readable[..readableLength],
                [],
                BarcodeTextSide.BelowBars,
                DataBarEncoder.MinimumXDimension,
                DataBarEncoder.MaximumXDimension,
                options,
                ref parts[0]);
        }
        finally
        {
            encoded.Dispose();
        }

        // The separator stands above the top row, which reads left to right like a GS1 DataBar Expanded symbol.
        Gs1CompositeLayout.AddExpandedSeparator(ref parts[0]);
        Gs1CompositeLayout.BuildComponent(second, Gs1CompositeComponent.CcA, Columns, 0, parts[0].XDimension, options, ref parts[1]);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        // Clause 12 e): the first bar module of the 2D component stands over the first space of the first symbol character of the top row, after its guard
        // space and bar.
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], 2, options);
    }
}
