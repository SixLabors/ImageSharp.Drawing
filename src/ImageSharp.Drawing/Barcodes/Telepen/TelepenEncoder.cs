// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Telepen symbology. Every character is an ASCII value with an even parity bit,
/// sent least significant bit first, and the bits become bars and spaces with a fixed 3:1 ratio: a 1 bit
/// is a narrow bar and a narrow space, the bits 00 are a wide bar and a narrow space, the bits 010 are a
/// wide bar and a wide space, and the bits 01 and, after them, 10 are a narrow bar and a wide space.
/// Every character is sixteen modules. A symbol is the start code, the data, the modulo 127 check
/// character and the stop code, without gaps.
/// </summary>
internal static class TelepenEncoder
{
    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side: the "minimum quiet zone width of 10X or 2.54 mm,
    /// whichever is greater".
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The number of characters a caller stack allocates to build symbol data in. Longer data grows into
    /// a pooled array.
    /// </summary>
    public const int StackBufferLength = 64;

    /// <summary>
    /// The start code, "binary 01011111 (ASCII _)".
    /// </summary>
    public const int Start = '_';

    /// <summary>
    /// The stop code, "binary 11111010 (ASCII z)".
    /// </summary>
    public const int Stop = 'z';

    /// <summary>
    /// The modulus of the check character.
    /// </summary>
    public const int CheckModulus = 127;

    /// <summary>
    /// The value that the numeric mode adds to a digit followed by X.
    /// </summary>
    public const int NumericSingleOffset = 17;

    /// <summary>
    /// The value that the numeric mode adds to a pair of digits read as a number.
    /// </summary>
    public const int NumericPairOffset = 27;

    /// <summary>
    /// The number of bits in a character: seven ASCII bits and the parity bit.
    /// </summary>
    private const int BitsPerCharacter = 8;

    /// <summary>
    /// The width of a character in modules: eight bits of two modules.
    /// </summary>
    private const int ModulesPerCharacter = 16;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no
    /// height. Section 3.2 of the AIM Europe Telepen specification gives a minimum of 6.35 mm or 15 per
    /// cent of the symbol length, whichever is greater.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none, section 3.2 of the AIM Europe
    /// Telepen specification.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 6.35F;

    /// <summary>
    /// Calculates the check character over the character values the symbol carries: "Add the ASCII
    /// values of each character excluding the start and stop characters. Divide by 127. Unless remainder
    /// equals zero subtract from 127. The character whose ASCII value is the result is the check
    /// character." For data of NUL characters alone "the check character is exceptionally ASCII 127".
    /// </summary>
    /// <param name="values">The character values the symbol carries.</param>
    /// <returns>The value of the check character.</returns>
    public static int CheckCharacter(ReadOnlySpan<int> values)
    {
        int sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }

        if (sum == 0)
        {
            return CheckModulus;
        }

        int remainder = sum % CheckModulus;
        return remainder == 0 ? 0 : CheckModulus - remainder;
    }

    /// <summary>
    /// Encodes character values into the alternating bar and space run widths the renderer draws,
    /// starting with the first bar of the start code and ending on the last bar of the stop code. The
    /// runs carry the start code, the values, the check character and the stop code.
    /// </summary>
    /// <param name="values">The character values the symbol carries, already validated.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(ReadOnlySpan<int> values)
    {
        // Every character is at most eight bars and eight spaces, and the stop code ends on a bar.
        int[] runs = new int[(values.Length + 3) * ModulesPerCharacter];
        int written = 0;
        Append(runs, ref written, Start);
        for (int i = 0; i < values.Length; i++)
        {
            Append(runs, ref written, values[i]);
        }

        Append(runs, ref written, CheckCharacter(values));
        Append(runs, ref written, Stop);
        return runs[..(written - 1)];
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. Telepen carries no guard bars, so every bar runs the
    /// full height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in modules.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(int[] runs, string text, BarcodeOptions options)
    {
        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

        float xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        float nominalBarHeight = MathF.Max(widthInModules * NominalBarHeightFraction, MinimumBarHeightMillimetres / xDimension);
        float barHeight = EanUpcEncoder.ResolveBarHeight(options, BarcodeSymbology.PointXDimension, nominalBarHeight);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            heights[i] = barHeight;
        }

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, QuietZone, QuietZone);
    }

    /// <summary>
    /// Writes the bars and spaces of one character. The seven ASCII bits take an even parity bit as
    /// their eighth, and the eight bits are read least significant first. A 1 bit is a narrow bar and a
    /// narrow space. The bits 00 are a wide bar and a narrow space, and the bits 010 are a wide bar and a
    /// wide space. The bits 01 followed by a 1 are a narrow bar and a wide space, after which the bits
    /// 10 are the same pair, so a lone 0 bit always pairs with a later one. Even parity gives every
    /// character an even number of 0 bits, so the pairing always closes inside the character.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by the element count.</param>
    /// <param name="value">The ASCII value of the character.</param>
    private static void Append(Span<int> runs, ref int written, int value)
    {
        int bits = value;
        if ((System.Numerics.BitOperations.PopCount((uint)value) & 1) == 1)
        {
            bits |= 1 << (BitsPerCharacter - 1);
        }

        int i = 0;
        bool open = false;
        while (i < BitsPerCharacter)
        {
            int bit = (bits >> i) & 1;
            if (open)
            {
                if (((bits >> (i + 1)) & 1) == 0)
                {
                    runs[written++] = 1;
                    runs[written++] = 3;
                    i += 2;
                    open = false;
                }
                else
                {
                    runs[written++] = 1;
                    runs[written++] = 1;
                    i++;
                }
            }
            else if (bit == 1)
            {
                runs[written++] = 1;
                runs[written++] = 1;
                i++;
            }
            else if (((bits >> (i + 1)) & 1) == 0)
            {
                runs[written++] = 3;
                runs[written++] = 1;
                i += 2;
            }
            else if (((bits >> (i + 2)) & 1) == 0)
            {
                runs[written++] = 3;
                runs[written++] = 3;
                i += 3;
            }
            else
            {
                runs[written++] = 1;
                runs[written++] = 3;
                i += 2;
                open = true;
            }
        }
    }
}
