// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 DataBar Truncated Composite symbology of ISO/IEC 24723: a GS1 DataBar Truncated linear component with its linkage flag set, a 1X
/// separator pattern, and a 4-column CC-A or CC-B 2D component above it. The first text is the element string of Application Identifier (01) that the
/// linear component carries, and the second the element strings of the 2D component.
/// </summary>
public sealed class Gs1DataBarTruncatedCompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of the 2D component, Table 1 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarTruncatedCompositeSymbology"/> class.
    /// </summary>
    public Gs1DataBarTruncatedCompositeSymbology()
        : base(new Gs1DataBarTruncatedSymbology(), new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcA, Columns))
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
        DataBarEncoder.BuildLinear(gtin, true, DataBarEncoder.TruncatedHeight, DataBarEncoder.TruncatedHeight, options, ref parts[0]);

        // Section 11.4: the separator is light over the four modules at each end and treats the two finder patterns apart. The left finder starts after the
        // guard pair and the first character of 16 modules. The right finder stands reversed, so its elements 1 to 3 start two modules into it, at module 65.
        Gs1CompositeLayout.AddSeparator(ref parts[0], 4, 4, [18, 65], true);
        Gs1CompositeLayout.BuildComponent(second, Gs1CompositeComponent.CcA, Columns, 0, parts[0].XDimension, options, ref parts[1]);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        // Clause 12 b): the stop bar of the 2D component stands over the last interior space of the linear component, the guard space before its last bar.
        int offset = (int)parts[0].WidthInModules - 2 - ((int)parts[1].WidthInModules - 1);
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], offset, options);
    }
}
