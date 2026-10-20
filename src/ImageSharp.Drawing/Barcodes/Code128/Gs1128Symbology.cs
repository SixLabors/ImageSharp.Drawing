// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1-128 symbology of section 5.4 of the GS1 General Specifications. A GS1-128 symbol is a Code 128 symbol with a Function 1 character after its start
/// character. It carries one or more element strings. Each element string is a GS1 Application Identifier and the data that belongs to it.
/// <para>
/// The input is the element string syntax the standard prints: an Application Identifier in parentheses, then its data, repeated. For example,
/// <c>(01)09521234543213(3103)000123</c>. The symbol does not carry the parentheses. Section 4.14 rule 2c requires them in the human readable interpretation,
/// and rule 2b keeps the separators out of it. The human readable interpretation prints the element strings on one line, except an element string of
/// Application Identifier (8200), which does not print.
/// </para>
/// <para>
/// The symbol specification tables of section 5.12.3 give GS1-128 X dimensions from 0.170 to 1.016 millimetres, and an X dimension outside that range is
/// clamped to it.
/// </para>
/// </summary>
public sealed class Gs1128Symbology : BarcodeSymbology
{
    /// <summary>
    /// The largest number of data characters in one symbol. Section 5.4.4.3: "The number of encoded data characters SHALL NOT exceed 48."
    /// </summary>
    private const int MaximumDataCharacters = 48;

    /// <summary>
    /// The largest length of a symbol in millimetres. Section 5.4.4.3: "The length, including Quiet Zones, cannot exceed 165.10 millimetres (6.500 inches)."
    /// </summary>
    internal const double MaximumLength = 165.10;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1128Symbology"/> class.
    /// </summary>
    public Gs1128Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(text, MaximumDataCharacters);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Prepare(text, ref encoded);
            Guard.MustBeLessThanOrEqualTo(encoded.Length, MaximumDataCharacters, nameof(text));

            // The interpretation is never longer than the text, which PreparedCapacity bounds to 80 characters, so the inline buffer always holds it.
            InlineArray128<char> readableStorage = default;
            Span<char> readable = readableStorage;
            int readableLength = options.Font is null ? 0 : Gs1Data.WriteReadable(text, false, readable);
            Code128Encoder.BuildSymbol(
                encoded.AsSpan(),
                true,
                [],
                readable[..readableLength],
                [],
                Gs1Data.Gs1128MinimumXDimension,
                Gs1Data.Gs1128MaximumXDimension,
                Code128Linkage.None,
                options,
                ref symbol);

            double length = (symbol.LeadingQuietZone + symbol.WidthInModules + symbol.TrailingQuietZone) * symbol.XDimension;
            if (length > MaximumLength)
            {
                throw new ArgumentException(
                    $"A GS1-128 symbol with its quiet zones is at most {MaximumLength} mm long; this one is {length:0.##} mm at its X dimension.",
                    nameof(text));
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
