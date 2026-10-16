// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The coupon code of GS1 Application Identifier (8110), as Table C of the North American Coupon Application Guideline of GS1 US defines it. The required
/// elements come first: the GS1 Company Prefix with its length indicator, the offer code, the save value with its length indicator, and the primary purchase
/// requirement with its length indicator, its code and its family code. Optional data fields 1, 2, 3, 4, 5, 6 and 9 follow, each at most once and in that
/// order, each led by its field number.
/// </summary>
internal static class NorthAmericanCouponCode
{
    /// <summary>
    /// The length the company prefix length indicator adds to. Table C: "an offset of 6 is added to the VLI to get the actual length".
    /// </summary>
    public const int CompanyPrefixOffset = 6;

    /// <summary>
    /// The length of the offer code. Table C: "The Offer Code is a 6-digit number".
    /// </summary>
    public const int OfferCodeLength = 6;

    /// <summary>
    /// The length the serial number length indicator and the retailer GS1 Company Prefix or GLN length indicator add to. Table C gives both elements 6 digits
    /// more than their indicator.
    /// </summary>
    private const int LengthIndicatorOffset = 6;

    /// <summary>
    /// The length indicator of a second or third purchase GS1 Company Prefix that stands for the primary GS1 Company Prefix, with no digits after it.
    /// </summary>
    private const int PrimaryCompanyPrefix = 9;

    /// <summary>
    /// Gets the number of days in each month of a year that is not a leap year.
    /// </summary>
    private static ReadOnlySpan<byte> DaysInMonth => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>
    /// Validates a coupon code against Table C of the guideline.
    /// </summary>
    /// <param name="data">The data of Application Identifier (8110).</param>
    /// <returns>The length of the primary GS1 Company Prefix, which follows the length indicator.</returns>
    /// <exception cref="ArgumentException">The coupon code is not numeric, an element is missing or out of range, or an optional field is unknown or out of
    /// order.</exception>
    public static int Validate(ReadOnlySpan<char> data)
    {
        BarcodeSymbology.ValidateDigits(data);

        // The required elements. The company prefix length indicator is 0 to 6, and the save value and purchase requirement indicators give lengths of 1 to 5
        // digits.
        CouponReader reader = new(data);
        int prefixLength = reader.Digit(0, 6, "primary GS1 Company Prefix length indicator") + CompanyPrefixOffset;
        reader.Skip(prefixLength, "primary GS1 Company Prefix");
        reader.Skip(OfferCodeLength, "offer code");
        reader.Skip(reader.Digit(1, 5, "save value length indicator"), "save value");
        reader.PurchaseRequirement("primary");

        int lastField = 0;
        int expiration = -1;
        while (!reader.AtEnd)
        {
            int field = reader.Digit(0, 9, "optional data field number");
            if (field <= lastField || field is 7 or 8 or 0)
            {
                throw new ArgumentException("The optional data fields of a coupon code are 1, 2, 3, 4, 5, 6 and 9, in increasing order.", nameof(data));
            }

            lastField = field;
            switch (field)
            {
                case 1:
                    // The second qualifying purchase: the additional purchase rules code, the requirement and a GS1 Company Prefix whose length indicator 9
                    // stands for the primary prefix.
                    _ = reader.Digit(0, 3, "additional purchase rules code");
                    reader.PurchaseRequirement("second");
                    reader.CompanyPrefix("second");
                    break;

                case 2:
                    reader.PurchaseRequirement("third");
                    reader.CompanyPrefix("third");
                    break;

                case 3:
                    expiration = reader.Date("expiration date");
                    break;

                case 4:
                    // Table C: the start date "must be earlier than or equal to the expiration date".
                    int start = reader.Date("start date");
                    if (expiration >= 0 && expiration < start)
                    {
                        throw new ArgumentException("The start date of a coupon code is later than its expiration date.", nameof(data));
                    }

                    break;

                case 5:
                    // The serial number is 6 to 15 digits, 6 more than its length indicator.
                    reader.Skip(reader.Digit(0, 9, "serial number length indicator") + LengthIndicatorOffset, "serial number");
                    break;

                case 6:
                    // The retailer GS1 Company Prefix or GLN is 7 to 13 digits, 6 more than its length indicator.
                    int retailerLength = reader.Digit(1, 7, "retailer GS1 Company Prefix or GLN length indicator") + LengthIndicatorOffset;
                    reader.Skip(retailerLength, "retailer GS1 Company Prefix or GLN");
                    break;

                default:
                    // The miscellaneous elements: the save value code, the item the save value applies to, the store coupon flag and the do not multiply flag.
                    int saveValueCode = reader.Digit(0, 6, "save value code");
                    if (saveValueCode is 3 or 4)
                    {
                        throw new ArgumentException($"The save value code of a coupon code is 0, 1, 2, 5 or 6; got {saveValueCode}.", nameof(data));
                    }

                    _ = reader.Digit(0, 2, "save value applies to item");
                    _ = reader.Digit(0, 9, "store coupon flag");
                    _ = reader.Digit(0, 1, "do not multiply flag");
                    break;
            }
        }

        return prefixLength;
    }

    /// <summary>
    /// Returns the name of an element for an error message: the purchase it belongs to, when it belongs to one, and its own name.
    /// </summary>
    /// <param name="which">The purchase the element belongs to, or <see langword="null"/>.</param>
    /// <param name="name">The name of the element.</param>
    /// <returns>The name for the message.</returns>
    private static string ElementName(string? which, string name)
        => which is null ? name : $"{which} purchase {name}";

    /// <summary>
    /// Throws the exception for a coupon code that breaks a rule of Table C. The exception names the parameter that holds the coupon code.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="paramName">The name of the parameter that holds the coupon code.</param>
    /// <exception cref="ArgumentException">Always thrown.</exception>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalid(string message, string paramName)
        => throw new ArgumentException(message, paramName);

    /// <summary>
    /// Reads the elements of a coupon code in order. Each read checks that the element is present and in range, and moves past it.
    /// </summary>
    private ref struct CouponReader
    {
        private readonly ReadOnlySpan<char> data;
        private int position;

        /// <summary>
        /// Initializes a new instance of the <see cref="CouponReader"/> struct at the start of the coupon code.
        /// </summary>
        /// <param name="data">The coupon code, all digits.</param>
        public CouponReader(ReadOnlySpan<char> data)
        {
            this.data = data;
            this.position = 0;
        }

        /// <summary>
        /// Gets a value indicating whether the reader is at the end of the coupon code.
        /// </summary>
        public readonly bool AtEnd => this.position >= this.data.Length;

        /// <summary>
        /// Reads one digit and checks its range.
        /// </summary>
        /// <param name="minimum">The smallest value.</param>
        /// <param name="maximum">The largest value.</param>
        /// <param name="name">The name of the element, for the error message.</param>
        /// <returns>The digit.</returns>
        public int Digit(int minimum, int maximum, string name)
            => this.Digit(minimum, maximum, null, name);

        /// <summary>
        /// Skips the digits of an element.
        /// </summary>
        /// <param name="count">The number of digits.</param>
        /// <param name="name">The name of the element, for the error message.</param>
        public void Skip(int count, string name)
            => this.Skip(count, null, name);

        /// <summary>
        /// Reads a purchase requirement: its length indicator of 1 to 5, the requirement, its code of 0 to 4 or 9, and its three digit family code.
        /// </summary>
        /// <param name="which">The purchase the requirement belongs to, for the error message.</param>
        public void PurchaseRequirement(string which)
        {
            this.Skip(this.Digit(1, 5, which, "requirement length indicator"), which, "requirement");
            int code = this.Digit(0, 9, which, "requirement code");
            if (code is 5 or 6 or 7 or 8)
            {
                ThrowInvalid($"The {which} purchase requirement code of a coupon code is 0 to 4 or 9; got {code}.", nameof(this.data));
            }

            this.Skip(3, which, "family code");
        }

        /// <summary>
        /// Reads a GS1 Company Prefix of a second or third purchase: its length indicator of 0 to 6, or 9 when the primary GS1 Company Prefix applies and no
        /// digits follow.
        /// </summary>
        /// <param name="which">The purchase the prefix belongs to, for the error message.</param>
        public void CompanyPrefix(string which)
        {
            int indicator = this.Digit(0, 9, which, "GS1 Company Prefix length indicator");
            if (indicator is 7 or 8)
            {
                ThrowInvalid($"The {which} purchase GS1 Company Prefix length indicator of a coupon code is 0 to 6 or 9; got {indicator}.", nameof(this.data));
            }

            if (indicator != PrimaryCompanyPrefix)
            {
                this.Skip(indicator + CompanyPrefixOffset, which, "GS1 Company Prefix");
            }
        }

        /// <summary>
        /// Reads a date of six digits, YYMMDD, with a month of 1 to 12 and a day of 1 to the last day of the month. A two digit year divisible by four is a
        /// leap year.
        /// </summary>
        /// <param name="name">The name of the date, for the error message.</param>
        /// <returns>The date as the number YYMMDD.</returns>
        public int Date(string name)
        {
            int start = this.position;
            this.Skip(6, name);
            ReadOnlySpan<char> date = this.data.Slice(start, 6);
            int year = ((date[0] - '0') * 10) + (date[1] - '0');
            int month = ((date[2] - '0') * 10) + (date[3] - '0');
            int day = ((date[4] - '0') * 10) + (date[5] - '0');
            if (month is < 1 or > 12)
            {
                ThrowInvalid($"The month of the {name} of a coupon code is 1 to 12; got {month}.", nameof(this.data));
            }

            int lastDay = month == 2 && year % 4 == 0 ? 29 : DaysInMonth[month - 1];
            if (day < 1 || day > lastDay)
            {
                ThrowInvalid($"The day of the {name} of a coupon code is 1 to {lastDay}; got {day}.", nameof(this.data));
            }

            return (year * 10000) + (month * 100) + day;
        }

        /// <summary>
        /// Reads one digit of an element of a second or third purchase and checks its range. The name of the element joins the purchase only when the error
        /// message is built.
        /// </summary>
        /// <param name="minimum">The smallest value.</param>
        /// <param name="maximum">The largest value.</param>
        /// <param name="which">The purchase the element belongs to, or <see langword="null"/> for an element of the coupon.</param>
        /// <param name="name">The name of the element, for the error message.</param>
        /// <returns>The digit.</returns>
        private int Digit(int minimum, int maximum, string? which, string name)
        {
            if (this.position >= this.data.Length)
            {
                ThrowInvalid($"The coupon code is too short: the {ElementName(which, name)} is missing.", nameof(this.data));
            }

            int value = this.data[this.position++] - '0';
            if (value < minimum || value > maximum)
            {
                ThrowInvalid($"The {ElementName(which, name)} of a coupon code is {minimum} to {maximum}; got {value}.", nameof(this.data));
            }

            return value;
        }

        /// <summary>
        /// Skips the digits of an element of a second or third purchase. The name of the element joins the purchase only when the error message is built.
        /// </summary>
        /// <param name="count">The number of digits.</param>
        /// <param name="which">The purchase the element belongs to, or <see langword="null"/> for an element of the coupon.</param>
        /// <param name="name">The name of the element, for the error message.</param>
        private void Skip(int count, string? which, string name)
        {
            if (this.position + count > this.data.Length)
            {
                ThrowInvalid($"The coupon code is too short: the {ElementName(which, name)} is cut off.", nameof(this.data));
            }

            this.position += count;
        }
    }
}
