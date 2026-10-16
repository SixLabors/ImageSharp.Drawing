// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The coupon code of GS1 Application Identifier (8110), as Table C of the North American Coupon Application
/// Guideline of GS1 US defines it. The required elements come first: the GS1 Company Prefix with its length
/// indicator, the offer code, the save value with its length indicator, and the primary purchase requirement
/// with its length indicator, its code and its family code. Optional data fields 1, 2, 3, 4, 5, 6 and 9
/// follow, each at most once and in that order, each led by its field number.
/// </summary>
internal static class NorthAmericanCouponCode
{
    /// <summary>
    /// The length the company prefix length indicator adds to. Table C: "an offset of 6 is added to the VLI to
    /// get the actual length".
    /// </summary>
    public const int CompanyPrefixOffset = 6;

    /// <summary>
    /// The length of the offer code. Table C: "The Offer Code is a 6-digit number".
    /// </summary>
    public const int OfferCodeLength = 6;

    /// <summary>
    /// Gets the number of days in each month of a year that is not a leap year.
    /// </summary>
    private static ReadOnlySpan<byte> DaysInMonth => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>
    /// Validates a coupon code against Table C of the guideline.
    /// </summary>
    /// <param name="data">The data of Application Identifier (8110).</param>
    /// <returns>The length of the primary GS1 Company Prefix, which follows the length indicator.</returns>
    /// <exception cref="ArgumentException">The coupon code is not numeric, an element is missing or out of range, or an optional field is unknown or out of order.</exception>
    public static int Validate(ReadOnlySpan<char> data)
    {
        BarcodeSymbology.ValidateDigits(data);

        int position = 0;

        // The required elements. The company prefix length indicator is 0 to 6, and the save value and
        // purchase requirement indicators give lengths of 1 to 5 digits.
        int prefixLength = Digit(data, ref position, 0, 6, "primary GS1 Company Prefix length indicator") + CompanyPrefixOffset;
        Skip(data, ref position, prefixLength, "primary GS1 Company Prefix");
        Skip(data, ref position, OfferCodeLength, "offer code");
        Skip(data, ref position, Digit(data, ref position, 1, 5, "save value length indicator"), "save value");
        PurchaseRequirement(data, ref position, "primary");

        int lastField = 0;
        int expiration = -1;
        while (position < data.Length)
        {
            int field = data[position] - '0';
            if (field <= lastField || field is 7 or 8 or 0)
            {
                throw new ArgumentException("The optional data fields of a coupon code are 1, 2, 3, 4, 5, 6 and 9, in increasing order.", nameof(data));
            }

            position++;
            lastField = field;
            switch (field)
            {
                case 1:
                    // The second qualifying purchase: the additional purchase rules code, the requirement and
                    // a GS1 Company Prefix whose length indicator 9 stands for the primary prefix.
                    _ = Digit(data, ref position, 0, 3, "additional purchase rules code");
                    PurchaseRequirement(data, ref position, "second");
                    CompanyPrefix(data, ref position, "second");
                    break;

                case 2:
                    PurchaseRequirement(data, ref position, "third");
                    CompanyPrefix(data, ref position, "third");
                    break;

                case 3:
                    expiration = Date(data, ref position, "expiration date");
                    break;

                case 4:
                    // Table C: the start date "must be earlier than or equal to the expiration date".
                    int start = Date(data, ref position, "start date");
                    if (expiration >= 0 && expiration < start)
                    {
                        throw new ArgumentException("The start date of a coupon code is later than its expiration date.", nameof(data));
                    }

                    break;

                case 5:
                    // The serial number is 6 to 15 digits, 6 more than its length indicator.
                    Skip(data, ref position, Digit(data, ref position, 0, 9, "serial number length indicator") + 6, "serial number");
                    break;

                case 6:
                    // The retailer GS1 Company Prefix or GLN is 7 to 13 digits, 6 more than its length indicator.
                    int retailerLength = Digit(data, ref position, 1, 7, "retailer GS1 Company Prefix or GLN length indicator") + 6;
                    Skip(data, ref position, retailerLength, "retailer GS1 Company Prefix or GLN");
                    break;

                default:
                    // The miscellaneous elements: the save value code, the item the save value applies to, the
                    // store coupon flag and the do not multiply flag.
                    int saveValueCode = Digit(data, ref position, 0, 6, "save value code");
                    if (saveValueCode is 3 or 4)
                    {
                        throw new ArgumentException($"The save value code of a coupon code is 0, 1, 2, 5 or 6; got {saveValueCode}.", nameof(data));
                    }

                    _ = Digit(data, ref position, 0, 2, "save value applies to item");
                    _ = Digit(data, ref position, 0, 9, "store coupon flag");
                    _ = Digit(data, ref position, 0, 1, "do not multiply flag");
                    break;
            }
        }

        return prefixLength;
    }

    /// <summary>
    /// Reads a purchase requirement: its length indicator of 1 to 5, the requirement, its code of 0 to 4 or 9,
    /// and its three digit family code.
    /// </summary>
    /// <param name="data">The coupon code.</param>
    /// <param name="position">The read position.</param>
    /// <param name="which">The purchase the requirement belongs to, for the error message.</param>
    private static void PurchaseRequirement(ReadOnlySpan<char> data, ref int position, string which)
    {
        Skip(data, ref position, Digit(data, ref position, 1, 5, which, "requirement length indicator"), which, "requirement");
        int code = Digit(data, ref position, 0, 9, which, "requirement code");
        if (code is 5 or 6 or 7 or 8)
        {
            throw new ArgumentException($"The {which} purchase requirement code of a coupon code is 0 to 4 or 9; got {code}.", nameof(data));
        }

        Skip(data, ref position, 3, which, "family code");
    }

    /// <summary>
    /// Reads a GS1 Company Prefix of a second or third purchase: its length indicator of 0 to 6, or 9 when the
    /// primary GS1 Company Prefix applies and no digits follow.
    /// </summary>
    /// <param name="data">The coupon code.</param>
    /// <param name="position">The read position.</param>
    /// <param name="which">The purchase the prefix belongs to, for the error message.</param>
    private static void CompanyPrefix(ReadOnlySpan<char> data, ref int position, string which)
    {
        int indicator = Digit(data, ref position, 0, 9, which, "GS1 Company Prefix length indicator");
        if (indicator is 7 or 8)
        {
            throw new ArgumentException(
                $"The {which} purchase GS1 Company Prefix length indicator of a coupon code is 0 to 6 or 9; got {indicator}.",
                nameof(data));
        }

        if (indicator != 9)
        {
            Skip(data, ref position, indicator + CompanyPrefixOffset, which, "GS1 Company Prefix");
        }
    }

    /// <summary>
    /// Reads a date of six digits, YYMMDD, with a month of 1 to 12 and a day of 1 to the last day of the month.
    /// A two digit year divisible by four is a leap year.
    /// </summary>
    /// <param name="data">The coupon code.</param>
    /// <param name="position">The read position.</param>
    /// <param name="name">The name of the date, for the error message.</param>
    /// <returns>The date as the number YYMMDD.</returns>
    private static int Date(ReadOnlySpan<char> data, ref int position, string name)
    {
        int start = position;
        Skip(data, ref position, 6, name);
        int year = ((data[start] - '0') * 10) + (data[start + 1] - '0');
        int month = ((data[start + 2] - '0') * 10) + (data[start + 3] - '0');
        int day = ((data[start + 4] - '0') * 10) + (data[start + 5] - '0');
        if (month is < 1 or > 12)
        {
            throw new ArgumentException($"The month of the {name} of a coupon code is 1 to 12; got {month}.", nameof(data));
        }

        int lastDay = month == 2 && year % 4 == 0 ? 29 : DaysInMonth[month - 1];
        if (day < 1 || day > lastDay)
        {
            throw new ArgumentException($"The day of the {name} of a coupon code is 1 to {lastDay}; got {day}.", nameof(data));
        }

        return (year * 10000) + (month * 100) + day;
    }

    /// <summary>
    /// Reads one digit and checks its range.
    /// </summary>
    /// <param name="data">The coupon code.</param>
    /// <param name="position">The read position.</param>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="name">The name of the element, for the error message.</param>
    /// <returns>The digit.</returns>
    private static int Digit(ReadOnlySpan<char> data, ref int position, int minimum, int maximum, string name)
        => Digit(data, ref position, minimum, maximum, null, name);

    /// <summary>
    /// Reads one digit of an element of a second or third purchase and checks its range. The name of the
    /// element joins the purchase only when the error message is built.
    /// </summary>
    /// <param name="data">The coupon code.</param>
    /// <param name="position">The read position.</param>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="which">The purchase the element belongs to, or <see langword="null"/> for an element of the coupon.</param>
    /// <param name="name">The name of the element, for the error message.</param>
    /// <returns>The digit.</returns>
    private static int Digit(ReadOnlySpan<char> data, ref int position, int minimum, int maximum, string? which, string name)
    {
        if (position >= data.Length)
        {
            throw new ArgumentException($"The coupon code is too short: the {ElementName(which, name)} is missing.", nameof(data));
        }

        int value = data[position++] - '0';
        if (value < minimum || value > maximum)
        {
            throw new ArgumentException($"The {ElementName(which, name)} of a coupon code is {minimum} to {maximum}; got {value}.", nameof(data));
        }

        return value;
    }

    /// <summary>
    /// Skips the digits of an element.
    /// </summary>
    /// <param name="data">The coupon code.</param>
    /// <param name="position">The read position.</param>
    /// <param name="count">The number of digits.</param>
    /// <param name="name">The name of the element, for the error message.</param>
    private static void Skip(ReadOnlySpan<char> data, ref int position, int count, string name)
        => Skip(data, ref position, count, null, name);

    /// <summary>
    /// Skips the digits of an element of a second or third purchase. The name of the element joins the
    /// purchase only when the error message is built.
    /// </summary>
    /// <param name="data">The coupon code.</param>
    /// <param name="position">The read position.</param>
    /// <param name="count">The number of digits.</param>
    /// <param name="which">The purchase the element belongs to, or <see langword="null"/> for an element of the coupon.</param>
    /// <param name="name">The name of the element, for the error message.</param>
    private static void Skip(ReadOnlySpan<char> data, ref int position, int count, string? which, string name)
    {
        if (position + count > data.Length)
        {
            throw new ArgumentException($"The coupon code is too short: the {ElementName(which, name)} is cut off.", nameof(data));
        }

        position += count;
    }

    /// <summary>
    /// Returns the name of an element for an error message: the purchase it belongs to, when it belongs to
    /// one, and its own name.
    /// </summary>
    /// <param name="which">The purchase the element belongs to, or <see langword="null"/>.</param>
    /// <param name="name">The name of the element.</param>
    /// <returns>The name for the message.</returns>
    private static string ElementName(string? which, string name)
        => which is null ? name : $"{which} purchase {name}";
}
