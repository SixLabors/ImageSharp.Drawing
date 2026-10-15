// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A GS1 Digital Link URI in a Data Matrix symbol of a square size, section 5.9 of the GS1 General
/// Specifications. The symbol carries the URI as plain data. The URI follows the GS1 Digital Link Standard:
/// URI Syntax: a primary identification key and its key qualifiers in the path, data attributes in the
/// query string, and every Application Identifier with valid data.
/// <para>
/// When a font is set, the element strings that the URI carries print below the symbol, one on each line,
/// with the Application Identifiers in parentheses, in the order of the URI. The URI does not print.
/// </para>
/// </summary>
public sealed class Gs1DigitalLinkDataMatrixSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DigitalLinkDataMatrixSymbology"/> class. The symbol
    /// takes the smallest square size that holds the URI.
    /// </summary>
    public Gs1DigitalLinkDataMatrixSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        // The URI encodes as plain data, so no URI longer than the largest symbol holds is parsed.
        Guard.MustBeLessThanOrEqualTo(text.Length, DataMatrixEncoder.MaximumLength, nameof(text));
        if (options.Font is null)
        {
            Gs1DigitalLink.Validate(text);
            DataMatrixEncoder.BuildSymbol(text, false, DataMatrixFormat.Square, 0, 0, ref symbol);
            return;
        }

        // The element strings print as GS1 DataMatrix prints them, from the values the validation decodes.
        // The URI is bounded above, so the buffer is too, and a longer URI rents it once.
        InlineArray64<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = Gs1DigitalLink.ReadableCapacity(text.Length);
        Span<char> readable = capacity <= Gs1Data.InlineBufferLength
            ? ((Span<char>)readableStorage)[..capacity]
            : (rented = new PooledBuffer<char>(capacity)).Span;
        try
        {
            int written = Gs1DigitalLink.Validate(text, readable);
            DataMatrixEncoder.BuildSymbol(text, false, DataMatrixFormat.Square, 0, 0, ref symbol);
            DataMatrixEncoder.AddReadable(readable[..written], BarcodeTextAlignment.Left, Gs1Data.TextScale(options.Font), ref symbol);
        }
        finally
        {
            rented.Dispose();
        }
    }
}
