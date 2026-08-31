// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1-128 symbology of section 5.4 of the GS1 General Specifications. A GS1-128 symbol is a Code 128
/// symbol whose start character is followed by a Function 1 character, carrying one or more element
/// strings: a GS1 Application Identifier and the data that belongs to it.
/// <para>
/// Input is the element string syntax the standard prints, an Application Identifier in parentheses
/// followed by its data, repeated: <c>(01)09521234543213(3103)000123</c>. Parentheses are not encoded;
/// section 4.14 rule 2c requires them in the human readable interpretation and rule 2b keeps the
/// separators out of it.
/// </para>
/// </summary>
public sealed class Gs1128Symbology : BarcodeSymbology
{
    /// <summary>
    /// The largest number of data characters section 5.4.1 allows in one symbol.
    /// </summary>
    private const int MaximumDataCharacters = 48;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1128Symbology"/> class.
    /// </summary>
    public Gs1128Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        Span<char> buffer = stackalloc char[Gs1Data.StackBufferLength];
        ValueStringBuilder encoded = new(buffer);
        try
        {
            Gs1Data.Prepare(text, ref encoded);
            Guard.MustBeLessThanOrEqualTo(encoded.Length, MaximumDataCharacters, nameof(text));

            // The human readable interpretation is the input itself: the parse consumes a parenthesis, an
            // Application Identifier or data, and nothing else, so re-emitting them in order rebuilds it.
            return Code128Encoder.BuildSymbol(
                Code128Encoder.Encode(encoded.AsSpan(), true),
                text,
                options);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
