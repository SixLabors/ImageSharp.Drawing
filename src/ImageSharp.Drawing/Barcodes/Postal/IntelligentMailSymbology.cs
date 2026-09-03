// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The United States Postal Service Intelligent Mail barcode, which USPS-B-3200 revision H defines. The
/// text is a 20-digit tracking code followed by a routing code of 0, 5, 9 or 11 digits, so the symbol
/// carries 20, 25, 29 or 31 digits. Section 2.1.3: the second digit of the barcode identifier "shall be
/// constrained to the range of 0-4". The symbol is 65 bars. Section 2.4.3: the printed line is the fields
/// of the tracking code, the barcode identifier, the service type identifier, the mailer ID of 6 or 9
/// digits and the serial number, then the 5-digit ZIP Code, the 4-digit add-on and the remaining 2
/// digits of the routing code, "separated with a space added between data fields".
/// </summary>
public sealed class IntelligentMailSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    public override float NominalXDimension => UspsPostalEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length is not (20 or 25 or 29 or 31))
        {
            throw new ArgumentException($"The Intelligent Mail barcode carries a 20-digit tracking code and a routing code of 0, 5, 9 or 11 digits; got {text.Length} characters.", nameof(text));
        }

        UspsPostalEncoder.ValidateDigits(text);
        if (text[1] > '4')
        {
            throw new ArgumentException($"The second digit of the barcode identifier is 0 to 4; got {text[1]}.", nameof(text));
        }

        Span<FourState> states = stackalloc FourState[IntelligentMailEncoder.BarCount];
        IntelligentMailEncoder.Encode(text, states);
        string readable = options.Font is null ? string.Empty : HumanReadable(text);
        return FourStateEncoder.BuildSymbol(states, IntelligentMailEncoder.Metrics, readable, options);
    }

    /// <summary>
    /// Builds the line of section 2.4.3. Section 2.1.3 C gives the mailer ID as "a unique, 6-or 9- digit
    /// number" whose 9-digit range is 900000000 to 999999999, so a mailer ID that starts with 9 is 9
    /// digits and the serial number 6, and otherwise 6 and 9.
    /// </summary>
    /// <param name="text">The digits, already validated.</param>
    /// <returns>The line.</returns>
    private static string HumanReadable(string text)
    {
        int mailerIdLength = text[5] == '9' ? 9 : 6;
        ValueStringBuilder builder = new(stackalloc char[text.Length + 6]);
        builder.Append(text.AsSpan(0, 2));
        builder.Append(' ');
        builder.Append(text.AsSpan(2, 3));
        builder.Append(' ');
        builder.Append(text.AsSpan(5, mailerIdLength));
        builder.Append(' ');
        builder.Append(text.AsSpan(5 + mailerIdLength, IntelligentMailEncoder.TrackingLength - 5 - mailerIdLength));

        int position = IntelligentMailEncoder.TrackingLength;
        ReadOnlySpan<int> groups = [5, 4, 2];
        for (int i = 0; i < groups.Length && position < text.Length; i++)
        {
            builder.Append(' ');
            builder.Append(text.AsSpan(position, groups[i]));
            position += groups[i];
        }

        return builder.ToString();
    }
}
