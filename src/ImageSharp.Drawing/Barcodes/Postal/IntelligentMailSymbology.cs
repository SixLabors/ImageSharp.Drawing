// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The United States Postal Service Intelligent Mail barcode, which USPS-B-3200 revision H defines. The text is a 20-digit tracking code and then a routing
/// code of 0, 5, 9 or 11 digits. The symbol thus carries 20, 25, 29 or 31 digits. Section 2.1.3: the second digit of the barcode identifier "shall be
/// constrained to the range of 0-4". Table 31 gives the postal barcode identifier 94 a "Fixed STID value of 009". The symbol is 65 bars. Section 2.4.3 gives
/// the printed line: the fields of the tracking code, then the 5-digit ZIP Code, the 4-digit add-on and the remaining 2 digits of the routing code. The fields
/// are "separated with a space added between data fields". Section 2.4.4: the line prints in "a minimum of 10- to 12-point size type". A smaller font thus
/// prints at 10 points.
/// </summary>
public sealed class IntelligentMailSymbology : BarcodeSymbology
{
    /// <summary>
    /// The barcode identifier of the postal barcode, the pIMb of section 11.3.2.1: "A USPS-applied pIMb is identified by the Barcode ID value of 94".
    /// </summary>
    private const string PostalIdentifier = "94";

    /// <summary>
    /// The service type identifier of the postal barcode. Table 31: "Fixed STID value of 009".
    /// </summary>
    private const string PostalServiceType = "009";

    /// <summary>
    /// The MPE type of the CFS/RFS construct of Table 33: "Coded 5 = CFS/RFS".
    /// </summary>
    private const char ForwardingMpeType = '5';

    /// <summary>
    /// The smallest size of the text line in points. Section 2.4.4: "a minimum of 10- to 12-point size type".
    /// </summary>
    private const float MinimumTextSize = 10F;

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length is not (20 or 25 or 29 or 31))
        {
            throw new ArgumentException(
                $"The Intelligent Mail barcode carries a 20-digit tracking code and a routing code of 0, 5, 9 or 11 digits; got {text.Length} characters.",
                nameof(text));
        }

        BarcodeSymbology.ValidateDigits(text);
        if (text[1] > '4')
        {
            throw new ArgumentException($"The second digit of the barcode identifier is 0 to 4; got {text[1]}.", nameof(text));
        }

        if (text.StartsWith(PostalIdentifier) && !text[2..5].SequenceEqual(PostalServiceType))
        {
            throw new ArgumentException($"The barcode identifier 94 carries the service type identifier 009; got {text[2..5]}.", nameof(text));
        }

        InlineArray65<FourState> stateStorage = default;
        Span<FourState> states = stateStorage;
        IntelligentMailEncoder.Encode(text, states);

        // The 31 digits and at most nine spaces between their groups: six between seven tracking code fields and three before the routing code fields.
        InlineArray64<char> lineStorage = default;
        Span<char> line = lineStorage;
        int lineLength = options.Font is null ? 0 : HumanReadable(text, line);
        FourStateEncoder.BuildSymbol(states, IntelligentMailEncoder.Metrics, line[..lineLength], options, ref symbol);
        symbol.SetTextSize(MinimumTextSize, 0F);
    }

    /// <summary>
    /// Builds the line of section 2.4.3. The fields of the tracking code follow the layouts of Appendix F. Tables 26 and 27: the barcode identifier, the
    /// service type identifier, the mailer ID and the serial number, where section 2.1.3 C gives the 9-digit mailer ID the range 900000000 to 999999999, so a
    /// mailer ID that starts with 9 is 9 digits and the serial number 6, and otherwise 6 and 9. Table 32: the barcode identifier 94 carries the service type
    /// identifier, the MPE type, the MPE number of 4 digits, the Julian date of 3, the time of 2 and the mailpiece number of 5. Table 34: the MPE type 5 is
    /// followed by fields of 4 and 10 digits.
    /// </summary>
    /// <param name="text">The digits, already validated.</param>
    /// <param name="line">The buffer that receives the line.</param>
    /// <returns>The length of the line.</returns>
    private static int HumanReadable(ReadOnlySpan<char> text, Span<char> line)
    {
        ReadOnlySpan<int> tracking = text.StartsWith(PostalIdentifier)
            ? text[5] == ForwardingMpeType ? [2, 3, 1, 4, 10] : [2, 3, 1, 4, 3, 2, 5]
            : text[5] == '9' ? [2, 3, 9, 6] : [2, 3, 6, 9];

        ValueStringBuilder builder = new(line);
        int position = 0;
        foreach (int length in tracking)
        {
            if (position > 0)
            {
                builder.Append(' ');
            }

            builder.Append(text.Slice(position, length));
            position += length;
        }

        ReadOnlySpan<int> routing = [5, 4, 2];
        for (int i = 0; i < routing.Length && position < text.Length; i++)
        {
            builder.Append(' ');
            builder.Append(text.Slice(position, routing[i]));
            position += routing[i];
        }

        return builder.Length;
    }
}
