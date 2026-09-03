// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Japan Post customer barcode. A symbol is a start code of two bars, twenty codes of
/// three bars, a check code of three bars and a stop code of two bars. The codes are the digits, the
/// hyphen and the control codes CC1 to CC8; a letter is CC1, CC2 or CC3 followed by a digit, and CC4 fills
/// the positions the data leaves empty.
/// </summary>
internal static class JapanPostEncoder
{
    /// <summary>
    /// The number of code positions between the start code and the check code.
    /// </summary>
    public const int CodeCount = 20;

    /// <summary>
    /// The number of bars in one code.
    /// </summary>
    public const int BarsPerCode = 3;

    /// <summary>
    /// The number of bars in a symbol.
    /// </summary>
    public const int BarCount = 2 + ((CodeCount + 1) * BarsPerCode) + 2;

    /// <summary>
    /// The value of the hyphen.
    /// </summary>
    public const int Hyphen = 10;

    /// <summary>
    /// The value of control code CC1; CC2 to CC8 follow it.
    /// </summary>
    public const int ControlCode1 = 11;

    /// <summary>
    /// The value of control code CC4, the filler.
    /// </summary>
    public const int ControlCode4 = 14;

    /// <summary>
    /// The modulus of the check calculation.
    /// </summary>
    public const int Modulus = 19;

    /// <summary>
    /// The dimensions of page 12 of the Japan Post zip code and barcode manual at the 10 point size: a bar
    /// width of 0.6 mm, a bar pitch of 1.2 mm, so a space of 0.6 mm, a timing bar of 1.2 mm and a long bar
    /// of 3.6 mm, so an extender of 1.2 mm above or below the timing bar. Page 13: "2mm以上の空白", a clear
    /// space of at least 2 mm above, below, left and right of the barcode, which also stands between the
    /// bars and the human readable interpretation.
    /// </summary>
    public static readonly FourStateMetrics Metrics = new(0.6F, 1F, 1, 1, 1.2F / 0.6F, 1.2F / 0.6F, 1.2F / 0.6F, 2F / 0.6F, BarcodeTextSide.BelowBars, 2F / 0.6F);

    /// <summary>
    /// Gets the bar states of every code value, three per code, as <see cref="FourState"/> values: the
    /// digits 0 to 9, the hyphen and CC1 to CC8.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        3, 0, 0, 3, 3, 0, 3, 1, 2, 1, 3, 2, 3, 2, 1, 3, 0, 3, 1, 2, 3, 2, 3, 1, 2, 1, 3, 0, 3, 3,
        0, 3, 0,
        1, 2, 0, 1, 0, 2, 2, 1, 0, 0, 1, 2, 2, 0, 1, 0, 2, 1, 0, 0, 3, 3, 3, 3,
    ];

    /// <summary>
    /// Converts the text to code values and validates it.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="codes">The buffer of <see cref="CodeCount"/> values that receives the codes, filled with CC4 after the data.</param>
    public static void Codes(string text, Span<byte> codes)
    {
        int count = 0;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            int value = codePoints.Current.Value;
            int needed = value is >= 'A' and <= 'Z' ? 2 : 1;
            if (count + needed > CodeCount)
            {
                throw new ArgumentException($"The Japan Post barcode carries {CodeCount} codes, and a letter is two codes; got more in {text}.", nameof(text));
            }

            switch (value)
            {
                case >= '0' and <= '9':
                    codes[count++] = (byte)(value - '0');
                    break;
                case '-':
                    codes[count++] = Hyphen;
                    break;
                case >= 'A' and <= 'Z':
                    int letter = value - 'A';
                    codes[count++] = (byte)(ControlCode1 + (letter / 10));
                    codes[count++] = (byte)(letter % 10);
                    break;
                default:
                    throw new ArgumentException($"The Japan Post barcode carries only digits, capital letters and the hyphen; got {codePoints.Current.ToDisplayString()}.", nameof(text));
            }
        }

        codes[count..].Fill(ControlCode4);
    }

    /// <summary>
    /// Calculates the check code: the value that brings the sum of the twenty codes to a multiple of 19.
    /// </summary>
    /// <param name="codes">The twenty codes.</param>
    /// <returns>The check code value, 0 to 18.</returns>
    public static int CheckCode(ReadOnlySpan<byte> codes)
    {
        int sum = 0;
        for (int i = 0; i < codes.Length; i++)
        {
            sum += codes[i];
        }

        return (Modulus - (sum % Modulus)) % Modulus;
    }

    /// <summary>
    /// Writes the bar states of the symbol.
    /// </summary>
    /// <param name="codes">The twenty codes.</param>
    /// <param name="states">The buffer that receives the <see cref="BarCount"/> bar states.</param>
    public static void Bars(ReadOnlySpan<byte> codes, Span<FourState> states)
    {
        states[0] = FourState.Full;
        states[1] = FourState.Descender;
        int bar = 2;
        for (int i = 0; i < codes.Length; i++)
        {
            bar = Append(codes[i], states, bar);
        }

        bar = Append(CheckCode(codes), states, bar);
        states[bar] = FourState.Descender;
        states[bar + 1] = FourState.Full;
    }

    private static int Append(int code, Span<FourState> states, int bar)
    {
        for (int i = 0; i < BarsPerCode; i++)
        {
            states[bar + i] = (FourState)Patterns[(code * BarsPerCode) + i];
        }

        return bar + BarsPerCode;
    }
}
