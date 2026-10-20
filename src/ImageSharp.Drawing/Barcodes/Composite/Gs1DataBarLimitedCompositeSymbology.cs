// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 DataBar Limited Composite symbology of ISO/IEC 24723: a GS1 DataBar Limited linear component with its linkage flag set, a 1X separator pattern,
/// and a 3-column CC-A or CC-B 2D component above it. The first text is the element string of Application Identifier (01) that the linear component carries,
/// and the second the element strings of the 2D component.
/// </summary>
public sealed class Gs1DataBarLimitedCompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of the 2D component, Table 1 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 3;

    /// <summary>
    /// The light modules at the end of the separator: the four at the right end of the bars and the five module space after them.
    /// </summary>
    private const int LightEnd = 9;

    /// <summary>
    /// The last interior space of the linear component: the guard space before its last bar, 72 modules from its start.
    /// </summary>
    private const int LastInteriorSpace = 72;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarLimitedCompositeSymbology"/> class.
    /// </summary>
    public Gs1DataBarLimitedCompositeSymbology()
        : base(new Gs1DataBarLimitedSymbology(), new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcA, Columns))
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
        if (gtin[0] is not ('0' or '1'))
        {
            throw new ArgumentException($"GS1 DataBar Limited carries a GTIN whose first digit is 0 or 1; got {gtin[0]}.", nameof(first));
        }

        DataBarLimitedEncoder.Build(gtin, true, options, ref parts[0]);

        // Section 11.4: the separator is the complement of the linear component, light over the four modules at each end of the symbol.
        Gs1CompositeLayout.AddSeparator(ref parts[0], 4, LightEnd, [], false);
        Gs1CompositeLayout.BuildComponent(second, Gs1CompositeComponent.CcA, Columns, 0, parts[0].XDimension, options, ref parts[1]);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        // Clause 12 c): the stop bar of the 2D component stands over the last interior space of the linear component.
        int offset = LastInteriorSpace - ((int)parts[1].WidthInModules - 1);
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], offset, options);
    }
}
