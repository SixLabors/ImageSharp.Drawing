// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the EAN/UPC symbology family as specified in ISO/IEC 15420 and the GS1 General
/// Specifications. Symbol characters are seven modules wide and encode one digit through number set A, B or C;
/// the number set choice carries the implied extra digit of EAN-13 and the check digit of UPC-E.
/// </summary>
internal static class EanUpcEncoder
{
    /// <summary>
    /// The number of modules a guard pattern extends below the digit bars when the human readable
    /// interpretation is printed. ISO/IEC 15420 extends the guard bars five modules into the text area at
    /// nominal size; the extension exists to flank the text row, so a symbol without text has uniform bars.
    /// </summary>
    public const float GuardExtension = 5F;

    /// <summary>
    /// The nominal EAN-13, UPC-A and UPC-E bar height in modules: 22.85mm bars at the nominal 0.33mm
    /// X-dimension of ISO/IEC 15420.
    /// </summary>
    public const float NominalBarHeight = 69.24F;

    /// <summary>
    /// The nominal EAN-8 bar height in modules: 18.23mm bars at the nominal 0.33mm X-dimension of ISO/IEC 15420.
    /// </summary>
    public const float NominalEan8BarHeight = 55.24F;

    /// <summary>
    /// The nominal add-on bar height in modules: 21.90mm bars at the nominal 0.33mm X-dimension per the
    /// GS1 General Specifications.
    /// </summary>
    public const float NominalAddOnBarHeight = 66.36F;

    /// <summary>
    /// The font scale for the UPC number system and check digits, which print in smaller type in the quiet
    /// zones at 10/12 of the digit size.
    /// </summary>
    public const float QuietZoneDigitScale = 10F / 12F;

    /// <summary>
    /// The smallest size in points that any text prints at. Section 8.1 of the ISBN Users' Manual: "The
    /// ISBN should always be printed in type large enough to be easily legible (i.e., 9-point or
    /// larger)." Section 7 of the ISMN Users' Manual gives the same floor.
    /// </summary>
    public const float MinimumTextPoints = 9F;

    /// <summary>
    /// The width of an EAN-13 symbol in modules, and the bar count within it.
    /// </summary>
    private const int Ean13Width = 95;

    private const int Ean13BarCount = 30;

    /// <summary>
    /// The width of an EAN-8 symbol in modules, and the bar count within it.
    /// </summary>
    private const int Ean8Width = 67;

    private const int Ean8BarCount = 22;

    private static readonly string[] DigitStrings = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    /// <summary>
    /// Gets the indexes of the extended bars of an EAN-13 symbol: the two bars of each of the left,
    /// centre and right guard patterns.
    /// </summary>
    private static ReadOnlySpan<int> Ean13GuardBars => [0, 1, 14, 15, 28, 29];

    /// <summary>
    /// Gets the indexes of the extended bars of an EAN-8 symbol.
    /// </summary>
    private static ReadOnlySpan<int> Ean8GuardBars => [0, 1, 10, 11, 20, 21];

    /// <summary>
    /// Gets the number set A symbol characters indexed by digit, seven modules per character with the most significant
    /// bit first and 1 meaning a dark module. ISO/IEC 15420 defines number set A with odd parity; every
    /// character starts with a space module and ends with a bar module.
    /// </summary>
    public static ReadOnlySpan<byte> NumberSetA =>
    [
        0b0001101, 0b0011001, 0b0010011, 0b0111101, 0b0100011,
        0b0110001, 0b0101111, 0b0111011, 0b0110111, 0b0001011,
    ];

    /// <summary>
    /// Gets the number set B symbol characters indexed by digit. ISO/IEC 15420 defines number set B with even parity;
    /// each character is number set C for the same digit read in reverse module order.
    /// </summary>
    public static ReadOnlySpan<byte> NumberSetB =>
    [
        0b0100111, 0b0110011, 0b0011011, 0b0100001, 0b0011101,
        0b0111001, 0b0000101, 0b0010001, 0b0001001, 0b0010111,
    ];

    /// <summary>
    /// Gets the number set C symbol characters indexed by digit. ISO/IEC 15420 defines number set C as the module-wise
    /// inverse of number set A; every character starts with a bar module and ends with a space module.
    /// </summary>
    public static ReadOnlySpan<byte> NumberSetC =>
    [
        0b1110010, 0b1100110, 0b1101100, 0b1000010, 0b1011100,
        0b1001110, 0b1010000, 0b1000100, 0b1001000, 0b1110100,
    ];

    /// <summary>
    /// Gets the number set sequence for the six left-half characters of an EAN-13 symbol, indexed by the leading
    /// digit. A set bit selects number set B, a clear bit number set A, most significant bit first.
    /// ISO/IEC 15420 encodes the thirteenth digit through this variable parity; the leading digit has no
    /// symbol character of its own.
    /// </summary>
    public static ReadOnlySpan<byte> Ean13LeftParity =>
    [
        0b000000, 0b001011, 0b001101, 0b001110, 0b010011,
        0b011001, 0b011100, 0b010101, 0b010110, 0b011010,
    ];

    /// <summary>
    /// Gets the number set sequence for the six characters of a UPC-E symbol with number system 0, indexed by the
    /// check digit. A set bit selects number set B, most significant bit first. Number system 1 uses the
    /// bitwise complement. ISO/IEC 15420 encodes both the number system and the check digit of UPC-E through
    /// this parity because neither has a symbol character of its own.
    /// </summary>
    public static ReadOnlySpan<byte> UpcEParity =>
    [
        0b111000, 0b110100, 0b110010, 0b110001, 0b101100,
        0b100110, 0b100011, 0b101010, 0b101001, 0b100101,
    ];

    /// <summary>
    /// Gets the number set sequence for a five-digit add-on, indexed by the add-on checksum. A set bit selects
    /// number set B, most significant bit first. The GS1 General Specifications derive the checksum from the
    /// add-on digits and convey it only through this parity.
    /// </summary>
    public static ReadOnlySpan<byte> AddOnFiveParity =>
    [
        0b11000, 0b10100, 0b10010, 0b10001, 0b01100,
        0b00110, 0b00011, 0b01010, 0b01001, 0b00101,
    ];

    /// <summary>
    /// Computes the GS1 check digit for the given data digits using the standard check digit calculation of
    /// the GS1 General Specifications: digits are weighted 3 and 1 alternately starting with 3 at the rightmost
    /// data digit, and the check digit lifts the sum to the next multiple of ten.
    /// </summary>
    /// <param name="digits">The data digits, excluding the check digit.</param>
    /// <returns>The check digit value.</returns>
    public static int ComputeCheckDigit(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        int weight = 3;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = 4 - weight;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Validates that the text consists solely of the decimal digits 0-9 and has one of the two permitted
    /// lengths: the data length, or the data length plus a check digit. When the check digit is present it is
    /// verified; when absent it is computed. The digits written always carry the check digit.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="dataLength">The number of data digits, excluding the check digit.</param>
    /// <param name="symbologyName">The symbology name used in error messages.</param>
    /// <param name="destination">The buffer the digits are written into, of at least the data length plus one.</param>
    /// <returns>The written digits, which are the data digits with the check digit appended.</returns>
    /// <exception cref="ArgumentException">The text has a wrong length, contains a non-digit, or carries a wrong check digit.</exception>
    public static ReadOnlySpan<char> ValidateAndApplyCheckDigit(ReadOnlySpan<char> text, int dataLength, string symbologyName, Span<char> destination)
    {
        if (text.Length != dataLength && text.Length != dataLength + 1)
        {
            throw new ArgumentException(
                $"{symbologyName} requires {dataLength} data digits with an optional check digit; got {text.Length} characters.",
                nameof(text));
        }

        ValidateDigits(text, symbologyName);

        int check = ComputeCheckDigit(text[..dataLength]);
        if (text.Length > dataLength && text[dataLength] - '0' != check)
        {
            throw new ArgumentException($"{symbologyName} check digit mismatch: expected {check}, got {text[dataLength]}.", nameof(text));
        }

        text[..dataLength].CopyTo(destination);
        destination[dataLength] = (char)('0' + check);
        return destination[..(dataLength + 1)];
    }

    /// <summary>
    /// Validates that every character of the text is a decimal digit 0-9.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="symbologyName">The symbology name used in error messages.</param>
    /// <exception cref="ArgumentException">The text contains a non-digit character.</exception>
    public static void ValidateDigits(ReadOnlySpan<char> text, string symbologyName)
    {
        // Walking code points rather than UTF-16 units reports a surrogate pair as the one character it
        // is, instead of showing half of it back to the caller.
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value is < '0' or > '9')
            {
                throw new ArgumentException(
                    $"{symbologyName} accepts only the digits 0-9; got U+{current.Value:X4}.",
                    nameof(text));
            }
        }
    }

    /// <summary>
    /// Appends a pattern to the module stream, most significant bit first.
    /// </summary>
    /// <param name="modules">The module stream; 1 is a dark module.</param>
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
    /// Converts a module stream into alternating bar and space run widths. The stream must start and end with
    /// a dark module so that even run indexes are bars.
    /// </summary>
    /// <param name="modules">The module stream; 1 is a dark module.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] ToRuns(ReadOnlySpan<byte> modules)
    {
        int count = 1;
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                count++;
            }
        }

        int[] runs = new int[count];
        int run = 0;
        int width = 1;
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                runs[run++] = width;
                width = 1;
            }
            else
            {
                width++;
            }
        }

        runs[run] = width;
        return runs;
    }

    /// <summary>
    /// Builds the bar height and top offset arrays for a symbol whose bars are top aligned. When the options
    /// carry a font the listed guard bars extend downwards by <see cref="GuardExtension"/> to flank the text
    /// row; without text all bars are uniform.
    /// </summary>
    /// <param name="barCount">The number of bars in the symbol.</param>
    /// <param name="barHeight">The digit bar height in modules.</param>
    /// <param name="guardBars">The indexes of the extended bars.</param>
    /// <param name="options">The options; the font decides whether the guards extend.</param>
    /// <param name="heights">The resulting per-bar heights in modules.</param>
    /// <param name="tops">The resulting per-bar top offsets in modules.</param>
    public static void BuildGuardedHeights(int barCount, float barHeight, ReadOnlySpan<int> guardBars, BarcodeOptions options, out float[] heights, out float[] tops)
    {
        heights = new float[barCount];
        tops = new float[barCount];
        Array.Fill(heights, barHeight);
        if (options.Font is null)
        {
            return;
        }

        foreach (int guard in guardBars)
        {
            heights[guard] = barHeight + GuardExtension;
        }
    }

    /// <summary>
    /// Resolves the digit bar height in modules from the options.
    /// </summary>
    /// <param name="options">The barcode options.</param>
    /// <param name="nominalHeight">The nominal height for the symbology, in modules.</param>
    /// <returns>The bar height in modules.</returns>
    public static float ResolveBarHeight(BarcodeOptions options, float nominalHeight)
        => options.BarHeight.HasValue ? options.BarHeight.Value / options.ModuleWidth : nominalHeight;

    /// <summary>
    /// Resolves the font an ISBN, ISMN or ISSN caption prints in. Under
    /// <see cref="BarcodeOptions.FitCaptionToSymbolWidth"/> the size is the one that spans the bars.
    /// Otherwise the font keeps its own size. The 9 point floor wins over both, because it is the one
    /// size rule the standards state, and a caption that cannot fit at 9 point widens the drawing.
    /// </summary>
    /// <param name="caption">The caption text.</param>
    /// <param name="spanModules">The width the caption prints across, in modules.</param>
    /// <param name="options">The options that carry the fonts and the module width.</param>
    /// <returns>The font to print the caption with.</returns>
    public static Font ResolveCaptionFont(string caption, float spanModules, BarcodeOptions options)
    {
        Font font = options.CaptionFont ?? options.Font!;
        float size = font.Size;
        if (options.FitCaptionToSymbolWidth)
        {
            float width = TextMeasurer.MeasureRenderableBounds(caption, new TextOptions(font)).Width;
            size = font.Size * spanModules * options.ModuleWidth / width;
        }

        size = MathF.Max(size, MinimumTextPoints);
        return size == font.Size ? font : new Font(font, size);
    }

    /// <summary>
    /// Fills one text placement per digit, each centered in its own character cell. ISO/IEC 15420 prints
    /// every digit of the interpretation below (or above, for add-ons) its own symbol character rather than
    /// centering digit groups, so placement is per digit.
    /// </summary>
    /// <param name="placements">The placement array to fill.</param>
    /// <param name="placementIndex">The first index to fill in <paramref name="placements"/>.</param>
    /// <param name="digits">The digit characters.</param>
    /// <param name="digitIndex">The first digit to place.</param>
    /// <param name="digitCount">The number of digits to place.</param>
    /// <param name="firstCellLeft">The left edge of the first character cell, in modules.</param>
    /// <param name="cellAdvance">The distance between successive cell left edges, in modules.</param>
    /// <param name="side">The side of the bars the digits print on.</param>
    /// <param name="barEdge">The bar edge the digits face, in modules from the symbol top.</param>
    public static void FillDigitPlacements(
        BarcodeTextPlacement[] placements,
        int placementIndex,
        ReadOnlySpan<char> digits,
        int digitIndex,
        int digitCount,
        float firstCellLeft,
        float cellAdvance,
        BarcodeTextSide side,
        float barEdge)
    {
        for (int i = 0; i < digitCount; i++)
        {
            float left = firstCellLeft + (i * cellAdvance);
            placements[placementIndex + i] = new BarcodeTextPlacement(
                DigitString(digits[digitIndex + i]),
                left,
                left + 7,
                side,
                barEdge);
        }
    }

    /// <summary>
    /// Returns the cached single character string for a digit, avoiding a string allocation per placement.
    /// </summary>
    /// <param name="digit">The digit character, 0-9.</param>
    /// <returns>The single character string.</returns>
    public static string DigitString(char digit)
        => DigitStrings[digit - '0'];

    /// <summary>
    /// Returns the prefix of a hyphenated number that contains the given count of digits, preserving the
    /// caller's hyphenation and trimming a trailing hyphen. The ISBN, ISMN and ISSN captions reproduce the
    /// number as the caller wrote it, without its check character.
    /// </summary>
    /// <param name="text">The hyphenated input.</param>
    /// <param name="digitCount">The number of digits the prefix must contain.</param>
    /// <returns>The hyphenated prefix.</returns>
    public static string TakeHyphenatedPrefix(ReadOnlySpan<char> text, int digitCount)
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

        return new string(text[..end]);
    }

    /// <summary>
    /// Encodes thirteen verified digits into an EAN-13 symbol, optionally with a caption above the bars.
    /// The ISBN, ISMN and ISSN symbologies print their own number above their EAN-13 symbol; the caption
    /// faces the bar tops, and the room it needs belongs to the renderer, which is what knows the font.
    /// </summary>
    /// <param name="digits">The thirteen digits including a verified check digit.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="caption">The text above the bars, or <see langword="null"/> for none.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildEan13(ReadOnlySpan<char> digits, BarcodeOptions options, string? caption)
    {
        Span<byte> modules = stackalloc byte[Ean13Width];
        int position = 0;
        AppendPattern(modules, ref position, 0b101, 3);

        int parity = Ean13LeftParity[digits[0] - '0'];
        for (int i = 1; i <= 6; i++)
        {
            ReadOnlySpan<byte> numberSet = ((parity >> (6 - i)) & 1) == 0 ? NumberSetA : NumberSetB;
            AppendPattern(modules, ref position, numberSet[digits[i] - '0'], 7);
        }

        AppendPattern(modules, ref position, 0b01010, 5);
        for (int i = 7; i <= 12; i++)
        {
            AppendPattern(modules, ref position, NumberSetC[digits[i] - '0'], 7);
        }

        AppendPattern(modules, ref position, 0b101, 3);

        float barHeight = ResolveBarHeight(options, NominalBarHeight);
        BuildGuardedHeights(Ean13BarCount, barHeight, Ean13GuardBars, options, out float[] heights, out float[] tops);

        // ISO/IEC 15420 prints the leading digit in the leading quiet zone and every other digit below its
        // own symbol character. Digits face the bottom of the digit bars and flow past the extended guard
        // bars, as in the nominal symbol drawing. A caption faces the bar tops and prints above them.
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            bool hasCaption = caption is not null;
            placements = new BarcodeTextPlacement[hasCaption ? 14 : 13];
            placements[0] = new BarcodeTextPlacement(DigitString(digits[0]), -9F, -2F, BarcodeTextSide.BelowBars, barHeight);
            FillDigitPlacements(placements, 1, digits, 1, 6, 3F, 7F, BarcodeTextSide.BelowBars, barHeight);
            FillDigitPlacements(placements, 7, digits, 7, 6, 50F, 7F, BarcodeTextSide.BelowBars, barHeight);
            if (hasCaption)
            {
                placements[13] = new BarcodeTextPlacement(caption!, 0F, Ean13Width, BarcodeTextSide.AboveBars, 0F, 1F, true);
            }
        }

        return new LinearBarcodeSymbol(ToRuns(modules), heights, tops, placements, 11, 7);
    }

    /// <summary>
    /// Encodes eight verified digits into an EAN-8 symbol.
    /// </summary>
    /// <param name="digits">The eight digits including a verified check digit.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildEan8(ReadOnlySpan<char> digits, BarcodeOptions options)
    {
        Span<byte> modules = stackalloc byte[Ean8Width];
        int position = 0;
        AppendPattern(modules, ref position, 0b101, 3);
        for (int i = 0; i < 4; i++)
        {
            AppendPattern(modules, ref position, NumberSetA[digits[i] - '0'], 7);
        }

        AppendPattern(modules, ref position, 0b01010, 5);
        for (int i = 4; i < 8; i++)
        {
            AppendPattern(modules, ref position, NumberSetC[digits[i] - '0'], 7);
        }

        AppendPattern(modules, ref position, 0b101, 3);

        float barHeight = ResolveBarHeight(options, NominalEan8BarHeight);
        BuildGuardedHeights(Ean8BarCount, barHeight, Ean8GuardBars, options, out float[] heights, out float[] tops);

        // ISO/IEC 15420 prints every digit below its own symbol character. Digits hang one module below the digit
        // bars and flow past the extended guard bars, as in the nominal symbol drawing.
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            placements = new BarcodeTextPlacement[8];
            FillDigitPlacements(placements, 0, digits, 0, 4, 3F, 7F, BarcodeTextSide.BelowBars, barHeight);
            FillDigitPlacements(placements, 4, digits, 4, 4, 36F, 7F, BarcodeTextSide.BelowBars, barHeight);
        }

        return new LinearBarcodeSymbol(ToRuns(modules), heights, tops, placements, 7, 7);
    }
}
