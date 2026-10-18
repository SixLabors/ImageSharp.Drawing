// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The United States Postal Service Intelligent Mail package barcode (IMpb), which USPS2000508 revision E defines and USPS Publication 199 applies. Section
/// 2.2.1 of USPS2000508: the barcode is a GS1-128 symbol, and "Only Subset C is permitted". The symbol carries an optional routing code, Application Identifier
/// (420) and a 5 or 9 digit destination ZIP Code, then the package identification code (PIC): a channel Application Identifier of 92, 93, 94 or 95, the fields
/// of that channel and a Mod 10 check digit.
/// <para>
/// The input is the element string syntax, for example <c>(420)12345(92)12391234567812345670</c>, and must match one barcode construct of USPS2000508 Appendix
/// D or Publication 199 Appendix J, Table 1. The check digit is required. Section 2.4.1 suppresses the routing code from the human readable interpretation, and
/// Appendix D prints the PIC in groups of four digits with any remaining digits in a group at the end.
/// </para>
/// <para>
/// Section 2.4 makes three more elements part of the IMpb structure: the PIC below the bars, a service banner above them, and an identification bar above the
/// banner and below the PIC. The symbol prints all three with the text. Without a font it prints the bars alone. Section 2.4.1 and section 2.4.2 call for a
/// bold sans serif font, which the caller supplies.
/// </para>
/// </summary>
public sealed class IntelligentMailPackageSymbology : BarcodeSymbology
{
    /// <summary>
    /// The nominal X dimension in millimetres. Section 2.2.2 permits 0.013 to 0.021 inch, and "X-dimensions ranging from 0.015 to 0.017 inch inclusive are
    /// preferred". The nominal value is the middle of the preferred range, 0.016 inch.
    /// </summary>
    private const double NominalXDimension = 0.016 * 25.4;

    /// <summary>
    /// The bar height in millimetres. Section 2.2.4: "The overall minimum height of the barcode shall measure at least 0.75 inch".
    /// </summary>
    private const double BarHeight = 0.75 * 25.4;

    /// <summary>
    /// The space between the bars and the human readable interpretation in millimetres. Section 2.4.1 prints the interpretation "at least 0.125 inch, but not
    /// more than 0.25 inch below the barcode". The space is the middle of that range, 0.1875 inch.
    /// </summary>
    private const double TextClearance = 0.1875 * 25.4;

    /// <summary>
    /// The longest input that can match a construct: "(420)", a 9 digit ZIP Code and "(92)" with the 20 digits that follow it in a 22 digit PIC, 38 characters.
    /// "(420)", a 5 digit ZIP Code and a 26 digit PIC, and a 30 digit PIC alone, are no longer. The bound keeps the prepared data in the inline buffer.
    /// </summary>
    private const int MaximumInputLength = 38;

    /// <summary>
    /// The space between the bars and the ink of the banner in millimetres. Section 2.4.2 keeps "A clear zone of 0.125, but not more than 0.500 inch" between
    /// the banner and the barcode, and section 4.9.1 of Publication 199 prints the banner "at a minimum 0.125 inch above the barcode, but not more than 0.25
    /// inch above the barcode". The space is the middle of the range both allow, 0.1875 inch.
    /// </summary>
    private const double BannerClearance = 0.1875 * 25.4;

    /// <summary>
    /// The thickness of an identification bar in millimetres. Section 2.4.3 gives "a thickness within the range of 0.031 to 0.063 inch". The thickness is the
    /// middle of that range, 0.047 inch.
    /// </summary>
    private const double IdentificationBarThickness = 0.047 * 25.4;

    /// <summary>
    /// The space between an identification bar and the ink of its text line in millimetres: the "0.031" minimum clearance" of Figure 1.
    /// </summary>
    private const double IdentificationBarClearance = 0.031 * 25.4;

    /// <summary>
    /// The smallest printed height of the characters in millimetres. Sections 2.4.1 and 2.4.2: "The printed height of the characters shall be between 0.090 to
    /// 0.125 inch".
    /// </summary>
    private const double MinimumTextHeight = 0.090 * 25.4;

    /// <summary>
    /// The largest printed height of the characters in millimetres, the upper limit of sections 2.4.1 and 2.4.2.
    /// </summary>
    private const double MaximumTextHeight = 0.125 * 25.4;

    /// <summary>
    /// The Application Identifier of the destination ZIP Code routing code, section 2.2.1, which opens the prepared data when the symbol carries one.
    /// </summary>
    private const string RoutingIdentifier = "420";

    /// <summary>
    /// Initializes a new instance of the <see cref="IntelligentMailPackageSymbology"/> class.
    /// </summary>
    /// <param name="banner"> The service banner printed above the bars, for example <c>USPS TRACKING #</c>. The banner depends on the product. Publication 199
    /// section 4.5 and Appendix I give the banner for each service type code. </param>
    /// <exception cref="ArgumentException">The banner is empty, white space, or has a lowercase letter.</exception>
    public IntelligentMailPackageSymbology(string banner)
    {
        Guard.NotNullOrWhiteSpace(banner, nameof(banner));

        // Section 2.4.2: "A service banner shall be printed in all uppercase letters".
        foreach (char character in banner)
        {
            if (char.IsLower(character))
            {
                throw new ArgumentException($"The service banner is printed in uppercase letters; got '{character}'.", nameof(banner));
            }
        }

        this.Banner = banner;
    }

    /// <summary>
    /// Gets the service banner printed above the bars.
    /// </summary>
    public string Banner { get; }

    /// <summary>
    /// Gets the barcode constructs as four bytes each: the channel Application Identifier, the length of the mailer ID (0 where the channel carries none), the
    /// length of the destination ZIP Code (0 where the construct carries no routing code) and the length of the PIC with its check digit. The commercial
    /// constructs C01 to C10 and the online constructs N01 to N10 are Publication 199 Appendix J, Table 1. The retail constructs A01, A02 and P02 are
    /// USPS2000508 Appendix D, Tables 24 to 26. N01 and N04, N02 and N05, and N03 and N06 differ only in the split of the mailer ID and the serial number, so
    /// the mailer ID length tells them apart.
    /// </summary>
    private static ReadOnlySpan<byte> Constructs =>
    [
        92, 9, 9, 22, // C01
        92, 9, 5, 26, // C02
        92, 9, 5, 22, // C03
        92, 9, 0, 22, // C04
        93, 6, 9, 22, // C05
        93, 6, 5, 26, // C06
        93, 6, 5, 22, // C07
        93, 6, 0, 22, // C08
        93, 6, 0, 26, // C09
        92, 9, 0, 26, // C10
        94, 6, 5, 22, // N01
        94, 6, 9, 22, // N02
        94, 6, 0, 22, // N03
        94, 9, 5, 22, // N04
        94, 9, 9, 22, // N05
        94, 9, 0, 22, // N06
        94, 6, 5, 26, // N07
        94, 9, 5, 26, // N08
        94, 6, 0, 26, // N09
        94, 9, 0, 30, // N10
        95, 0, 9, 22, // A01
        95, 0, 5, 22, // A02
        95, 0, 0, 22, // P02
    ];

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumInputLength, nameof(text));

        InlineArray64<char> encodedStorage = default;
        ValueStringBuilder encoded = new(encodedStorage);
        try
        {
            Gs1Data.Prepare(text, ref encoded);

            // Gs1Data writes a separator after the variable length routing code, which section 2.2.1 requires: "Concatenated barcodes use a second Function 1
            // (FNC1) character immediately following the ZIP Code". Everything after it is the PIC.
            ReadOnlySpan<char> data = encoded.AsSpan();
            int zipLength = 0;
            if (data.StartsWith(RoutingIdentifier))
            {
                int separator = data.IndexOf(Gs1Data.Separator);
                if (separator < 0)
                {
                    throw new ArgumentException("The Intelligent Mail package barcode carries a PIC after the routing code.", nameof(text));
                }

                zipLength = separator - RoutingIdentifier.Length;
                if (data[RoutingIdentifier.Length..separator].ContainsAnyExceptInRange('0', '9'))
                {
                    throw new ArgumentException("The destination ZIP Code of the Intelligent Mail package barcode is digits.", nameof(text));
                }

                data = data[(separator + 1)..];
            }

            if (data.Length < 2 || data.ContainsAnyExceptInRange('0', '9'))
            {
                throw new ArgumentException("The Intelligent Mail package barcode carries an optional (420) routing code and one PIC of digits.", nameof(text));
            }

            int channel = ((data[0] - '0') * 10) + (data[1] - '0');
            int mailerIdLength = MailerIdLength(channel, data);
            if (!IsConstruct(channel, mailerIdLength, zipLength, data.Length))
            {
                throw new ArgumentException(
                    $"No Intelligent Mail package barcode construct has channel {channel}, a {zipLength}-digit ZIP Code and a {data.Length}-digit PIC.",
                    nameof(text));
            }

            // Publication 199 section 1.4: "A six-digit MID must begin with 0-8" and "A nine-digit MID must begin with 9". Channel 92 carries a nine digit MID
            // and channel 93 a six digit MID, Table 5 of USPS2000508. Every construct of those channels has a PIC of at least 22 digits.
            if ((channel == 92 && data[5] != '9') || (channel == 93 && data[5] == '9'))
            {
                throw new ArgumentException(
                    $"Channel {channel} carries a {mailerIdLength}-digit mailer ID, which must begin with {(channel == 92 ? "9" : "0 to 8")}.",
                    nameof(text));
            }

            // USPS2000508 Appendix E weights the PIC digits 3 and 1 from the rightmost digit before the check digit, the check digit calculation of the GS1
            // General Specifications.
            int check = Gs1Data.ComputeCheckDigit(data[..^1]);
            if (data[^1] - '0' != check)
            {
                throw new ArgumentException($"Incorrect Intelligent Mail package barcode check digit: expected {check}, got {data[^1]}.", nameof(text));
            }

            // The longest PIC is 30 digits, which prints as seven groups of four and one of two with seven spaces between them.
            InlineArray64<char> readableStorage = default;
            Span<char> readable = readableStorage;
            int readableLength = 0;
            if (options.Font is not null)
            {
                // Each step copies one group of four digits and the space after it. The last group takes the remaining digits and no space: two, because every
                // PIC is 22, 26 or 30 digits.
                int start = 0;
                for (; start + 4 < data.Length; start += 4)
                {
                    data.Slice(start, 4).CopyTo(readable[readableLength..]);
                    readable[readableLength + 4] = ' ';
                    readableLength += 5;
                }

                data[start..].CopyTo(readable[readableLength..]);
                readableLength += data.Length - start;
            }

            double xDimension = options.XDimension ?? NominalXDimension;
            Code128Encoder.BuildSymbol(
                encoded.AsSpan(),
                true,
                [],
                readable[..readableLength],
                [],
                options.Font is null ? [] : this.Banner,
                xDimension,
                (float)(BarHeight / xDimension),
                (float)(TextClearance / xDimension),
                (float)(BannerClearance / xDimension),
                options,
                ref symbol);

            // The identification bars stand beyond the banner and the PIC, so they print only with them.
            if (options.Font is not null)
            {
                symbol.AddIdentificationBars((float)(IdentificationBarThickness / xDimension), (float)(IdentificationBarClearance / xDimension));
                symbol.SetTextHeight((float)(MinimumTextHeight / xDimension), (float)(MaximumTextHeight / xDimension));
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }

    /// <summary>
    /// Gets the length of the mailer ID of a PIC. Table 5 of USPS2000508 gives channel 92 a nine digit and channel 93 a six digit mailer ID. Channel 94 carries
    /// either after its two digit source identifier, and section 1.4 of Publication 199 tells them apart by the first digit. Channel 95 carries none.
    /// </summary>
    /// <param name="channel">The channel Application Identifier.</param>
    /// <param name="pic">The PIC digits.</param>
    /// <returns>The length of the mailer ID, or zero.</returns>
    private static int MailerIdLength(int channel, ReadOnlySpan<char> pic)
        => channel switch
        {
            92 => 9,
            93 => 6,
            94 => pic.Length > 7 && pic[7] == '9' ? 9 : 6,
            _ => 0,
        };

    /// <summary>
    /// Gets a value indicating whether the fields match one barcode construct.
    /// </summary>
    /// <param name="channel">The channel Application Identifier.</param>
    /// <param name="mailerIdLength">The length of the mailer ID, or zero.</param>
    /// <param name="zipLength">The length of the destination ZIP Code, or zero.</param>
    /// <param name="picLength">The length of the PIC with its check digit.</param>
    /// <returns><see langword="true"/> when a construct matches; otherwise, <see langword="false"/>.</returns>
    private static bool IsConstruct(int channel, int mailerIdLength, int zipLength, int picLength)
    {
        ReadOnlySpan<byte> constructs = Constructs;
        for (int i = 0; i < constructs.Length; i += 4)
        {
            if (constructs[i] == channel && constructs[i + 1] == mailerIdLength && constructs[i + 2] == zipLength && constructs[i + 3] == picLength)
            {
                return true;
            }
        }

        return false;
    }
}
