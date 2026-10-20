// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A GS1 Digital Link URI in a Data Matrix symbol of a square size, section 5.9 of the GS1 General Specifications. The symbol carries the URI as plain data.
/// The URI follows the GS1 Digital Link Standard: URI Syntax: a primary identification key and its key qualifiers in the path, data attributes in the query
/// string, and every Application Identifier with valid data.
/// <para>
/// When a font is set, the element strings that the URI carries print below the symbol, one on each line, with the Application Identifiers in parentheses, in
/// the order of the URI. The URI does not print.
/// </para>
/// <para>
/// Table 5-46, symbol specification table 1 addendum 2, gives an X dimension of 0.396 to 0.990 millimetres with a target of 0.495, and an X dimension outside
/// that range is clamped to it.
/// </para>
/// </summary>
public sealed class Gs1DigitalLinkDataMatrixSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DigitalLinkDataMatrixSymbology"/> class. The symbol takes the smallest square size that holds the URI.
    /// </summary>
    public Gs1DigitalLinkDataMatrixSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // The URI encodes as plain data, so no URI longer than the largest symbol holds is parsed.
        Guard.MustBeLessThanOrEqualTo(text.Length, DataMatrixEncoder.MaximumLength, nameof(text));
        double xDimension = options.ResolveXDimension(
            Gs1Data.DigitalLinkMinimumXDimension,
            Gs1Data.DigitalLinkXDimension,
            Gs1Data.DigitalLinkMaximumXDimension);

        if (options.Font is null)
        {
            Gs1DigitalLink.Validate(text);
            DataMatrixEncoder.BuildSymbol(text, false, DataMatrixFormat.Square, 0, 0, xDimension, ref symbol);
            return;
        }

        // The element strings print as GS1 DataMatrix prints them, from the values the validation decodes. The URI is bounded above, so the buffer is too, and
        // a longer URI rents it once.
        InlineArray64<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = Gs1DigitalLink.ReadableCapacity(text.Length);
        Span<char> readable = capacity <= Gs1Data.InlineBufferLength
            ? ((Span<char>)readableStorage)[..capacity]
            : (rented = new PooledBuffer<char>(capacity)).Span;
        try
        {
            int written = Gs1DigitalLink.Validate(text, readable);
            DataMatrixEncoder.BuildSymbol(text, false, DataMatrixFormat.Square, 0, 0, xDimension, ref symbol);
            symbol.AddMatrixText(readable[..written], BarcodeTextAlignment.Left, Gs1Data.TextScale(options.Font));
        }
        finally
        {
            rented.Dispose();
        }
    }
}
