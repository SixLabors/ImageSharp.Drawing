// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Globalization;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data of a Swiss QR Code, chapter 4 of the Swiss Implementation Guidelines for the QR-bill: the
/// character set of section 4.1.1, the separators of section 4.1.4, the data groups of section 4.1.5 and
/// the lines of Table 8, with the rules of chapter 2, sections 4.3 and 4.4 and Annex B. The checks follow
/// version 2.4 of the guidelines. Version 2.4 adds one rule to version 2.3, a QR reference in CHF only, so
/// data that passes the checks follows both versions.
/// </summary>
internal static class SwissQrBill
{
    /// <summary>
    /// The largest number of characters of the data, the separators included, section 6.2.
    /// </summary>
    public const int MaximumLength = 997;

    /// <summary>
    /// The number of lines up to the trailer, which every Swiss QR Code has, Table 8.
    /// </summary>
    private const int MandatoryLines = 31;

    /// <summary>
    /// The number of lines with the billing information and the two alternative procedures, Table 8.
    /// </summary>
    private const int MaximumLines = 34;

    /// <summary>
    /// The index of the account line, Table 8 line 4.
    /// </summary>
    private const int Account = 3;

    /// <summary>
    /// The index of the first line of the creditor, the address type, Table 8 lines 5 to 11.
    /// </summary>
    private const int Creditor = 4;

    /// <summary>
    /// The index of the first line of the ultimate creditor, Table 8 lines 12 to 18.
    /// </summary>
    private const int UltimateCreditor = 11;

    /// <summary>
    /// The index of the amount line, Table 8 line 19.
    /// </summary>
    private const int Amount = 18;

    /// <summary>
    /// The index of the currency line, Table 8 line 20.
    /// </summary>
    private const int Currency = 19;

    /// <summary>
    /// The index of the first line of the ultimate debtor, Table 8 lines 21 to 27.
    /// </summary>
    private const int UltimateDebtor = 20;

    /// <summary>
    /// The index of the reference type line, Table 8 line 28.
    /// </summary>
    private const int ReferenceType = 27;

    /// <summary>
    /// The index of the reference line, Table 8 line 29.
    /// </summary>
    private const int Reference = 28;

    /// <summary>
    /// The index of the unstructured message line, Table 8 line 30.
    /// </summary>
    private const int Message = 29;

    /// <summary>
    /// The index of the trailer line, Table 8 line 31.
    /// </summary>
    private const int Trailer = 30;

    /// <summary>
    /// The index of the billing information line, Table 8 line 32.
    /// </summary>
    private const int BillingInformation = 31;

    /// <summary>
    /// The number of lines of an address: the address type, the name, the street, the building number, the
    /// postal code, the town and the country.
    /// </summary>
    private const int AddressLines = 7;

    /// <summary>
    /// The longest unstructured message and billing information together, section 4.3.3.
    /// </summary>
    private const int MaximumAdditionalInformation = 140;

    /// <summary>
    /// The longest alternative procedure, Table 8 lines 33 and 34.
    /// </summary>
    private const int MaximumAlternativeProcedure = 100;

    /// <summary>
    /// The first QR-IID, section 2.8: "QR-IIDs consist exclusively of numbers from 30000 to 31999".
    /// </summary>
    private const int FirstQrIid = 30000;

    /// <summary>
    /// The last QR-IID, section 2.8.
    /// </summary>
    private const int LastQrIid = 31999;

    /// <summary>
    /// The characters of an IBAN in electronic form, ISO 13616: digits and capital letters.
    /// </summary>
    private static readonly SearchValues<char> IbanCharacters = SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");

    /// <summary>
    /// The alphanumeric characters of section 4.1.2: "A-Z a-z 0-9".
    /// </summary>
    private static readonly SearchValues<char> AlphanumericCharacters =
        SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    /// <summary>
    /// Gets the first row of the check digit matrix of Annex B, Figure 21: the carry-over that follows
    /// carry-over 0 and each digit 0 to 9. Each row of the matrix is this row moved left by its carry-over,
    /// so the carry-over that follows carry-over c and digit d is this entry at (c + d) mod 10.
    /// </summary>
    private static ReadOnlySpan<byte> CarryOver => [0, 9, 4, 6, 8, 2, 7, 1, 3, 5];

    /// <summary>
    /// Checks the data of a Swiss QR Code.
    /// </summary>
    /// <param name="text">The data, at most <see cref="MaximumLength"/> characters.</param>
    /// <exception cref="ArgumentException">The data does not follow the guidelines.</exception>
    public static void Validate(ReadOnlySpan<char> text)
    {
        // Section 4.1.1 permits U+0020 to U+007E, U+00A0 to U+017F, U+0218 to U+021B and U+20AC. Section
        // 4.1.4 separates the lines with CR LF or with LF, the same one throughout. Each line is held as its
        // start and its end in the text, two entries of the bounds.
        InlineArray68<int> boundStorage = default;
        Span<int> bounds = boundStorage;
        int lineCount = 0;
        int lineStart = 0;
        int position = 0;
        bool crLf = false;
        bool lf = false;
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            int value = current.Value;
            if (value is '\r' or '\n')
            {
                if (value == '\r' && (position + 1 >= text.Length || text[position + 1] != '\n'))
                {
                    throw new ArgumentException("A Swiss QR Code separates its lines with CR LF or with LF; got a CR without an LF.", nameof(text));
                }

                if (lineCount == MaximumLines)
                {
                    throw new ArgumentException($"A Swiss QR Code has at most {MaximumLines} lines, Table 8 of the QR-bill guidelines.", nameof(text));
                }

                bounds[2 * lineCount] = lineStart;
                bounds[(2 * lineCount) + 1] = position;
                lineCount++;
                if (value == '\r')
                {
                    crLf = true;
                    codePoints.MoveNext();
                    position++;
                }
                else
                {
                    lf = true;
                }

                position++;
                lineStart = position;
                continue;
            }

            if (value is not ((>= 0x20 and <= 0x7E) or (>= 0xA0 and <= 0x17F) or (>= 0x218 and <= 0x21B) or 0x20AC))
            {
                throw new ArgumentException(
                    $"A Swiss QR Code carries only the characters of section 4.1.1 of the QR-bill guidelines; got {current.ToDisplayString()}.",
                    nameof(text));
            }

            position += current.Utf16SequenceLength;
        }

        if (crLf && lf)
        {
            throw new ArgumentException("A Swiss QR Code separates all its lines with CR LF or all with LF.", nameof(text));
        }

        if (lineCount == MaximumLines)
        {
            throw new ArgumentException($"A Swiss QR Code has at most {MaximumLines} lines, Table 8 of the QR-bill guidelines.", nameof(text));
        }

        bounds[2 * lineCount] = lineStart;
        bounds[(2 * lineCount) + 1] = text.Length;
        lineCount++;
        if (lineCount < MandatoryLines)
        {
            throw new ArgumentException(
                $"A Swiss QR Code has the {MandatoryLines} lines up to the trailer of Table 8 of the QR-bill guidelines; got {lineCount}.",
                nameof(text));
        }

        // Table 8 lines 1 to 3, the header, of fixed values.
        CheckFixed(text, bounds, 0, "SPC", "the QR type");
        CheckFixed(text, bounds, 1, "0200", "the version");
        CheckFixed(text, bounds, 2, "1", "the coding type");

        // Table 8 line 4: "Fixed length: 21 alphanumeric characters, no spaces allowed, only IBANs with CH or
        // LI country code permitted", an IBAN of ISO 13616 with its MOD 97-10 check digits. Positions 5 to 9
        // of a Swiss or Liechtenstein IBAN are the institution identification, and section 2.8 reserves 30000
        // to 31999 for the QR-IID of a QR-IBAN.
        ReadOnlySpan<char> account = Line(text, bounds, Account);
        if (account.Length != 21
            || !(account.StartsWith("CH") || account.StartsWith("LI"))
            || account.ContainsAnyExcept(IbanCharacters)
            || Iso7064.Mod97(account) != 1)
        {
            throw new ArgumentException(
                $"Line 4 of a Swiss QR Code, the account, is an IBAN of 21 characters with the country code CH or LI and valid check digits; got '{account}'.",
                nameof(text));
        }

        ReadOnlySpan<char> iid = account[4..9];
        bool qrIban = !iid.ContainsAnyExceptInRange('0', '9') && int.Parse(iid, CultureInfo.InvariantCulture) is >= FirstQrIid and <= LastQrIid;

        // Table 8 lines 5 to 11, the creditor, a mandatory group. Section 4.3.1: "Postal code", "Town" and
        // "Country" must be filled in.
        CheckAddress(text, bounds, Creditor, "the creditor");

        // Table 8 lines 12 to 18, the ultimate creditor: "The entire data group must not be filled in".
        for (int i = UltimateCreditor; i < UltimateCreditor + AddressLines; i++)
        {
            if (!Line(text, bounds, i).IsEmpty)
            {
                throw new ArgumentException($"Line {i + 1} of a Swiss QR Code belongs to the ultimate creditor, which must not be filled in.", nameof(text));
            }
        }

        // Table 8 line 20: "Only CHF and EUR are permitted".
        ReadOnlySpan<char> currency = Line(text, bounds, Currency);
        bool chf = currency.SequenceEqual("CHF");
        if (!chf && !currency.SequenceEqual("EUR"))
        {
            throw new ArgumentException($"Line 20 of a Swiss QR Code, the currency, is CHF or EUR; got '{currency}'.", nameof(text));
        }

        // Table 8 lines 21 to 27, the ultimate debtor, an optional group. Section 4.1.5: "If a data group is
        // used, in those marked with "Optional", all sub-elements marked as "Dependent" must be filled."
        for (int i = UltimateDebtor; i < UltimateDebtor + AddressLines; i++)
        {
            if (!Line(text, bounds, i).IsEmpty)
            {
                CheckAddress(text, bounds, UltimateDebtor, "the ultimate debtor");
                break;
            }
        }

        // Table 8 lines 28 and 29, the reference type and the reference. Sections 2.10, 2.12 and 4.3.2: a QR
        // reference goes with a QR-IBAN and in CHF only, a Creditor Reference with an IBAN, and no reference
        // with an IBAN.
        ReadOnlySpan<char> referenceType = Line(text, bounds, ReferenceType);
        ReadOnlySpan<char> reference = Line(text, bounds, Reference);
        bool qrReference = referenceType.SequenceEqual("QRR");
        if (!qrReference && !referenceType.SequenceEqual("SCOR") && !referenceType.SequenceEqual("NON"))
        {
            throw new ArgumentException($"Line 28 of a Swiss QR Code, the reference type, is QRR, SCOR or NON; got '{referenceType}'.", nameof(text));
        }

        if (qrReference != qrIban)
        {
            throw new ArgumentException(
                qrIban
                    ? "A Swiss QR Code with a QR-IBAN, an IBAN with a QR-IID of 30000 to 31999, has the reference type QRR."
                    : $"A Swiss QR Code with the reference type QRR has a QR-IBAN, an IBAN with a QR-IID of 30000 to 31999; got '{account}'.",
                nameof(text));
        }

        if (qrReference)
        {
            if (!chf)
            {
                throw new ArgumentException("A Swiss QR Code with the reference type QRR is in CHF.", nameof(text));
            }

            // Section 2.12.1: "26 numerical characters followed by a modulo 10 recursive check digit (see
            // Annex B)", and "The reference must not consist exclusively of zeros". Annex B: from carry-over
            // 0, each digit from the left takes the next carry-over from the matrix, and the check digit is
            // the last column of the row of the final carry-over, 10 less the carry-over, mod 10.
            if (reference.Length != 27 || reference.ContainsAnyExceptInRange('0', '9') || !reference[..26].ContainsAnyExcept('0'))
            {
                throw new ArgumentException(
                    $"Line 29 of a Swiss QR Code, the reference, is a QR reference of 27 digits, not all zeros; got '{reference}'.",
                    nameof(text));
            }

            int carry = 0;
            for (int i = 0; i < 26; i++)
            {
                carry = CarryOver[(carry + (reference[i] - '0')) % 10];
            }

            int check = (10 - carry) % 10;
            if (reference[26] - '0' != check)
            {
                throw new ArgumentException(
                    $"Line 29 of a Swiss QR Code, the QR reference, ends with the check digit {check}; got {reference[26]}.",
                    nameof(text));
            }
        }
        else if (referenceType.SequenceEqual("SCOR"))
        {
            // Section 2.12.2: "a minimum of 5 and a maximum of 25 alphanumerical characters. Starting with RF,
            // followed by the check digits (3rd and 4th digit)", calculated "with modulo 97-10". Table 8: "The
            // banks draw no distinction between lower and upper case when processing."
            if (reference.Length is < 5 or > 25
                || !reference.StartsWith("RF", StringComparison.OrdinalIgnoreCase)
                || reference.ContainsAnyExcept(AlphanumericCharacters)
                || Iso7064.Mod97(reference) != 1)
            {
                throw new ArgumentException(
                    $"Line 29 of a Swiss QR Code, the reference, is a Creditor Reference: RF, valid check digits, 5 to 25 characters; got '{reference}'.",
                    nameof(text));
            }
        }
        else if (!reference.IsEmpty)
        {
            throw new ArgumentException("Line 29 of a Swiss QR Code, the reference, is empty for the reference type NON.", nameof(text));
        }

        // Table 8 line 30, the unstructured message of at most 140 characters, and line 31, the trailer.
        ReadOnlySpan<char> message = Line(text, bounds, Message);
        CheckLength(text, bounds, Message, 0, MaximumAdditionalInformation, "the unstructured message");
        CheckFixed(text, bounds, Trailer, "EPD", "the trailer");

        // Table 8 line 19, the amount: "without leading zeroes, including decimal separators and two decimal
        // places. Decimal, maximum 12 digits permitted, including decimal separators. Only decimal points
        // (".") are permitted as decimal separators. The amount must be between CHF/EUR 0.01 and
        // 999,999,999.99." Section 4.4 fills the amount 0.00 for a notification, with one of the texts of
        // Table 10 as the additional information.
        ReadOnlySpan<char> amount = Line(text, bounds, Amount);
        if (!amount.IsEmpty)
        {
            int point = amount.Length - 3;
            if (amount.Length > 12
                || point < 1
                || amount[point] != '.'
                || amount[..point].ContainsAnyExceptInRange('0', '9')
                || amount[(point + 1)..].ContainsAnyExceptInRange('0', '9')
                || (amount[0] == '0' && point > 1))
            {
                throw new ArgumentException(
                    $"Line 19 of a Swiss QR Code, the amount, has no leading zeros, a point, two decimals, at most 12 characters; got '{amount}'.",
                    nameof(text));
            }

            if (amount.SequenceEqual("0.00") && !IsNotification(message))
            {
                throw new ArgumentException(
                    "Line 19 of a Swiss QR Code, the amount, is at least 0.01, or 0.00 with a DO NOT USE FOR PAYMENT text of section 4.4 as the message.",
                    nameof(text));
            }
        }

        // Table 8 line 32, the billing information, and lines 33 and 34, the alternative procedures, are
        // omitted when they are not used. Section 4.3.3: the billing information "always begins with "//"
        // (slash slash) followed by the double-digit, abbreviated name" of its rules, and the message and the
        // billing information together "must not exceed 140 characters".
        if (lineCount > BillingInformation)
        {
            ReadOnlySpan<char> billing = Line(text, bounds, BillingInformation);
            if (!billing.IsEmpty && (billing.Length < 4 || !billing.StartsWith("//") || billing[2..4].ContainsAnyExcept(AlphanumericCharacters)))
            {
                throw new ArgumentException(
                    $"Line 32 of a Swiss QR Code, the billing information, begins with // and a name of two characters; got '{billing}'.",
                    nameof(text));
            }

            int additional = message.Length + billing.Length;
            if (additional > MaximumAdditionalInformation)
            {
                throw new ArgumentException(
                    $"The message and the billing information of a Swiss QR Code are at most {MaximumAdditionalInformation} characters; got {additional}.",
                    nameof(text));
            }
        }

        for (int i = BillingInformation + 1; i < lineCount; i++)
        {
            CheckLength(text, bounds, i, 0, MaximumAlternativeProcedure, "an alternative procedure");
        }
    }

    /// <summary>
    /// Returns the characters of a line.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="bounds">The start and the end of each line.</param>
    /// <param name="index">The index of the line, its number in Table 8 less one.</param>
    /// <returns>The characters.</returns>
    private static ReadOnlySpan<char> Line(ReadOnlySpan<char> text, ReadOnlySpan<int> bounds, int index)
        => text[bounds[2 * index]..bounds[(2 * index) + 1]];

    /// <summary>
    /// Checks a line that holds a fixed value.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="bounds">The start and the end of each line.</param>
    /// <param name="index">The index of the line.</param>
    /// <param name="expected">The fixed value.</param>
    /// <param name="element">The element the line holds, for the message.</param>
    /// <exception cref="ArgumentException">The line holds another value.</exception>
    private static void CheckFixed(ReadOnlySpan<char> text, ReadOnlySpan<int> bounds, int index, string expected, string element)
        => CheckFixed(text, bounds, index, expected, element, null);

    /// <summary>
    /// Checks a line that holds a fixed value of the address of a party. The message names the element and
    /// the party only when the check fails, so a valid line builds no text.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="bounds">The start and the end of each line.</param>
    /// <param name="index">The index of the line.</param>
    /// <param name="expected">The fixed value.</param>
    /// <param name="element">The element the line holds, for the message.</param>
    /// <param name="party">The party the address belongs to, for the message, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">The line holds another value.</exception>
    private static void CheckFixed(ReadOnlySpan<char> text, ReadOnlySpan<int> bounds, int index, string expected, string element, string? party)
    {
        ReadOnlySpan<char> line = Line(text, bounds, index);
        if (!line.SequenceEqual(expected))
        {
            throw new ArgumentException($"Line {index + 1} of a Swiss QR Code, {Describe(element, party)}, is '{expected}'; got '{line}'.", nameof(text));
        }
    }

    /// <summary>
    /// Checks the length of a line. Section 4.1.3: "It is not permitted to fill in the elements with blanks
    /// up to the maximum length", so a line of the maximum length does not end with a blank.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="bounds">The start and the end of each line.</param>
    /// <param name="index">The index of the line.</param>
    /// <param name="minimum">The fewest characters.</param>
    /// <param name="maximum">The most characters.</param>
    /// <param name="element">The element the line holds, for the message.</param>
    /// <exception cref="ArgumentException">The line is shorter or longer, or blanks fill it to the maximum length.</exception>
    private static void CheckLength(ReadOnlySpan<char> text, ReadOnlySpan<int> bounds, int index, int minimum, int maximum, string element)
        => CheckLength(text, bounds, index, minimum, maximum, element, null);

    /// <summary>
    /// Checks the length of a line of the address of a party, as the overload without a party does. The
    /// message names the element and the party only when the check fails, so a valid line builds no text.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="bounds">The start and the end of each line.</param>
    /// <param name="index">The index of the line.</param>
    /// <param name="minimum">The fewest characters.</param>
    /// <param name="maximum">The most characters.</param>
    /// <param name="element">The element the line holds, for the message.</param>
    /// <param name="party">The party the address belongs to, for the message, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">The line is shorter or longer, or blanks fill it to the maximum length.</exception>
    private static void CheckLength(ReadOnlySpan<char> text, ReadOnlySpan<int> bounds, int index, int minimum, int maximum, string element, string? party)
    {
        ReadOnlySpan<char> line = Line(text, bounds, index);
        int length = line.Length;
        if (length < minimum || length > maximum)
        {
            throw new ArgumentException(
                minimum == 0
                    ? $"Line {index + 1} of a Swiss QR Code, {Describe(element, party)}, is at most {maximum} characters; got {length}."
                    : $"Line {index + 1} of a Swiss QR Code, {Describe(element, party)}, is {minimum} to {maximum} characters; got {length}.",
                nameof(text));
        }

        if (length == maximum && line[^1] == ' ')
        {
            throw new ArgumentException(
                $"Line {index + 1} of a Swiss QR Code, {Describe(element, party)}, is padded with blanks to {maximum} characters, which section 4.1.3 forbids.",
                nameof(text));
        }
    }

    /// <summary>
    /// Names an element for a message: the element alone, or the element of a party.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="party">The party, or <see langword="null"/>.</param>
    /// <returns>The name.</returns>
    private static string Describe(string element, string? party)
        => party is null ? element : $"{element} of {party}";

    /// <summary>
    /// Checks the seven lines of a structured address, Table 8: the address type "S", a name of 1 to 70
    /// characters, a street of at most 70, a building number of at most 16, a postal code of 1 to 16, a town
    /// of 1 to 35 and an ISO 3166-1 alpha-2 country code.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="bounds">The start and the end of each line.</param>
    /// <param name="first">The index of the address type line.</param>
    /// <param name="party">The party the address belongs to, for the message.</param>
    /// <exception cref="ArgumentException">A line does not follow its definition.</exception>
    private static void CheckAddress(ReadOnlySpan<char> text, ReadOnlySpan<int> bounds, int first, string party)
    {
        CheckFixed(text, bounds, first, "S", "the address type", party);
        CheckLength(text, bounds, first + 1, 1, 70, "the name", party);
        CheckLength(text, bounds, first + 2, 0, 70, "the street", party);
        CheckLength(text, bounds, first + 3, 0, 16, "the building number", party);
        CheckLength(text, bounds, first + 4, 1, 16, "the postal code", party);
        CheckLength(text, bounds, first + 5, 1, 35, "the town", party);

        ReadOnlySpan<char> country = Line(text, bounds, first + 6);
        if (!Iso3166.IsAlpha2Code(country))
        {
            throw new ArgumentException(
                $"Line {first + 7} of a Swiss QR Code, the country of {party}, is an ISO 3166-1 alpha-2 country code; got '{country}'.",
                nameof(text));
        }
    }

    /// <summary>
    /// Returns whether the message is one of the texts of Table 10 of section 4.4, which a notification
    /// that must not be paid carries in capital letters.
    /// </summary>
    /// <param name="message">The unstructured message.</param>
    /// <returns><see langword="true"/> when the message is such a text.</returns>
    private static bool IsNotification(ReadOnlySpan<char> message)
        => message.SequenceEqual("NICHT ZUR ZAHLUNG VERWENDEN")
        || message.SequenceEqual("NE PAS UTILISER POUR LE PAIEMENT")
        || message.SequenceEqual("NON UTILIZZARE PER IL PAGAMENTO")
        || message.SequenceEqual("DO NOT USE FOR PAYMENT")
        || message.SequenceEqual("BETG DUVRAR PER IL PAJAMENT");
}
