// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
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
/// The symbol is always 46 mm wide. Section 6.4: "The measurements of the Swiss QR Code for printing must always be 46 x 46 mm (without surrounding quiet
/// space) regardless of the Swiss QR Code version." So <see cref="BarcodeOptions.XDimension"/> must be <see langword="null"/>.
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
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // Section 6.4: the symbol "must always be 46 x 46 mm", so its X dimension follows from its version.
        if (options.XDimension is not null)
        {
            throw new ArgumentException("A Swiss QR Code is always 46 x 46 mm, so its X dimension is not set.", nameof(options));
        }

        Guard.MustBeLessThanOrEqualTo(text.Length, SwissQrBill.MaximumLength, nameof(text));
        SwissQrBill.Validate(text);

        // Section 4.1.1: the data is UTF-8. The validation leaves only characters of the Basic Multilingual Plane up to U+20AC, which take at most 3 bytes
        // each. Each byte goes to the encoder as the character of that value, which Byte mode writes as the byte. One rent holds the characters and, after
        // them, the bytes.
        int capacity = 3 * text.Length;
        PooledBuffer<char> rented = new(capacity + ((capacity + 1) / 2));
        try
        {
            Span<char> characters = rented.Span[..capacity];
            Span<byte> bytes = MemoryMarshal.AsBytes(rented.Span[capacity..])[..capacity];
            _ = Utf8.FromUtf16(text, bytes, out _, out int written);
            int count = Encoding.Latin1.GetChars(bytes[..written], characters);

            // Sections 6.1 and 6.4: level M and the smallest version, drawn 46 mm wide with the 7 mm cross at the same scale.
            QrEncoder.BuildSymbol(characters[..count], false, QrFormat.Full, QrErrorCorrectionLevel.M, 0, null, SwissCross.SymbolWidth, ref symbol);
            symbol.AddOverlay((float)(SwissCross.Width / SwissCross.SymbolWidth * symbol.Columns), SwissCross.Side, SwissCross.DarkEdges);
        }
        finally
        {
            rented.Dispose();
        }
    }
}
