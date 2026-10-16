// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Swiss QR Code of the QR-bill, chapter 6 of the Swiss Implementation Guidelines for the QR-bill: a QR Code symbol at error correction level M in the
/// smallest version that holds the data, drawn 46 x 46 mm with a 7 x 7 mm Swiss cross over its centre.
/// <para>
/// The input is the payment data of chapter 4: at most 997 characters in 31 to 34 lines, the lines separated by CR LF or by LF, the same one throughout, with
/// the characters of section 4.1.1. Each line follows its definition in Table 8: the header SPC, 0200 and 1; a Swiss or Liechtenstein IBAN with valid check
/// digits; the structured address of the creditor; an empty ultimate creditor; the amount; the currency CHF or EUR; an empty or structured ultimate debtor; the
/// reference type and the reference; the unstructured message; the trailer EPD; and the optional billing information and alternative procedures. A QR reference
/// goes with a QR-IBAN and CHF, a Creditor Reference or no reference with an IBAN. The amount 0.00 carries a DO NOT USE FOR PAYMENT text of section 4.4 as its
/// message. The symbol carries the data in UTF-8.
/// </para>
/// <para>
/// <see cref="BarcodeOptions.XDimension"/> left <see langword="null"/> draws the symbol 46 mm wide. An X dimension the caller sets draws the modules at that
/// width, and the cross keeps its share of the symbol.
/// </para>
/// </summary>
public sealed class SwissQrCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SwissQrCodeSymbology"/> class.
    /// </summary>
    public SwissQrCodeSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, SwissQrBill.MaximumLength, nameof(text));
        SwissQrBill.Validate(text);

        // Section 4.1.1: the data is UTF-8. The validation leaves only characters of the Basic Multilingual Plane up to U+20AC, which take at most 3 bytes
        // each. Each byte goes to the encoder as the character of that value, which Byte mode writes as the byte.
        PooledBuffer<char> rented = new(3 * text.Length);
        try
        {
            Span<char> bytes = rented.Span;
            int written = 0;
            foreach (char c in text)
            {
                if (c < 0x80)
                {
                    bytes[written++] = c;
                }
                else if (c < 0x800)
                {
                    bytes[written++] = (char)(0xC0 | (c >> 6));
                    bytes[written++] = (char)(0x80 | (c & 0x3F));
                }
                else
                {
                    bytes[written++] = (char)(0xE0 | (c >> 12));
                    bytes[written++] = (char)(0x80 | ((c >> 6) & 0x3F));
                    bytes[written++] = (char)(0x80 | (c & 0x3F));
                }
            }

            // Sections 6.1 and 6.4: level M and the smallest version, drawn 46 mm wide with the 7 mm cross at the same scale.
            QrEncoder.BuildSymbol(bytes[..written], false, QrFormat.Full, QrErrorCorrectionLevel.M, 0, options.XDimension, SwissCross.SymbolWidth, ref symbol);
            symbol.AddOverlay((float)(SwissCross.Width / SwissCross.SymbolWidth * symbol.Columns), SwissCross.Side, SwissCross.DarkEdges);
        }
        finally
        {
            rented.Dispose();
        }
    }
}
