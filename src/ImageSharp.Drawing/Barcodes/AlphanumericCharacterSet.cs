// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The 43 character alphanumeric set that several bar code standards define identically: the digits, the
/// capital letters, the space and the symbols <c>-.$/+%</c>. ISO/IEC 16388 Table A.1 gives it for Code
/// 39, ANSI/AIM BC5-1995 gives the same 43 as the first part of the Code 93 set, and the Health Industry
/// Bar Code standard gives the same again. Each standard values a character by its position in the set,
/// and each takes its check character modulo the set size.
/// </summary>
internal static class AlphanumericCharacterSet
{
    /// <summary>
    /// The characters in value order, so the value of a character is its index.
    /// </summary>
    public const string Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>
    /// Gets the value the set assigns the given code point, or a negative number when the code point is
    /// outside the set. Taking a code point rather than a UTF-16 unit keeps a surrogate pair from
    /// truncating onto a character the set carries.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or a negative number.</returns>
    public static int Value(int codePoint) => codePoint switch
    {
        >= '0' and <= '9' => codePoint - '0',
        >= 'A' and <= 'Z' => codePoint - 'A' + 10,
        '-' => 36,
        '.' => 37,
        ' ' => 38,
        '$' => 39,
        '/' => 40,
        '+' => 41,
        '%' => 42,
        _ => -1,
    };
}
