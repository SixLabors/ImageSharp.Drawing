// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the EAN/UPC symbology family as specified in section 5.2 of the GS1 General Specifications. Symbol characters are seven modules
/// wide, and each carries one digit through number set A, B or C. The choice of number set carries the implied extra digit of EAN-13 and the check digit of
/// UPC-E.
/// </summary>
internal static class EanUpcEncoder
{
    /// <summary>
    /// The number of modules a guard pattern extends below the digit bars. Section 5.2.3.2 of the GS1 General Specifications: "the bars (dark bars) forming
    /// the left, centre and right guard bar patterns SHALL be extended downward by 5x".
    /// </summary>
    public const float GuardExtension = 5F;

    /// <summary>
    /// The nominal X dimension in millimetres. Section 5.2.6.6 of the GS1 General Specifications: "X-dimension = 0.330 mm".
    /// </summary>
    public const double NominalXDimension = 0.33;

    /// <summary>
    /// The smallest X dimension in millimetres of an EAN-13, EAN-8, UPC-A or UPC-E symbol and its add-on, the smallest minimum of the symbol specification
    /// tables of section 5.12.3 that list them: 0.170 in Table 5-52. Section 5.2.3.3: "The X-dimension of an add-on symbol SHALL be the same as the
    /// X-dimension of its associated main symbol", so the add-on takes the same range.
    /// </summary>
    public const double MinimumXDimension = 0.170;

    /// <summary>
    /// The largest X dimension in millimetres of an EAN-13, EAN-8, UPC-A or UPC-E symbol and its add-on, the maximum of every symbol specification table that
    /// lists them.
    /// </summary>
    public const double MaximumXDimension = 0.660;

    /// <summary>
    /// The nominal EAN-13, UPC-A and UPC-E bar height in modules: section 5.2.3.2 of the GS1 General Specifications gives 22.85mm at the nominal 0.33mm
    /// X-dimension.
    /// </summary>
    public const float NominalBarHeight = 69.24F;

    /// <summary>
    /// The nominal EAN-8 bar height in modules: section 5.2.3.2 of the GS1 General Specifications gives 18.23mm at the nominal 0.33mm X-dimension.
    /// </summary>
    public const float NominalEan8BarHeight = 55.24F;

    /// <summary>
    /// The nominal add-on bar height in modules: Figures 5-27 and 5-28 of the GS1 General Specifications give 21.90mm at the nominal 0.33mm X-dimension.
    /// </summary>
    public const float NominalAddOnBarHeight = 66.36F;

    /// <summary>
    /// The width of an EAN-8 symbol in modules.
    /// </summary>
    public const int Ean8Width = 67;

    /// <summary>
    /// The width of an EAN-13 symbol in modules, which a UPC-A symbol shares.
    /// </summary>
    public const int Ean13Width = 95;

    /// <summary>
    /// The width of a symbol character in modules.
    /// </summary>
    public const int CharacterWidth = 7;

    /// <summary>
    /// The number of run units in a module. Section 5.2.3.1 of the GS1 General Specifications: for the characters 1, 2, 7 and 8 "the bars (dark bars) and
    /// spaces (light bars) are reduced or enlarged by one-thirteenth of a module", so a run counts thirteenths of a module.
    /// </summary>
    public const int RunUnitsPerModule = 13;

    /// <summary>
    /// The width of a run unit in modules.
    /// </summary>
    public const float RunUnit = 1F / RunUnitsPerModule;

    /// <summary>
    /// The normal guard pattern that opens and closes a symbol: bar, space, bar.
    /// </summary>
    public const int NormalGuard = 0b101;

    /// <summary>
    /// The width of <see cref="NormalGuard"/> in modules.
    /// </summary>
    public const int NormalGuardWidth = 3;

    /// <summary>
    /// The centre guard pattern between the two halves of a symbol: space, bar, space, bar, space.
    /// </summary>
    private const int CentreGuard = 0b01010;

    /// <summary>
    /// The width of <see cref="CentreGuard"/> in modules.
    /// </summary>
    private const int CentreGuardWidth = 5;

    /// <summary>
    /// The quiet zone before an EAN-13 symbol in modules, which holds the leading digit.
    /// </summary>
    private const int Ean13LeadingQuietZone = 11;

    /// <summary>
    /// The quiet zone after an EAN-13 symbol, and on both sides of an EAN-8 symbol, in modules.
    /// </summary>
    private const int QuietZone = 7;

    /// <summary>
    /// Gets the indexes of the extended bars of an EAN-13 symbol: the two bars of each of the left, centre and right guard patterns.
    /// </summary>
    private static ReadOnlySpan<int> Ean13GuardBars => [0, 1, 14, 15, 28, 29];

    /// <summary>
    /// Gets the indexes of the extended bars of an EAN-8 symbol.
    /// </summary>
    private static ReadOnlySpan<int> Ean8GuardBars => [0, 1, 10, 11, 20, 21];

    /// <summary>
    /// Gets the number set A symbol characters indexed by digit, seven modules per character with the most significant bit first and 1 meaning a dark module.
    /// Section 5.2.1.2.1 of the GS1 General Specifications: "Symbol characters in number set A are odd parity characters." Every character starts with a
    /// space module and ends with a bar module.
    /// </summary>
    public static ReadOnlySpan<byte> NumberSetA =>
    [
        0b0001101, 0b0011001, 0b0010011, 0b0111101, 0b0100011,
        0b0110001, 0b0101111, 0b0111011, 0b0110111, 0b0001011,
    ];

    /// <summary>
    /// Gets the number set B symbol characters indexed by digit. Section 5.2.1.2.1 of the GS1 General Specifications gives number set B even parity, and
    /// "Number set C characters are mirror images of number set B characters", so each character is number set C for the same digit, read in reverse module
    /// order.
    /// </summary>
    public static ReadOnlySpan<byte> NumberSetB =>
    [
        0b0100111, 0b0110011, 0b0011011, 0b0100001, 0b0011101,
        0b0111001, 0b0000101, 0b0010001, 0b0001001, 0b0010111,
    ];

    /// <summary>
    /// Gets the number set C symbol characters indexed by digit. Table 5-3 of the GS1 General Specifications gives number set C the element widths of number
    /// set A with bars and spaces exchanged, so each character is the module-wise inverse of number set A. Every character starts with a bar module and ends
    /// with a space module.
    /// </summary>
    public static ReadOnlySpan<byte> NumberSetC =>
    [
        0b1110010, 0b1100110, 0b1101100, 0b1000010, 0b1011100,
        0b1001110, 0b1010000, 0b1000100, 0b1001000, 0b1110100,
    ];

    /// <summary>
    /// Gets the number set sequence for the six left-half characters of an EAN-13 symbol, indexed by the leading digit. A set bit selects number set B, a clear
    /// bit number set A, most significant bit first. Table 5-5 of the GS1 General Specifications carries the thirteenth digit through this variable parity. The
    /// leading digit has no symbol character of its own.
    /// </summary>
    public static ReadOnlySpan<byte> Ean13LeftParity =>
    [
        0b000000, 0b001011, 0b001101, 0b001110, 0b010011,
        0b011001, 0b011100, 0b010101, 0b010110, 0b011010,
    ];

    /// <summary>
    /// Gets the number set sequence for the six characters of a UPC-E symbol, indexed by the check digit, from Table 5-6 of the GS1 General Specifications. A
    /// set bit selects number set B, most significant bit first. The check digit of UPC-E has no symbol character of its own.
    /// </summary>
    public static ReadOnlySpan<byte> UpcEParity =>
    [
        0b111000, 0b110100, 0b110010, 0b110001, 0b101100,
        0b100110, 0b100011, 0b101010, 0b101001, 0b100101,
    ];

    /// <summary>
    /// Gets the number set sequence for a five-digit add-on, indexed by the add-on checksum. A set bit selects number set B, most significant bit first. The
    /// GS1 General Specifications derive the checksum from the add-on digits and convey it only through this parity.
    /// </summary>
    public static ReadOnlySpan<byte> AddOnFiveParity =>
    [
        0b11000, 0b10100, 0b10010, 0b10001, 0b01100,
        0b00110, 0b00011, 0b01010, 0b01001, 0b00101,
    ];

    /// <summary>
    /// Gets the correction of the bars of a number set A character in run units, indexed by digit, from Table 5-10 of the GS1 General Specifications: 0.025 mm
    /// at the nominal X dimension of 0.330 mm, one run unit. A bar of the characters 1 and 2 is reduced and a bar of 7 and 8 enlarged, and each space takes the
    /// opposite. Number sets B and C take the opposite of number set A.
    /// </summary>
    private static ReadOnlySpan<sbyte> NumberSetABarCorrections => [0, -1, -1, 0, 0, 0, 0, 1, 1, 0];

    /// <summary>
    /// Validates that the text holds only the decimal digits 0 to 9, and that its length is the data length or the data length plus a check digit. When the
    /// check digit is present, this method compares it with the calculated one. When it is absent, this method calculates it. The digits written always carry
    /// the check digit.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="dataLength">The number of data digits, excluding the check digit.</param>
    /// <param name="destination">The buffer the digits are written into, of at least the data length plus one.</param>
    /// <returns>The written digits, which are the data digits with the check digit appended.</returns>
    /// <exception cref="ArgumentException">The text has a wrong length, contains a non-digit, or carries a wrong check digit.</exception>
    public static ReadOnlySpan<char> ValidateAndApplyCheckDigit(ReadOnlySpan<char> text, int dataLength, Span<char> destination)
    {
        Guard.MustBeBetweenOrEqualTo(text.Length, dataLength, dataLength + 1, nameof(text));
        BarcodeSymbology.ValidateDigits(text);

        int check = Gs1Data.ComputeCheckDigit(text[..dataLength]);
        if (text.Length > dataLength && text[dataLength] - '0' != check)
        {
            throw new ArgumentException($"Check digit mismatch: expected {check}, got {text[dataLength]}.", nameof(text));
        }

        text[..dataLength].CopyTo(destination);
        destination[dataLength] = (char)('0' + check);
        return destination[..(dataLength + 1)];
    }

    /// <summary>
    /// Appends a pattern to the module stream, most significant bit first.
    /// </summary>
    /// <param name="modules">The module stream, where 1 is a dark module.</param>
    /// <param name="position">The write position, advanced by <paramref name="bitCount"/>.</param>
    /// <param name="pattern">The pattern bits.</param>
    /// <param name="bitCount">The number of pattern bits.</param>
    public static void AppendPattern(Span<byte> modules, ref int position, int pattern, int bitCount)
    {
        for (int bit = bitCount - 1; bit >= 0; bit--)
        {
            modules[position++] = (byte)((pattern >> bit) & 1);
        }
    }

    /// <summary>
    /// Appends the symbol character of one digit to the module stream, and the width correction of Table 5-10 of the GS1 General Specifications at the first
    /// module of each of its bars and spaces.
    /// </summary>
    /// <param name="modules">The module stream, where 1 is a dark module.</param>
    /// <param name="corrections">The width corrections in run units, one for each module, 0 where no element starts or no correction applies.</param>
    /// <param name="position">The write position, advanced by <see cref="CharacterWidth"/>.</param>
    /// <param name="numberSet">The number set the digit is encoded in.</param>
    /// <param name="digit">The digit, already validated.</param>
    public static void AppendCharacter(Span<byte> modules, Span<sbyte> corrections, ref int position, EanUpcNumberSet numberSet, char digit)
    {
        int value = digit - '0';
        ReadOnlySpan<byte> patterns = numberSet switch
        {
            EanUpcNumberSet.A => NumberSetA,
            EanUpcNumberSet.B => NumberSetB,
            _ => NumberSetC,
        };

        int start = position;
        AppendPattern(modules, ref position, patterns[value], CharacterWidth);

        int bar = numberSet == EanUpcNumberSet.A ? NumberSetABarCorrections[value] : -NumberSetABarCorrections[value];
        if (bar == 0)
        {
            return;
        }

        // A character has two bars and two spaces, so its width stays seven modules.
        for (int i = start; i < position; i++)
        {
            if (i == start || modules[i] != modules[i - 1])
            {
                corrections[i] = (sbyte)(modules[i] == 1 ? bar : -bar);
            }
        }
    }

    /// <summary>
    /// Begins a linear symbol from a module stream and writes its alternating bar and space run widths in run units of <see cref="RunUnit"/>. The stream must
    /// start and end with a dark module so that even run indexes are bars. A symbol character starts and ends on a module of the other colour from the
    /// patterns beside it, so each of its bars and spaces is one run, and the correction at the first module of a run applies to that run.
    /// </summary>
    /// <param name="modules">The module stream, where 1 is a dark module.</param>
    /// <param name="corrections">The width corrections in run units from <see cref="AppendCharacter"/>.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <param name="heights">The bar heights of the symbol, to write.</param>
    public static void BeginRuns(
        ReadOnlySpan<byte> modules,
        ReadOnlySpan<sbyte> corrections,
        double xDimension,
        ref BarcodeSymbol symbol,
        out Span<float> heights)
    {
        int count = 1;
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                count++;
            }
        }

        Span<int> runs = symbol.BeginLinear(count, xDimension, out heights, out _);
        symbol.SetCorrectedModuleUnits(RunUnitsPerModule);
        int run = 0;
        int start = 0;
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                runs[run++] = ((i - start) * RunUnitsPerModule) + corrections[start];
                start = i;
            }
        }

        runs[run] = ((modules.Length - start) * RunUnitsPerModule) + corrections[start];
    }

    /// <summary>
    /// Writes the bar heights of a symbol whose bars are top aligned. The listed guard bars extend downwards by <see cref="GuardExtension"/>, with or without
    /// text.
    /// </summary>
    /// <param name="heights">The per-bar heights in modules to write.</param>
    /// <param name="barHeight">The digit bar height in modules.</param>
    /// <param name="guardBars">The indexes of the extended bars.</param>
    public static void FillGuardedHeights(Span<float> heights, float barHeight, ReadOnlySpan<int> guardBars)
    {
        heights.Fill(barHeight);
        foreach (int guard in guardBars)
        {
            heights[guard] = barHeight + GuardExtension;
        }
    }

    /// <summary>
    /// Adds one text placement per digit, each centered in its own character cell. The nominal symbol drawings of section 5.2.6.6 of the GS1 General
    /// Specifications print every digit of the interpretation below (or above, for add-ons) its own symbol character rather than centering digit groups, so
    /// placement is per digit.
    /// </summary>
    /// <param name="digits">The digits to place.</param>
    /// <param name="firstCellLeft">The left edge of the first character cell, in modules.</param>
    /// <param name="cellAdvance">The distance between successive cell left edges, in modules.</param>
    /// <param name="side">The side of the bars the digits print on.</param>
    /// <param name="textEdge">The edge of the ink of the digits, in modules from the symbol top.</param>
    /// <param name="symbol">The symbol to write, with room for the digits reserved.</param>
    public static void AddDigits(
        ReadOnlySpan<char> digits,
        float firstCellLeft,
        float cellAdvance,
        BarcodeTextSide side,
        float textEdge,
        ref BarcodeSymbol symbol)
    {
        for (int i = 0; i < digits.Length; i++)
        {
            float left = firstCellLeft + (i * cellAdvance);
            symbol.AddText(digits.Slice(i, 1), left, left + 7, side, textEdge);
        }
    }

    /// <summary>
    /// Returns the prefix of a hyphenated number that contains the given count of digits, preserving the caller's hyphenation and trimming a trailing hyphen.
    /// The ISBN caption reproduces the number as the caller wrote it, without its check character.
    /// </summary>
    /// <param name="text">The hyphenated input.</param>
    /// <param name="digitCount">The number of digits the prefix must contain.</param>
    /// <returns>The hyphenated prefix.</returns>
    public static ReadOnlySpan<char> TakeHyphenatedPrefix(ReadOnlySpan<char> text, int digitCount)
    {
        int digits = 0;
        int end = 0;
        while (digits < digitCount)
        {
            if (text[end] != '-')
            {
                digits++;
            }

            end++;
        }

        return text[..end];
    }

    /// <summary>
    /// Encodes thirteen validated digits into an EAN-13 symbol.
    /// </summary>
    /// <param name="digits">The thirteen digits, with the check digit already validated.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildEan13(ReadOnlySpan<char> digits, BarcodeOptions options, ref BarcodeSymbol symbol)
        => BuildEan13(digits, options, false, [], [], [], ref symbol);

    /// <summary>
    /// Encodes thirteen validated digits into an EAN-13 symbol with a caption above the bars. The ISBN, ISMN and ISSN symbologies print their own number above
    /// their EAN-13 symbol. The caption stands one module above the bar tops, and the room it needs belongs to the renderer, which is what knows the font.
    /// </summary>
    /// <param name="digits">The thirteen digits, with the check digit already validated.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="captionPrefix">The characters of the caption before its body.</param>
    /// <param name="captionBody">The body of the caption.</param>
    /// <param name="captionSuffix">The characters of the caption after its body.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildEan13(
        ReadOnlySpan<char> digits,
        BarcodeOptions options,
        ReadOnlySpan<char> captionPrefix,
        ReadOnlySpan<char> captionBody,
        ReadOnlySpan<char> captionSuffix,
        ref BarcodeSymbol symbol)
        => BuildEan13(digits, options, true, captionPrefix, captionBody, captionSuffix, ref symbol);

    /// <summary>
    /// Encodes eight validated digits into an EAN-8 symbol.
    /// </summary>
    /// <param name="digits">The eight digits, with the check digit already validated.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildEan8(ReadOnlySpan<char> digits, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray128<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..Ean8Width];
        InlineArray128<sbyte> correctionStorage = default;
        Span<sbyte> corrections = ((Span<sbyte>)correctionStorage)[..Ean8Width];
        WriteEan8Modules(digits, modules, corrections);

        BeginRuns(
            modules,
            corrections,
            options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension),
            ref symbol,
            out Span<float> heights);

        float barHeight = options.ResolveBarHeight(symbol.XDimension, NominalEan8BarHeight);
        FillGuardedHeights(heights, barHeight, Ean8GuardBars);

        // The nominal symbol drawings of section 5.2.6.6 of the GS1 General Specifications print every digit below its own symbol character. Section 5.2.5:
        // "Normally the minimum is one module" between the digits and the bars. Digits flow past the extended guard bars.
        if (options.Font is not null)
        {
            symbol.ReserveText(8, 8);
            float textEdge = barHeight + BarcodeTextPlacement.Clearance;
            AddDigits(digits[..4], 3F, 7F, BarcodeTextSide.BelowBars, textEdge, ref symbol);
            AddDigits(digits[4..], 36F, 7F, BarcodeTextSide.BelowBars, textEdge, ref symbol);
        }

        symbol.EndLinear(QuietZone, QuietZone, 0F, RunUnit, false);
    }

    /// <summary>
    /// Writes the modules of an EAN-8 symbol: the guard patterns, four digits of number set A and four of number set C.
    /// </summary>
    /// <param name="digits">The eight digits, with the check digit already validated.</param>
    /// <param name="modules">The <see cref="Ean8Width"/> modules to write, 1 for dark.</param>
    /// <param name="corrections">The <see cref="Ean8Width"/> width corrections to write, zeroed by the caller.</param>
    public static void WriteEan8Modules(ReadOnlySpan<char> digits, Span<byte> modules, Span<sbyte> corrections)
    {
        int position = 0;
        AppendPattern(modules, ref position, NormalGuard, NormalGuardWidth);
        foreach (char digit in digits[..4])
        {
            AppendCharacter(modules, corrections, ref position, EanUpcNumberSet.A, digit);
        }

        AppendPattern(modules, ref position, CentreGuard, CentreGuardWidth);
        foreach (char digit in digits[4..8])
        {
            AppendCharacter(modules, corrections, ref position, EanUpcNumberSet.C, digit);
        }

        AppendPattern(modules, ref position, NormalGuard, NormalGuardWidth);
    }

    /// <summary>
    /// Writes the modules of an EAN-13 symbol: the guard patterns, six left half digits of number sets A and B and six right half digits of number set C. The
    /// leading digit of an EAN-13 number has no symbol character, and the number set sequence of the left half carries it.
    /// </summary>
    /// <param name="leftParity">The left half number set sequence from <see cref="Ean13LeftParity"/>, the first digit in the highest of six bits.</param>
    /// <param name="digits">The twelve digits that take symbol characters.</param>
    /// <param name="modules">The <see cref="Ean13Width"/> modules to write, 1 for dark.</param>
    /// <param name="corrections">The <see cref="Ean13Width"/> width corrections to write, zeroed by the caller.</param>
    public static void WriteEan13Modules(int leftParity, ReadOnlySpan<char> digits, Span<byte> modules, Span<sbyte> corrections)
    {
        int position = 0;
        AppendPattern(modules, ref position, NormalGuard, NormalGuardWidth);
        for (int i = 0; i < 6; i++)
        {
            EanUpcNumberSet numberSet = ((leftParity >> (5 - i)) & 1) == 0 ? EanUpcNumberSet.A : EanUpcNumberSet.B;
            AppendCharacter(modules, corrections, ref position, numberSet, digits[i]);
        }

        AppendPattern(modules, ref position, CentreGuard, CentreGuardWidth);
        foreach (char digit in digits[6..12])
        {
            AppendCharacter(modules, corrections, ref position, EanUpcNumberSet.C, digit);
        }

        AppendPattern(modules, ref position, NormalGuard, NormalGuardWidth);
    }

    /// <summary>
    /// Encodes thirteen validated digits into an EAN-13 symbol, with or without a caption above the bars.
    /// </summary>
    /// <param name="digits">The thirteen digits, with the check digit already validated.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="hasCaption">Whether the symbol carries a caption above the bars.</param>
    /// <param name="captionPrefix">The characters of the caption before its body.</param>
    /// <param name="captionBody">The body of the caption.</param>
    /// <param name="captionSuffix">The characters of the caption after its body.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildEan13(
        ReadOnlySpan<char> digits,
        BarcodeOptions options,
        bool hasCaption,
        ReadOnlySpan<char> captionPrefix,
        ReadOnlySpan<char> captionBody,
        ReadOnlySpan<char> captionSuffix,
        ref BarcodeSymbol symbol)
    {
        InlineArray128<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..Ean13Width];
        InlineArray128<sbyte> correctionStorage = default;
        Span<sbyte> corrections = ((Span<sbyte>)correctionStorage)[..Ean13Width];
        WriteEan13Modules(Ean13LeftParity[digits[0] - '0'], digits[1..], modules, corrections);

        BeginRuns(
            modules,
            corrections,
            options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension),
            ref symbol,
            out Span<float> heights);

        float barHeight = options.ResolveBarHeight(symbol.XDimension, NominalBarHeight);
        FillGuardedHeights(heights, barHeight, Ean13GuardBars);

        // Section 5.2.5 of the GS1 General Specifications: "the leftmost digit, which is encoded by variable parity, is printed to the left of the start guard
        // pattern in line with the other digits". The nominal symbol drawings of section 5.2.6.6 print every other digit below its own symbol character.
        // "Normally the minimum is one module" between the digits and the bars. Digits flow past the extended guard bars. A caption stands one module above the
        // bar tops.
        if (options.Font is not null)
        {
            int captionLength = captionPrefix.Length + captionBody.Length + captionSuffix.Length;
            symbol.ReserveText(hasCaption ? 14 : 13, 13 + captionLength);
            float textEdge = barHeight + BarcodeTextPlacement.Clearance;
            symbol.AddText(digits[..1], -9F, -2F, BarcodeTextSide.BelowBars, textEdge);
            AddDigits(digits[1..7], 3F, 7F, BarcodeTextSide.BelowBars, textEdge, ref symbol);
            AddDigits(digits[7..13], 50F, 7F, BarcodeTextSide.BelowBars, textEdge, ref symbol);
            if (hasCaption)
            {
                symbol.AddText(
                    captionPrefix,
                    captionBody,
                    captionSuffix,
                    0F,
                    Ean13Width,
                    BarcodeTextSide.AboveBars,
                    -BarcodeTextPlacement.Clearance,
                    1F,
                    true,
                    BarcodeTextAlignment.Centered);
            }
        }

        symbol.EndLinear(Ean13LeadingQuietZone, QuietZone, 0F, RunUnit, false);
    }
}
