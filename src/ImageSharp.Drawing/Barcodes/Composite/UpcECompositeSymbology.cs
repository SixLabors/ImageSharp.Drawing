// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The UPC-E Composite symbology of ISO/IEC 24723: a UPC-E linear component, a 6X separator pattern, and a 2-column CC-A or CC-B 2D component above it.
/// The first text is the digits of the UPC-E symbol, and the second the element strings of the 2D component.
/// </summary>
public sealed class UpcECompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of the 2D component, Table 1 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcECompositeSymbology"/> class.
    /// </summary>
    public UpcECompositeSymbology()
        : base(new UpcESymbology(), new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcA, Columns))
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
        InlineArray16<char> gtinStorage = default;
        ReadOnlySpan<char> gtin = UpcESymbology.ExpandToGtin12(first, gtinStorage);
        Gs1CompositeLayout.EncodeEanUpc(this.Symbologies[0], Columns, parts, options, first, second, gtin);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        int offset = Gs1CompositeLayout.EanUpcOffset((int)parts[0].WidthInModules, (int)parts[1].WidthInModules);
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], offset, options);
    }
}
