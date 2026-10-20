// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The 2D component of a GS1 Composite symbol, ISO/IEC 24723, drawn alone. A composite symbology such as <see cref="Ean13CompositeSymbology"/> draws it above
/// its linear component.
/// <para>
/// The input is the element string syntax the standard prints, each Application Identifier in parentheses followed by its data. The symbol takes the
/// smallest size of its 2D component and columns that holds the data. A CC-A symbol whose data exceeds CC-A takes CC-B of the same columns. The rows are 2X
/// high in CC-A and CC-B and 3X high in CC-C, and the bar height of the options does not change them. The human readable interpretation prints each element
/// string on a line of its own below the symbol.
/// </para>
/// </summary>
public sealed class Gs1CompositeComponentSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1CompositeComponentSymbology"/> class: CC-A, or CC-B when the data exceeds CC-A, of two columns.
    /// </summary>
    public Gs1CompositeComponentSymbology()
        : this(Gs1CompositeComponent.CcA, Gs1CompositeComponentEncoder.MinimumColumns)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1CompositeComponentSymbology"/> class.
    /// </summary>
    /// <param name="component">The 2D component. CC-A takes CC-B of the same columns when the data exceeds CC-A.</param>
    /// <param name="columns">The number of data columns: 2 to 4 for CC-A and CC-B, 1 to 30 for CC-C. A CC-C takes more columns when its rows would
    /// exceed 30, and fewer when its columns, with the start and stop patterns and the two row indicators, would reach four times its rows.</param>
    public Gs1CompositeComponentSymbology(Gs1CompositeComponent component, int columns)
    {
        Guard.MustBeBetweenOrEqualTo((int)component, (int)Gs1CompositeComponent.CcA, (int)Gs1CompositeComponent.CcC, nameof(component));
        if (component == Gs1CompositeComponent.CcC)
        {
            Guard.MustBeBetweenOrEqualTo(
                columns,
                Gs1CompositeComponentEncoder.MinimumCcCColumns,
                Gs1CompositeComponentEncoder.MaximumCcCColumns,
                nameof(columns));
        }
        else
        {
            Guard.MustBeBetweenOrEqualTo(columns, Gs1CompositeComponentEncoder.MinimumColumns, Gs1CompositeComponentEncoder.MaximumColumns, nameof(columns));
        }

        this.Component = component;
        this.Columns = columns;
    }

    /// <summary>
    /// Gets the 2D component.
    /// </summary>
    public Gs1CompositeComponent Component { get; }

    /// <summary>
    /// Gets the number of data columns.
    /// </summary>
    public int Columns { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        double xDimension = options.ResolveXDimension(DefaultXDimension);
        _ = Gs1CompositeComponentEncoder.Build(text, this.Component, this.Columns, 0, xDimension, ref symbol);
        if (options.Font is null)
        {
            return;
        }

        // The interpretation is never longer than ReadableCapacity, so short text works inline and longer text rents once.
        InlineArray128<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = Gs1Data.ReadableCapacity(text.Length);
        Span<char> readable = capacity <= 128 ? ((Span<char>)readableStorage)[..capacity] : (rented = new PooledBuffer<char>(capacity)).Span;
        try
        {
            int written = Gs1Data.WriteReadable(text, true, readable);
            symbol.AddStackedText(readable[..written], BarcodeTextAlignment.Left, Gs1Data.TextScale(options.Font));
        }
        finally
        {
            rented.Dispose();
        }
    }
}
