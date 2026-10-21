// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1-128 Composite symbology of ISO/IEC 24723: a GS1-128 linear component with its linkage character, a 1X separator pattern, and a 2D component above
/// it. The first text is the element strings the linear component carries, and the second those of the 2D component.
/// <para>
/// Section 7.4 takes a 4-column CC-A or CC-B, or a CC-C of any columns. CC-A takes CC-B when the data exceeds it, and CC-B takes CC-C when the data exceeds
/// it. CC-C takes the most columns whose rows fit over the linear component and its quiet zones, and more when the data needs them.
/// </para>
/// </summary>
public sealed class Gs1128CompositeSymbology : CompositeBarcodeSymbology
{
    /// <summary>
    /// The data columns of CC-A and CC-B, section 7.4 of ISO/IEC 24723.
    /// </summary>
    private const int Columns = 4;

    /// <summary>
    /// The largest number of data characters in the linear component. Section 5.4.4.3 of the GS1 General Specifications: "The number of encoded data
    /// characters SHALL NOT exceed 48."
    /// </summary>
    private const int MaximumDataCharacters = 48;

    /// <summary>
    /// The CC-C columns over the linear component: a CC-C row of c columns is 17c + 69 modules, from its start pattern to its stop pattern. Clause 12 f) puts
    /// it 7 modules left of the linear component, so it fits over the linear component and its trailing quiet zone of 10 modules when 17c + 62 is at most the
    /// width of the linear component plus 10.
    /// </summary>
    private const int CcCOverhang = 52;

    /// <summary>
    /// The modules of a CC-C data column.
    /// </summary>
    private const int CcCColumnModules = 17;

    /// <summary>
    /// The offset of a CC-C from the linear component. Clause 12 f): the first interior space of the CC-C, the ninth module of its start pattern, stands over
    /// the second module of the start character of the linear component.
    /// </summary>
    private const int CcCOffset = -7;

    /// <summary>
    /// The width in modules of a 4-column CC-A or CC-B. A CC-C of c columns is 17c + 69 modules wide, which is never this width.
    /// </summary>
    private const int CcAOrCcBWidth = 99;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1128CompositeSymbology"/> class with a CC-A 2D component, which grows to CC-B and to CC-C as the data
    /// needs.
    /// </summary>
    public Gs1128CompositeSymbology()
        : this(Gs1CompositeComponent.CcA)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1128CompositeSymbology"/> class.
    /// </summary>
    /// <param name="component">The smallest 2D component the symbol takes.</param>
    public Gs1128CompositeSymbology(Gs1CompositeComponent component)
        : base(new Gs1128Symbology(), new Gs1CompositeComponentSymbology(component, Columns))
        => this.Component = component;

    /// <summary>
    /// Gets the smallest 2D component the symbol takes.
    /// </summary>
    public Gs1CompositeComponent Component { get; }

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
        int capacity = Gs1Data.PreparedCapacity(first, MaximumDataCharacters);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Encode(first, ref encoded);
            Guard.MustBeLessThanOrEqualTo(encoded.Length, MaximumDataCharacters, nameof(first));

            // The interpretation is never longer than the text, which PreparedCapacity bounds to 80 characters, so the inline buffer always holds it.
            InlineArray128<char> readableStorage = default;
            Span<char> readable = readableStorage;
            int readableLength = options.Font is null ? 0 : Gs1Data.WriteReadable(first, false, readable);

            // The linkage character of CC-A and CC-B and that of CC-C are each one symbol character, so the width of the linear component, which sets the
            // columns of CC-C, is the same for both. A symbol that takes CC-C encodes the linear component again with its linkage character.
            Code128Linkage linkage = this.Component == Gs1CompositeComponent.CcC ? Code128Linkage.CcC : Code128Linkage.CcAOrCcB;
            BuildLinear(encoded.AsSpan(), readable[..readableLength], linkage, options, ref parts[0]);
            int ccCColumns = Math.Max(Gs1CompositeComponentEncoder.MinimumCcCColumns, ((int)parts[0].WidthInModules - CcCOverhang) / CcCColumnModules);
            int columns = this.Component == Gs1CompositeComponent.CcC ? ccCColumns : Columns;
            double xDimension = parts[0].XDimension;
            Gs1CompositeComponent used = Gs1CompositeLayout.BuildComponent(second, this.Component, columns, ccCColumns, xDimension, options, ref parts[1]);
            if (used == Gs1CompositeComponent.CcC && linkage != Code128Linkage.CcC)
            {
                parts[0].Dispose();
                parts[0] = default;
                BuildLinear(encoded.AsSpan(), readable[..readableLength], Code128Linkage.CcC, options, ref parts[0]);
            }

            if (used != Gs1CompositeComponent.CcC)
            {
                Gs1CompositeLayout.ValidateGs1128Length((int)parts[0].WidthInModules);
            }

            BarcodeSymbol linear = parts[0];
            double length = (linear.LeadingQuietZone + linear.WidthInModules + linear.TrailingQuietZone) * linear.XDimension;
            if (length > Gs1128Symbology.MaximumLength)
            {
                throw new ArgumentException(
                    $"A GS1-128 symbol with its quiet zones is at most {Gs1128Symbology.MaximumLength} mm long; this one is {length:0.##} mm at its X dimension.",
                    nameof(first));
            }
        }
        finally
        {
            encoded.Dispose();
        }

        // Section 11.4: the separator is the complement of the linear component.
        Gs1CompositeLayout.AddSeparator(ref parts[0], 0, 0, [], false);
    }

    /// <inheritdoc/>
    internal override PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options)
    {
        // The width of the 2D component tells CC-A or CC-B from CC-C.
        int componentWidth = (int)parts[1].WidthInModules;
        int offset = componentWidth == CcAOrCcBWidth ? Gs1CompositeLayout.Gs1128Offset((int)parts[0].WidthInModules, componentWidth) : CcCOffset;
        return Gs1CompositeLayout.PlaceAbove(parts, symbolOrigins[0], offset, options);
    }

    /// <summary>
    /// Builds the linear component, as <see cref="Gs1128Symbology"/> builds it, with a linkage character.
    /// </summary>
    /// <param name="encoded">The prepared element strings.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="linkage">The linkage character.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildLinear(
        ReadOnlySpan<char> encoded,
        ReadOnlySpan<char> readable,
        Code128Linkage linkage,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
        => Code128Encoder.BuildSymbol(
            encoded,
            true,
            [],
            readable,
            [],
            Gs1Data.Gs1128MinimumXDimension,
            Gs1Data.Gs1128MaximumXDimension,
            linkage,
            options,
            ref symbol);
}
