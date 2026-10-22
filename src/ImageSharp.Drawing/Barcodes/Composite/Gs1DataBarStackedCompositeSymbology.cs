// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 DataBar Stacked Composite symbology of ISO/IEC 24723: a GS1 DataBar Stacked linear component with its linkage flag set, a 1X separator pattern,
/// and a 2-column CC-A or CC-B 2D component above it. The first text is the element string of Application Identifier (01) that the linear component carries,
/// and the second the element strings of the 2D component.
/// </summary>
public sealed class Gs1DataBarStackedCompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of the 2D component, Table 1 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarStackedCompositeSymbology"/> class.
    /// </summary>
    public Gs1DataBarStackedCompositeSymbology()
        : base(new Gs1DataBarStackedSymbology(), new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcA, Columns))
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
        InlineArray14<char> gtinStorage = default;
        Span<char> gtin = gtinStorage;
        Gs1Data.ReadGtin(first, gtin);
        DataBarEncoder.BuildStacked(gtin, true, options, ref parts[0]);

        // Section 11.4: the separator stands above the upper row, light over the four modules at each end, and treats its finder pattern apart. The finder
        // starts after the guard pair and the first character of 16 modules.
        Gs1CompositeLayout.AddSeparator(ref parts[0], 4, 4, [18], true);
        Gs1CompositeLayout.BuildComponent(second, Gs1CompositeComponent.CcA, Columns, 0, parts[0].XDimension, options, ref parts[1]);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        // Clause 12 d): the first bar module of the 2D component stands over the first bar of the linear component, after its guard space.
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], 1, options);
    }
}
