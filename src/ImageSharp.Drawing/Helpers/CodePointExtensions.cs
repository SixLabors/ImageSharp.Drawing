// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Extension methods for <see cref="CodePoint"/>.
/// </summary>
internal static class CodePointExtensions
{
    /// <summary>
    /// Describes the code point for a reader: its value, then the character it prints as. A value that is
    /// not a valid Unicode scalar has no character, so the replacement character stands in for it.
    /// </summary>
    /// <param name="codePoint">The code point to describe.</param>
    /// <returns>The description, in the form <c>U+0041 'A'</c>.</returns>
    public static string ToDisplayString(this CodePoint codePoint)
        => FormattableString.Invariant(FormattableStringFactory.Create(
            "U+{0:X4} '{1}'",
            codePoint.Value,
            CodePoint.IsValid(codePoint.Value) ? codePoint.ToString() : "\ufffd"));

    /// <summary>
    /// Returns a value indicating whether the code point is one of the ASCII digits 0 to 9.
    /// <c>CodePoint.IsDigit</c> is wider: outside ASCII it takes any code point of the decimal digit
    /// category, which no barcode symbology encodes.
    /// </summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns><see langword="true"/> if the code point is an ASCII digit; otherwise <see langword="false"/>.</returns>
    public static bool IsAsciiDigit(this CodePoint codePoint)
        => codePoint.IsAscii && char.IsAsciiDigit((char)codePoint.Value);

    /// <summary>
    /// Returns the code point that starts at an index of the text: a surrogate pair as the one code point it
    /// encodes, and a lone surrogate as itself.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index of the first UTF-16 code unit of the code point.</param>
    /// <returns>The code point.</returns>
    public static CodePoint CodePointAt(this ReadOnlySpan<char> text, int index)
    {
        SpanCodePointEnumerator codePoints = text[index..].EnumerateCodePoints();
        codePoints.MoveNext();
        return codePoints.Current;
    }
}
