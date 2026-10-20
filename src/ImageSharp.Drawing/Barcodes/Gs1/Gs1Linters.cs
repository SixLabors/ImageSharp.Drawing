// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The content checks of the components of GS1 Application Identifiers: the character sets of section 7.11 of the GS1 General Specifications, the check digit
/// and check character pair of sections 7.9.1 and 7.9.5, the dates of section 7.12, and the code lists and formats the GS1 Barcode Syntax Dictionary names.
/// </summary>
internal static class Gs1Linters
{
    /// <summary>
    /// The length of the shortest GS1 Company Prefix.
    /// </summary>
    private const int MinimumCompanyPrefix = 4;

    /// <summary>
    /// The longest International Bank Account Number, ISO 13616.
    /// </summary>
    private const int MaximumIban = 34;

    /// <summary>
    /// The shortest International Bank Account Number the check accepts.
    /// </summary>
    private const int MinimumIban = 11;

    /// <summary>
    /// The length the coupon funder ID and serial number length indicators of a positive offer file coupon code add to.
    /// </summary>
    private const int LengthIndicatorOffset = 6;

    /// <summary>
    /// The characters of the check character pair, section 7.9.5: the digits and capital letters without 0, 1, I and O, in the order of their values 0 to 31.
    /// </summary>
    private const string CheckPairCharacters = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>
    /// GS1 AI encodable character set 82, Table 7-20. A <see cref="SearchValues{T}"/> instance checks a whole component with one vectorized search.
    /// </summary>
    private static readonly SearchValues<char> CharacterSet82 =
        SearchValues.Create("!\"%&'()*+,-./0123456789:;<=>?ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz");

    /// <summary>
    /// GS1 AI encodable character set 39, Table 7-21.
    /// </summary>
    private static readonly SearchValues<char> CharacterSet39 = SearchValues.Create("#-/0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");

    /// <summary>
    /// The characters of an International Bank Account Number in electronic form, ISO 13616: digits and capital letters.
    /// </summary>
    private static readonly SearchValues<char> Iban = SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");

    /// <summary>
    /// GS1 AI encodable character set 64, Table 7-22, without its pad character.
    /// </summary>
    private static readonly SearchValues<char> CharacterSet64 = SearchValues.Create("-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz");

    /// <summary>
    /// Gets the ISO 4217 numeric currency codes 000 to 999 as a bit field, code n at bit 7 - (n mod 8) of byte n div 8. The constant bytes compile into the
    /// assembly data, so reading them allocates nothing.
    /// </summary>
    private static ReadOnlySpan<byte> Iso4217Numeric =>
    [
        0x00, 0x88, 0x00, 0x00, 0x88, 0x08, 0xb8, 0x08, 0x88, 0x80, 0x08, 0x20, 0x80, 0x88, 0x08, 0x08,
        0x08, 0x80, 0x80, 0x88, 0x00, 0x22, 0x00, 0x08, 0x80, 0x10, 0x82, 0x02, 0x02, 0x82, 0x20, 0x00,
        0x02, 0x02, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x88, 0x88, 0x08, 0x88, 0x88, 0x88, 0x80, 0x80,
        0x08, 0x8a, 0x88, 0xa2, 0x62, 0x22, 0x20, 0x02, 0x02, 0x22, 0x00, 0x00, 0x88, 0x00, 0xa0, 0x80,
        0x88, 0x08, 0x0c, 0x00, 0x08, 0x22, 0x02, 0x00, 0x20, 0x22, 0x02, 0x88, 0x80, 0x00, 0x00, 0x20,
        0x12, 0x02, 0x00, 0x00, 0x00, 0x20, 0x20, 0x02, 0xa2, 0x00, 0x00, 0x80, 0x00, 0x08, 0x88, 0x88,
        0x00, 0x88, 0x88, 0x00, 0x81, 0x00, 0x20, 0x20, 0x20, 0x80, 0x00, 0x28, 0x00, 0x00, 0x22, 0x00,
        0x04, 0x00, 0x00, 0x0f, 0xe6, 0xad, 0xbf, 0xdf, 0xfd, 0xfd, 0xfc, 0xe2, 0x25,
    ];

    /// <summary>
    /// Gets the reference values of Table 7-18 by character code, 0 to 81 for the characters of character set 82 in code order, and 255 for the other codes
    /// below 128.
    /// </summary>
    private static ReadOnlySpan<byte> ReferenceValues =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 0, 1, 255, 255, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
        13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
        255, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43,
        44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 255, 255, 255, 255, 55,
        255, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70,
        71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 255, 255, 255, 255, 255,
    ];

    /// <summary>
    /// Gets the first 97 primes, the weights of section 7.9.5 step 2 from the rightmost data character. A component of character set 82 is at most 90
    /// characters, so the data before a check pair takes fewer weights than these.
    /// </summary>
    private static ReadOnlySpan<ushort> Primes =>
    [
        2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53,
        59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131,
        137, 139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223,
        227, 229, 233, 239, 241, 251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311,
        313, 317, 331, 337, 347, 349, 353, 359, 367, 373, 379, 383, 389, 397, 401, 409,
        419, 421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499, 503,
        509,
    ];

    /// <summary>
    /// Gets the 431 package type codes of Application Identifier (7041), each packed into one number and sorted for a binary search. A code is one to three
    /// characters from 0 to 9 and A to Z, so it packs as three base 37 digits, the first character the most significant: 0 for no character, 1 to 10 for the
    /// digits and 11 to 36 for the letters. "1A" is 2 x 1369 + 11 x 37 + 0 = 3145, and the largest key, 36 x 1369 + 36 x 37 + 36 = 50652, fits a
    /// <see cref="ushort"/>.
    /// </summary>
    private static ReadOnlySpan<ushort> PackageTypes =>
    [
        3145, 3182, 3256, 3330, 3367, 3959, 4145, 4146, 4147, 4148, 4149, 4150, 4151, 4182,
        4183, 4184, 4588, 5883, 6142, 6993, 7030, 7252, 7289, 7326, 7363, 7437, 7474, 7511,
        8880, 9028, 9065, 10249, 10545, 11359, 11396, 12321, 12728, 12765, 12802, 13690, 15466, 15503,
        15540, 15577, 15651, 15688, 15725, 15762, 15799, 15873, 15910, 16021, 16036, 16169, 16243, 16613,
        16872, 16909, 16946, 16983, 17020, 17057, 17072, 17094, 17131, 17168, 17205, 17242, 17279, 17294,
        17316, 17353, 17390, 17427, 17464, 17483, 17501, 17538, 17575, 17612, 17649, 17686, 17723, 17760,
        18204, 18241, 18263, 18278, 18293, 18315, 18352, 18389, 18426, 18463, 18500, 18537, 18574, 18611,
        18648, 18685, 18722, 18759, 18796, 18833, 18870, 18907, 18944, 18981, 19018, 19055, 19092, 19129,
        19573, 19610, 19647, 19795, 19832, 19869, 19906, 19943, 19980, 20017, 20054, 20128, 20143, 20202,
        20239, 20276, 20313, 20350, 20387, 20424, 20461, 20609, 20646, 20683, 21016, 21053, 21090, 21127,
        21164, 21201, 21238, 21423, 22348, 22385, 22422, 22459, 22607, 22718, 22829, 22841, 22866, 22881,
        22940, 23014, 23125, 23162, 23717, 23976, 24087, 24309, 24420, 24568, 24605, 25049, 25086, 25123,
        25271, 25530, 25678, 26418, 26455, 26492, 26529, 26566, 26603, 26640, 26677, 26788, 26825, 26899,
        27343, 27824, 27861, 28009, 28416, 28490, 28675, 29378, 29452, 30537, 30673, 30747, 31228, 31265,
        31302, 31450, 31894, 31931, 31968, 32042, 32464, 32523, 32560, 32597, 32708, 32745, 33263, 33411,
        33448, 33485, 33929, 33966, 34003, 34040, 34632, 34669, 34706, 34743, 34780, 34817, 35002, 35202,
        35335, 35372, 35705, 36001, 36016, 36038, 36075, 36112, 36149, 36186, 36223, 36260, 36297, 36334,
        36371, 36408, 36434, 36482, 36519, 36545, 36556, 36571, 36630, 36704, 36741, 36756, 36778, 36852,
        36889, 36926, 37370, 37407, 37444, 37481, 37555, 37592, 37629, 37703, 37740, 37777, 37814, 37851,
        37925, 37962, 37999, 38036, 38778, 38779, 38780, 38825, 38850, 38961, 39072, 39109, 39146, 39257,
        39442, 39664, 39775, 40108, 40145, 40182, 40219, 40256, 40269, 40367, 40404, 40478, 40515, 40552,
        40626, 40663, 40774, 40811, 40833, 40848, 40885, 40922, 40959, 40996, 41033, 41144, 41514, 41551,
        41588, 41625, 41657, 41699, 41751, 41773, 41847, 41884, 41958, 41995, 42106, 42121, 42143, 42180,
        42195, 42217, 42254, 42291, 42306, 42365, 42402, 42920, 43327, 43601, 44215, 44437, 44511, 44585,
        44622, 44696, 44733, 44770, 44807, 44844, 44881, 45103, 45584, 45621, 45658, 45695, 45769, 45806,
        45843, 45917, 45954, 45991, 46028, 46065, 46139, 46176, 46213, 46239, 46250, 46287, 46324, 46361,
        46398, 46435, 46472, 46509, 46622, 46623, 46626, 46627, 46628, 46629, 46630, 46658, 46694, 46953,
        46990, 47027, 47064, 47138, 47175, 47212, 47286, 47323, 48322, 48359, 48396, 48433, 48507, 48544,
        48581, 48655, 48692, 48729, 48766, 48803, 48877, 48914, 48951, 48988, 49025, 49099, 49136, 49173,
        49210, 49247, 49691, 49728, 49765, 49802, 49876, 49913, 49950, 50024, 50061, 50098, 50135, 50172,
        50246, 50283, 50320, 50357, 50394, 50431, 50468, 50505, 50542, 50579, 50616,
    ];

    /// <summary>
    /// Checks the characters of a component against its character set: N the digits, X GS1 AI encodable character set 82 of Table 7-20, Y character set 39 of
    /// Table 7-21 and Z character set 64 of Table 7-22.
    /// </summary>
    /// <param name="characterSet">The character set, 'N', 'X', 'Y' or 'Z'.</param>
    /// <param name="identifier">The Application Identifier, for the error message.</param>
    /// <param name="data">The component.</param>
    /// <exception cref="ArgumentException">A character is not in the set.</exception>
    public static void CheckCharacterSet(char characterSet, ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        // Character set 64 is base64 with the pad character, which may only end the data, padding it to a multiple of three characters with one or two pads.
        if (characterSet == 'Z')
        {
            int pads = data.Length - data.TrimEnd('=').Length;
            if (pads > 2 || (pads > 0 && data.Length % 3 != 0))
            {
                ThrowInvalidData(identifier, "has base64 padding that is not one or two '=' to a multiple of three characters", nameof(data));
            }

            data = data[..^pads];
        }

        // One vectorized search finds the first character outside the set, or -1.
        int bad = characterSet switch
        {
            'N' => data.IndexOfAnyExceptInRange('0', '9'),
            'X' => data.IndexOfAnyExcept(CharacterSet82),
            'Y' => data.IndexOfAnyExcept(CharacterSet39),
            _ => data.IndexOfAnyExcept(CharacterSet64),
        };

        if (bad >= 0)
        {
            string set = characterSet switch
            {
                'N' => "digits",
                'X' => "GS1 AI encodable character set 82",
                'Y' => "GS1 AI encodable character set 39",
                _ => "GS1 AI encodable character set 64",
            };

            ThrowInvalidData(identifier, $"carries '{data[bad]}' at position {bad + 1}, which is not in {set}", nameof(data));
        }
    }

    /// <summary>
    /// Runs one linter over a component.
    /// </summary>
    /// <param name="linter">The linter.</param>
    /// <param name="identifier">The Application Identifier, for the error message.</param>
    /// <param name="data">The component, already checked against its character set.</param>
    /// <exception cref="ArgumentException">The component fails the check.</exception>
    public static void Check(Gs1Linter linter, ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        switch (linter)
        {
            case Gs1Linter.Csum:
                // Section 7.9.1: the digits weigh 3 and 1 alternately from the digit before the check digit, and the check digit lifts the sum to a multiple of
                // 10.
                if (data.Length == 0 || data[^1] - '0' != Gs1Data.ComputeCheckDigit(data[..^1]))
                {
                    ThrowInvalidData(identifier, "has an incorrect check digit", nameof(data));
                }

                break;

            case Gs1Linter.CsumAlpha:
                CheckPair(identifier, data);
                break;

            case Gs1Linter.GcpPos1:
                // A GS1 Company Prefix is at least four digits. Which prefixes are allocated is not known here, so only the digits are checked.
                if (data.Length < MinimumCompanyPrefix || data[..MinimumCompanyPrefix].ContainsAnyExceptInRange('0', '9'))
                {
                    ThrowInvalidData(identifier, "does not start with a GS1 Company Prefix of at least four digits", nameof(data));
                }

                break;

            case Gs1Linter.GcpPos2:
                if (data.Length < 1 + MinimumCompanyPrefix || data.Slice(1, MinimumCompanyPrefix).ContainsAnyExceptInRange('0', '9'))
                {
                    ThrowInvalidData(identifier, "does not carry a GS1 Company Prefix of at least four digits after its first digit", nameof(data));
                }

                break;

            case Gs1Linter.Yymmd0:
                CheckDate(identifier, data, true);
                break;

            case Gs1Linter.Yymmdd:
                CheckDate(identifier, data, false);
                break;

            case Gs1Linter.Yyyymmdd:
                CheckFullDate(identifier, data, Number(data[..4]), false);
                break;

            case Gs1Linter.Hh:
                CheckRange(identifier, data, 2, 23, "an hour from 00 to 23");
                break;

            case Gs1Linter.Mi:
                CheckRange(identifier, data, 2, 59, "a minute from 00 to 59");
                break;

            case Gs1Linter.Ss:
                CheckRange(identifier, data, 2, 59, "a second from 00 to 59");
                break;

            case Gs1Linter.Hhmi:
                if (data.Length != 4)
                {
                    ThrowInvalidData(identifier, "does not carry a time HHMI of four digits", nameof(data));
                }

                CheckRange(identifier, data[..2], 2, 23, "an hour from 00 to 23");
                CheckRange(identifier, data[2..], 2, 59, "a minute from 00 to 59");
                break;

            case Gs1Linter.Iso4217:
                if (data.Length != 3 || !IsSet(Iso4217Numeric, Number(data)))
                {
                    ThrowInvalidData(identifier, $"carries '{data}', which is not an ISO 4217 numeric currency code", nameof(data));
                }

                break;

            case Gs1Linter.Iso3166:
                if (data.Length != 3 || !Iso3166.IsNumericCode(Number(data)))
                {
                    ThrowInvalidData(identifier, $"carries '{data}', which is not an ISO 3166-1 numeric country code", nameof(data));
                }

                break;

            case Gs1Linter.Iso3166999:
                // The value 999 stands for a country that is not known.
                if (data.Length != 3 || (!data.SequenceEqual("999") && !Iso3166.IsNumericCode(Number(data))))
                {
                    ThrowInvalidData(identifier, $"carries '{data}', which is not an ISO 3166-1 numeric country code or 999", nameof(data));
                }

                break;

            case Gs1Linter.Iso3166Alpha2:
                if (!Iso3166.IsAlpha2Code(data))
                {
                    ThrowInvalidData(identifier, $"carries '{data}', which is not an ISO 3166-1 alpha-2 country code", nameof(data));
                }

                break;

            case Gs1Linter.Iso5218:
                // ISO/IEC 5218 has the four codes 0, 1, 2 and 9.
                if (data.Length != 1 || data[0] is not ('0' or '1' or '2' or '9'))
                {
                    ThrowInvalidData(identifier, "does not carry an ISO/IEC 5218 code 0, 1, 2 or 9", nameof(data));
                }

                break;

            case Gs1Linter.PercentEncoded:
                // Section 7.11: the AIs 4300 to 4320 may percent-encode characters as RFC 3986 defines, so every percent sign starts two hexadecimal digits.
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == '%')
                    {
                        if (i + 2 >= data.Length || !char.IsAsciiHexDigit(data[i + 1]) || !char.IsAsciiHexDigit(data[i + 2]))
                        {
                            ThrowInvalidData(identifier, $"has a '%' at position {i + 1} that does not start two hexadecimal digits", nameof(data));
                        }

                        i += 2;
                    }
                }

                break;

            case Gs1Linter.YesNo:
                if (data.Length != 1 || data[0] is not ('0' or '1'))
                {
                    ThrowInvalidData(identifier, "does not carry 0 or 1", nameof(data));
                }

                break;

            case Gs1Linter.Hyphen:
                if (data.Length == 0 || data.ContainsAnyExcept('-'))
                {
                    ThrowInvalidData(identifier, "does not carry a hyphen where one is required", nameof(data));
                }

                break;

            case Gs1Linter.Latitude:
                // Section 7.13: X = 10 000 000 x (latitude + 90), so it is at most 1 800 000 000.
                if (data.Length != 10 || long.Parse(data, CultureInfo.InvariantCulture) > 1_800_000_000)
                {
                    ThrowInvalidData(identifier, "does not carry a latitude of ten digits up to 1800000000", nameof(data));
                }

                break;

            case Gs1Linter.Longitude:
                // Section 7.13: Y = 10 000 000 x ((longitude + 360) mod 360). The remainder is below 360, so Y is at most 3 599 999 999.
                if (data.Length != 10 || long.Parse(data, CultureInfo.InvariantCulture) > 3_599_999_999)
                {
                    ThrowInvalidData(identifier, "does not carry a longitude of ten digits up to 3599999999", nameof(data));
                }

                break;

            case Gs1Linter.ImporterIndex:
                if (data.Length != 1 || !(data[0] is '-' or '_' || char.IsAsciiLetterOrDigit(data[0])))
                {
                    ThrowInvalidData(identifier, "does not carry an importer index of one character from the file-safe base64 alphabet", nameof(data));
                }

                break;

            case Gs1Linter.PackageType:
                if (!IsPackageType(data))
                {
                    ThrowInvalidData(identifier, $"carries '{data}', which is not a package type code", nameof(data));
                }

                break;

            case Gs1Linter.MediaType:
                // The AIDC media types 01 to 10 are assigned, and 80 to 99 are for local or national use.
                int mediaType = data.Length == 2 ? Number(data) : -1;
                if (mediaType is not ((>= 1 and <= 10) or (>= 80 and <= 99)))
                {
                    ThrowInvalidData(identifier, $"carries '{data}', which is not an AIDC media type", nameof(data));
                }

                break;

            case Gs1Linter.PositionInSequence:
                CheckPositionInSequence(identifier, data);
                break;

            case Gs1Linter.NonZero:
                if (!data.ContainsAnyExcept('0'))
                {
                    ThrowInvalidData(identifier, "carries a zero value where one is not allowed", nameof(data));
                }

                break;

            case Gs1Linter.Winding:
                if (data.Length != 1 || data[0] is not ('0' or '1' or '9'))
                {
                    ThrowInvalidData(identifier, "does not carry a winding direction 0, 1 or 9", nameof(data));
                }

                break;

            case Gs1Linter.Zero:
                if (data.Length == 0 || data.ContainsAnyExcept('0'))
                {
                    ThrowInvalidData(identifier, "does not carry a zero where one is required", nameof(data));
                }

                break;

            case Gs1Linter.PieceOfTotal:
                CheckPieceOfTotal(identifier, data);
                break;

            case Gs1Linter.Iban:
                CheckIban(identifier, data);
                break;

            case Gs1Linter.NoZeroPrefix:
                if (data.Length > 0 && data[0] == '0')
                {
                    ThrowInvalidData(identifier, "starts with a zero, which is not allowed", nameof(data));
                }

                break;

            case Gs1Linter.HasNonDigit:
                if (!data.ContainsAnyExceptInRange('0', '9'))
                {
                    ThrowInvalidData(identifier, "carries only digits where at least one other character is required", nameof(data));
                }

                break;

            case Gs1Linter.CouponCode:
                // Table C of the North American Coupon Application Guideline defines the coupon code.
                _ = NorthAmericanCouponCode.Validate(data);
                break;

            default:
                CheckPositiveOfferCoupon(identifier, data);
                break;
        }
    }

    /// <summary>
    /// Returns the number the digits stand for.
    /// </summary>
    /// <param name="digits">The digits, at most nine.</param>
    /// <returns>The number.</returns>
    private static int Number(ReadOnlySpan<char> digits)
    {
        int value = 0;
        foreach (char c in digits)
        {
            value = (value * 10) + (c - '0');
        }

        return value;
    }

    /// <summary>
    /// Throws the exception for data that fails a check. The message names the Application Identifier, so the caller states the problem as the end of a
    /// sentence about its data.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="problem">The problem, as the end of a sentence about the data.</param>
    /// <param name="paramName">The name of the parameter that holds the data.</param>
    /// <exception cref="ArgumentException">Always thrown.</exception>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidData(ReadOnlySpan<char> identifier, string problem, string paramName)
        => throw new ArgumentException($"The data of Application Identifier ({identifier}) {problem}.", paramName);

    /// <summary>
    /// Returns whether a bit is set in a bit field whose bytes hold their bits from the most significant. A bit past the end of the field is clear, so trailing
    /// zero bytes are left out of the fields.
    /// </summary>
    /// <param name="field">The bit field.</param>
    /// <param name="bit">The bit index.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    private static bool IsSet(ReadOnlySpan<byte> field, int bit)
        => (uint)(bit >> 3) < (uint)field.Length && (field[bit >> 3] & (0x80 >> (bit & 7))) != 0;

    /// <summary>
    /// Checks a two digit field against a maximum value.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The field.</param>
    /// <param name="length">The length of the field.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="what">What the field holds, for the error message.</param>
    private static void CheckRange(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data, int length, int maximum, string what)
    {
        if (data.Length != length || Number(data) > maximum)
        {
            ThrowInvalidData(identifier, $"does not carry {what}", nameof(data));
        }
    }

    /// <summary>
    /// Checks a date YYMMDD. Section 7.12, Figure 7-17: the two digit year is the year within 49 years before and 50 years after the current year, which
    /// decides whether February has 29 days.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The six digits.</param>
    /// <param name="dayZero">Whether a day of 00 is allowed.</param>
    private static void CheckDate(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data, bool dayZero)
    {
        if (data.Length != 6)
        {
            ThrowInvalidData(identifier, "does not carry a date of six digits YYMMDD", nameof(data));
        }

        // The current century holds the years from 49 years back to 50 years ahead. A two digit year more than 50 years ahead in the current century belongs to
        // the century before, and one more than 49 years back belongs to the century after.
        int currentYear = DateTime.UtcNow.Year;
        int century = currentYear / 100 * 100;
        int year = century + Number(data[..2]);
        if (year - currentYear > 50)
        {
            year -= 100;
        }
        else if (year - currentYear < -49)
        {
            year += 100;
        }

        CheckFullDate(identifier, data[2..], year, dayZero);
    }

    /// <summary>
    /// Checks the month and the day of a date.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The date, YYYYMMDD or the MMDD that ends it.</param>
    /// <param name="year">The year.</param>
    /// <param name="dayZero">Whether a day of 00 is allowed.</param>
    private static void CheckFullDate(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data, int year, bool dayZero)
    {
        if (data.Length != 8 && data.Length != 4)
        {
            ThrowInvalidData(identifier, "does not carry a date of eight digits YYYYMMDD", nameof(data));
        }

        ReadOnlySpan<char> monthDay = data[^4..];
        int month = Number(monthDay[..2]);
        int day = Number(monthDay[2..]);
        if (month is < 1 or > 12)
        {
            ThrowInvalidData(identifier, $"carries the month {month:D2}, which is not 01 to 12", nameof(data));
        }

        // The runtime gives the days of the month in the Gregorian calendar, leap years included, for the years 1 to 9999. The calendar repeats every 400
        // years, so the year 0000 of a YYYYMMDD date has the month lengths of the year 400.
        int lastDay = DateTime.DaysInMonth(year == 0 ? 400 : year, month);
        if (day > lastDay || (day == 0 && !dayZero))
        {
            ThrowInvalidData(identifier, $"carries the day {day:D2}, which is not a day of month {month:D2} of {year}", nameof(data));
        }
    }

    /// <summary>
    /// Checks the check character pair of section 7.9.5: the reference values of Table 7-18 weighted by the primes 2, 3, 5 and on from the rightmost data
    /// character, summed modulo 1021, give two values of five bits each, which the last two characters stand for.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The data, ending with the check character pair.</param>
    private static void CheckPair(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        if (data.Length < 2)
        {
            ThrowInvalidData(identifier, "is too short to carry a check character pair", nameof(data));
        }

        // The data is already checked against character set 82, so every character has a reference value.
        ReadOnlySpan<byte> values = ReferenceValues;
        ReadOnlySpan<ushort> primes = Primes;
        int sum = 0;
        for (int i = data.Length - 3, weight = 0; i >= 0; i--, weight++)
        {
            sum += values[data[i]] * primes[weight];
        }

        sum %= 1021;
        if (data[^2] != CheckPairCharacters[sum >> 5] || data[^1] != CheckPairCharacters[sum & 31])
        {
            ThrowInvalidData(identifier, "has an incorrect check character pair", nameof(data));
        }
    }

    /// <summary>
    /// Returns whether the data is a package type code.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns><see langword="true"/> when the code is in the list.</returns>
    private static bool IsPackageType(ReadOnlySpan<char> data)
    {
        if (data.Length is < 1 or > 3)
        {
            return false;
        }

        // Pack the code as the table does. A character outside 0 to 9 and A to Z is in no code.
        int key = 0;
        for (int i = 0; i < 3; i++)
        {
            int digit = 0;
            if (i < data.Length)
            {
                char c = data[i];
                if (char.IsAsciiDigit(c))
                {
                    digit = c - '0' + 1;
                }
                else if (char.IsAsciiLetterUpper(c))
                {
                    digit = c - 'A' + 11;
                }
                else
                {
                    return false;
                }
            }

            key = (key * 37) + digit;
        }

        return PackageTypes.BinarySearch((ushort)key) >= 0;
    }

    /// <summary>
    /// Checks a position in a sequence, N/M: two numbers without leading zeros and N no larger than M.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The data.</param>
    private static void CheckPositionInSequence(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        int slash = data.IndexOf('/');
        ReadOnlySpan<char> position = slash < 0 ? data : data[..slash];
        ReadOnlySpan<char> end = slash < 0 ? [] : data[(slash + 1)..];
        if (position.Length == 0
            || end.Length == 0
            || position.ContainsAnyExceptInRange('0', '9')
            || end.ContainsAnyExceptInRange('0', '9')
            || position[0] == '0'
            || end[0] == '0')
        {
            ThrowInvalidData(identifier, "does not carry a position in a sequence N/M of two numbers without leading zeros", nameof(data));
        }

        // Without leading zeros a longer number is larger, and numbers of one length compare as text.
        if (position.Length > end.Length || (position.Length == end.Length && position.SequenceCompareTo(end) > 0))
        {
            ThrowInvalidData(identifier, "carries a position after the end of its sequence", nameof(data));
        }
    }

    /// <summary>
    /// Checks a piece number and a total count: the data splits into two halves of equal length, neither is zero, and the piece is not after the total.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The data.</param>
    private static void CheckPieceOfTotal(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        if (data.Length == 0 || data.Length % 2 != 0)
        {
            ThrowInvalidData(identifier, "does not carry a piece number and a total count of equal length", nameof(data));
        }

        ReadOnlySpan<char> piece = data[..(data.Length / 2)];
        ReadOnlySpan<char> total = data[(data.Length / 2)..];
        if (!piece.ContainsAnyExcept('0') || !total.ContainsAnyExcept('0'))
        {
            ThrowInvalidData(identifier, "carries a piece number or a total count of zero", nameof(data));
        }

        // The two halves have one length, so they compare as text.
        if (piece.SequenceCompareTo(total) > 0)
        {
            ThrowInvalidData(identifier, "carries a piece number larger than the total count", nameof(data));
        }
    }

    /// <summary>
    /// Checks an International Bank Account Number, ISO 13616: an ISO 3166-1 alpha-2 country code, then characters whose value, read with the first four
    /// characters moved to the end and each letter as two digits from 10 to 35, leaves 1 modulo 97.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The data.</param>
    private static void CheckIban(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        if (data.Length < MinimumIban || data.Length > MaximumIban)
        {
            ThrowInvalidData(identifier, "does not carry an International Bank Account Number of 11 to 34 characters", nameof(data));
        }

        Check(Gs1Linter.Iso3166Alpha2, identifier, data[..2]);

        // The electronic form holds digits and capital letters only.
        int invalid = data.IndexOfAnyExcept(Iban);
        if (invalid >= 0)
        {
            ThrowInvalidData(identifier, $"carries '{data[invalid]}', which an International Bank Account Number does not hold", nameof(data));
        }

        if (Iso7064.Mod97(data) != 1)
        {
            ThrowInvalidData(identifier, "has incorrect International Bank Account Number check digits", nameof(data));
        }
    }

    /// <summary>
    /// Checks the positive offer file coupon code of Application Identifier (8112): the format code 0 or 1, the coupon funder ID with its length indicator, the
    /// six digit offer code and the serial number with its length indicator, and nothing after them.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The data.</param>
    private static void CheckPositiveOfferCoupon(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        if (data.ContainsAnyExceptInRange('0', '9'))
        {
            ThrowInvalidData(identifier, "carries a character that is not a digit", nameof(data));
        }

        if (data.Length == 0 || data[0] is not ('0' or '1'))
        {
            ThrowInvalidData(identifier, "does not start with the format code 0 or 1", nameof(data));
        }

        // The funder ID and the serial number are each LengthIndicatorOffset digits longer than their length indicator; the funder indicator is 0 to 6.
        int position = 1;
        if (position >= data.Length || data[position] > '6')
        {
            ThrowInvalidData(identifier, "does not carry a coupon funder ID length indicator of 0 to 6", nameof(data));
        }

        position += 1 + (data[position] - '0') + LengthIndicatorOffset;
        position += NorthAmericanCouponCode.OfferCodeLength;
        if (position >= data.Length)
        {
            ThrowInvalidData(identifier, "is too short for its coupon funder ID, offer code and serial number length indicator", nameof(data));
        }

        position += 1 + (data[position] - '0') + LengthIndicatorOffset;
        if (position != data.Length)
        {
            ThrowInvalidData(identifier, position > data.Length ? "is too short for its serial number" : "carries data after the serial number", nameof(data));
        }
    }
}
