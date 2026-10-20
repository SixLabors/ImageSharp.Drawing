// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds QR Code and Micro QR Code symbols, ISO/IEC 18004, and Rectangular Micro QR Code symbols, ISO/IEC 23941: the mode segments of the data, the symbol
/// version, the Reed-Solomon error correction, the module placement, the data mask and the format and version information. Rectangular Micro QR Code shares the
/// data encodation, the error correction and the placement of QR Code, with its own function patterns, indicator lengths, format information and a single data
/// mask.
/// </summary>
internal static class QrEncoder
{
    /// <summary>
    /// The largest number of message values a symbol carries: 7089 digits in a version 40-L symbol, Table 7.
    /// </summary>
    public const int MaximumLength = 7089;

    /// <summary>
    /// The message value of the FNC1 character, which separates element strings in a symbol in FNC1 first position mode. No byte has a negative value.
    /// </summary>
    public const int Fnc1 = -1;

    /// <summary>
    /// The message value of a % of the data in a symbol in FNC1 first position mode. Clause 13.4 of ISO/IEC 18004 writes it as %% in Alphanumeric mode,
    /// because a single % there is the FNC1 character, and Byte mode writes it as the one byte %.
    /// </summary>
    private const int DataPercent = -2;

    /// <summary>
    /// The version group of Micro QR Code version M1. Groups 0 to 2 are QR Code versions 1 to 9, 10 to 26 and 27 to 40, whose character count indicators differ
    /// in length (Table 3), groups 3 to 6 are versions M1 to M4, and groups 7 to 38 are Rectangular Micro QR Code versions R7x43 to R17x139, each with
    /// indicators of its own length.
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
    /// The data mask pattern reference of Table 10 of ISO/IEC 18004 that every Rectangular Micro QR Code symbol uses: (i div 2 + j div 3) mod 2 = 0.
    /// </summary>
    private const int RectangularMask = 4;

    /// <summary>
    /// The mask the format information of a Rectangular Micro QR Code symbol is XORed with, beside the finder pattern.
    /// </summary>
    private const int RectangularFormatMaskLeft = 0x1FAB2;

    /// <summary>
    /// The mask the format information of a Rectangular Micro QR Code symbol is XORed with, beside the finder sub-pattern.
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
    /// The number of rows of per-position values in <see cref="CharacterRuns"/>: four runs and three distances.
    /// </summary>
    private const int RunRows = 7;

    /// <summary>
    /// The generator polynomial of the (15, 5) BCH code of the format information, Annex C: x^10 + x^8 + x^5 + x^4 + x^2 + x + 1.
    /// </summary>
    private const int FormatGenerator = 0x537;

    /// <summary>
    /// The generator polynomial of the (18, 6) Golay code of the version information, Annex D: x^12 + x^11 + x^10 + x^9 + x^8 + x^5 + x^2 + 1.
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
    /// The field of the error correction, section 6.5.2: GF(2^8) with the primitive polynomial x^8 + x^4 + x^3 + x^2 + 1, 285 decimal.
    /// </summary>
    private static readonly GaloisField Field = new(256, 285);

    /// <summary>
    /// The symbol sizes, Micro QR Code versions M1 to M4, QR Code versions 1 to 40 and then Rectangular Micro QR Code versions R7x43 to R17x139. Each carries
    /// its data modules (Table 1 of ISO/IEC 18004), its error correction codewords and blocks for levels L, M, Q and H (Table 9, -1 where the level does not
    /// exist), and the first two interior alignment coordinates, after which the coordinates step evenly to the last. Those are the second and third
    /// coordinates of Table E.1 for QR Code, and the columns of the interior alignment patterns for Rectangular Micro QR Code, which has levels M and H only.
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
    /// The data encodation modes, sections 6.4.3 to 6.4.5. The value is the column of the mode in <see cref="CountBits"/> and the Micro QR Code mode indicator,
    /// Table 2.
    /// </summary>
    private enum SegmentMode
    {
        /// <summary>
        /// Numeric mode: three digits in 10 bits.
        /// </summary>
        Numeric,

        /// <summary>
        /// Alphanumeric mode: two characters of Table 5 in 11 bits.
        /// </summary>
        Alphanumeric,

        /// <summary>
        /// Byte mode: one byte in 8 bits.
        /// </summary>
        Byte,
    }

    /// <summary>
    /// The state of a module while the function patterns and the data fill the grid.
    /// </summary>
    private enum ModuleState : byte
    {
        /// <summary>
        /// A light function module.
        /// </summary>
        Light,

        /// <summary>
        /// A dark function module.
        /// </summary>
        Dark,

        /// <summary>
        /// A module the codeword stream fills.
        /// </summary>
        Data,
    }

    /// <summary>
    /// Gets the length of the character count indicator of each version group for Numeric, Alphanumeric and Byte mode, Table 3, or 0 where the version does not
    /// have the mode. The rows after M4 are the Rectangular Micro QR Code versions R7x43 to R17x139.
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
    /// Gets the length of the mode indicator of each version group, Table 2. Every Rectangular Micro QR Code version has a 3-bit mode indicator.
    /// </summary>
    private static ReadOnlySpan<byte> ModeBits =>
    [
        4, 4, 4, 0, 1, 2, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    ];

    /// <summary>
    /// Gets the length of the terminator of each version group, Table 2. Every Rectangular Micro QR Code version has a 3-bit terminator.
    /// </summary>
    private static ReadOnlySpan<byte> TerminatorBits =>
    [
        4, 4, 4, 3, 5, 7, 9,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    ];

    // The run lengths at which the data analysis changes mode, for each version group. A mode change costs a mode indicator and a character count indicator, so
    // a run that is short enough stays in the current mode. The counts are the break-even run lengths for the indicator lengths of each group.

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
    /// Gets the characters of Alphanumeric or Numeric mode that end the data and make it start in Alphanumeric mode.
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
    /// Gets the characters only Alphanumeric mode holds that leave Byte mode when a Byte mode character follows them.
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
    /// Gets the digits that leave Alphanumeric mode when a character only Alphanumeric mode holds follows them.
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
    /// Gets the data mask pattern references of Table 10 a Micro QR Code symbol uses, in the order of its two bit references 00 to 11.
    /// </summary>
    private static ReadOnlySpan<byte> MicroMasks => [1, 4, 6, 7];

    /// <summary>
    /// Gets the symbol numbers of Table 13, for versions M1 to M4 and levels L, M and Q, or -1 where the version does not have the level.
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
    /// <param name="fnc1First"> Whether the symbol is in FNC1 first position mode, section 6.4.8.1, for GS1 element strings. Each
    /// <see cref="Gs1Data.Separator"/> is then an FNC1 character. Micro QR Code does not have the mode. </param>
    /// <param name="format">The symbol format.</param>
    /// <param name="level">The error correction level, M or H for Rectangular Micro QR Code.</param>
    /// <param name="version"> The version, 1 to 40, 1 to 4 for M1 to M4 or the <see cref="RectangularMicroQrCodeVersion"/> value, or 0 to choose it from the
    /// data. </param>
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
    /// Encodes the text into a symbol whose specification fixes its width rather than its X dimension. The version follows from the data, so the X dimension is
    /// the width divided by the module count of the version.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="fnc1First"> Whether the symbol is in FNC1 first position mode, section 6.4.8.1, for GS1 element strings. Each
    /// <see cref="Gs1Data.Separator"/> is then an FNC1 character. Micro QR Code does not have the mode. </param>
    /// <param name="format">The symbol format.</param>
    /// <param name="level">The error correction level, M or H for Rectangular Micro QR Code.</param>
    /// <param name="version"> The version, 1 to 40, 1 to 4 for M1 to M4 or the <see cref="RectangularMicroQrCodeVersion"/> value, or 0 to choose it from the
    /// data. </param>
    /// <param name="xDimension"> The X dimension in millimetres the caller sets, <see cref="BarcodeOptions.XDimension"/>, or <see langword="null"/> to take
    /// <paramref name="symbolWidth"/> divided by the module count. </param>
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

        // Clause 6.3.4: Byte mode carries ISO/IEC 8859-1, the default interpretation, so each character must be one byte value. The message reports the code
        // point at the first invalid character, so a surrogate pair reports as one character.
        int invalid = text.IndexOfAnyExceptInRange('\0', (char)byte.MaxValue);
        if (invalid >= 0)
        {
            throw new ArgumentException(
                $"{name} carries only the characters U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.",
                nameof(text));
        }

        int length = text.Length;
        if (length > MaximumLength)
        {
            throw new ArgumentException($"The data takes {length} characters; a {name} symbol carries at most {MaximumLength}.", nameof(text));
        }

        // One rent holds the message, the seven rows of per-position runs and the mode segments. Each run row has one entry past the message, which stops the
        // runs. Every segment holds at least one character, so the message length bounds the segments, and each segment takes two ints.
        int stride = length + 1;
        PooledBuffer<int> analysis = new(length + (RunRows * stride) + (2 * stride));
        try
        {
            Span<int> message = analysis.Span[..length];
            Span<Segment> segments = MemoryMarshal.Cast<int, Segment>(analysis.Span.Slice(length + (RunRows * stride), 2 * stride));

            int m = 0;
            foreach (char c in text)
            {
                if (fnc1First && c == Gs1Data.Separator)
                {
                    message[m++] = Fnc1;
                    continue;
                }

                // Clause 13.4: a % of the data doubles in Alphanumeric mode only, so it stays one value until its segment's mode is known.
                message[m++] = fnc1First && c == '%' ? DataPercent : c;
            }

            CharacterRuns runs = new(message, analysis.Span.Slice(length, RunRows * stride));

            // Step 1 of section 6.1: the smallest version that holds the data at the level. The data takes a different number of bits in each version group,
            // because the indicators differ in length. The sizes of a group are adjacent in the table, so each group is segmented once, when the walk reaches
            // it. A Rectangular Micro QR Code size is smallest by area, and of the two sizes with the same area, R7x99 and R9x77, the later one in the table is
            // chosen, so every size that holds the data is compared.
            int chosen = -1;
            int segmentedGroup = -1;
            int segmentCount = -1;
            int bits = -1;
            for (int i = 0; i < Sizes.Length; i++)
            {
                SymbolSize size = Sizes[i];
                if (size.Format != format || (version != 0 && size.Version != version) || size.ErrorCodewords(level) < 0)
                {
                    continue;
                }

                if (size.Group != segmentedGroup)
                {
                    segmentedGroup = size.Group;
                    segmentCount = runs.BuildSegments(segmentedGroup, segments);
                    bits = segmentCount < 0 ? -1 : BitLength(message, segments[..segmentCount], segmentedGroup, fnc1First);
                }

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

            // The walk past a chosen Rectangular Micro QR Code size can segment later groups, so the segments are rebuilt for the chosen group.
            SymbolSize chosenSize = Sizes[chosen];
            if (chosenSize.Group != segmentedGroup)
            {
                segmentCount = runs.BuildSegments(chosenSize.Group, segments);
            }

            double moduleWidth = xDimension ?? (symbolWidth / chosenSize.Columns);
            BuildCodewords(chosenSize, level, message, segments[..segmentCount], fnc1First, moduleWidth, ref symbol);
        }
        finally
        {
            analysis.Dispose();
        }
    }

    /// <summary>
    /// Returns the number of bits the segments take in a version group, or -1 when a segment holds more characters than its character count indicator counts or
    /// its mode does not exist in the group.
    /// </summary>
    /// <param name="message">The message values the segments cover.</param>
    /// <param name="segments">The segments.</param>
    /// <param name="group">The version group.</param>
    /// <param name="fnc1First">Whether the FNC1 first position mode indicator precedes the segments.</param>
    /// <returns>The number of bits.</returns>
    private static int BitLength(ReadOnlySpan<int> message, ReadOnlySpan<Segment> segments, int group, bool fnc1First)
    {
        int bits = fnc1First ? ModeBits[group] : 0;
        int start = 0;
        foreach (Segment segment in segments)
        {
            int count = EncodedCount(message.Slice(start, segment.Count), segment.Mode);
            start += segment.Count;
            int countBits = CountBits[(group * 3) + (int)segment.Mode];
            if (countBits == 0 || count >= 1 << countBits)
            {
                return -1;
            }

            // Sections 6.4.3 to 6.4.5: three digits take 10 bits, two Alphanumeric characters 11 bits and a byte 8 bits, with 4 or 7 bits for the last one or
            // two digits and 6 bits for a last Alphanumeric character.
            int lastDigits = count % 3;
            bits += ModeBits[group] + countBits + segment.Mode switch
            {
                SegmentMode.Numeric => (10 * (count / 3)) + (lastDigits == 2 ? 7 : lastDigits * 4),
                SegmentMode.Alphanumeric => (11 * (count / 2)) + (6 * (count % 2)),
                _ => 8 * count,
            };
        }

        return bits;
    }

    /// <summary>
    /// Returns the number of characters a segment encodes, the value of its character count indicator. Clause 13.4 of ISO/IEC 18004 writes a % of the data
    /// as %% in Alphanumeric mode, so each one counts twice there.
    /// </summary>
    /// <param name="data">The message values of the segment.</param>
    /// <param name="mode">The mode of the segment.</param>
    /// <returns>The number of characters the segment encodes.</returns>
    private static int EncodedCount(ReadOnlySpan<int> data, SegmentMode mode)
        => mode == SegmentMode.Alphanumeric ? data.Length + data.Count(DataPercent) : data.Length;

    /// <summary>
    /// Builds the data codewords of a symbol, its error correction codewords and its module grid.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="message">The message values.</param>
    /// <param name="segments">The segments of the message.</param>
    /// <param name="fnc1First">Whether the symbol is in FNC1 first position mode.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildCodewords(
        SymbolSize size,
        QrErrorCorrectionLevel level,
        ReadOnlySpan<int> message,
        ReadOnlySpan<Segment> segments,
        bool fnc1First,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        int total = size.TotalCodewords;
        int errorCount = size.ErrorCodewords(level);
        int dataCount = total - errorCount;
        int dataBits = size.DataBits(level);
        int group = size.Group;

        // One rent holds the data codewords, the error correction codewords of each block and the final codeword stream, which has one more codeword for the
        // remainder bits. Each codeword is an int, so the Reed-Solomon division reads it directly.
        PooledBuffer<int> buffer = new(total + total + 1);
        try
        {
            buffer.Span.Clear();
            Span<int> codewords = buffer.Span[..total];
            Span<int> stream = buffer.Span.Slice(total, total + 1);

            // Section 6.4: the segments, each a mode indicator, a character count indicator and the data. The FNC1 first position indicator is 0101 in a QR
            // Code symbol and 101 in a Rectangular Micro QR Code symbol.
            int position = 0;
            if (fnc1First)
            {
                position = WriteBits(codewords, position, 0b0101, ModeBits[group]);
            }

            // Table 2: a QR Code mode indicator has one bit set for each mode, 0001, 0010 and 0100, a Micro QR Code one counts the modes from 0, and a
            // Rectangular Micro QR Code one counts them from 1, 001, 010 and 011.
            int start = 0;
            foreach (Segment segment in segments)
            {
                int mode = (int)segment.Mode;
                int count = segment.Count;
                ReadOnlySpan<int> data = message.Slice(start, count);
                start += count;
                int modeIndicator = group < GroupM1 ? 1 << mode : group >= GroupR7x43 ? mode + 1 : mode;
                position = WriteBits(codewords, position, modeIndicator, ModeBits[group]);
                position = WriteBits(codewords, position, EncodedCount(data, segment.Mode), CountBits[(group * 3) + mode]);

                if (segment.Mode == SegmentMode.Numeric)
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
                else if (segment.Mode == SegmentMode.Alphanumeric)
                {
                    // Section 6.4.8.1 writes the FNC1 character as %, and clause 13.4 a % of the data as %%. Each pair of characters takes 11 bits, so a
                    // character waits for the next one, and a last unpaired character takes 6 bits.
                    int pending = -1;
                    foreach (int value in data)
                    {
                        int alphanumeric = AlphanumericValue(value < 0 ? '%' : value);
                        for (int repeat = value == DataPercent ? 2 : 1; repeat > 0; repeat--)
                        {
                            if (pending < 0)
                            {
                                pending = alphanumeric;
                            }
                            else
                            {
                                position = WriteBits(codewords, position, (pending * 45) + alphanumeric, 11);
                                pending = -1;
                            }
                        }
                    }

                    if (pending >= 0)
                    {
                        position = WriteBits(codewords, position, pending, 6);
                    }
                }
                else
                {
                    // Section 6.4.8.1: Byte mode writes the FNC1 character as GS, and a % of the data as the one byte %.
                    foreach (int value in data)
                    {
                        position = WriteBits(codewords, position, value switch { Fnc1 => Gs1Data.Separator, DataPercent => '%', _ => value }, 8);
                    }
                }
            }

            // Sections 6.4.9 and 6.4.10: the terminator, shortened where the data leaves less room, then zero bits to the codeword boundary and the pad
            // codewords 11101100 and 00010001 in turn. The codewords start clear, so the zero bits need no writing. The last data codeword of an M1 or M3
            // symbol is 4 bits long and takes no pad codeword.
            position += Math.Min(TerminatorBits[group], dataBits - position);
            int lastPad = dataBits - (size.ShortLastCodeword ? 5 : 1);
            int padIndex = 0;
            for (int p = (position + 7) & ~7; p <= lastPad; p += 8)
            {
                WriteBits(codewords, p, padIndex++ % 2 == 0 ? 0b11101100 : 0b00010001, 8);
            }

            int blocks = size.Blocks(level);
            AppendErrorCorrection(blocks, dataCount, errorCount, codewords);

            // Section 6.6: the data codewords of the blocks in turn, then their error correction codewords in turn. The short last data codeword of an M1 or M3
            // symbol keeps its 4 high bits, so the stream stays continuous.
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
    /// Writes the error correction codewords of each block after the data codewords, section 6.5.2. Block b takes its error correction codewords at
    /// <paramref name="dataCount"/> + b x the codewords per block.
    /// </summary>
    /// <param name="blocks">The number of blocks.</param>
    /// <param name="dataCount">The number of data codewords.</param>
    /// <param name="errorCount">The number of error correction codewords.</param>
    /// <param name="codewords">The data codewords, followed by room for the error correction codewords.</param>
    private static void AppendErrorCorrection(int blocks, int dataCount, int errorCount, Span<int> codewords)
    {
        // Every block has the same number of error correction codewords, at most 30, so one generator polynomial serves all blocks and it is inline. Its roots
        // are alpha^0 to alpha^(k-1).
        int perBlock = errorCount / blocks;
        InlineArray32<int> generatorStorage = default;
        Span<int> generator = ((Span<int>)generatorStorage)[..perBlock];
        GaloisField field = Field;
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
    /// Places the function patterns and the codeword stream in the module grid, chooses the data mask and adds the format and version information, sections 6.7
    /// to 6.10.
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

        // One rent holds the state of each module and the mask layers. Bit m of a mask layer is the module under data mask pattern m.
        PooledBuffer<byte> buffer = new(2 * cells);
        try
        {
            Span<ModuleState> states = MemoryMarshal.Cast<byte, ModuleState>(buffer.Span[..cells]);
            Span<byte> layers = buffer.Span.Slice(cells, cells);
            states.Fill(ModuleState.Data);
            PlaceFunctionPatterns(size, new ModuleGrid(states, rows, columns));

            // A function module is the same under every mask, so all eight bits of its layer take its state. The walk below writes the layer of each data
            // module.
            for (int i = 0; i < cells; i++)
            {
                layers[i] = states[i] == ModuleState.Dark ? (byte)0xFF : (byte)0;
            }

            // Section 6.7.3: two-module columns from the lower right corner, upwards and downwards in turn, the right module before the left one, skipping the
            // vertical timing pattern of a QR Code symbol. The most significant bit of a codeword comes first. A Rectangular Micro QR Code symbol has a timing
            // pattern down its right edge, so its first column pair starts one column in.
            int bit = 0;
            int x = size.Format == QrFormat.Rectangular ? columns - 2 : columns - 1;
            int y = rows - 1;
            int direction = -1;
            bool right = true;
            while (x >= 0)
            {
                int index = (y * columns) + x;
                if (states[index] == ModuleState.Data)
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

            // Section 6.8.2.1: "the area to be evaluated is the complete symbol", so the format information of each mask, the dark module and the version
            // information of a QR Code symbol are in the layers before the masks are scored.
            if (size.Format == QrFormat.Full)
            {
                WriteFormatLayers(size, level, layers);
            }

            int mask = size.Format switch
            {
                QrFormat.Full => ChooseMask(layers, columns),
                QrFormat.Micro => ChooseMicroMask(layers, columns),
                _ => RectangularMask,
            };

            // Section 5.3.7 of ISO/IEC 18004: the quiet zone is 4X around a QR Code symbol and 2X around a Micro QR Code symbol. It is 2X around a Rectangular
            // Micro QR Code symbol too.
            Span<bool> modules = symbol.BeginMatrix(columns, rows, size.Format == QrFormat.Full ? 4 : 2, xDimension);
            for (int i = 0; i < cells; i++)
            {
                modules[i] = ((layers[i] >> mask) & 1) != 0;
            }

            if (size.Format != QrFormat.Full)
            {
                WriteMicroFormat(size, level, mask, modules);
            }
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Writes the finder patterns with their separators, the timing patterns, the alignment patterns and the reserved format and version information modules
    /// into the module states.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="grid">The module states.</param>
    private static void PlaceFunctionPatterns(SymbolSize size, ModuleGrid grid)
    {
        int rows = size.Rows;
        int columns = size.Columns;
        if (size.Format == QrFormat.Rectangular)
        {
            // The timing patterns alternate from a dark module along all four edges. The patterns placed after them replace the modules they cover.
            for (int x = 0; x < columns; x++)
            {
                ModuleState value = Timing(x);
                grid[0, x] = value;
                grid[rows - 1, x] = value;
            }

            for (int y = 0; y < rows; y++)
            {
                ModuleState value = Timing(y);
                grid[y, 0] = value;
                grid[y, columns - 1] = value;
            }

            // The corner patterns at the upper right and lower left corners: three dark modules along the edge, one dark module inward on each side of a light
            // one, and one more on the side edge. They go before the finder pattern, whose separator replaces the lower left one in a 9-row symbol.
            grid[0, columns - 2] = ModuleState.Dark;
            grid[0, columns - 3] = ModuleState.Dark;
            grid[1, columns - 2] = ModuleState.Light;
            grid[1, columns - 1] = ModuleState.Dark;
            grid[rows - 1, 1] = ModuleState.Dark;
            grid[rows - 1, 2] = ModuleState.Dark;
            grid[rows - 2, 1] = ModuleState.Light;
            grid[rows - 2, 0] = ModuleState.Dark;
            grid[rows - 3, 0] = ModuleState.Dark;

            // The finder pattern and its separator at the upper left corner, the separator row only where the symbol has more than 7 rows, and the 5 x 5 finder
            // sub-pattern at the lower right corner, which has the form of an alignment pattern.
            grid.PlaceFinder(0, 0, 1, 1);
            grid.PlaceAlignment(rows - 3, columns - 3);

            // The interior alignment patterns: a timing column that alternates from a dark module, with a 3 x 3 dark ring around a light centre at its top and
            // its bottom.
            if (size.Align2 > 0)
            {
                int step = size.Align3 > 0 ? size.Align3 - size.Align2 : columns;
                for (int column = size.Align2; column < columns - 12; column += step)
                {
                    for (int y = 3; y < rows - 3; y++)
                    {
                        grid[y, column] = Timing(y);
                    }

                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = -1; c <= 1; c++)
                        {
                            ModuleState value = r == 1 && c == 0 ? ModuleState.Light : ModuleState.Dark;
                            grid[r, column + c] = value;
                            grid[rows - 3 + r, column + c] = value;
                        }
                    }
                }
            }

            // The two copies of the format information are reserved dark. The symbol has one data mask, so their value does not take part in a mask evaluation.
            for (int i = 0; i < 18; i++)
            {
                FormatPosition(size, i, 0, out int row, out int column);
                grid[row, column] = ModuleState.Dark;
                FormatPosition(size, i, 1, out row, out column);
                grid[row, column] = ModuleState.Dark;
            }

            return;
        }

        int n = columns;

        // Section 5.3.4: the timing patterns alternate from a dark module, along row and column 6 of a QR Code symbol and along row and column 0 of a Micro QR
        // Code symbol.
        bool micro = size.Format == QrFormat.Micro;
        int line = micro ? 0 : 6;
        int last = micro ? n - 1 : n - 9;
        for (int i = 8; i <= last; i++)
        {
            ModuleState value = Timing(i);
            grid[line, i] = value;
            grid[i, line] = value;
        }

        // Sections 5.3.2 and 5.3.3: a 7 x 7 finder pattern of rings 1 : 1 : 3 : 1 : 1 and a light separator facing the symbol, at the upper left corner, and at
        // the other two corners of a QR Code symbol.
        grid.PlaceFinder(0, 0, 1, 1);
        if (!micro)
        {
            grid.PlaceFinder(0, n - 1, 1, -1);
            grid.PlaceFinder(n - 1, 0, -1, 1);

            // Annex E: an alignment pattern at every pair of the coordinates, except where a finder pattern is.
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

                        grid.PlaceAlignment(row, column);
                    }
                }
            }
        }

        // Sections 6.9 and 6.10: the format information modules, the dark module and the version information modules are reserved, so the data skips them.
        // They take their values before the masks of a QR Code symbol are scored, and after the mask of a Micro QR Code symbol is chosen.
        for (int i = 0; i < 15; i++)
        {
            FormatPosition(size, i, 0, out int row, out int column);
            grid[row, column] = ModuleState.Dark;
            if (!micro)
            {
                FormatPosition(size, i, 1, out row, out column);
                grid[row, column] = ModuleState.Dark;
            }
        }

        if (!micro)
        {
            grid[n - 8, 8] = ModuleState.Light;
            if (size.Version >= 7)
            {
                for (int i = 0; i < 18; i++)
                {
                    int a = i / 3;
                    int b = i % 3;
                    grid[5 - a, n - 9 - b] = ModuleState.Light;
                    grid[n - 9 - b, 5 - a] = ModuleState.Light;
                }
            }
        }
    }

    /// <summary>
    /// Returns the module of a timing pattern at a position along it. Section 5.3.4: the pattern alternates and starts dark at position 0.
    /// </summary>
    /// <param name="position">The row or column of the module.</param>
    /// <returns>The module state.</returns>
    private static ModuleState Timing(int position) => (position & 1) == 0 ? ModuleState.Dark : ModuleState.Light;

    /// <summary>
    /// Returns the module of a format information bit, counted from the most significant: bit 14 - <paramref name="index"/> of Figures 25 and 26 of ISO/IEC
    /// 18004, or bit 17 - <paramref name="index"/> of a Rectangular Micro QR Code symbol.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="index">The index of the bit from the most significant, 0 to 14, or 0 to 17.</param>
    /// <param name="copy"> The copy, 0 around the upper left finder pattern and 1 by the other two of a QR Code symbol, or 0 beside the finder pattern and 1
    /// beside the finder sub-pattern of a Rectangular Micro QR Code symbol. </param>
    /// <param name="row">The row of the module.</param>
    /// <param name="column">The column of the module.</param>
    private static void FormatPosition(SymbolSize size, int index, int copy, out int row, out int column)
    {
        int n = size.Columns;
        if (size.Format == QrFormat.Rectangular)
        {
            // Bits 0 to 14 fill a block of 3 columns x 5 rows column by column, each column from the top, and bits 15 to 17 follow in a line of 3 modules.
            // Beside the finder pattern the block is columns 8 to 10 of rows 1 to 5, with bits 15 to 17 down column 11 from row 1. Beside the finder
            // sub-pattern the block is columns n - 8 to n - 6 of the 5 rows above the bottom row, with bits 15 to 17 along the top of those rows from column n
            // - 5.
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
            // Bits 14 to 9 along row 8 from column 0, the timing column skipped, then up column 8 to row 0, the timing row skipped.
            row = index <= 7 ? 8 : index <= 8 ? 7 : 14 - index;
            column = index <= 5 ? index : index == 6 ? 7 : 8;
            return;
        }

        // Bits 14 to 8 up column 8 from the bottom, then bits 7 to 0 along row 8 to the right edge.
        row = index <= 6 ? n - 1 - index : 8;
        column = index <= 6 ? 8 : n - 15 + index;
    }

    /// <summary>
    /// Writes the dark module, the format information of every data mask pattern and the version information of a QR Code symbol into its mask layers. Bit m
    /// of a layer is the module under data mask pattern m, and the format information carries the reference of its mask, so bit m of a format information
    /// module takes the format information of mask m. The dark module and the version information are the same under every mask.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="layers">The mask layers of the modules, row-major.</param>
    private static void WriteFormatLayers(SymbolSize size, QrErrorCorrectionLevel level, Span<byte> layers)
    {
        int n = size.Columns;

        // Section 6.9.1: the module in position (4V + 9, 8) "shall always be dark".
        layers[((n - 8) * n) + 8] = 0xFF;

        for (int mask = 0; mask < 8; mask++)
        {
            // Section 6.9.1: the level indicator of Table 12 and the mask reference.
            int format = BchCode((LevelIndicators[(int)level] << 3) | mask, 10, FormatGenerator) ^ FormatMask;
            int clear = ~(1 << mask);
            for (int i = 0; i < 15; i++)
            {
                int bit = ((format >> (14 - i)) & 1) << mask;
                for (int copy = 0; copy < 2; copy++)
                {
                    FormatPosition(size, i, copy, out int row, out int column);
                    int index = (row * n) + column;
                    layers[index] = (byte)((layers[index] & clear) | bit);
                }
            }
        }

        // Section 6.10 and Figure 28: version 7 and above carry the version in two 6 x 3 blocks, bit 17 - i at row 5 - i / 3 and column n - 9 - i mod 3 of the
        // upper right block, transposed in the lower left.
        if (size.Version >= 7)
        {
            int version = BchCode(size.Version, 12, VersionGenerator);
            for (int i = 0; i < 18; i++)
            {
                byte layer = ((version >> (17 - i)) & 1) != 0 ? (byte)0xFF : (byte)0;
                int a = i / 3;
                int b = i % 3;
                layers[((5 - a) * n) + (n - 9 - b)] = layer;
                layers[((n - 9 - b) * n) + (5 - a)] = layer;
            }
        }
    }

    /// <summary>
    /// Writes the format information of a Micro QR Code or Rectangular Micro QR Code symbol over the chosen grid.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="mask">The data mask pattern reference, 0 to 7, of Table 10.</param>
    /// <param name="modules">The modules, row-major.</param>
    private static void WriteMicroFormat(SymbolSize size, QrErrorCorrectionLevel level, int mask, Span<bool> modules)
    {
        int n = size.Columns;
        if (size.Format == QrFormat.Rectangular)
        {
            // The level, 0 for M and 1 for H, and the version indicator, 0 to 31 for R7x43 to R17x139, in the (18, 6) Golay code of the QR Code version
            // information, XORed with a different mask for each copy.
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

        // Section 6.9.2: the symbol number of Table 13 and the two-bit reference of the mask.
        int symbolNumber = MicroSymbolNumbers[((size.Version - 1) * 3) + (int)level];
        int format = BchCode((symbolNumber << 2) | MicroMasks.IndexOf((byte)mask), 10, FormatGenerator) ^ MicroFormatMask;
        for (int i = 0; i < 15; i++)
        {
            FormatPosition(size, i, 0, out int row, out int column);
            modules[(row * n) + column] = ((format >> (14 - i)) & 1) != 0;
        }
    }

    /// <summary>
    /// Chooses the data mask of a QR Code symbol, section 6.8.2.1: the pattern with the lowest penalty of Table 11, the first of equal ones.
    /// </summary>
    /// <param name="layers">The mask layers of the modules.</param>
    /// <param name="n">The number of modules on a side.</param>
    /// <returns>The data mask pattern reference.</returns>
    private static int ChooseMask(ReadOnlySpan<byte> layers, int n)
    {
        // A line of n modules has at most n + 1 runs, the light run first, and n is at most 177.
        InlineArray256<int> runStorage = default;
        MaskPenalty penalty = new(layers, n, runStorage);
        int best = 0;
        int bestScore = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            int score = penalty.Score(mask);
            if (score < bestScore)
            {
                bestScore = score;
                best = mask;
            }
        }

        return best;
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

        // N3: 40 points for a dark run of 3k modules between light and dark runs of k modules on each side, the 1 : 1 : 3 : 1 : 1 ratio of the finder pattern,
        // with a light run of at least 4 modules or the edge of the symbol beside it, the "light area 4 modules wide" of Table 11. Odd runs are dark.
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
    /// Chooses the data mask of a Micro QR Code symbol, section 6.8.2.2: the pattern with the most dark modules on the right and bottom edges, SUM1 x 16 + SUM2
    /// with the smaller sum first, the first of equal ones.
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
    /// Returns the mask layer of a data module: bit m is set where the condition of data mask pattern m, Table 10, is true, so the module is reversed.
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
    /// Returns a value followed by the remainder of its division by a generator polynomial over GF(2), Annexes C and D.
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
    /// One QR Code, Micro QR Code or Rectangular Micro QR Code symbol size.
    /// </summary>
    private readonly struct SymbolSize
    {
        /// <summary>
        /// The error correction codewords at level L, or -1 where the level does not exist.
        /// </summary>
        private readonly short errorL;

        /// <summary>
        /// The error correction codewords at level M, or -1 where the level does not exist.
        /// </summary>
        private readonly short errorM;

        /// <summary>
        /// The error correction codewords at level Q, or -1 where the level does not exist.
        /// </summary>
        private readonly short errorQ;

        /// <summary>
        /// The error correction codewords at level H, or -1 where the level does not exist.
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
        /// Initializes a new instance of the <see cref="SymbolSize"/> struct for a square size. Section 5.3.1: a QR Code symbol is 21 modules on a side for
        /// version 1 in steps of 4, and a Micro QR Code symbol 11 for M1 in steps of 2.
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
        /// Initializes a new instance of the <see cref="SymbolSize"/> struct for a Rectangular Micro QR Code size, which has error correction levels M and H
        /// only.
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
        public SymbolSize(int version, int rows, int columns, int dataModules, int align2, int align3, int errorM, int errorH, int blocksM, int blocksH)
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
        /// Gets the version, 1 to 40, 1 to 4 for M1 to M4, or the <see cref="RectangularMicroQrCodeVersion"/> value.
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
        /// Gets the second alignment pattern coordinate of Table E.1, or the column of the first interior alignment pattern of a Rectangular Micro QR Code
        /// size, or 0 for none.
        /// </summary>
        public int Align2 { get; }

        /// <summary>
        /// Gets the third alignment pattern coordinate of Table E.1, or the column of the second interior alignment pattern of a Rectangular Micro QR Code
        /// size, or 0 when there is none.
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
        public int DataBits(QrErrorCorrectionLevel level) => ((this.TotalCodewords - this.ErrorCodewords(level)) * 8) - (this.ShortLastCodeword ? 4 : 0);
    }

    /// <summary>
    /// One mode segment of the message: its mode and the number of characters it holds.
    /// </summary>
    private struct Segment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Segment"/> struct.
        /// </summary>
        /// <param name="mode">The mode.</param>
        /// <param name="count">The number of characters.</param>
        public Segment(SegmentMode mode, int count)
        {
            this.Mode = mode;
            this.Count = count;
        }

        /// <summary>
        /// Gets the mode.
        /// </summary>
        public SegmentMode Mode { get; }

        /// <summary>
        /// Gets or sets the number of characters. Each character the segmentation keeps in the mode in force adds one.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// The per-position runs of a message, from which <see cref="BuildSegments(int, Span{Segment})"/> divides it into mode segments. A character is a digit, a
    /// character only Alphanumeric mode holds besides the digits (Table 5, and the FNC1 character, which Alphanumeric mode writes as %), or a character only
    /// Byte mode holds. Each row has one entry past the message, which stops the runs.
    /// </summary>
    private readonly ref struct CharacterRuns
    {
        /// <summary>
        /// The digits that start at each position.
        /// </summary>
        private readonly ReadOnlySpan<int> numeric;

        /// <summary>
        /// The characters only Alphanumeric mode holds that start at each position.
        /// </summary>
        private readonly ReadOnlySpan<int> alphanumeric;

        /// <summary>
        /// The characters Numeric or Alphanumeric mode holds that start at each position.
        /// </summary>
        private readonly ReadOnlySpan<int> either;

        /// <summary>
        /// The characters only Byte mode holds that start at each position.
        /// </summary>
        private readonly ReadOnlySpan<int> bytes;

        /// <summary>
        /// The distance from each position to the next digit.
        /// </summary>
        private readonly ReadOnlySpan<int> nextNumeric;

        /// <summary>
        /// The distance from each position to the next character only Alphanumeric mode holds.
        /// </summary>
        private readonly ReadOnlySpan<int> nextAlphanumeric;

        /// <summary>
        /// The distance from each position to the next character only Byte mode holds.
        /// </summary>
        private readonly ReadOnlySpan<int> nextByte;

        /// <summary>
        /// The length of the message.
        /// </summary>
        private readonly int length;

        /// <summary>
        /// Initializes a new instance of the <see cref="CharacterRuns"/> struct and writes the runs of the message.
        /// </summary>
        /// <param name="message">The message values.</param>
        /// <param name="buffer">The buffer of the <see cref="RunRows"/> rows, each one longer than the message.</param>
        public CharacterRuns(ReadOnlySpan<int> message, Span<int> buffer)
        {
            int length = message.Length;
            int stride = length + 1;
            Span<int> numeric = buffer.Slice(0, stride);
            Span<int> alphanumeric = buffer.Slice(stride, stride);
            Span<int> either = buffer.Slice(2 * stride, stride);
            Span<int> bytes = buffer.Slice(3 * stride, stride);
            Span<int> nextNumeric = buffer.Slice(4 * stride, stride);
            Span<int> nextAlphanumeric = buffer.Slice(5 * stride, stride);
            Span<int> nextByte = buffer.Slice(6 * stride, stride);

            // Past the message no run continues and no character of any class follows.
            numeric[length] = 0;
            alphanumeric[length] = 0;
            either[length] = 0;
            bytes[length] = 0;
            nextNumeric[length] = NoneAhead;
            nextAlphanumeric[length] = NoneAhead;
            nextByte[length] = NoneAhead;

            // One pass from the end: each run and each distance extends the one at the next position.
            for (int i = length - 1; i >= 0; i--)
            {
                int value = message[i];
                bool digit = value is >= '0' and <= '9';
                bool alpha = value is Fnc1 or DataPercent || AlphanumericValue(value) >= 10;

                numeric[i] = digit ? numeric[i + 1] + 1 : 0;
                nextNumeric[i] = digit ? 0 : nextNumeric[i + 1] + 1;
                alphanumeric[i] = alpha ? alphanumeric[i + 1] + 1 : 0;
                nextAlphanumeric[i] = alpha ? 0 : nextAlphanumeric[i + 1] + 1;
                either[i] = digit || alpha ? either[i + 1] + 1 : 0;
                bytes[i] = digit || alpha ? 0 : bytes[i + 1] + 1;
                nextByte[i] = digit || alpha ? nextByte[i + 1] + 1 : 0;
            }

            this.numeric = numeric;
            this.alphanumeric = alphanumeric;
            this.either = either;
            this.bytes = bytes;
            this.nextNumeric = nextNumeric;
            this.nextAlphanumeric = nextAlphanumeric;
            this.nextByte = nextByte;
            this.length = length;
        }

        /// <summary>
        /// Divides the message into mode segments for a version group. At each position a run of digits or Alphanumeric characters that is long enough to pay
        /// for its indicators starts a segment of its own; a shorter run stays in the current mode.
        /// </summary>
        /// <param name="group">The version group.</param>
        /// <param name="segments">The buffer that receives the segments, at least as long as the message.</param>
        /// <returns>The number of segments, or -1 when the version group cannot hold a character of the message.</returns>
        public int BuildSegments(int group, Span<Segment> segments)
        {
            int count = 0;
            SegmentMode mode = SegmentMode.Numeric;
            int i = 0;
            while (i < this.length)
            {
                int numericCount = this.numeric[i];
                int alphanumericCount = this.alphanumeric[i];
                int eitherCount = this.either[i];
                int byteCount = this.bytes[i];

                // Sections 6.3.3 and 6.3.4: M1 has only Numeric mode and M2 has no Byte mode.
                if ((group == GroupM1 && (alphanumericCount > 0 || byteCount > 0)) || (group == GroupM2 && byteCount > 0))
                {
                    return -1;
                }

                SegmentMode next;
                if (count == 0)
                {
                    // The first segment, before any mode is in force.
                    if (Before(i, numericCount, FirstNumericBeforeByte[group], this.nextByte))
                    {
                        next = SegmentMode.Numeric;
                    }
                    else if (Before(i, numericCount, FirstByteAfterNumeric[group], this.nextByte))
                    {
                        next = SegmentMode.Byte;
                    }
                    else if (Before(i, numericCount, AlphanumericToNumericToEnd[group], this.nextAlphanumeric))
                    {
                        next = SegmentMode.Numeric;
                    }
                    else if (this.Ends(i, numericCount, FirstNumericToEnd[group]))
                    {
                        next = SegmentMode.Numeric;
                    }
                    else if (Before(i, eitherCount, ByteToAlphanumericToEnd[group], this.nextByte))
                    {
                        next = SegmentMode.Alphanumeric;
                    }
                    else if (this.Ends(i, eitherCount, FirstAlphanumericToEnd[group]))
                    {
                        next = SegmentMode.Alphanumeric;
                    }
                    else
                    {
                        next = SegmentMode.Byte;
                    }
                }
                else if (mode == SegmentMode.Byte)
                {
                    if (Before(i, alphanumericCount, ByteToAlphanumericBeforeByte[group], this.nextByte)
                        || Before(i, alphanumericCount, ByteToAlphanumericBeforeNumeric[group], this.nextNumeric)
                        || this.Ends(i, alphanumericCount, ByteToAlphanumericToEnd[group]))
                    {
                        next = SegmentMode.Alphanumeric;
                    }
                    else if (Before(i, numericCount, ByteToNumericBeforeByte[group], this.nextByte)
                        || Before(i, numericCount, ByteToNumericBeforeAlphanumeric[group], this.nextAlphanumeric)
                        || this.Ends(i, numericCount, ByteToNumericToEnd[group]))
                    {
                        next = SegmentMode.Numeric;
                    }
                    else if (this.Ends(i, eitherCount, ByteToAlphanumericToEnd[group])
                        && eitherCount <= ByteToAlphanumericBeforeNumeric[group]
                        && this.NextDigitsShort(i, ByteToNumericBeforeAlphanumeric[group]))
                    {
                        // A short run of Alphanumeric and digit characters that ends the data leaves Byte mode for Alphanumeric mode, unless a run of digits
                        // within it is long enough for Numeric mode.
                        next = SegmentMode.Alphanumeric;
                    }
                    else
                    {
                        next = SegmentMode.Byte;
                    }
                }
                else if (mode == SegmentMode.Alphanumeric)
                {
                    if (byteCount > 0)
                    {
                        next = SegmentMode.Byte;
                    }
                    else if (Before(i, numericCount, AlphanumericToNumericBeforeAlphanumeric[group], this.nextAlphanumeric)
                        || Before(i, numericCount, AlphanumericToNumericBeforeByte[group], this.nextByte)
                        || this.Ends(i, numericCount, AlphanumericToNumericToEnd[group]))
                    {
                        next = SegmentMode.Numeric;
                    }
                    else
                    {
                        next = SegmentMode.Alphanumeric;
                    }
                }
                else
                {
                    next = byteCount > 0 ? SegmentMode.Byte : alphanumericCount > 0 ? SegmentMode.Alphanumeric : SegmentMode.Numeric;
                }

                if (count > 0 && next == mode)
                {
                    // The character joins the segment in force.
                    segments[count - 1].Count++;
                    i++;
                }
                else
                {
                    // A new segment takes the run of its own class that starts here.
                    mode = next;
                    int take = mode switch
                    {
                        SegmentMode.Numeric => numericCount,
                        SegmentMode.Alphanumeric => alphanumericCount,
                        _ => byteCount,
                    };

                    segments[count++] = new Segment(mode, take);
                    i += take;
                }
            }

            return count;
        }

        /// <summary>
        /// Returns whether a run starting at a position is at least a given length and a character of a class follows it.
        /// </summary>
        /// <param name="position">The position the run starts at.</param>
        /// <param name="run">The length of the run.</param>
        /// <param name="threshold">The length the run must reach.</param>
        /// <param name="following">The distances to the next character of the class that must follow.</param>
        /// <returns><see langword="true"/> when the run is long enough and the class follows it.</returns>
        private static bool Before(int position, int run, int threshold, ReadOnlySpan<int> following) => run >= threshold && following[position + run] == 0;

        /// <summary>
        /// Returns whether a run starting at a position is at least a given length and ends the message.
        /// </summary>
        /// <param name="position">The position the run starts at.</param>
        /// <param name="run">The length of the run.</param>
        /// <param name="threshold">The length the run must reach.</param>
        /// <returns><see langword="true"/> when the run is long enough and ends the message.</returns>
        private bool Ends(int position, int run, int threshold) => run >= threshold && position + run == this.length;

        /// <summary>
        /// Returns whether the next run of digits from a position is shorter than a length, or no digit follows.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="threshold">The length.</param>
        /// <returns><see langword="true"/> when the next run of digits is shorter or absent.</returns>
        private bool NextDigitsShort(int position, int threshold)
        {
            int distance = this.nextNumeric[position];
            return distance >= this.length || this.numeric[position + distance] < threshold;
        }
    }

    /// <summary>
    /// The module states of a symbol, row-major, with the writers of the finder and alignment patterns.
    /// </summary>
    private readonly ref struct ModuleGrid
    {
        /// <summary>
        /// The module states, row-major.
        /// </summary>
        private readonly Span<ModuleState> states;

        /// <summary>
        /// The number of module rows.
        /// </summary>
        private readonly int rows;

        /// <summary>
        /// The number of module columns.
        /// </summary>
        private readonly int columns;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModuleGrid"/> struct.
        /// </summary>
        /// <param name="states">The module states, row-major, <paramref name="rows"/> x <paramref name="columns"/> long.</param>
        /// <param name="rows">The number of module rows.</param>
        /// <param name="columns">The number of module columns.</param>
        public ModuleGrid(Span<ModuleState> states, int rows, int columns)
        {
            this.states = states;
            this.rows = rows;
            this.columns = columns;
        }

        /// <summary>
        /// Gets the state of a module.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        /// <returns>A reference to the state.</returns>
        public ref ModuleState this[int row, int column] => ref this.states[(row * this.columns) + column];

        /// <summary>
        /// Writes a finder pattern and its separator, the 8 x 8 modules from a corner, without the rows the symbol does not have.
        /// </summary>
        /// <param name="cornerRow">The row of the corner.</param>
        /// <param name="cornerColumn">The column of the corner.</param>
        /// <param name="rowStep">The direction from the corner along a column, 1 or -1.</param>
        /// <param name="columnStep">The direction from the corner along a row, 1 or -1.</param>
        public void PlaceFinder(int cornerRow, int cornerColumn, int rowStep, int columnStep)
        {
            // A 7-row Rectangular Micro QR Code symbol has no row for the separator below the finder pattern.
            int rowCount = Math.Min(8, this.rows);
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    // Row and column 7 are the separator. The outer ring and the 3 x 3 centre are dark.
                    bool dark = r < 7 && c < 7 && (r == 0 || r == 6 || c == 0 || c == 6 || (r is >= 2 and <= 4 && c is >= 2 and <= 4));
                    this[cornerRow + (r * rowStep), cornerColumn + (c * columnStep)] = dark ? ModuleState.Dark : ModuleState.Light;
                }
            }
        }

        /// <summary>
        /// Writes an alignment pattern, section 5.3.5: dark 5 x 5, light 3 x 3 and a dark centre module.
        /// </summary>
        /// <param name="row">The row of the centre module.</param>
        /// <param name="column">The column of the centre module.</param>
        public void PlaceAlignment(int row, int column)
        {
            for (int r = -2; r <= 2; r++)
            {
                for (int c = -2; c <= 2; c++)
                {
                    bool dark = r is -2 or 2 || c is -2 or 2 || (r == 0 && c == 0);
                    this[row + r, column + c] = dark ? ModuleState.Dark : ModuleState.Light;
                }
            }
        }
    }

    /// <summary>
    /// Scores the data mask patterns of a QR Code symbol, Table 11, over the mask layers of its modules.
    /// </summary>
    private readonly ref struct MaskPenalty
    {
        /// <summary>
        /// The mask layers of the modules, row-major. Bit m of a layer is the module under data mask pattern m.
        /// </summary>
        private readonly ReadOnlySpan<byte> layers;

        /// <summary>
        /// The number of modules on a side.
        /// </summary>
        private readonly int n;

        /// <summary>
        /// The run lengths of the line being scored, at least n + 1 long.
        /// </summary>
        private readonly Span<int> runs;

        /// <summary>
        /// Initializes a new instance of the <see cref="MaskPenalty"/> struct.
        /// </summary>
        /// <param name="layers">The mask layers of the modules.</param>
        /// <param name="n">The number of modules on a side.</param>
        /// <param name="runs">A buffer of at least n + 1 run lengths.</param>
        public MaskPenalty(ReadOnlySpan<byte> layers, int n, Span<int> runs)
        {
            this.layers = layers;
            this.n = n;
            this.runs = runs;
        }

        /// <summary>
        /// Returns the penalty of a data mask pattern over the complete symbol.
        /// </summary>
        /// <param name="mask">The data mask pattern reference.</param>
        /// <returns>The penalty.</returns>
        public int Score(int mask)
        {
            ReadOnlySpan<byte> layers = this.layers;
            int n = this.n;
            int lines = 0;
            for (int i = 0; i < n; i++)
            {
                lines += LinePenalty(this.runs[..this.RunLengths(i, n, mask)]);
                lines += LinePenalty(this.runs[..this.RunLengths(i * n, 1, mask)]);
            }

            // N2: 3 points for each 2 x 2 block of one colour, so a larger block counts each block it holds. A block is one colour under the mask where the
            // mask bit of the exclusive or of its upper left layer with each of its other three layers is clear.
            int blocks = 0;
            for (int r = 0; r < n - 1; r++)
            {
                ReadOnlySpan<byte> row = layers.Slice(r * n, n);
                ReadOnlySpan<byte> below = layers.Slice((r + 1) * n, n);
                for (int c = 0; c < n - 1; c++)
                {
                    int corner = row[c];
                    int differs = (corner ^ row[c + 1]) | (corner ^ below[c]) | (corner ^ below[c + 1]);
                    if (((differs >> mask) & 1) == 0)
                    {
                        blocks += 3;
                    }
                }
            }

            // N4: 10 points for each full 5% the proportion of dark modules is away from 50%. The steps are |100 dark / n^2 - 50| / 5, which integers give
            // exactly as |20 dark - 10 n^2| / n^2.
            int dark = 0;
            foreach (byte layer in layers)
            {
                dark += (layer >> mask) & 1;
            }

            int area = n * n;
            int k = Math.Abs((20 * dark) - (10 * area)) / area;
            return lines + blocks + (10 * k);
        }

        /// <summary>
        /// Writes the run lengths of one row or column into the run buffer, light and dark in turn from a light run, which is 0 when the line starts dark.
        /// </summary>
        /// <param name="start">The index of the first module.</param>
        /// <param name="step">The distance between two modules of the line.</param>
        /// <param name="mask">The data mask pattern reference.</param>
        /// <returns>The number of runs.</returns>
        private int RunLengths(int start, int step, int mask)
        {
            Span<int> runs = this.runs;
            int count = 0;
            int color = 0;
            runs[0] = 0;
            for (int i = 0; i < this.n; i++)
            {
                int value = (this.layers[start + (i * step)] >> mask) & 1;
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
    }
}
