// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds QR Code and Micro QR Code symbols, ISO/IEC 18004, and Rectangular Micro QR Code symbols, ISO/IEC
/// 23941: the mode segments of the data, the symbol version, the Reed-Solomon error correction, the module
/// placement, the data mask and the format and version information. Rectangular Micro QR Code shares the
/// data encodation, the error correction and the placement of QR Code, with its own function patterns,
/// indicator lengths, format information and a single data mask.
/// </summary>
internal static class QrEncoder
{
    /// <summary>
    /// The largest number of message values a symbol carries: 7089 digits in a version 40-L symbol, Table 7.
    /// </summary>
    public const int MaximumLength = 7089;

    /// <summary>
    /// The message value of the FNC1 character, which separates element strings in a symbol in FNC1 first
    /// position mode. No byte has a negative value.
    /// </summary>
    public const int Fnc1 = -1;

    /// <summary>
    /// The Numeric mode.
    /// </summary>
    private const int Numeric = 0;

    /// <summary>
    /// The Alphanumeric mode.
    /// </summary>
    private const int Alphanumeric = 1;

    /// <summary>
    /// The Byte mode.
    /// </summary>
    private const int Byte = 2;

    /// <summary>
    /// The version group of Micro QR Code version M1. Groups 0 to 2 are QR Code versions 1 to 9, 10 to 26
    /// and 27 to 40, whose character count indicators differ in length (Table 3), groups 3 to 6 are
    /// versions M1 to M4, and groups 7 to 38 are Rectangular Micro QR Code versions R7x43 to R17x139, each
    /// with indicators of its own length.
    /// </summary>
    private const int GroupM1 = 3;

    /// <summary>
    /// The version group of Micro QR Code version M2.
    /// </summary>
    private const int GroupM2 = 4;

    /// <summary>
    /// The version group of Rectangular Micro QR Code version R7x43, the first of 32.
    /// </summary>
    private const int GroupR7x43 = 7;

    /// <summary>
    /// The number of version groups.
    /// </summary>
    private const int GroupCount = 39;

    /// <summary>
    /// The data mask pattern reference of Table 10 of ISO/IEC 18004 that every Rectangular Micro QR Code
    /// symbol uses: (i div 2 + j div 3) mod 2 = 0.
    /// </summary>
    private const int RectangularMask = 4;

    /// <summary>
    /// The mask the format information of a Rectangular Micro QR Code symbol is XORed with, beside the
    /// finder pattern.
    /// </summary>
    private const int RectangularFormatMaskLeft = 0x1FAB2;

    /// <summary>
    /// The mask the format information of a Rectangular Micro QR Code symbol is XORed with, beside the
    /// finder sub-pattern.
    /// </summary>
    private const int RectangularFormatMaskRight = 0x20A7B;

    /// <summary>
    /// A count no run reaches. A mode change that needs it never happens.
    /// </summary>
    private const short Never = 10000;

    /// <summary>
    /// The distance stored where no character of a class follows, larger than any message.
    /// </summary>
    private const int NoneAhead = 9999;

    /// <summary>
    /// The rows of the per-position runs: the digits, the characters only Alphanumeric mode holds, the
    /// characters either of those modes holds, and the characters only Byte mode holds that start at each
    /// position, then the distances to the next character of the first, second and fourth class.
    /// </summary>
    private const int NumericRun = 0;

    /// <summary>
    /// The row of the runs of characters only Alphanumeric mode holds.
    /// </summary>
    private const int AlphanumericRun = 1;

    /// <summary>
    /// The row of the runs of characters Numeric or Alphanumeric mode holds.
    /// </summary>
    private const int AlphanumericOrNumericRun = 2;

    /// <summary>
    /// The row of the runs of characters only Byte mode holds.
    /// </summary>
    private const int ByteRun = 3;

    /// <summary>
    /// The row of the distances to the next digit.
    /// </summary>
    private const int NextNumeric = 4;

    /// <summary>
    /// The row of the distances to the next character only Alphanumeric mode holds.
    /// </summary>
    private const int NextAlphanumeric = 5;

    /// <summary>
    /// The row of the distances to the next character only Byte mode holds.
    /// </summary>
    private const int NextByte = 6;

    /// <summary>
    /// The number of rows of per-position runs.
    /// </summary>
    private const int RunRows = 7;

    /// <summary>
    /// The value that marks the end of the message where a mode change rule reads the class of the
    /// character after a run.
    /// </summary>
    private const int End = -1;

    /// <summary>
    /// The state of a module the data fills.
    /// </summary>
    private const byte DataModule = 2;

    /// <summary>
    /// The generator polynomial of the (15, 5) BCH code of the format information, Annex C:
    /// x^10 + x^8 + x^5 + x^4 + x^2 + x + 1.
    /// </summary>
    private const int FormatGenerator = 0x537;

    /// <summary>
    /// The generator polynomial of the (18, 6) Golay code of the version information, Annex D:
    /// x^12 + x^11 + x^10 + x^9 + x^8 + x^5 + x^2 + 1.
    /// </summary>
    private const int VersionGenerator = 0x1F25;

    /// <summary>
    /// The mask the format information of a QR Code symbol is XORed with, section 6.9.1.
    /// </summary>
    private const int FormatMask = 0b101010000010010;

    /// <summary>
    /// The mask the format information of a Micro QR Code symbol is XORed with, section 6.9.2.
    /// </summary>
    private const int MicroFormatMask = 0b100010001000101;

    /// <summary>
    /// The symbol sizes, Micro QR Code versions M1 to M4, QR Code versions 1 to 40 and then Rectangular
    /// Micro QR Code versions R7x43 to R17x139. Each carries its data modules (Table 1 of ISO/IEC 18004),
    /// its error correction codewords and blocks for levels L, M, Q and H (Table 9, -1 where the level does
    /// not exist), and the first two interior alignment coordinates, after which the coordinates step
    /// evenly to the last. Those are the second and third coordinates of Table E.1 for QR Code, and the
    /// columns of the interior alignment patterns for Rectangular Micro QR Code, which has levels M and H
    /// only.
    /// </summary>
    private static readonly SymbolSize[] Sizes =
    [
        new(QrFormat.Micro, 1, 36, 0, 0, 2, -1, -1, -1, 1, 0, 0, 0),
        new(QrFormat.Micro, 2, 80, 0, 0, 5, 6, -1, -1, 1, 1, 0, 0),
        new(QrFormat.Micro, 3, 132, 0, 0, 6, 8, -1, -1, 1, 1, 0, 0),
        new(QrFormat.Micro, 4, 192, 0, 0, 8, 10, 14, -1, 1, 1, 1, 0),
        new(QrFormat.Full, 1, 208, 0, 0, 7, 10, 13, 17, 1, 1, 1, 1),
        new(QrFormat.Full, 2, 359, 18, 0, 10, 16, 22, 28, 1, 1, 1, 1),
        new(QrFormat.Full, 3, 567, 22, 0, 15, 26, 36, 44, 1, 1, 2, 2),
        new(QrFormat.Full, 4, 807, 26, 0, 20, 36, 52, 64, 1, 2, 2, 4),
        new(QrFormat.Full, 5, 1079, 30, 0, 26, 48, 72, 88, 1, 2, 4, 4),
        new(QrFormat.Full, 6, 1383, 34, 0, 36, 64, 96, 112, 2, 4, 4, 4),
        new(QrFormat.Full, 7, 1568, 22, 38, 40, 72, 108, 130, 2, 4, 6, 5),
        new(QrFormat.Full, 8, 1936, 24, 42, 48, 88, 132, 156, 2, 4, 6, 6),
        new(QrFormat.Full, 9, 2336, 26, 46, 60, 110, 160, 192, 2, 5, 8, 8),
        new(QrFormat.Full, 10, 2768, 28, 50, 72, 130, 192, 224, 4, 5, 8, 8),
        new(QrFormat.Full, 11, 3232, 30, 54, 80, 150, 224, 264, 4, 5, 8, 11),
        new(QrFormat.Full, 12, 3728, 32, 58, 96, 176, 260, 308, 4, 8, 10, 11),
        new(QrFormat.Full, 13, 4256, 34, 62, 104, 198, 288, 352, 4, 9, 12, 16),
        new(QrFormat.Full, 14, 4651, 26, 46, 120, 216, 320, 384, 4, 9, 16, 16),
        new(QrFormat.Full, 15, 5243, 26, 48, 132, 240, 360, 432, 6, 10, 12, 18),
        new(QrFormat.Full, 16, 5867, 26, 50, 144, 280, 408, 480, 6, 10, 17, 16),
        new(QrFormat.Full, 17, 6523, 30, 54, 168, 308, 448, 532, 6, 11, 16, 19),
        new(QrFormat.Full, 18, 7211, 30, 56, 180, 338, 504, 588, 6, 13, 18, 21),
        new(QrFormat.Full, 19, 7931, 30, 58, 196, 364, 546, 650, 7, 14, 21, 25),
        new(QrFormat.Full, 20, 8683, 34, 62, 224, 416, 600, 700, 8, 16, 20, 25),
        new(QrFormat.Full, 21, 9252, 28, 50, 224, 442, 644, 750, 8, 17, 23, 25),
        new(QrFormat.Full, 22, 10068, 26, 50, 252, 476, 690, 816, 9, 17, 23, 34),
        new(QrFormat.Full, 23, 10916, 30, 54, 270, 504, 750, 900, 9, 18, 25, 30),
        new(QrFormat.Full, 24, 11796, 28, 54, 300, 560, 810, 960, 10, 20, 27, 32),
        new(QrFormat.Full, 25, 12708, 32, 58, 312, 588, 870, 1050, 12, 21, 29, 35),
        new(QrFormat.Full, 26, 13652, 30, 58, 336, 644, 952, 1110, 12, 23, 34, 37),
        new(QrFormat.Full, 27, 14628, 34, 62, 360, 700, 1020, 1200, 12, 25, 34, 40),
        new(QrFormat.Full, 28, 15371, 26, 50, 390, 728, 1050, 1260, 13, 26, 35, 42),
        new(QrFormat.Full, 29, 16411, 30, 54, 420, 784, 1140, 1350, 14, 28, 38, 45),
        new(QrFormat.Full, 30, 17483, 26, 52, 450, 812, 1200, 1440, 15, 29, 40, 48),
        new(QrFormat.Full, 31, 18587, 30, 56, 480, 868, 1290, 1530, 16, 31, 43, 51),
        new(QrFormat.Full, 32, 19723, 34, 60, 510, 924, 1350, 1620, 17, 33, 45, 54),
        new(QrFormat.Full, 33, 20891, 30, 58, 540, 980, 1440, 1710, 18, 35, 48, 57),
        new(QrFormat.Full, 34, 22091, 34, 62, 570, 1036, 1530, 1800, 19, 37, 51, 60),
        new(QrFormat.Full, 35, 23008, 30, 54, 570, 1064, 1590, 1890, 19, 38, 53, 63),
        new(QrFormat.Full, 36, 24272, 24, 50, 600, 1120, 1680, 1980, 20, 40, 56, 66),
        new(QrFormat.Full, 37, 25568, 28, 54, 630, 1204, 1770, 2100, 21, 43, 59, 70),
        new(QrFormat.Full, 38, 26896, 32, 58, 660, 1260, 1860, 2220, 22, 45, 62, 74),
        new(QrFormat.Full, 39, 28256, 26, 54, 720, 1316, 1950, 2310, 24, 47, 65, 77),
        new(QrFormat.Full, 40, 29648, 30, 58, 750, 1372, 2040, 2430, 25, 49, 68, 81),
        new(1, 7, 43, 104, 21, 0, 7, 10, 1, 1),
        new(2, 7, 59, 171, 19, 39, 9, 14, 1, 1),
        new(3, 7, 77, 261, 25, 51, 12, 22, 1, 1),
        new(4, 7, 99, 358, 23, 49, 16, 30, 1, 1),
        new(5, 7, 139, 545, 27, 55, 24, 44, 1, 2),
        new(6, 9, 43, 170, 21, 0, 9, 14, 1, 1),
        new(7, 9, 59, 267, 19, 39, 12, 22, 1, 1),
        new(8, 9, 77, 393, 25, 51, 18, 32, 1, 2),
        new(9, 9, 99, 532, 23, 49, 24, 44, 1, 2),
        new(10, 9, 139, 797, 27, 55, 36, 66, 2, 3),
        new(11, 11, 27, 122, 0, 0, 8, 10, 1, 1),
        new(12, 11, 43, 249, 21, 0, 12, 20, 1, 1),
        new(13, 11, 59, 376, 19, 39, 16, 32, 1, 2),
        new(14, 11, 77, 538, 25, 51, 24, 44, 1, 2),
        new(15, 11, 99, 719, 23, 49, 32, 60, 2, 2),
        new(16, 11, 139, 1062, 27, 55, 48, 90, 2, 3),
        new(17, 13, 27, 172, 0, 0, 9, 14, 1, 1),
        new(18, 13, 43, 329, 21, 0, 14, 28, 1, 1),
        new(19, 13, 59, 486, 19, 39, 22, 40, 1, 2),
        new(20, 13, 77, 684, 25, 51, 32, 56, 2, 2),
        new(21, 13, 99, 907, 23, 49, 40, 78, 2, 3),
        new(22, 13, 139, 1328, 27, 55, 60, 112, 3, 4),
        new(23, 15, 43, 409, 21, 0, 18, 36, 1, 2),
        new(24, 15, 59, 596, 19, 39, 26, 48, 1, 2),
        new(25, 15, 77, 830, 25, 51, 36, 72, 2, 3),
        new(26, 15, 99, 1095, 23, 49, 48, 88, 2, 4),
        new(27, 15, 139, 1594, 27, 55, 72, 130, 3, 5),
        new(28, 17, 43, 489, 21, 0, 22, 40, 1, 2),
        new(29, 17, 59, 706, 19, 39, 32, 60, 2, 2),
        new(30, 17, 77, 976, 25, 51, 44, 84, 2, 3),
        new(31, 17, 99, 1283, 23, 49, 60, 104, 3, 4),
        new(32, 17, 139, 1860, 27, 55, 80, 156, 4, 6),
    ];

    /// <summary>
    /// Gets the length of the character count indicator of each version group for Numeric, Alphanumeric
    /// and Byte mode, Table 3, or 0 where the version does not have the mode. The rows after M4 are the
    /// Rectangular Micro QR Code versions R7x43 to R17x139.
    /// </summary>
    private static ReadOnlySpan<byte> CountBits =>
    [
        10, 9, 8,
        12, 11, 16,
        14, 13, 16,
        3, 0, 0,
        4, 3, 0,
        5, 4, 4,
        6, 5, 5,
        4, 3, 3,
        5, 5, 4,
        6, 5, 5,
        7, 6, 5,
        7, 6, 6,
        5, 5, 4,
        6, 5, 5,
        7, 6, 5,
        7, 6, 6,
        8, 7, 6,
        4, 4, 3,
        6, 5, 5,
        7, 6, 5,
        7, 6, 6,
        8, 7, 6,
        8, 7, 7,
        5, 5, 4,
        6, 6, 5,
        7, 6, 6,
        7, 7, 6,
        8, 7, 7,
        8, 8, 7,
        7, 6, 6,
        7, 7, 6,
        8, 7, 7,
        8, 7, 7,
        9, 8, 7,
        7, 6, 6,
        8, 7, 6,
        8, 7, 7,
        8, 8, 7,
        9, 8, 8,
    ];

    /// <summary>
    /// Gets the length of the mode indicator of each version group, Table 2. Every Rectangular Micro QR Code
    /// version has a 3-bit mode indicator.
    /// </summary>
    private static ReadOnlySpan<byte> ModeBits =>
    [
        4, 4, 4, 0, 1, 2, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    ];

    /// <summary>
    /// Gets the length of the terminator of each version group, Table 2. Every Rectangular Micro QR Code
    /// version has a 3-bit terminator.
    /// </summary>
    private static ReadOnlySpan<byte> TerminatorBits =>
    [
        4, 4, 4, 3, 5, 7, 9,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    ];

    // The run lengths at which the data analysis changes mode, for each version group. A mode change
    // costs a mode indicator and a character count indicator, so a run that is short enough stays in the
    // current mode. The counts are the break-even run lengths for the indicator lengths of each group.

    /// <summary>
    /// Gets the digits that start the data in Numeric mode when a Byte mode character follows them.
    /// </summary>
    private static ReadOnlySpan<short> FirstNumericBeforeByte =>
    [
        4, 4, 5, Never, Never, 2, 3,
        2, 2, 3, 3, 3, 2, 3, 3, 3, 3, 2, 3, 3, 3, 3, 3, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    ];

    /// <summary>
    /// Gets the digits after which a Byte mode character makes the data start in Byte mode.
    /// </summary>
    private static ReadOnlySpan<short> FirstByteAfterNumeric =>
    [
        1, 1, 1, Never, Never, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    ];

    /// <summary>
    /// Gets the characters of Alphanumeric or Numeric mode that end the data and make it start in
    /// Alphanumeric mode.
    /// </summary>
    private static ReadOnlySpan<short> FirstAlphanumericToEnd =>
    [
        1, 1, 1, Never, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    ];

    /// <summary>
    /// Gets the digits that end the data and make it start in Numeric mode.
    /// </summary>
    private static ReadOnlySpan<short> FirstNumericToEnd =>
    [
        1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    ];

    /// <summary>
    /// Gets the characters only Alphanumeric mode holds that leave Byte mode when a Byte mode character
    /// follows them.
    /// </summary>
    private static ReadOnlySpan<short> ByteToAlphanumericBeforeByte =>
    [
        11, 15, 16, Never, Never, 6, 7,
        6, 7, 7, 8, 8, 7, 7, 8, 8, 8, 6, 7, 8, 8, 8, 9, 7, 8, 8, 8, 9, 9, 8, 8, 9, 9, 9, 8, 8, 9, 9, 10,
    ];

    /// <summary>
    /// Gets the characters only Alphanumeric mode holds that leave Byte mode when a digit follows them.
    /// </summary>
    private static ReadOnlySpan<short> ByteToAlphanumericBeforeNumeric =>
    [
        12, 13, 15, Never, Never, 6, 8,
        6, 7, 8, 8, 8, 7, 8, 8, 8, 9, 6, 8, 8, 8, 9, 9, 7, 8, 8, 9, 9, 10, 8, 9, 9, 9, 10, 8, 9, 9, 10, 10,
    ];

    /// <summary>
    /// Gets the characters only Alphanumeric mode holds that leave Byte mode when they end the data.
    /// </summary>
    private static ReadOnlySpan<short> ByteToAlphanumericToEnd =>
    [
        6, 7, 8, Never, Never, 3, 4,
        3, 4, 4, 4, 4, 4, 4, 4, 4, 5, 4, 4, 4, 4, 5, 5, 4, 4, 4, 5, 5, 5, 4, 5, 5, 5, 5, 4, 5, 5, 5, 5,
    ];

    /// <summary>
    /// Gets the digits that leave Byte mode when a Byte mode character follows them.
    /// </summary>
    private static ReadOnlySpan<short> ByteToNumericBeforeByte =>
    [
        6, 8, 9, Never, Never, 3, 4,
        3, 4, 4, 5, 5, 4, 4, 5, 5, 5, 3, 4, 5, 5, 5, 5, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6,
    ];

    /// <summary>
    /// Gets the digits that leave Byte mode when a character only Alphanumeric mode holds follows them.
    /// </summary>
    private static ReadOnlySpan<short> ByteToNumericBeforeAlphanumeric =>
    [
        6, 7, 8, Never, Never, 3, 4,
        3, 4, 4, 5, 5, 4, 4, 5, 5, 5, 4, 4, 5, 5, 5, 5, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 5, 5, 5, 5, 6,
    ];

    /// <summary>
    /// Gets the digits that leave Byte mode when they end the data.
    /// </summary>
    private static ReadOnlySpan<short> ByteToNumericToEnd =>
    [
        3, 4, 4, Never, Never, 2, 3,
        2, 2, 3, 3, 3, 2, 3, 3, 3, 3, 2, 3, 3, 3, 3, 3, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    ];

    /// <summary>
    /// Gets the digits that leave Alphanumeric mode when a character only Alphanumeric mode holds follows
    /// them.
    /// </summary>
    private static ReadOnlySpan<short> AlphanumericToNumericBeforeAlphanumeric =>
    [
        13, 15, 17, Never, 5, 7, 9,
        7, 8, 9, 9, 9, 8, 9, 9, 9, 11, 7, 9, 9, 9, 11, 11, 8, 9, 9, 10, 11, 11, 9, 10, 11, 11, 11, 9, 11, 11, 11, 11,
    ];

    /// <summary>
    /// Gets the digits that leave Alphanumeric mode when a Byte mode character follows them.
    /// </summary>
    private static ReadOnlySpan<short> AlphanumericToNumericBeforeByte =>
    [
        13, 17, 18, Never, Never, 7, 9,
        7, 8, 9, 9, 9, 8, 9, 9, 9, 10, 7, 9, 9, 9, 10, 11, 8, 9, 9, 9, 11, 11, 9, 9, 11, 11, 11, 9, 10, 11, 11, 11,
    ];

    /// <summary>
    /// Gets the digits that leave Alphanumeric mode when they end the data.
    /// </summary>
    private static ReadOnlySpan<short> AlphanumericToNumericToEnd =>
    [
        7, 8, 9, Never, 3, 4, 5,
        4, 5, 5, 5, 5, 5, 5, 5, 5, 6, 4, 5, 5, 5, 6, 6, 5, 5, 5, 5, 6, 6, 5, 5, 6, 6, 6, 5, 6, 6, 6, 6,
    ];

    /// <summary>
    /// Gets the data mask pattern references of Table 10 a Micro QR Code symbol uses, in the order of its
    /// two bit references 00 to 11.
    /// </summary>
    private static ReadOnlySpan<byte> MicroMasks => [1, 4, 6, 7];

    /// <summary>
    /// Gets the symbol numbers of Table 13, for versions M1 to M4 and levels L, M and Q, or -1 where the
    /// version does not have the level.
    /// </summary>
    private static ReadOnlySpan<sbyte> MicroSymbolNumbers =>
    [
        0, -1, -1,
        1, 2, -1,
        3, 4, -1,
        5, 6, 7,
    ];

    /// <summary>
    /// Gets the error correction level indicators of Table 12, for levels L, M, Q and H.
    /// </summary>
    private static ReadOnlySpan<byte> LevelIndicators => [0b01, 0b00, 0b11, 0b10];

    /// <summary>
    /// Encodes the text into a symbol.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="fnc1First">
    /// Whether the symbol is in FNC1 first position mode, section 6.4.8.1, for GS1 element strings. Each
    /// <see cref="Gs1Data.Separator"/> is then an FNC1 character. Micro QR Code does not have the mode.
    /// </param>
    /// <param name="format">The symbol format.</param>
    /// <param name="level">The error correction level, M or H for Rectangular Micro QR Code.</param>
    /// <param name="version">
    /// The version, 1 to 40, 1 to 4 for M1 to M4 or the <see cref="RectangularMicroQrCodeVersion"/> value, or
    /// 0 to choose it from the data.
    /// </param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data is not valid or does not fit the symbol.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        bool fnc1First,
        QrFormat format,
        QrErrorCorrectionLevel level,
        int version,
        double xDimension,
        ref BarcodeSymbol symbol)
        => BuildSymbol(text, fnc1First, format, level, version, xDimension, 0, ref symbol);

    /// <summary>
    /// Encodes the text into a symbol whose specification fixes its width rather than its X dimension. The
    /// version follows from the data, so the X dimension is the width divided by the module count of the
    /// version.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="fnc1First">
    /// Whether the symbol is in FNC1 first position mode, section 6.4.8.1, for GS1 element strings. Each
    /// <see cref="Gs1Data.Separator"/> is then an FNC1 character. Micro QR Code does not have the mode.
    /// </param>
    /// <param name="format">The symbol format.</param>
    /// <param name="level">The error correction level, M or H for Rectangular Micro QR Code.</param>
    /// <param name="version">
    /// The version, 1 to 40, 1 to 4 for M1 to M4 or the <see cref="RectangularMicroQrCodeVersion"/> value, or
    /// 0 to choose it from the data.
    /// </param>
    /// <param name="xDimension">
    /// The X dimension in millimetres the caller sets, <see cref="BarcodeOptions.XDimension"/>, or
    /// <see langword="null"/> to take <paramref name="symbolWidth"/> divided by the module count.
    /// </param>
    /// <param name="symbolWidth">The width in millimetres the specification fixes for the symbol, its quiet zones excluded.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data is not valid or does not fit the symbol.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        bool fnc1First,
        QrFormat format,
        QrErrorCorrectionLevel level,
        int version,
        double? xDimension,
        double symbolWidth,
        ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        string name = format switch
        {
            QrFormat.Full => "QR Code",
            QrFormat.Micro => "Micro QR Code",
            _ => "Rectangular Micro QR Code",
        };

        // Clause 6.3.4: Byte mode carries ISO/IEC 8859-1, the default interpretation, so each character must
        // be one byte value. The check runs by code point so a surrogate pair reports as one character.
        int percents = 0;
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value > byte.MaxValue)
            {
                throw new ArgumentException(
                    $"{name} carries only the characters U+0000 to U+00FF; got {current.ToDisplayString()}.",
                    nameof(text));
            }

            if (current.Value == '%')
            {
                percents++;
            }
        }

        // Section 6.4.8.1: in FNC1 first position mode a % of the data doubles, because a single % stands
        // for the FNC1 character.
        int length = fnc1First ? text.Length + percents : text.Length;
        if (length > MaximumLength)
        {
            throw new ArgumentException($"The data takes {length} characters; a {name} symbol carries at most {MaximumLength}.", nameof(text));
        }

        // One rent holds the message, the seven rows of per-position runs and the mode segments. Each run
        // row has one entry past the message, which stops the runs.
        int stride = length + 1;
        PooledBuffer<int> analysis = new(length + (RunRows * stride) + (2 * stride));
        try
        {
            Span<int> message = analysis.Span[..length];
            Span<int> runs = analysis.Span.Slice(length, RunRows * stride);
            Span<int> segments = analysis.Span.Slice(length + (RunRows * stride), 2 * stride);

            int m = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (fnc1First && c == Gs1Data.Separator)
                {
                    message[m++] = Fnc1;
                    continue;
                }

                message[m++] = c;
                if (fnc1First && c == '%')
                {
                    message[m++] = c;
                }
            }

            Analyze(message, runs, stride);

            // The data takes a different number of bits in each version group, because the indicators differ
            // in length. Each group of the format is encoded, or the one group of a fixed version.
            InlineArray64<int> groupBitsStorage = default;
            Span<int> groupBits = ((Span<int>)groupBitsStorage)[..GroupCount];
            groupBits.Fill(-1);
            foreach (SymbolSize size in Sizes)
            {
                if (size.Format == format && (version == 0 || size.Version == version) && groupBits[size.Group] == -1)
                {
                    // -2 marks a group that cannot hold the data, so it is not encoded again.
                    int count = Segment(runs, stride, length, size.Group, segments);
                    int bits = count < 0 ? -1 : BitLength(segments[..(2 * count)], size.Group, fnc1First);
                    groupBits[size.Group] = bits < 0 ? -2 : bits;
                }
            }

            // Step 1 of section 6.1: the smallest version that holds the data at the level. A Rectangular Micro
            // QR Code size is smallest by area, and of the two sizes with the same area, R7x99 and R9x77, the
            // later one in the table is chosen, so every size that holds the data is compared.
            int chosen = -1;
            for (int i = 0; i < Sizes.Length; i++)
            {
                SymbolSize size = Sizes[i];
                if (size.Format != format || (version != 0 && size.Version != version) || size.ErrorCodewords(level) < 0)
                {
                    continue;
                }

                int bits = groupBits[size.Group];
                if (bits >= 0 && bits <= size.DataBits(level))
                {
                    if (format != QrFormat.Rectangular)
                    {
                        chosen = i;
                        break;
                    }

                    if (chosen < 0 || size.Rows * size.Columns <= Sizes[chosen].Rows * Sizes[chosen].Columns)
                    {
                        chosen = i;
                    }
                }
            }

            if (chosen < 0)
            {
                string versionName = format switch
                {
                    QrFormat.Full => version.ToString(CultureInfo.InvariantCulture),
                    QrFormat.Micro => $"M{version}",
                    _ => ((RectangularMicroQrCodeVersion)version).ToString(),
                };

                throw new ArgumentException(
                    $"The data does not fit a {name} symbol{(version == 0 ? string.Empty : $" of version {versionName}")} at error correction level {level}.",
                    nameof(text));
            }

            SymbolSize chosenSize = Sizes[chosen];
            int segmentCount = Segment(runs, stride, length, chosenSize.Group, segments);
            double moduleWidth = xDimension ?? (symbolWidth / chosenSize.Columns);
            BuildCodewords(chosenSize, level, message, segments[..(2 * segmentCount)], fnc1First, moduleWidth, ref symbol);
        }
        finally
        {
            analysis.Dispose();
        }
    }

    /// <summary>
    /// Writes the per-position runs of the message: how many characters of each class start at each
    /// position, and how far the next character of a class is.
    /// </summary>
    /// <param name="message">The message values.</param>
    /// <param name="runs">The seven rows of runs, each <paramref name="stride"/> long.</param>
    /// <param name="stride">The length of a row, one more than the message.</param>
    private static void Analyze(ReadOnlySpan<int> message, Span<int> runs, int stride)
    {
        int length = message.Length;
        Span<int> numeric = runs.Slice(NumericRun * stride, stride);
        Span<int> alphanumeric = runs.Slice(AlphanumericRun * stride, stride);
        Span<int> either = runs.Slice(AlphanumericOrNumericRun * stride, stride);
        Span<int> bytes = runs.Slice(ByteRun * stride, stride);
        Span<int> nextNumeric = runs.Slice(NextNumeric * stride, stride);
        Span<int> nextAlphanumeric = runs.Slice(NextAlphanumeric * stride, stride);
        Span<int> nextByte = runs.Slice(NextByte * stride, stride);

        numeric[length] = 0;
        alphanumeric[length] = 0;
        either[length] = 0;
        bytes[length] = 0;
        nextNumeric[length] = NoneAhead;
        nextAlphanumeric[length] = NoneAhead;
        nextByte[length] = NoneAhead;

        // A character is a digit, a character only Alphanumeric mode holds besides the digits (Table 5, and
        // the FNC1 character, which Alphanumeric mode writes as %), or a character only Byte mode holds.
        for (int i = length - 1; i >= 0; i--)
        {
            int value = message[i];
            bool digit = value is >= '0' and <= '9';
            bool alpha = value == Fnc1 || AlphanumericValue(value) >= 10;

            numeric[i] = digit ? numeric[i + 1] + 1 : 0;
            nextNumeric[i] = digit ? 0 : nextNumeric[i + 1] + 1;
            alphanumeric[i] = alpha ? alphanumeric[i + 1] + 1 : 0;
            nextAlphanumeric[i] = alpha ? 0 : nextAlphanumeric[i + 1] + 1;
            either[i] = digit || alpha ? either[i + 1] + 1 : 0;
            bytes[i] = digit || alpha ? 0 : bytes[i + 1] + 1;
            nextByte[i] = digit || alpha ? nextByte[i + 1] + 1 : 0;
        }
    }

    /// <summary>
    /// Divides the message into mode segments for a version group, section 6.2 and Annex J. At each position
    /// a run of digits or Alphanumeric characters that is long enough to pay for its indicators starts a
    /// segment of its own; a shorter run stays in the current mode.
    /// </summary>
    /// <param name="runs">The per-position runs, from <see cref="Analyze"/>.</param>
    /// <param name="stride">The length of a row of runs.</param>
    /// <param name="length">The length of the message.</param>
    /// <param name="group">The version group.</param>
    /// <param name="segments">The buffer that receives the mode and the character count of each segment.</param>
    /// <returns>The number of segments, or -1 when the version group cannot hold a character of the message.</returns>
    private static int Segment(ReadOnlySpan<int> runs, int stride, int length, int group, Span<int> segments)
    {
        int count = 0;
        int mode = -1;
        int i = 0;
        while (i < length)
        {
            int numericCount = runs[(NumericRun * stride) + i];
            int alphanumericCount = runs[(AlphanumericRun * stride) + i];
            int eitherCount = runs[(AlphanumericOrNumericRun * stride) + i];
            int byteCount = runs[(ByteRun * stride) + i];

            // Sections 6.3.3 and 6.3.4: M1 has only Numeric mode and M2 has no Byte mode.
            if ((group == GroupM1 && (alphanumericCount > 0 || byteCount > 0)) || (group == GroupM2 && byteCount > 0))
            {
                return -1;
            }

            int next;
            if (mode == -1)
            {
                // The first segment, before any mode is in force.
                if (Before(runs, stride, length, i, numericCount, FirstNumericBeforeByte[group], NextByte))
                {
                    next = Numeric;
                }
                else if (Before(runs, stride, length, i, numericCount, FirstByteAfterNumeric[group], NextByte))
                {
                    next = Byte;
                }
                else if (Before(runs, stride, length, i, numericCount, AlphanumericToNumericToEnd[group], NextAlphanumeric))
                {
                    next = Numeric;
                }
                else if (Before(runs, stride, length, i, numericCount, FirstNumericToEnd[group], End))
                {
                    next = Numeric;
                }
                else if (Before(runs, stride, length, i, eitherCount, ByteToAlphanumericToEnd[group], NextByte))
                {
                    next = Alphanumeric;
                }
                else if (Before(runs, stride, length, i, eitherCount, FirstAlphanumericToEnd[group], End))
                {
                    next = Alphanumeric;
                }
                else
                {
                    next = Byte;
                }
            }
            else if (mode == Byte)
            {
                if (Before(runs, stride, length, i, alphanumericCount, ByteToAlphanumericBeforeByte[group], NextByte)
                    || Before(runs, stride, length, i, alphanumericCount, ByteToAlphanumericBeforeNumeric[group], NextNumeric)
                    || Before(runs, stride, length, i, alphanumericCount, ByteToAlphanumericToEnd[group], End))
                {
                    next = Alphanumeric;
                }
                else if (Before(runs, stride, length, i, numericCount, ByteToNumericBeforeByte[group], NextByte)
                    || Before(runs, stride, length, i, numericCount, ByteToNumericBeforeAlphanumeric[group], NextAlphanumeric)
                    || Before(runs, stride, length, i, numericCount, ByteToNumericToEnd[group], End))
                {
                    next = Numeric;
                }
                else if (Before(runs, stride, length, i, eitherCount, ByteToAlphanumericToEnd[group], End)
                    && eitherCount <= ByteToAlphanumericBeforeNumeric[group]
                    && NextDigitsShort(runs, stride, length, i, ByteToNumericBeforeAlphanumeric[group]))
                {
                    // A short run of Alphanumeric and digit characters that ends the data leaves Byte mode
                    // for Alphanumeric mode, unless a run of digits within it is long enough for Numeric mode.
                    next = Alphanumeric;
                }
                else
                {
                    next = Byte;
                }
            }
            else if (mode == Alphanumeric)
            {
                if (byteCount > 0)
                {
                    next = Byte;
                }
                else if (Before(runs, stride, length, i, numericCount, AlphanumericToNumericBeforeAlphanumeric[group], NextAlphanumeric)
                    || Before(runs, stride, length, i, numericCount, AlphanumericToNumericBeforeByte[group], NextByte)
                    || Before(runs, stride, length, i, numericCount, AlphanumericToNumericToEnd[group], End))
                {
                    next = Numeric;
                }
                else
                {
                    next = Alphanumeric;
                }
            }
            else
            {
                next = byteCount > 0 ? Byte : alphanumericCount > 0 ? Alphanumeric : Numeric;
            }

            if (next == mode)
            {
                // The character joins the segment in force.
                segments[(2 * count) - 1]++;
                i++;
            }
            else
            {
                // A new segment takes the run of its own class that starts here.
                mode = next;
                int take = mode == Numeric ? numericCount : mode == Alphanumeric ? alphanumericCount : byteCount;
                segments[2 * count] = mode;
                segments[(2 * count) + 1] = take;
                count++;
                i += take;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns whether a run starting at a position is at least a given length and is followed by a
    /// character of a class or by the end of the message.
    /// </summary>
    /// <param name="runs">The per-position runs.</param>
    /// <param name="stride">The length of a row of runs.</param>
    /// <param name="length">The length of the message.</param>
    /// <param name="position">The position the run starts at.</param>
    /// <param name="run">The length of the run.</param>
    /// <param name="threshold">The length the run must reach.</param>
    /// <param name="following">The row of distances of the class that must follow, or <see cref="End"/> for the end of the message.</param>
    /// <returns><see langword="true"/> when the run is long enough and the class follows it.</returns>
    private static bool Before(ReadOnlySpan<int> runs, int stride, int length, int position, int run, int threshold, int following)
        => run >= threshold && (following == End ? position + run == length : runs[(following * stride) + position + run] == 0);

    /// <summary>
    /// Returns whether the next run of digits from a position is shorter than a length, or no digit follows.
    /// </summary>
    /// <param name="runs">The per-position runs.</param>
    /// <param name="stride">The length of a row of runs.</param>
    /// <param name="length">The length of the message.</param>
    /// <param name="position">The position.</param>
    /// <param name="threshold">The length.</param>
    /// <returns><see langword="true"/> when the next run of digits is shorter or absent.</returns>
    private static bool NextDigitsShort(ReadOnlySpan<int> runs, int stride, int length, int position, int threshold)
    {
        int distance = runs[(NextNumeric * stride) + position];
        return distance >= length || runs[(NumericRun * stride) + position + distance] < threshold;
    }

    /// <summary>
    /// Returns the number of bits the segments take in a version group, or -1 when a segment holds more
    /// characters than its character count indicator counts or its mode does not exist in the group.
    /// </summary>
    /// <param name="segments">The mode and the character count of each segment.</param>
    /// <param name="group">The version group.</param>
    /// <param name="fnc1First">Whether the FNC1 first position mode indicator precedes the segments.</param>
    /// <returns>The number of bits.</returns>
    private static int BitLength(ReadOnlySpan<int> segments, int group, bool fnc1First)
    {
        int bits = fnc1First ? ModeBits[group] : 0;
        for (int s = 0; s < segments.Length; s += 2)
        {
            int mode = segments[s];
            int count = segments[s + 1];
            int countBits = CountBits[(group * 3) + mode];
            if (countBits == 0 || count >= 1 << countBits)
            {
                return -1;
            }

            // Sections 6.4.3 to 6.4.5: three digits take 10 bits, two Alphanumeric characters 11 bits and a
            // byte 8 bits, with 4 or 7 bits for the last one or two digits and 6 bits for a last
            // Alphanumeric character.
            int lastDigits = count % 3;
            bits += ModeBits[group] + countBits + mode switch
            {
                Numeric => (10 * (count / 3)) + (lastDigits == 2 ? 7 : lastDigits * 4),
                Alphanumeric => (11 * (count / 2)) + (6 * (count % 2)),
                _ => 8 * count,
            };
        }

        return bits;
    }

    /// <summary>
    /// Builds the data codewords of a symbol, its error correction codewords and its module grid.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="message">The message values.</param>
    /// <param name="segments">The mode and the character count of each segment.</param>
    /// <param name="fnc1First">Whether the symbol is in FNC1 first position mode.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildCodewords(
        SymbolSize size,
        QrErrorCorrectionLevel level,
        ReadOnlySpan<int> message,
        ReadOnlySpan<int> segments,
        bool fnc1First,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        int total = size.TotalCodewords;
        int errorCount = size.ErrorCodewords(level);
        int dataCount = total - errorCount;
        int dataBits = size.DataBits(level);
        int group = size.Group;

        // One rent holds the data codewords, the error correction codewords of each block and the final
        // codeword stream, which has one more codeword for the remainder bits. Each codeword is an int, so
        // the Reed-Solomon division reads it directly.
        PooledBuffer<int> buffer = new(total + total + 1);
        try
        {
            buffer.Span.Clear();
            Span<int> codewords = buffer.Span[..total];
            Span<int> stream = buffer.Span.Slice(total, total + 1);

            // Section 6.4: the segments, each a mode indicator, a character count indicator and the data. The
            // FNC1 first position indicator is 0101 in a QR Code symbol and 101 in a Rectangular Micro QR Code
            // symbol.
            int position = 0;
            if (fnc1First)
            {
                position = WriteBits(codewords, position, 0b0101, ModeBits[group]);
            }

            // Table 2: a QR Code mode indicator has one bit set for each mode, 0001, 0010 and 0100, a Micro QR
            // Code one counts the modes from 0, and a Rectangular Micro QR Code one counts them from 1, 001,
            // 010 and 011.
            int start = 0;
            for (int s = 0; s < segments.Length; s += 2)
            {
                int mode = segments[s];
                int count = segments[s + 1];
                int modeIndicator = group <= 2 ? 1 << mode : group >= GroupR7x43 ? mode + 1 : mode;
                position = WriteBits(codewords, position, modeIndicator, ModeBits[group]);
                position = WriteBits(codewords, position, count, CountBits[(group * 3) + mode]);
                ReadOnlySpan<int> data = message.Slice(start, count);
                start += count;

                if (mode == Numeric)
                {
                    for (int i = 0; i < count; i += 3)
                    {
                        int digits = Math.Min(3, count - i);
                        int value = 0;
                        for (int d = 0; d < digits; d++)
                        {
                            value = (value * 10) + (data[i + d] - '0');
                        }

                        position = WriteBits(codewords, position, value, digits switch { 3 => 10, 2 => 7, _ => 4 });
                    }
                }
                else if (mode == Alphanumeric)
                {
                    // Section 6.4.8.1: Alphanumeric mode writes the FNC1 character as %.
                    for (int i = 0; i < count; i += 2)
                    {
                        int first = AlphanumericValue(data[i] == Fnc1 ? '%' : data[i]);
                        position = i + 1 < count
                            ? WriteBits(codewords, position, (first * 45) + AlphanumericValue(data[i + 1] == Fnc1 ? '%' : data[i + 1]), 11)
                            : WriteBits(codewords, position, first, 6);
                    }
                }
                else
                {
                    // Section 6.4.8.1: Byte mode writes the FNC1 character as GS.
                    for (int i = 0; i < count; i++)
                    {
                        position = WriteBits(codewords, position, data[i] == Fnc1 ? Gs1Data.Separator : data[i], 8);
                    }
                }
            }

            // Sections 6.4.9 and 6.4.10: the terminator, shortened where the data leaves less room, then zero
            // bits to the codeword boundary and the pad codewords 11101100 and 00010001 in turn. The
            // codewords start clear, so the zero bits need no writing. The last data codeword of an M1 or M3
            // symbol is 4 bits long and takes no pad codeword.
            position += Math.Min(TerminatorBits[group], dataBits - position);
            int lastPad = dataBits - (size.ShortLastCodeword ? 5 : 1);
            int padIndex = 0;
            for (int p = (position + 7) & ~7; p <= lastPad; p += 8)
            {
                WriteBits(codewords, p, padIndex++ % 2 == 0 ? 0b11101100 : 0b00010001, 8);
            }

            AppendErrorCorrection(size.Blocks(level), dataCount, errorCount, codewords);

            // Section 6.6: the data codewords of the blocks in turn, then their error correction codewords
            // in turn. The short last data codeword of an M1 or M3 symbol keeps its 4 high bits, so the
            // stream stays continuous.
            int blocks = size.Blocks(level);
            int shortData = dataCount / blocks;
            int longBlocks = dataCount % blocks;
            int shortBlocks = blocks - longBlocks;
            int errorPerBlock = errorCount / blocks;
            int bit = 0;
            for (int i = 0; i <= shortData; i++)
            {
                for (int b = 0; b < blocks; b++)
                {
                    int blockLength = b < shortBlocks ? shortData : shortData + 1;
                    if (i < blockLength)
                    {
                        int blockStart = b < shortBlocks ? b * shortData : (shortBlocks * shortData) + ((b - shortBlocks) * (shortData + 1));
                        int codeword = codewords[blockStart + i];
                        bit = size.ShortLastCodeword && blockStart + i == dataCount - 1
                            ? WriteBits(stream, bit, codeword >> 4, 4)
                            : WriteBits(stream, bit, codeword, 8);
                    }
                }
            }

            for (int i = 0; i < errorPerBlock; i++)
            {
                for (int b = 0; b < blocks; b++)
                {
                    bit = WriteBits(stream, bit, codewords[dataCount + (b * errorPerBlock) + i], 8);
                }
            }

            BuildMatrix(size, level, stream, xDimension, ref symbol);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Writes the error correction codewords of each block after the data codewords, section 6.5.2. Block b
    /// takes its error correction codewords at <paramref name="dataCount"/> + b x the codewords per block.
    /// </summary>
    /// <param name="blocks">The number of blocks.</param>
    /// <param name="dataCount">The number of data codewords.</param>
    /// <param name="errorCount">The number of error correction codewords.</param>
    /// <param name="codewords">The data codewords, followed by room for the error correction codewords.</param>
    private static void AppendErrorCorrection(int blocks, int dataCount, int errorCount, Span<int> codewords)
    {
        // Every block has the same number of error correction codewords, at most 30, so one generator
        // polynomial serves all blocks and it is inline. Its roots are alpha^0 to alpha^(k-1).
        int perBlock = errorCount / blocks;
        InlineArray32<int> generatorStorage = default;
        Span<int> generator = ((Span<int>)generatorStorage)[..perBlock];
        GaloisField field = GaloisField.QrCode;
        field.Generator(0, generator);

        // Table 9: the blocks with fewer data codewords come first, and each later block has one more.
        int shortData = dataCount / blocks;
        int shortBlocks = blocks - (dataCount % blocks);
        int start = 0;
        for (int b = 0; b < blocks; b++)
        {
            int count = b < shortBlocks ? shortData : shortData + 1;
            field.Remainder(codewords[start..], 1, count, generator, codewords.Slice(dataCount + (b * perBlock), perBlock));
            start += count;
        }
    }

    /// <summary>
    /// Places the function patterns and the codeword stream in the module grid, chooses the data mask and
    /// adds the format and version information, sections 6.7 to 6.10.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="stream">The final codeword stream, one 8-bit codeword in each int.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildMatrix(SymbolSize size, QrErrorCorrectionLevel level, ReadOnlySpan<int> stream, double xDimension, ref BarcodeSymbol symbol)
    {
        int rows = size.Rows;
        int columns = size.Columns;
        int cells = rows * columns;

        // One rent holds the state of each module, light 0, dark 1 or data 2, and the mask layers. Bit m of
        // a mask layer is the module under data mask pattern m.
        PooledBuffer<byte> buffer = new(2 * cells);
        try
        {
            Span<byte> states = buffer.Span[..cells];
            Span<byte> layers = buffer.Span.Slice(cells, cells);
            states.Fill(DataModule);
            PlaceFunctionPatterns(size, states);

            // A function module is the same under every mask, so all eight bits of its layer take its state.
            // The walk below writes the layer of each data module.
            for (int i = 0; i < cells; i++)
            {
                layers[i] = states[i] == 1 ? (byte)0xFF : (byte)0;
            }

            // Section 6.7.3: two-module columns from the lower right corner, upwards and downwards in turn,
            // the right module before the left one, skipping the vertical timing pattern of a QR Code
            // symbol. The most significant bit of a codeword comes first. A Rectangular Micro QR Code symbol
            // has a timing pattern down its right edge, so its first column pair starts one column in.
            int bit = 0;
            int x = size.Format == QrFormat.Rectangular ? columns - 2 : columns - 1;
            int y = rows - 1;
            int direction = -1;
            bool right = true;
            while (x >= 0)
            {
                int index = (y * columns) + x;
                if (states[index] == DataModule)
                {
                    bool dark = ((stream[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                    layers[index] = (byte)(MaskLayer(y, x) ^ (dark ? 0xFF : 0));
                    bit++;
                }

                if (right)
                {
                    right = false;
                    x--;
                }
                else
                {
                    right = true;
                    x++;
                    y += direction;
                    if (y < 0 || y >= rows)
                    {
                        direction = -direction;
                        y += direction;
                        x -= 2;
                        if (size.Format == QrFormat.Full && x == 6)
                        {
                            x--;
                        }
                    }
                }
            }

            // Section 5.3.7 of ISO/IEC 18004: the quiet zone is 4X around a QR Code symbol and 2X around a
            // Micro QR Code symbol. It is 2X around a Rectangular Micro QR Code symbol too.
            int mask = size.Format switch
            {
                QrFormat.Full => ChooseMask(layers, columns),
                QrFormat.Micro => ChooseMicroMask(layers, columns),
                _ => RectangularMask,
            };

            Span<bool> modules = symbol.BeginMatrix(columns, rows, size.Format == QrFormat.Full ? 4 : 2, xDimension);
            for (int i = 0; i < cells; i++)
            {
                modules[i] = ((layers[i] >> mask) & 1) != 0;
            }

            WriteFormatAndVersion(size, level, mask, modules);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Writes the finder patterns with their separators, the timing patterns, the alignment patterns and
    /// the reserved format and version information modules into the module states.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="states">The module states, row-major.</param>
    private static void PlaceFunctionPatterns(SymbolSize size, Span<byte> states)
    {
        int rows = size.Rows;
        int columns = size.Columns;
        if (size.Format == QrFormat.Rectangular)
        {
            // The timing patterns alternate from a dark module along all four edges. The patterns placed
            // after them replace the modules they cover.
            for (int x = 0; x < columns; x++)
            {
                byte value = (byte)((x + 1) & 1);
                states[x] = value;
                states[((rows - 1) * columns) + x] = value;
            }

            for (int y = 0; y < rows; y++)
            {
                byte value = (byte)((y + 1) & 1);
                states[y * columns] = value;
                states[(y * columns) + columns - 1] = value;
            }

            // The corner patterns at the upper right and lower left corners: three dark modules along the
            // edge, one dark module inward on each side of a light one, and one more on the side edge. They
            // go before the finder pattern, whose separator replaces the lower left one in a 9-row symbol.
            states[columns - 2] = 1;
            states[columns - 3] = 1;
            states[columns + columns - 2] = 0;
            states[columns + columns - 1] = 1;
            states[((rows - 1) * columns) + 1] = 1;
            states[((rows - 1) * columns) + 2] = 1;
            states[((rows - 2) * columns) + 1] = 0;
            states[(rows - 2) * columns] = 1;
            states[(rows - 3) * columns] = 1;

            // The finder pattern and its separator at the upper left corner, the separator row only where the
            // symbol has more than 7 rows, and the 5 x 5 finder sub-pattern at the lower right corner: dark
            // 5 x 5, light 3 x 3 and a dark centre module.
            PlaceFinder(states, columns, rows, 0, 0, 1, 1);
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    bool dark = r is 0 or 4 || c is 0 or 4 || (r == 2 && c == 2);
                    states[((rows - 5 + r) * columns) + columns - 5 + c] = dark ? (byte)1 : (byte)0;
                }
            }

            // The interior alignment patterns: a timing column that alternates from a dark module, with a 3 x 3
            // dark ring around a light centre at its top and its bottom.
            if (size.Align2 > 0)
            {
                int step = size.Align3 > 0 ? size.Align3 - size.Align2 : columns;
                for (int column = size.Align2; column < columns - 12; column += step)
                {
                    for (int y = 3; y < rows - 3; y++)
                    {
                        states[(y * columns) + column] = (byte)((y + 1) & 1);
                    }

                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = -1; c <= 1; c++)
                        {
                            byte value = r == 1 && c == 0 ? (byte)0 : (byte)1;
                            states[(r * columns) + column + c] = value;
                            states[((rows - 3 + r) * columns) + column + c] = value;
                        }
                    }
                }
            }

            // The two copies of the format information are reserved dark. The symbol has one data mask, so
            // their value does not take part in a mask evaluation.
            for (int i = 0; i < 18; i++)
            {
                FormatPosition(size, i, 0, out int row, out int column);
                states[(row * columns) + column] = 1;
                FormatPosition(size, i, 1, out row, out column);
                states[(row * columns) + column] = 1;
            }

            return;
        }

        int n = columns;

        // Section 5.3.4: the timing patterns alternate from a dark module, along row and column 6 of a QR Code
        // symbol and along row and column 0 of a Micro QR Code symbol.
        bool micro = size.Format == QrFormat.Micro;
        int line = micro ? 0 : 6;
        int last = micro ? n - 1 : n - 9;
        for (int i = 8; i <= last; i++)
        {
            byte value = (byte)((i + 1) & 1);
            states[(line * n) + i] = value;
            states[(i * n) + line] = value;
        }

        // Sections 5.3.2 and 5.3.3: a 7 x 7 finder pattern of rings 1 : 1 : 3 : 1 : 1 and a light separator
        // facing the symbol, at the upper left corner, and at the other two corners of a QR Code symbol.
        PlaceFinder(states, n, n, 0, 0, 1, 1);
        if (!micro)
        {
            PlaceFinder(states, n, n, 0, n - 1, 1, -1);
            PlaceFinder(states, n, n, n - 1, 0, -1, 1);

            // Annex E: an alignment pattern at every pair of the coordinates, except where a finder pattern
            // is.
            if (size.Align2 > 0)
            {
                int step = size.Align3 > 0 ? size.Align3 - size.Align2 : n;
                for (int row = 6; row <= n - 7; row = row == 6 ? size.Align2 : row + step)
                {
                    for (int column = 6; column <= n - 7; column = column == 6 ? size.Align2 : column + step)
                    {
                        if ((row == 6 && (column == 6 || column == n - 7)) || (row == n - 7 && column == 6))
                        {
                            continue;
                        }

                        PlaceAlignment(states, n, row, column);
                    }
                }
            }
        }

        // Sections 6.9 and 6.10: the format information modules are reserved dark and the version information
        // modules and the dark module light while the masks are evaluated, then take their final values.
        for (int i = 0; i < 15; i++)
        {
            FormatPosition(size, i, 0, out int row, out int column);
            states[(row * n) + column] = 1;
            if (!micro)
            {
                FormatPosition(size, i, 1, out row, out column);
                states[(row * n) + column] = 1;
            }
        }

        if (!micro)
        {
            states[((n - 8) * n) + 8] = 0;
            if (size.Version >= 7)
            {
                for (int i = 0; i < 18; i++)
                {
                    int a = i / 3;
                    int b = i % 3;
                    states[((5 - a) * n) + (n - 9 - b)] = 0;
                    states[((n - 9 - b) * n) + (5 - a)] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Writes a finder pattern and its separator, the 8 x 8 modules from a corner, without the rows the
    /// symbol does not have.
    /// </summary>
    /// <param name="states">The module states, row-major.</param>
    /// <param name="columns">The number of module columns.</param>
    /// <param name="rows">The number of module rows.</param>
    /// <param name="cornerRow">The row of the corner.</param>
    /// <param name="cornerColumn">The column of the corner.</param>
    /// <param name="rowStep">The direction from the corner along a column, 1 or -1.</param>
    /// <param name="columnStep">The direction from the corner along a row, 1 or -1.</param>
    private static void PlaceFinder(Span<byte> states, int columns, int rows, int cornerRow, int cornerColumn, int rowStep, int columnStep)
    {
        // A 7-row Rectangular Micro QR Code symbol has no row for the separator below the finder pattern.
        int rowCount = Math.Min(8, rows);
        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                // Row and column 7 are the separator. The outer ring and the 3 x 3 centre are dark.
                bool dark = r < 7 && c < 7 && (r == 0 || r == 6 || c == 0 || c == 6 || (r is >= 2 and <= 4 && c is >= 2 and <= 4));
                states[((cornerRow + (r * rowStep)) * columns) + cornerColumn + (c * columnStep)] = dark ? (byte)1 : (byte)0;
            }
        }
    }

    /// <summary>
    /// Writes an alignment pattern, section 5.3.5: dark 5 x 5, light 3 x 3 and a dark centre module.
    /// </summary>
    /// <param name="states">The module states, row-major.</param>
    /// <param name="n">The number of modules on a side.</param>
    /// <param name="row">The row of the centre module.</param>
    /// <param name="column">The column of the centre module.</param>
    private static void PlaceAlignment(Span<byte> states, int n, int row, int column)
    {
        for (int r = -2; r <= 2; r++)
        {
            for (int c = -2; c <= 2; c++)
            {
                bool dark = r is -2 or 2 || c is -2 or 2 || (r == 0 && c == 0);
                states[((row + r) * n) + column + c] = dark ? (byte)1 : (byte)0;
            }
        }
    }

    /// <summary>
    /// Returns the module of a format information bit, counted from the most significant: bit 14 -
    /// <paramref name="index"/> of Figures 25 and 26 of ISO/IEC 18004, or bit 17 - <paramref name="index"/>
    /// of a Rectangular Micro QR Code symbol.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="index">The index of the bit from the most significant, 0 to 14, or 0 to 17.</param>
    /// <param name="copy">
    /// The copy, 0 around the upper left finder pattern and 1 by the other two of a QR Code symbol, or 0
    /// beside the finder pattern and 1 beside the finder sub-pattern of a Rectangular Micro QR Code symbol.
    /// </param>
    /// <param name="row">The row of the module.</param>
    /// <param name="column">The column of the module.</param>
    private static void FormatPosition(SymbolSize size, int index, int copy, out int row, out int column)
    {
        int n = size.Columns;
        if (size.Format == QrFormat.Rectangular)
        {
            // Bits 0 to 14 fill a block of 3 columns x 5 rows column by column, each column from the top,
            // and bits 15 to 17 follow in a line of 3 modules. Beside the finder pattern the block is
            // columns 8 to 10 of rows 1 to 5, with bits 15 to 17 down column 11 from row 1. Beside the
            // finder sub-pattern the block is columns n - 8 to n - 6 of the 5 rows above the bottom row,
            // with bits 15 to 17 along the top of those rows from column n - 5.
            int bit = 17 - index;
            if (copy == 0)
            {
                row = bit < 15 ? 1 + (bit % 5) : bit - 14;
                column = bit < 15 ? 8 + (bit / 5) : 11;
                return;
            }

            row = size.Rows - 6 + (bit < 15 ? bit % 5 : 0);
            column = bit < 15 ? n - 8 + (bit / 5) : n - 20 + bit;
            return;
        }

        if (size.Format == QrFormat.Micro)
        {
            // Bits 14 to 7 along row 8 from column 1, then bits 6 to 0 up column 8 from row 7.
            row = index <= 7 ? 8 : 15 - index;
            column = index <= 7 ? index + 1 : 8;
            return;
        }

        if (copy == 0)
        {
            // Bits 14 to 9 along row 8 from column 0, the timing column skipped, then up column 8 to row 0,
            // the timing row skipped.
            row = index <= 7 ? 8 : index <= 8 ? 7 : 14 - index;
            column = index <= 5 ? index : index == 6 ? 7 : 8;
            return;
        }

        // Bits 14 to 8 up column 8 from the bottom, then bits 7 to 0 along row 8 to the right edge.
        row = index <= 6 ? n - 1 - index : 8;
        column = index <= 6 ? 8 : n - 15 + index;
    }

    /// <summary>
    /// Writes the dark module, the format information and the version information over the chosen grid.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="mask">The data mask pattern reference, 0 to 7, of Table 10.</param>
    /// <param name="modules">The modules, row-major.</param>
    private static void WriteFormatAndVersion(SymbolSize size, QrErrorCorrectionLevel level, int mask, Span<bool> modules)
    {
        int n = size.Columns;
        if (size.Format == QrFormat.Rectangular)
        {
            // The level, 0 for M and 1 for H, and the version indicator, 0 to 31 for R7x43 to R17x139, in the
            // (18, 6) Golay code of the QR Code version information, XORed with a different mask for each copy.
            int data = ((level == QrErrorCorrectionLevel.H ? 1 : 0) << 5) | (size.Version - 1);
            int code = BchCode(data, 12, VersionGenerator);
            int left = code ^ RectangularFormatMaskLeft;
            int right = code ^ RectangularFormatMaskRight;
            for (int i = 0; i < 18; i++)
            {
                FormatPosition(size, i, 0, out int row, out int column);
                modules[(row * n) + column] = ((left >> (17 - i)) & 1) != 0;
                FormatPosition(size, i, 1, out row, out column);
                modules[(row * n) + column] = ((right >> (17 - i)) & 1) != 0;
            }

            return;
        }

        int format;
        if (size.Format == QrFormat.Micro)
        {
            // Section 6.9.2: the symbol number of Table 13 and the two-bit reference of the mask.
            int symbolNumber = MicroSymbolNumbers[((size.Version - 1) * 3) + (int)level];
            format = BchCode((symbolNumber << 2) | MicroMasks.IndexOf((byte)mask), 10, FormatGenerator) ^ MicroFormatMask;
        }
        else
        {
            // Section 6.9.1: the level indicator of Table 12 and the mask reference.
            format = BchCode((LevelIndicators[(int)level] << 3) | mask, 10, FormatGenerator) ^ FormatMask;
            modules[((n - 8) * n) + 8] = true;
        }

        for (int i = 0; i < 15; i++)
        {
            bool dark = ((format >> (14 - i)) & 1) != 0;
            FormatPosition(size, i, 0, out int row, out int column);
            modules[(row * n) + column] = dark;
            if (size.Format == QrFormat.Full)
            {
                FormatPosition(size, i, 1, out row, out column);
                modules[(row * n) + column] = dark;
            }
        }

        // Section 6.10 and Figure 28: version 7 and above carry the version in two 6 x 3 blocks, bit 17 - i at
        // row 5 - i / 3 and column n - 9 - i mod 3 of the upper right block, transposed in the lower left.
        if (size.Format == QrFormat.Full && size.Version >= 7)
        {
            int version = BchCode(size.Version, 12, VersionGenerator);
            for (int i = 0; i < 18; i++)
            {
                bool dark = ((version >> (17 - i)) & 1) != 0;
                int a = i / 3;
                int b = i % 3;
                modules[((5 - a) * n) + (n - 9 - b)] = dark;
                modules[((n - 9 - b) * n) + (5 - a)] = dark;
            }
        }
    }

    /// <summary>
    /// Chooses the data mask of a QR Code symbol, section 6.8.2.1: the pattern with the lowest penalty of
    /// Table 11, the first of equal ones.
    /// </summary>
    /// <param name="layers">The mask layers of the modules.</param>
    /// <param name="n">The number of modules on a side.</param>
    /// <returns>The data mask pattern reference.</returns>
    private static int ChooseMask(ReadOnlySpan<byte> layers, int n)
    {
        InlineArray256<int> runStorage = default;
        Span<int> runs = runStorage;
        int best = 0;
        int bestScore = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            int score = Penalty(layers, n, mask, runs);
            if (score < bestScore)
            {
                bestScore = score;
                best = mask;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the penalty of a data mask pattern, Table 11, over the complete symbol.
    /// </summary>
    /// <param name="layers">The mask layers of the modules.</param>
    /// <param name="n">The number of modules on a side.</param>
    /// <param name="mask">The data mask pattern reference.</param>
    /// <param name="runs">A buffer of at least n + 1 run lengths.</param>
    /// <returns>The penalty.</returns>
    private static int Penalty(ReadOnlySpan<byte> layers, int n, int mask, Span<int> runs)
    {
        int lines = 0;
        for (int i = 0; i < n; i++)
        {
            lines += LinePenalty(runs[..RunLengths(layers, i, n, n, mask, runs)]);
            lines += LinePenalty(runs[..RunLengths(layers, i * n, 1, n, mask, runs)]);
        }

        // N2: 3 points for each 2 x 2 block of one colour, so a larger block counts each block it holds.
        int blocks = 0;
        int dark = 0;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                int value = (layers[(r * n) + c] >> mask) & 1;
                dark += value;
                if (r < n - 1
                    && c < n - 1
                    && ((layers[(r * n) + c + 1] >> mask) & 1) == value
                    && ((layers[((r + 1) * n) + c] >> mask) & 1) == value
                    && ((layers[((r + 1) * n) + c + 1] >> mask) & 1) == value)
                {
                    blocks += 3;
                }
            }
        }

        // N4: 10 points for each full 5% the proportion of dark modules is away from 50%.
        int k = (int)(Math.Abs((dark * 100.0 / (n * n)) - 50) / 5);
        return lines + blocks + (10 * k);
    }

    /// <summary>
    /// Writes the run lengths of one row or column, light and dark in turn from a light run, which is 0
    /// when the line starts dark.
    /// </summary>
    /// <param name="layers">The mask layers of the modules.</param>
    /// <param name="start">The index of the first module.</param>
    /// <param name="step">The distance between two modules of the line.</param>
    /// <param name="n">The number of modules of the line.</param>
    /// <param name="mask">The data mask pattern reference.</param>
    /// <param name="runs">The buffer that receives the run lengths.</param>
    /// <returns>The number of runs.</returns>
    private static int RunLengths(ReadOnlySpan<byte> layers, int start, int step, int n, int mask, Span<int> runs)
    {
        int count = 0;
        int color = 0;
        runs[0] = 0;
        for (int i = 0; i < n; i++)
        {
            int value = (layers[start + (i * step)] >> mask) & 1;
            if (value == color)
            {
                runs[count]++;
            }
            else
            {
                color = value;
                runs[++count] = 1;
            }
        }

        return count + 1;
    }

    /// <summary>
    /// Returns the N1 and N3 penalties of one row or column, Table 11.
    /// </summary>
    /// <param name="runs">The run lengths, light and dark in turn from a light run.</param>
    /// <returns>The penalty.</returns>
    private static int LinePenalty(ReadOnlySpan<int> runs)
    {
        // N1: 3 points for 5 modules of one colour in a line and 1 point for each further module.
        int penalty = 0;
        foreach (int run in runs)
        {
            if (run >= 5)
            {
                penalty += run - 2;
            }
        }

        // N3: 40 points for a dark run of 3k modules between light and dark runs of k modules on each
        // side, the 1 : 1 : 3 : 1 : 1 ratio of the finder pattern, with a light run of at least 4k modules
        // or the edge of the symbol beside it. Odd runs are dark.
        for (int j = 3; j <= runs.Length - 3; j += 2)
        {
            int center = runs[j];
            if (center % 3 != 0)
            {
                continue;
            }

            int unit = center / 3;
            if (runs[j - 2] == unit && runs[j - 1] == unit && runs[j + 1] == unit && runs[j + 2] == unit
                && (j == 3 || j + 4 >= runs.Length || runs[j - 3] >= 4 || runs[j + 3] >= 4))
            {
                penalty += 40;
            }
        }

        return penalty;
    }

    /// <summary>
    /// Chooses the data mask of a Micro QR Code symbol, section 6.8.2.2: the pattern with the most dark
    /// modules on the right and bottom edges, SUM1 x 16 + SUM2 with the smaller sum first, the first of equal
    /// ones.
    /// </summary>
    /// <param name="layers">The mask layers of the modules.</param>
    /// <param name="n">The number of modules on a side.</param>
    /// <returns>The data mask pattern reference of Table 10.</returns>
    private static int ChooseMicroMask(ReadOnlySpan<byte> layers, int n)
    {
        int best = MicroMasks[0];
        int bestScore = -1;
        foreach (byte mask in MicroMasks)
        {
            // The edges exclude the first module of each, which is part of the timing pattern.
            int right = 0;
            int bottom = 0;
            for (int i = 1; i < n; i++)
            {
                right += (layers[(i * n) + n - 1] >> mask) & 1;
                bottom += (layers[((n - 1) * n) + i] >> mask) & 1;
            }

            int score = right <= bottom ? (right * 16) + bottom : (bottom * 16) + right;
            if (score > bestScore)
            {
                bestScore = score;
                best = mask;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the mask layer of a data module: bit m is set where the condition of data mask pattern m,
    /// Table 10, is true, so the module is reversed.
    /// </summary>
    /// <param name="row">The row, i in Table 10.</param>
    /// <param name="column">The column, j in Table 10.</param>
    /// <returns>The mask layer.</returns>
    private static int MaskLayer(int row, int column)
    {
        int product = row * column;
        int layer = 0;
        layer |= (row + column) % 2 == 0 ? 1 : 0;
        layer |= row % 2 == 0 ? 1 << 1 : 0;
        layer |= column % 3 == 0 ? 1 << 2 : 0;
        layer |= (row + column) % 3 == 0 ? 1 << 3 : 0;
        layer |= ((row / 2) + (column / 3)) % 2 == 0 ? 1 << 4 : 0;
        layer |= (product % 2) + (product % 3) == 0 ? 1 << 5 : 0;
        layer |= ((product % 2) + (product % 3)) % 2 == 0 ? 1 << 6 : 0;
        layer |= (((row + column) % 2) + (product % 3)) % 2 == 0 ? 1 << 7 : 0;
        return layer;
    }

    /// <summary>
    /// Returns the value of a character in Alphanumeric mode, Table 5, or -1 when the mode does not hold it.
    /// </summary>
    /// <param name="value">The character value.</param>
    /// <returns>The value, 0 to 44, or -1.</returns>
    private static int AlphanumericValue(int value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'A' and <= 'Z' => value - 'A' + 10,
        ' ' => 36,
        '$' => 37,
        '%' => 38,
        '*' => 39,
        '+' => 40,
        '-' => 41,
        '.' => 42,
        '/' => 43,
        ':' => 44,
        _ => -1,
    };

    /// <summary>
    /// Writes bits into codewords, the most significant first. The codewords start clear.
    /// </summary>
    /// <param name="codewords">The codewords, eight bits in each.</param>
    /// <param name="position">The position of the first bit.</param>
    /// <param name="value">The value.</param>
    /// <param name="count">The number of bits.</param>
    /// <returns>The position after the last bit.</returns>
    private static int WriteBits(Span<int> codewords, int position, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            if (((value >> i) & 1) != 0)
            {
                codewords[position >> 3] |= 1 << (7 - (position & 7));
            }

            position++;
        }

        return position;
    }

    /// <summary>
    /// Returns a value followed by the remainder of its division by a generator polynomial over GF(2),
    /// Annexes C and D.
    /// </summary>
    /// <param name="data">The data bits.</param>
    /// <param name="checkBits">The number of check bits, the degree of the generator.</param>
    /// <param name="generator">The generator polynomial, its highest term included.</param>
    /// <returns>The data bits and then the check bits.</returns>
    private static int BchCode(int data, int checkBits, int generator)
    {
        int remainder = data << checkBits;
        while (remainder != 0)
        {
            int degree = 31 - BitOperations.LeadingZeroCount((uint)remainder);
            if (degree < checkBits)
            {
                break;
            }

            remainder ^= generator << (degree - checkBits);
        }

        return (data << checkBits) | remainder;
    }

    /// <summary>
    /// One QR Code or Micro QR Code symbol size.
    /// </summary>
    private readonly struct SymbolSize
    {
        /// <summary>
        /// The error correction codewords for levels L, M, Q and H, -1 where the level does not exist.
        /// </summary>
        private readonly short errorL;

        /// <summary>
        /// The error correction codewords at level M.
        /// </summary>
        private readonly short errorM;

        /// <summary>
        /// The error correction codewords at level Q.
        /// </summary>
        private readonly short errorQ;

        /// <summary>
        /// The error correction codewords at level H.
        /// </summary>
        private readonly short errorH;

        /// <summary>
        /// The error correction blocks at level L.
        /// </summary>
        private readonly byte blocksL;

        /// <summary>
        /// The error correction blocks at level M.
        /// </summary>
        private readonly byte blocksM;

        /// <summary>
        /// The error correction blocks at level Q.
        /// </summary>
        private readonly byte blocksQ;

        /// <summary>
        /// The error correction blocks at level H.
        /// </summary>
        private readonly byte blocksH;

        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolSize"/> struct for a square size. Section 5.3.1:
        /// a QR Code symbol is 21 modules on a side for version 1 in steps of 4, and a Micro QR Code symbol 11
        /// for M1 in steps of 2.
        /// </summary>
        /// <param name="format">The format, <see cref="QrFormat.Full"/> or <see cref="QrFormat.Micro"/>.</param>
        /// <param name="version">The version, 1 to 40, or 1 to 4 for M1 to M4.</param>
        /// <param name="dataModules">The modules that hold codewords, Table 1.</param>
        /// <param name="align2">The second alignment pattern coordinate, or 0 for none.</param>
        /// <param name="align3">The third alignment pattern coordinate, or 0 for none.</param>
        /// <param name="errorL">The error correction codewords at level L.</param>
        /// <param name="errorM">The error correction codewords at level M, or -1.</param>
        /// <param name="errorQ">The error correction codewords at level Q, or -1.</param>
        /// <param name="errorH">The error correction codewords at level H, or -1.</param>
        /// <param name="blocksL">The error correction blocks at level L.</param>
        /// <param name="blocksM">The error correction blocks at level M, or 0.</param>
        /// <param name="blocksQ">The error correction blocks at level Q, or 0.</param>
        /// <param name="blocksH">The error correction blocks at level H, or 0.</param>
        public SymbolSize(
            QrFormat format,
            int version,
            int dataModules,
            int align2,
            int align3,
            int errorL,
            int errorM,
            int errorQ,
            int errorH,
            int blocksL,
            int blocksM,
            int blocksQ,
            int blocksH)
        {
            int side = format == QrFormat.Micro ? 9 + (2 * version) : 17 + (4 * version);
            this.Format = format;
            this.Version = version;
            this.Rows = side;
            this.Columns = side;
            this.DataModules = dataModules;
            this.Align2 = align2;
            this.Align3 = align3;
            this.errorL = (short)errorL;
            this.errorM = (short)errorM;
            this.errorQ = (short)errorQ;
            this.errorH = (short)errorH;
            this.blocksL = (byte)blocksL;
            this.blocksM = (byte)blocksM;
            this.blocksQ = (byte)blocksQ;
            this.blocksH = (byte)blocksH;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolSize"/> struct for a Rectangular Micro QR Code
        /// size, which has error correction levels M and H only.
        /// </summary>
        /// <param name="version">The <see cref="RectangularMicroQrCodeVersion"/> value, 1 to 32.</param>
        /// <param name="rows">The number of module rows.</param>
        /// <param name="columns">The number of module columns.</param>
        /// <param name="dataModules">The modules that hold codewords.</param>
        /// <param name="align2">The column of the first interior alignment pattern, or 0 for none.</param>
        /// <param name="align3">The column of the second interior alignment pattern, or 0 for none.</param>
        /// <param name="errorM">The error correction codewords at level M.</param>
        /// <param name="errorH">The error correction codewords at level H.</param>
        /// <param name="blocksM">The error correction blocks at level M.</param>
        /// <param name="blocksH">The error correction blocks at level H.</param>
        public SymbolSize(
            int version,
            int rows,
            int columns,
            int dataModules,
            int align2,
            int align3,
            int errorM,
            int errorH,
            int blocksM,
            int blocksH)
        {
            this.Format = QrFormat.Rectangular;
            this.Version = version;
            this.Rows = rows;
            this.Columns = columns;
            this.DataModules = dataModules;
            this.Align2 = align2;
            this.Align3 = align3;
            this.errorL = -1;
            this.errorM = (short)errorM;
            this.errorQ = -1;
            this.errorH = (short)errorH;
            this.blocksM = (byte)blocksM;
            this.blocksH = (byte)blocksH;
        }

        /// <summary>
        /// Gets the format.
        /// </summary>
        public QrFormat Format { get; }

        /// <summary>
        /// Gets the version, 1 to 40, 1 to 4 for M1 to M4, or the <see cref="RectangularMicroQrCodeVersion"/>
        /// value.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Gets the number of module rows.
        /// </summary>
        public int Rows { get; }

        /// <summary>
        /// Gets the number of module columns.
        /// </summary>
        public int Columns { get; }

        /// <summary>
        /// Gets the modules that hold codewords, Table 1.
        /// </summary>
        public int DataModules { get; }

        /// <summary>
        /// Gets the second alignment pattern coordinate of Table E.1, or the column of the first interior
        /// alignment pattern of a Rectangular Micro QR Code size, or 0 for none.
        /// </summary>
        public int Align2 { get; }

        /// <summary>
        /// Gets the third alignment pattern coordinate of Table E.1, or the column of the second interior
        /// alignment pattern of a Rectangular Micro QR Code size, or 0 when there is none.
        /// </summary>
        public int Align3 { get; }

        /// <summary>
        /// Gets the version group, whose character count indicators share their lengths.
        /// </summary>
        public int Group => this.Format switch
        {
            QrFormat.Full => this.Version <= 9 ? 0 : this.Version <= 26 ? 1 : 2,
            QrFormat.Micro => GroupM1 - 1 + this.Version,
            _ => GroupR7x43 - 1 + this.Version,
        };

        /// <summary>
        /// Gets a value indicating whether the last data codeword is 4 bits long, in M1 and M3 symbols.
        /// </summary>
        public bool ShortLastCodeword => this.Format == QrFormat.Micro && (this.Version & 1) == 1;

        /// <summary>
        /// Gets the number of codewords, the last data codeword of M1 and M3 counted as one.
        /// </summary>
        public int TotalCodewords => (this.DataModules / 8) + (this.ShortLastCodeword ? 1 : 0);

        /// <summary>
        /// Returns the number of error correction codewords at a level, or -1 where the level does not exist.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <returns>The number of error correction codewords.</returns>
        public int ErrorCodewords(QrErrorCorrectionLevel level) => level switch
        {
            QrErrorCorrectionLevel.L => this.errorL,
            QrErrorCorrectionLevel.M => this.errorM,
            QrErrorCorrectionLevel.Q => this.errorQ,
            _ => this.errorH,
        };

        /// <summary>
        /// Returns the number of error correction blocks at a level.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <returns>The number of blocks.</returns>
        public int Blocks(QrErrorCorrectionLevel level) => level switch
        {
            QrErrorCorrectionLevel.L => this.blocksL,
            QrErrorCorrectionLevel.M => this.blocksM,
            QrErrorCorrectionLevel.Q => this.blocksQ,
            _ => this.blocksH,
        };

        /// <summary>
        /// Returns the number of data bits at a level, Table 7.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <returns>The number of data bits.</returns>
        public int DataBits(QrErrorCorrectionLevel level)
            => ((this.TotalCodewords - this.ErrorCodewords(level)) * 8) - (this.ShortLastCodeword ? 4 : 0);
    }
}
