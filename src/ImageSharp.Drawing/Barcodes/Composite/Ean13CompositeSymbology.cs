// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-13 Composite symbology of ISO/IEC 24723: an EAN-13 linear component, a 6X separator pattern, and a 4-column CC-A or CC-B 2D component above it.
/// The first text is the digits of the EAN-13 symbol, and the second the element strings of the 2D component.
/// </summary>
public sealed class Ean13CompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of the 2D component, Table 1 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ean13CompositeSymbology"/> class.
    /// </summary>
    public Ean13CompositeSymbology()
        : base(new Ean13Symbology(), new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcA, Columns))
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
        ReadOnlySpan<char> gtin = EanUpcEncoder.ValidateAndApplyCheckDigit(first, 12, gtinStorage);
        Gs1CompositeLayout.EncodeEanUpc(this.Symbologies[0], Columns, parts, options, first, second, gtin);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        int offset = Gs1CompositeLayout.EanUpcOffset((int)parts[0].WidthInModules, (int)parts[1].WidthInModules);
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], offset, options);
    }
}
