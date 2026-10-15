// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The country codes of ISO 3166-1 that barcode data carries.
/// </summary>
internal static class Iso3166
{
    /// <summary>
    /// Gets the ISO 3166-1 alpha-2 country codes as a bit field, code XY at bit index
    /// 26 x (X - 'A') + (Y - 'A'), bit n at bit 7 - (n mod 8) of byte n div 8. A bit past the end of the
    /// field is clear, so the trailing zero bytes are left out. The constant bytes compile into the assembly
    /// data, so reading them allocates nothing.
    /// </summary>
    private static ReadOnlySpan<byte> Alpha2Codes =>
    [
        0x1e, 0x9a, 0xfb, 0x77, 0xf7, 0xbd, 0xbb, 0x7b, 0xe4, 0xfc, 0x21, 0xa8, 0x01, 0x2b, 0x00, 0x70,
        0x00, 0x3a, 0x90, 0x0d, 0xf9, 0xdf, 0xa8, 0x00, 0xb1, 0x60, 0x18, 0x1e, 0xf0, 0x02, 0x02, 0xc0,
        0x00, 0xb8, 0xd4, 0x2f, 0x82, 0x81, 0xf2, 0xbf, 0x3f, 0xff, 0xeb, 0xa4, 0xd2, 0x10, 0x00, 0x80,
        0x02, 0x3c, 0xf1, 0xca, 0x80, 0x00, 0x00, 0x02, 0x00, 0x8a, 0x8f, 0xbf, 0xe7, 0x5c, 0xdd, 0xf9,
        0x59, 0x82, 0x08, 0x20, 0xea, 0xa1, 0x02, 0x00, 0x40, 0x02, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00,
        0x10, 0x20, 0x02, 0x00, 0x80,
    ];

    /// <summary>
    /// Returns whether the characters are an ISO 3166-1 alpha-2 country code: two capital letters that name
    /// an assigned code.
    /// </summary>
    /// <param name="code">The characters.</param>
    /// <returns><see langword="true"/> when they are a country code.</returns>
    public static bool IsAlpha2Code(ReadOnlySpan<char> code)
    {
        if (code.Length != 2 || !char.IsAsciiLetterUpper(code[0]) || !char.IsAsciiLetterUpper(code[1]))
        {
            return false;
        }

        int bit = (26 * (code[0] - 'A')) + (code[1] - 'A');
        ReadOnlySpan<byte> field = Alpha2Codes;
        return (uint)(bit >> 3) < (uint)field.Length && (field[bit >> 3] & (0x80 >> (bit & 7))) != 0;
    }
}
