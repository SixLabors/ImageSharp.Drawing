// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The content checks a component of a GS1 Application Identifier takes, by the names the GS1 Barcode Syntax Dictionary gives them.
/// </summary>
internal enum Gs1Linter
{
    /// <summary>
    /// The last digit is the GS1 check digit of the digits before it.
    /// </summary>
    Csum,

    /// <summary>
    /// The last two characters are the GS1 check character pair of the characters before them.
    /// </summary>
    CsumAlpha,

    /// <summary>
    /// The component starts with a GS1 Company Prefix.
    /// </summary>
    GcpPos1,

    /// <summary>
    /// The component starts with one digit and then a GS1 Company Prefix.
    /// </summary>
    GcpPos2,

    /// <summary>
    /// A date YYMMDD, where the day can be 00.
    /// </summary>
    Yymmd0,

    /// <summary>
    /// A date YYMMDD.
    /// </summary>
    Yymmdd,

    /// <summary>
    /// A date YYYYMMDD.
    /// </summary>
    Yyyymmdd,

    /// <summary>
    /// An hour HH.
    /// </summary>
    Hh,

    /// <summary>
    /// A minute MI.
    /// </summary>
    Mi,

    /// <summary>
    /// A second SS.
    /// </summary>
    Ss,

    /// <summary>
    /// A time HHMI.
    /// </summary>
    Hhmi,

    /// <summary>
    /// An ISO 4217 numeric currency code.
    /// </summary>
    Iso4217,

    /// <summary>
    /// An ISO 3166-1 numeric country code.
    /// </summary>
    Iso3166,

    /// <summary>
    /// An ISO 3166-1 numeric country code, or 999.
    /// </summary>
    Iso3166999,

    /// <summary>
    /// An ISO 3166-1 alpha-2 country code.
    /// </summary>
    Iso3166Alpha2,

    /// <summary>
    /// An ISO/IEC 5218 code for the sex of a person.
    /// </summary>
    Iso5218,

    /// <summary>
    /// Percent-encoding is well formed.
    /// </summary>
    PercentEncoded,

    /// <summary>
    /// The single digit 0 or 1.
    /// </summary>
    YesNo,

    /// <summary>
    /// Hyphens only.
    /// </summary>
    Hyphen,

    /// <summary>
    /// A latitude of ten digits, at most 1800000000.
    /// </summary>
    Latitude,

    /// <summary>
    /// A longitude of ten digits, at most 3599999999.
    /// </summary>
    Longitude,

    /// <summary>
    /// The importer index of a GS1 UIC: one character of the file-safe base64 alphabet.
    /// </summary>
    ImporterIndex,

    /// <summary>
    /// A package type code.
    /// </summary>
    PackageType,

    /// <summary>
    /// An AIDC media type.
    /// </summary>
    MediaType,

    /// <summary>
    /// A position in a sequence, N/M with 1 &lt;= N &lt;= M.
    /// </summary>
    PositionInSequence,

    /// <summary>
    /// Digits that are not all zero.
    /// </summary>
    NonZero,

    /// <summary>
    /// A winding direction: 0, 1 or 9.
    /// </summary>
    Winding,

    /// <summary>
    /// Zeros only.
    /// </summary>
    Zero,

    /// <summary>
    /// A piece number and a total count of equal length, with 1 &lt;= piece &lt;= total.
    /// </summary>
    PieceOfTotal,

    /// <summary>
    /// An International Bank Account Number.
    /// </summary>
    Iban,

    /// <summary>
    /// Digits with no leading zero.
    /// </summary>
    NoZeroPrefix,

    /// <summary>
    /// At least one character that is not a digit.
    /// </summary>
    HasNonDigit,

    /// <summary>
    /// The coupon code of Application Identifier (8110).
    /// </summary>
    CouponCode,

    /// <summary>
    /// The positive offer file coupon code of Application Identifier (8112).
    /// </summary>
    CouponPositiveOffer,
}
