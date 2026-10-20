// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 DataBar Expanded Composite symbology of ISO/IEC 24723: a GS1 DataBar Expanded linear component with its linkage bit set, a 1X separator pattern,
/// and a 4-column CC-A or CC-B 2D component above it. The first text is the element strings the linear component carries, and the second those of the 2D
/// component.
/// </summary>
public sealed class Gs1DataBarExpandedCompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of the 2D component, Table 1 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarExpandedCompositeSymbology"/> class.
    /// </summary>
    public Gs1DataBarExpandedCompositeSymbology()
        : base(new Gs1DataBarExpandedSymbology(), new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcA, Columns))
    {
    }

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
            Gs1Data.Prepare(first, ref encoded);
            DataBarExpandedEncoder.ValidateGtinFirst(first);

            // The interpretation is never longer than the text, which PreparedCapacity bounds to 122 characters, so the inline buffer always holds it.
            InlineArray128<char> readableStorage = default;
            Span<char> readable = readableStorage;
            int readableLength = options.Font is null ? 0 : Gs1Data.WriteReadable(first, false, readable);
            DataBarExpandedEncoder.BuildLinear(encoded.AsSpan(), readable[..readableLength], true, options, ref parts[0]);
        }
        finally
        {
            encoded.Dispose();
        }

        BarcodeRow row = parts[0].Rows[0];
        InlineArray16<int> finderStorage = default;
        Span<int> finders = finderStorage;
        int finderCount = Gs1CompositeLayout.ExpandedFinderStarts(BarcodeSymbol.RunWidth(parts[0].Runs.Slice(row.RunStart, row.RunCount)), finders);
        Gs1CompositeLayout.AddSeparator(ref parts[0], 4, 4, finders[..finderCount], false);
        Gs1CompositeLayout.BuildComponent(second, Gs1CompositeComponent.CcA, Columns, 0, parts[0].XDimension, options, ref parts[1]);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        // Clause 12 e): the first bar module of the 2D component stands over the first space of the first symbol character of the linear component, after
        // its guard space and bar.
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], 2, options);
    }
}
