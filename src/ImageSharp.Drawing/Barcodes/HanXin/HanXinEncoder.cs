// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes Han Xin Code symbols: versions 1 to 84, squares of 23 to 189 modules with finder patterns at the four corners and a grid of alignment patterns from
/// version 4. The data is Numeric and Byte mode segments, its codewords take Reed-Solomon error correction over GF(256) in up to three groups of blocks at one
/// of four levels, and one of four masks makes the modules evenly dark. The function information beside the finder patterns carries the version, the level and
/// the mask with error correction over GF(16).
/// </summary>
internal static class HanXinEncoder
{
    /// <summary>
    /// The longest input: version 84 at level L1 holds 3264 data codewords, 26112 bits, and Numeric mode, the densest, takes 10 bits for three digits, so no
    /// more than 7827 characters fit.
    /// </summary>
    public const int MaximumLength = 7827;

    /// <summary>
    /// The quiet zone on each of the four sides in modules.
    /// </summary>
    private const int QuietZone = 3;

    /// <summary>
    /// The number of versions.
    /// </summary>
    private const int VersionCount = 84;

    /// <summary>
    /// The data codewords of version 84 at level L1, the most any symbol holds.
    /// </summary>
    private const int MaximumDataCodewords = 3264;

    /// <summary>
    /// The codewords of version 84, the most any symbol holds, data and error correction: its 31091 data modules, the last entry of <see cref="DataModules"/>,
    /// as whole codewords.
    /// </summary>
    private const int MaximumCodewords = 31091 / 8;

    /// <summary>
    /// The side of version 84 in modules, the largest symbol.
    /// </summary>
    private const int MaximumSide = 21 + (2 * VersionCount);

    /// <summary>
    /// The shortest digit run that starts a Numeric segment, or <see cref="ShortestFinalNumericRun"/> when the run ends the data.
    /// </summary>
    private const int ShortestNumericRun = 8;

    /// <summary>
    /// The shortest digit run that starts a Numeric segment when the run ends the data.
    /// </summary>
    private const int ShortestFinalNumericRun = 5;

    /// <summary>
    /// The number of bits of the byte count of a Byte segment. A segment is at most <see cref="MaximumLength"/> bytes, which the 13 bits hold.
    /// </summary>
    private const int ByteCountBits = 13;

    /// <summary>
    /// The terminator of a Numeric segment whose last group holds one digit. The terminators for two and three digits follow it.
    /// </summary>
    private const int NumericTerminator = 0b1111111101;

    /// <summary>
    /// The number of rows and columns of a finder pattern, its separator included.
    /// </summary>
    private const int FinderSize = 8;

    /// <summary>
    /// The number of bits of the function information: three 4-bit data words, four 4-bit check words and six fixed bits.
    /// </summary>
    private const int FunctionBits = 34;

    /// <summary>
    /// The number of masks.
    /// </summary>
    private const int MaskCount = 4;

    /// <summary>
    /// The field of the data error correction: GF(256) with the polynomial x^8 + x^6 + x^5 + x + 1, 355 decimal.
    /// </summary>
    private static readonly GaloisField Field = new(256, 355);

    /// <summary>
    /// The field of the function information: GF(16) with the polynomial x^4 + x + 1, 19 decimal.
    /// </summary>
    private static readonly GaloisField FunctionField = new(16, 19);

    /// <summary>
    /// The modes of the data segments, each value the 4-bit mode indicator that starts its segment.
    /// </summary>
    private enum SegmentMode
    {
        /// <summary>
        /// Numeric mode: three digits in 10 bits.
        /// </summary>
        Numeric = 0b0001,

        /// <summary>
        /// Byte mode: a 13-bit byte count, then 8 bits for each byte.
        /// </summary>
        Byte = 0b0011,
    }

    /// <summary>
    /// Gets the alignment pattern spacing of each version, two bytes a version: the regular distance k between the alignment lines and the number n of regular
    /// intervals across the side, both 0 for versions 1 to 3, which have none.
    /// </summary>
    private static ReadOnlySpan<byte> Alignment =>
    [
        0, 0, 0, 0, 0, 0, 14, 1, 16, 1, 16, 1, 17, 1, 18, 1,
        19, 1, 20, 1, 14, 2, 15, 2, 16, 2, 16, 2, 17, 2, 17, 2,
        18, 2, 19, 2, 20, 2, 20, 2, 21, 2, 16, 3, 17, 3, 17, 3,
        18, 3, 18, 3, 19, 3, 19, 3, 20, 3, 20, 3, 21, 3, 17, 4,
        17, 4, 18, 4, 18, 4, 19, 4, 19, 4, 19, 4, 20, 4, 20, 4,
        17, 5, 17, 5, 18, 5, 18, 5, 18, 5, 19, 5, 19, 5, 19, 5,
        17, 6, 17, 6, 18, 6, 18, 6, 18, 6, 18, 6, 19, 6, 19, 6,
        19, 6, 17, 7, 17, 7, 18, 7, 18, 7, 18, 7, 18, 7, 19, 7,
        19, 7, 17, 8, 17, 8, 17, 8, 18, 8, 18, 8, 18, 8, 18, 8,
        19, 8, 19, 8, 17, 9, 17, 9, 17, 9, 18, 9, 18, 9, 18, 9,
        18, 9, 18, 9, 17, 10, 17, 10,
    ];

    /// <summary>
    /// Gets the number of modules that carry codewords in each version, the side squared less the finder patterns, the alignment patterns and the function
    /// information.
    /// </summary>
    private static ReadOnlySpan<ushort> DataModules =>
    [
        205, 301, 405, 439, 555, 675, 805, 943, 1089, 1243, 1289, 1455,
        1629, 1805, 1995, 2187, 2393, 2607, 2829, 3053, 3291, 3383, 3631, 3887,
        4151, 4423, 4703, 4991, 5287, 5591, 5903, 6033, 6353, 6689, 7025, 7377,
        7729, 8089, 8465, 8841, 9009, 9401, 9799, 10207, 10623, 11045, 11477, 11917,
        12111, 12559, 13025, 13489, 13961, 14441, 14939, 15435, 15939, 16171, 16691, 17215,
        17751, 18295, 18847, 19403, 19971, 20229, 20805, 21389, 21993, 22593, 23201, 23817,
        24453, 25085, 25373, 26021, 26677, 27335, 28007, 28687, 29375, 30071, 30387, 31091,
    ];

    /// <summary>
    /// Gets the error correction blocks of each version and level, nine bytes a version and level from version 1 at level L1: three groups, each the number of
    /// blocks, the data codewords of a block and the check codewords of a block. A group of no blocks is three zeros.
    /// </summary>
    private static ReadOnlySpan<byte> Blocks =>
    [
        1, 21, 4, 0, 0, 0, 0, 0, 0, 1, 17, 8, 0, 0, 0, 0, 0, 0, 1, 13, 12, 0, 0, 0, 0, 0, 0, 1, 9, 16, 0, 0, 0, 0, 0, 0,
        1, 31, 6, 0, 0, 0, 0, 0, 0, 1, 25, 12, 0, 0, 0, 0, 0, 0, 1, 19, 18, 0, 0, 0, 0, 0, 0, 1, 15, 22, 0, 0, 0, 0, 0, 0,
        1, 42, 8, 0, 0, 0, 0, 0, 0, 1, 34, 16, 0, 0, 0, 0, 0, 0, 1, 26, 24, 0, 0, 0, 0, 0, 0, 1, 20, 30, 0, 0, 0, 0, 0, 0,
        1, 46, 8, 0, 0, 0, 0, 0, 0, 1, 38, 16, 0, 0, 0, 0, 0, 0, 1, 30, 24, 0, 0, 0, 0, 0, 0, 1, 22, 32, 0, 0, 0, 0, 0, 0,
        1, 57, 12, 0, 0, 0, 0, 0, 0, 1, 49, 20, 0, 0, 0, 0, 0, 0, 1, 37, 32, 0, 0, 0, 0, 0, 0, 1, 14, 20, 1, 13, 22, 0, 0, 0,
        1, 70, 14, 0, 0, 0, 0, 0, 0, 1, 58, 26, 0, 0, 0, 0, 0, 0, 1, 24, 20, 1, 22, 18, 0, 0, 0, 1, 16, 24, 1, 18, 26, 0, 0, 0,
        1, 84, 16, 0, 0, 0, 0, 0, 0, 1, 70, 30, 0, 0, 0, 0, 0, 0, 1, 26, 22, 1, 28, 24, 0, 0, 0, 2, 14, 20, 1, 12, 20, 0, 0, 0,
        1, 99, 18, 0, 0, 0, 0, 0, 0, 1, 40, 18, 1, 41, 18, 0, 0, 0, 1, 31, 26, 1, 32, 28, 0, 0, 0, 2, 16, 24, 1, 15, 22, 0, 0, 0,
        1, 114, 22, 0, 0, 0, 0, 0, 0, 2, 48, 20, 0, 0, 0, 0, 0, 0, 2, 24, 20, 1, 26, 22, 0, 0, 0, 2, 18, 28, 1, 18, 26, 0, 0, 0,
        1, 131, 24, 0, 0, 0, 0, 0, 0, 1, 52, 22, 1, 57, 24, 0, 0, 0, 2, 27, 24, 1, 29, 24, 0, 0, 0, 2, 21, 32, 1, 19, 30, 0, 0, 0,
        1, 135, 26, 0, 0, 0, 0, 0, 0, 1, 56, 24, 1, 57, 24, 0, 0, 0, 2, 28, 24, 1, 31, 26, 0, 0, 0, 2, 22, 32, 1, 21, 32, 0, 0, 0,
        1, 153, 28, 0, 0, 0, 0, 0, 0, 1, 62, 26, 1, 65, 28, 0, 0, 0, 2, 32, 28, 1, 33, 28, 0, 0, 0, 3, 17, 26, 1, 22, 30, 0, 0, 0,
        1, 86, 16, 1, 85, 16, 0, 0, 0, 1, 71, 30, 1, 72, 30, 0, 0, 0, 2, 37, 32, 1, 35, 30, 0, 0, 0, 3, 20, 30, 1, 21, 32, 0, 0, 0,
        1, 94, 18, 1, 95, 18, 0, 0, 0, 2, 51, 22, 1, 55, 24, 0, 0, 0, 3, 30, 26, 1, 31, 26, 0, 0, 0, 4, 18, 28, 1, 17, 24, 0, 0, 0,
        1, 104, 20, 1, 105, 20, 0, 0, 0, 2, 57, 24, 1, 61, 26, 0, 0, 0, 3, 33, 28, 1, 36, 30, 0, 0, 0, 4, 20, 30, 1, 19, 30, 0, 0, 0,
        1, 115, 22, 1, 114, 22, 0, 0, 0, 2, 65, 28, 1, 61, 26, 0, 0, 0, 3, 38, 32, 1, 33, 30, 0, 0, 0, 5, 19, 28, 1, 14, 24, 0, 0, 0,
        1, 126, 24, 1, 125, 24, 0, 0, 0, 2, 70, 30, 1, 69, 30, 0, 0, 0, 4, 33, 28, 1, 29, 26, 0, 0, 0, 5, 20, 30, 1, 19, 30, 0, 0, 0,
        1, 136, 26, 1, 137, 26, 0, 0, 0, 3, 56, 24, 1, 59, 26, 0, 0, 0, 5, 35, 30, 0, 0, 0, 0, 0, 0, 6, 18, 28, 1, 21, 28, 0, 0, 0,
        1, 148, 28, 1, 149, 28, 0, 0, 0, 3, 61, 26, 1, 64, 28, 0, 0, 0, 7, 24, 20, 1, 23, 22, 0, 0, 0, 6, 20, 30, 1, 21, 32, 0, 0, 0,
        3, 107, 20, 0, 0, 0, 0, 0, 0, 3, 65, 28, 1, 72, 30, 0, 0, 0, 7, 26, 22, 1, 23, 22, 0, 0, 0, 7, 19, 28, 1, 20, 32, 0, 0, 0,
        3, 115, 22, 0, 0, 0, 0, 0, 0, 4, 56, 24, 1, 63, 28, 0, 0, 0, 7, 28, 24, 1, 25, 22, 0, 0, 0, 8, 18, 28, 1, 21, 22, 0, 0, 0,
        2, 116, 22, 1, 122, 24, 0, 0, 0, 4, 56, 24, 1, 72, 30, 0, 0, 0, 7, 28, 24, 1, 32, 26, 0, 0, 0, 8, 18, 28, 1, 24, 30, 0, 0, 0,
        3, 127, 24, 0, 0, 0, 0, 0, 0, 5, 51, 22, 1, 62, 26, 0, 0, 0, 7, 30, 26, 1, 35, 26, 0, 0, 0, 8, 20, 30, 1, 21, 32, 0, 0, 0,
        2, 135, 26, 1, 137, 26, 0, 0, 0, 5, 56, 24, 1, 59, 26, 0, 0, 0, 7, 33, 28, 1, 30, 28, 0, 0, 0, 11, 16, 24, 1, 19, 26, 0, 0, 0,
        3, 105, 20, 1, 121, 22, 0, 0, 0, 5, 61, 26, 1, 57, 26, 0, 0, 0, 9, 28, 24, 1, 28, 22, 0, 0, 0, 10, 19, 28, 1, 18, 30, 0, 0, 0,
        2, 157, 30, 1, 150, 28, 0, 0, 0, 5, 65, 28, 1, 61, 26, 0, 0, 0, 8, 33, 28, 1, 34, 30, 0, 0, 0, 10, 19, 28, 2, 15, 26, 0, 0, 0,
        3, 126, 24, 1, 115, 22, 0, 0, 0, 7, 51, 22, 1, 54, 22, 0, 0, 0, 8, 35, 30, 1, 37, 30, 0, 0, 0, 15, 15, 22, 1, 10, 22, 0, 0, 0,
        4, 105, 20, 1, 103, 20, 0, 0, 0, 7, 56, 24, 1, 45, 18, 0, 0, 0, 10, 31, 26, 1, 27, 26, 0, 0, 0, 10, 17, 26, 3, 20, 28, 1, 21, 28,
        3, 139, 26, 1, 137, 28, 0, 0, 0, 6, 66, 28, 1, 66, 30, 0, 0, 0, 9, 36, 30, 1, 34, 32, 0, 0, 0, 13, 19, 28, 1, 17, 32, 0, 0, 0,
        6, 84, 16, 1, 82, 16, 0, 0, 0, 6, 70, 30, 1, 68, 30, 0, 0, 0, 7, 35, 30, 3, 33, 28, 1, 32, 28, 13, 20, 30, 1, 20, 28, 0, 0, 0,
        5, 105, 20, 1, 94, 18, 0, 0, 0, 6, 74, 32, 1, 71, 30, 0, 0, 0, 11, 33, 28, 1, 34, 32, 0, 0, 0, 13, 19, 28, 3, 16, 26, 0, 0, 0,
        4, 127, 24, 1, 126, 24, 0, 0, 0, 7, 66, 28, 1, 66, 30, 0, 0, 0, 12, 30, 24, 1, 24, 28, 1, 24, 30, 15, 19, 28, 1, 17, 32, 0, 0, 0,
        7, 84, 16, 1, 78, 16, 0, 0, 0, 7, 70, 30, 1, 66, 28, 0, 0, 0, 12, 33, 28, 1, 32, 30, 0, 0, 0, 14, 21, 32, 1, 24, 28, 0, 0, 0,
        5, 117, 22, 1, 117, 24, 0, 0, 0, 8, 66, 28, 1, 58, 26, 0, 0, 0, 11, 38, 32, 1, 34, 32, 0, 0, 0, 15, 20, 30, 2, 17, 26, 0, 0, 0,
        4, 148, 28, 1, 146, 28, 0, 0, 0, 8, 68, 30, 1, 70, 24, 0, 0, 0, 10, 36, 32, 3, 38, 28, 0, 0, 0, 16, 19, 28, 3, 16, 26, 0, 0, 0,
        4, 126, 24, 2, 135, 26, 0, 0, 0, 8, 70, 28, 2, 43, 26, 0, 0, 0, 13, 32, 28, 2, 41, 30, 0, 0, 0, 17, 19, 28, 3, 15, 26, 0, 0, 0,
        5, 136, 26, 1, 132, 24, 0, 0, 0, 5, 67, 30, 4, 68, 28, 1, 69, 28, 14, 35, 30, 1, 32, 24, 0, 0, 0, 18, 18, 26, 3, 16, 28, 1, 14, 28,
        3, 142, 26, 3, 141, 28, 0, 0, 0, 8, 70, 30, 1, 73, 32, 1, 74, 32, 12, 34, 30, 3, 34, 26, 1, 35, 28, 18, 21, 32, 1, 27, 30, 0, 0, 0,
        5, 116, 22, 2, 103, 20, 1, 102, 20, 9, 74, 32, 1, 74, 30, 0, 0, 0, 14, 34, 28, 2, 32, 32, 1, 32, 30, 19, 21, 32, 1, 25, 26, 0, 0, 0,
        7, 116, 22, 1, 117, 22, 0, 0, 0, 11, 65, 28, 1, 58, 24, 0, 0, 0, 15, 38, 32, 1, 27, 28, 0, 0, 0, 20, 20, 30, 1, 20, 32, 1, 21, 32,
        6, 136, 26, 1, 130, 24, 0, 0, 0, 11, 66, 28, 1, 62, 30, 0, 0, 0, 14, 34, 28, 3, 34, 32, 1, 30, 30, 18, 20, 30, 3, 20, 28, 2, 15, 26,
        5, 105, 20, 2, 115, 22, 2, 116, 22, 10, 75, 32, 1, 73, 32, 0, 0, 0, 16, 38, 32, 1, 27, 28, 0, 0, 0, 22, 19, 28, 2, 16, 30, 1, 19, 30,
        6, 147, 28, 1, 146, 28, 0, 0, 0, 11, 66, 28, 2, 65, 30, 0, 0, 0, 18, 33, 28, 2, 33, 30, 0, 0, 0, 22, 21, 32, 1, 28, 30, 0, 0, 0,
        6, 116, 22, 3, 125, 24, 0, 0, 0, 11, 75, 32, 1, 68, 30, 0, 0, 0, 13, 35, 28, 6, 34, 32, 1, 30, 30, 23, 21, 32, 1, 26, 30, 0, 0, 0,
        7, 105, 20, 4, 95, 18, 0, 0, 0, 12, 67, 28, 1, 63, 30, 1, 62, 32, 21, 31, 26, 2, 33, 32, 0, 0, 0, 23, 21, 32, 2, 24, 30, 0, 0, 0,
        10, 116, 22, 0, 0, 0, 0, 0, 0, 12, 74, 32, 1, 78, 30, 0, 0, 0, 18, 37, 32, 1, 39, 30, 1, 41, 28, 25, 21, 32, 1, 27, 28, 0, 0, 0,
        5, 126, 24, 4, 115, 22, 1, 114, 22, 12, 67, 28, 2, 66, 32, 1, 68, 30, 21, 35, 30, 1, 39, 30, 0, 0, 0, 26, 21, 32, 1, 28, 28, 0, 0, 0,
        9, 126, 24, 1, 117, 22, 0, 0, 0, 13, 75, 32, 1, 68, 30, 0, 0, 0, 20, 35, 30, 3, 35, 28, 0, 0, 0, 27, 21, 32, 1, 28, 30, 0, 0, 0,
        9, 126, 24, 1, 137, 26, 0, 0, 0, 13, 71, 30, 2, 68, 32, 0, 0, 0, 20, 37, 32, 1, 39, 28, 1, 38, 28, 24, 20, 32, 5, 25, 28, 0, 0, 0,
        8, 147, 28, 1, 141, 28, 0, 0, 0, 10, 73, 32, 4, 74, 30, 1, 73, 30, 16, 36, 32, 6, 39, 30, 1, 37, 30, 27, 21, 32, 3, 20, 26, 0, 0, 0,
        9, 137, 26, 1, 135, 26, 0, 0, 0, 12, 70, 30, 4, 75, 32, 0, 0, 0, 24, 35, 30, 1, 40, 28, 0, 0, 0, 23, 20, 32, 8, 24, 30, 0, 0, 0,
        14, 95, 18, 1, 86, 18, 0, 0, 0, 13, 73, 32, 3, 77, 30, 0, 0, 0, 24, 35, 30, 2, 35, 28, 0, 0, 0, 26, 21, 32, 5, 21, 30, 1, 23, 30,
        9, 147, 28, 1, 142, 28, 0, 0, 0, 10, 73, 30, 6, 70, 32, 1, 71, 32, 25, 35, 30, 2, 34, 26, 0, 0, 0, 29, 21, 32, 4, 22, 30, 0, 0, 0,
        11, 126, 24, 1, 131, 24, 0, 0, 0, 16, 74, 32, 1, 79, 30, 0, 0, 0, 25, 38, 32, 1, 25, 30, 0, 0, 0, 33, 21, 32, 1, 28, 28, 0, 0, 0,
        14, 105, 20, 1, 99, 18, 0, 0, 0, 19, 65, 28, 1, 72, 28, 0, 0, 0, 24, 37, 32, 2, 40, 30, 1, 41, 30, 31, 21, 32, 4, 24, 32, 0, 0, 0,
        10, 147, 28, 1, 151, 28, 0, 0, 0, 15, 71, 30, 3, 71, 32, 1, 73, 32, 24, 37, 32, 3, 38, 30, 1, 39, 30, 36, 19, 30, 3, 29, 26, 0, 0, 0,
        15, 105, 20, 1, 99, 18, 0, 0, 0, 19, 70, 30, 1, 64, 28, 0, 0, 0, 27, 38, 32, 2, 25, 26, 0, 0, 0, 38, 20, 30, 2, 18, 28, 0, 0, 0,
        14, 105, 20, 1, 113, 22, 1, 114, 22, 17, 67, 30, 3, 92, 32, 0, 0, 0, 30, 35, 30, 1, 41, 30, 0, 0, 0, 36, 21, 32, 1, 26, 30, 1, 27, 30,
        11, 146, 28, 1, 146, 26, 0, 0, 0, 20, 70, 30, 1, 60, 26, 0, 0, 0, 29, 38, 32, 1, 24, 32, 0, 0, 0, 40, 20, 30, 2, 17, 26, 0, 0, 0,
        3, 137, 26, 1, 136, 26, 10, 126, 24, 22, 65, 28, 1, 75, 30, 0, 0, 0, 30, 37, 32, 1, 51, 30, 0, 0, 0, 42, 20, 30, 1, 21, 30, 0, 0, 0,
        12, 126, 24, 2, 118, 22, 1, 116, 22, 19, 74, 32, 1, 74, 30, 1, 72, 28, 30, 38, 32, 2, 29, 30, 0, 0, 0, 39, 20, 32, 2, 37, 26, 1, 38, 26,
        12, 126, 24, 3, 136, 26, 0, 0, 0, 21, 70, 30, 2, 65, 28, 0, 0, 0, 34, 35, 30, 1, 44, 32, 0, 0, 0, 42, 20, 30, 2, 19, 28, 2, 18, 28,
        12, 126, 24, 3, 117, 22, 1, 116, 22, 25, 61, 26, 2, 62, 28, 0, 0, 0, 34, 35, 30, 1, 40, 32, 1, 41, 32, 45, 20, 30, 1, 20, 32, 1, 21, 32,
        15, 105, 20, 2, 115, 22, 2, 116, 22, 25, 65, 28, 1, 72, 28, 0, 0, 0, 18, 35, 30, 17, 37, 32, 1, 50, 32, 42, 20, 30, 6, 19, 28, 1, 15, 28,
        19, 105, 20, 1, 101, 20, 0, 0, 0, 33, 51, 22, 1, 65, 22, 0, 0, 0, 40, 33, 28, 1, 28, 28, 0, 0, 0, 49, 20, 30, 1, 18, 28, 0, 0, 0,
        18, 105, 20, 2, 117, 22, 0, 0, 0, 26, 65, 28, 1, 80, 30, 0, 0, 0, 35, 35, 30, 3, 35, 28, 1, 36, 28, 52, 18, 28, 2, 38, 30, 0, 0, 0,
        26, 84, 16, 0, 0, 0, 0, 0, 0, 26, 70, 30, 0, 0, 0, 0, 0, 0, 45, 31, 26, 1, 9, 26, 0, 0, 0, 52, 20, 30, 0, 0, 0, 0, 0, 0,
        16, 126, 24, 1, 114, 22, 1, 115, 22, 23, 70, 30, 3, 65, 28, 1, 66, 28, 40, 35, 30, 1, 43, 30, 0, 0, 0, 46, 20, 30, 7, 19, 28, 1, 16, 28,
        19, 116, 22, 1, 105, 22, 0, 0, 0, 20, 70, 30, 7, 66, 28, 1, 63, 28, 40, 35, 30, 1, 42, 32, 1, 43, 32, 54, 20, 30, 1, 19, 30, 0, 0, 0,
        17, 126, 24, 2, 115, 22, 0, 0, 0, 24, 70, 30, 4, 74, 32, 0, 0, 0, 48, 31, 26, 2, 18, 26, 0, 0, 0, 54, 19, 28, 6, 15, 26, 1, 14, 26,
        29, 84, 16, 0, 0, 0, 0, 0, 0, 29, 70, 30, 0, 0, 0, 0, 0, 0, 6, 34, 30, 3, 36, 30, 38, 33, 28, 58, 20, 30, 0, 0, 0, 0, 0, 0,
        16, 147, 28, 1, 149, 28, 0, 0, 0, 31, 66, 28, 1, 37, 26, 0, 0, 0, 48, 33, 28, 1, 23, 26, 0, 0, 0, 53, 20, 30, 6, 19, 28, 1, 17, 28,
        20, 115, 22, 2, 134, 24, 0, 0, 0, 29, 66, 28, 2, 56, 26, 2, 57, 26, 45, 36, 30, 2, 15, 28, 0, 0, 0, 59, 20, 30, 2, 21, 32, 0, 0, 0,
        17, 147, 28, 1, 134, 26, 0, 0, 0, 26, 70, 30, 5, 75, 32, 0, 0, 0, 47, 35, 30, 1, 48, 32, 0, 0, 0, 64, 18, 28, 2, 33, 30, 1, 35, 30,
        22, 115, 22, 1, 133, 24, 0, 0, 0, 33, 65, 28, 1, 74, 28, 0, 0, 0, 43, 36, 30, 5, 27, 28, 1, 30, 28, 57, 20, 30, 5, 21, 32, 1, 24, 32,
        18, 136, 26, 2, 142, 26, 0, 0, 0, 33, 66, 28, 2, 49, 26, 0, 0, 0, 48, 35, 30, 2, 38, 28, 0, 0, 0, 64, 20, 30, 1, 20, 32, 0, 0, 0,
        19, 126, 24, 2, 135, 26, 1, 136, 26, 32, 66, 28, 2, 55, 26, 2, 56, 26, 49, 36, 30, 2, 18, 32, 0, 0, 0, 65, 18, 28, 5, 27, 30, 1, 29, 30,
        20, 137, 26, 1, 130, 26, 0, 0, 0, 30, 75, 32, 2, 71, 32, 0, 0, 0, 46, 35, 30, 6, 39, 32, 0, 0, 0, 3, 12, 30, 70, 19, 28, 0, 0, 0,
        20, 147, 28, 0, 0, 0, 0, 0, 0, 35, 70, 30, 0, 0, 0, 0, 0, 0, 49, 35, 30, 5, 35, 28, 0, 0, 0, 70, 20, 30, 0, 0, 0, 0, 0, 0,
        21, 136, 26, 1, 155, 28, 0, 0, 0, 34, 70, 30, 1, 64, 28, 1, 65, 28, 54, 35, 30, 1, 45, 30, 0, 0, 0, 68, 20, 30, 3, 18, 28, 1, 19, 28,
        19, 126, 24, 5, 115, 22, 1, 114, 22, 33, 70, 30, 3, 65, 28, 1, 64, 28, 52, 35, 30, 3, 41, 32, 1, 40, 32, 67, 20, 30, 5, 21, 32, 1, 24, 32,
        2, 150, 28, 21, 136, 26, 0, 0, 0, 32, 70, 30, 6, 65, 28, 0, 0, 0, 52, 38, 32, 2, 27, 32, 0, 0, 0, 73, 20, 30, 2, 22, 32, 0, 0, 0,
        21, 126, 24, 4, 136, 26, 0, 0, 0, 30, 74, 32, 6, 73, 30, 0, 0, 0, 54, 35, 30, 4, 40, 32, 0, 0, 0, 75, 20, 30, 1, 20, 28, 0, 0, 0,
        30, 105, 20, 1, 114, 22, 0, 0, 0, 3, 45, 22, 55, 47, 20, 0, 0, 0, 2, 26, 26, 62, 33, 28, 0, 0, 0, 79, 18, 28, 4, 33, 30, 0, 0, 0,
    ];

    /// <summary>
    /// Gets the finder pattern at the top left, top right and bottom right corners, row by row from the corner outward, its light separator included. The top
    /// right and bottom right patterns mirror it.
    /// </summary>
    private static ReadOnlySpan<byte> FinderPattern =>
    [
        1, 1, 1, 1, 1, 1, 1, 0,
        1, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 1, 1, 1, 1, 1, 0,
        1, 0, 1, 0, 0, 0, 0, 0,
        1, 0, 1, 0, 1, 1, 1, 0,
        1, 0, 1, 0, 1, 1, 1, 0,
        1, 0, 1, 0, 1, 1, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>
    /// Gets the finder pattern at the bottom left corner, row by row from the bottom edge upward, its light separator included.
    /// </summary>
    private static ReadOnlySpan<byte> CornerFinderPattern =>
    [
        1, 1, 1, 0, 1, 0, 1, 0,
        1, 1, 1, 0, 1, 0, 1, 0,
        1, 1, 1, 0, 1, 0, 1, 0,
        0, 0, 0, 0, 1, 0, 1, 0,
        1, 1, 1, 1, 1, 0, 1, 0,
        0, 0, 0, 0, 0, 0, 1, 0,
        1, 1, 1, 1, 1, 1, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>
    /// Builds a Han Xin Code symbol.
    /// </summary>
    /// <param name="text">The text, characters U+0000 to U+00FF.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="version">The version, 1 to 84, or 0 for the smallest that holds the data.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text is empty, has a character above U+00FF, or does not fit.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, HanXinErrorCorrectionLevel level, int version, double xDimension, ref BarcodeSymbol symbol)
    {
        Guard.MustBeBetweenOrEqualTo(text.Length, 1, MaximumLength, nameof(text));
        int invalid = text.IndexOfAnyExceptInRange('\0', '\u00FF');
        if (invalid >= 0)
        {
            throw new ArgumentException($"Han Xin Code encodes U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.", nameof(text));
        }

        // The first pass counts the bits, which choose the version; the second writes them.
        int bitCount = WriteBits(text, []);
        int levelIndex = (int)level;
        int chosen = version;
        if (chosen == 0)
        {
            chosen = 1;
            while (chosen < VersionCount && bitCount > DataCodewords(chosen, levelIndex) * 8)
            {
                chosen++;
            }
        }

        if (bitCount > DataCodewords(chosen, levelIndex) * 8)
        {
            throw new ArgumentException("The text does not fit in a Han Xin Code symbol of the requested version and level.", nameof(text));
        }

        // One rented block holds the data codewords, the codewords in block order, the codewords in placement order and the cells of the largest symbol. The
        // codeword spans hold one more than the most codewords for the zero codeword that can follow them. The cells are bytes, so their part of the block is
        // the side squared in bytes, rounded up to whole ints and read as bytes.
        int size = 21 + (2 * chosen);
        const int codewordLength = MaximumCodewords + 1;
        const int cellLength = ((MaximumSide * MaximumSide) + sizeof(int) - 1) / sizeof(int);
        using PooledBuffer<int> buffer = new((MaximumDataCodewords + (2 * codewordLength)) + cellLength);
        Span<int> data = buffer.Span[..MaximumDataCodewords];
        Span<int> blocks = buffer.Span.Slice(MaximumDataCodewords, codewordLength);
        Span<int> placed = buffer.Span.Slice(MaximumDataCodewords + codewordLength, codewordLength);
        Span<byte> cells = MemoryMarshal.Cast<int, byte>(buffer.Span.Slice(MaximumDataCodewords + (2 * codewordLength), cellLength))[..(size * size)];

        // The data bits run to the end of the data codewords, and zero bits pad the rest.
        int dataCount = DataCodewords(chosen, levelIndex);
        data = data[..dataCount];
        data.Clear();
        WriteBits(text, data);

        int codewordCount = AddErrorCorrection(data, chosen, levelIndex, blocks);
        Interleave(blocks[..codewordCount], placed);

        // A symbol whose modules are not a whole number of codewords takes one more zero codeword, whose bits fill the modules left.
        int moduleCount = DataModules[chosen - 1];
        int placedCount = codewordCount + ((moduleCount & 7) == 0 ? 0 : 1);
        placed[codewordCount] = 0;

        Layout(chosen, placed[..placedCount], cells);
        int mask = ChooseMask(cells, size);

        Span<bool> modules = symbol.BeginMatrix(size, size, QuietZone, xDimension);
        for (int i = 0; i < cells.Length; i++)
        {
            modules[i] = ((cells[i] >> mask) & 1) != 0;
        }

        WriteFunctionInformation(chosen, levelIndex, mask, size, modules);
    }

    /// <summary>
    /// Returns the data codewords of a version at a level: the codewords of the version less the check codewords of its blocks.
    /// </summary>
    /// <param name="version">The version, 1 to 84.</param>
    /// <param name="level">The level, 0 for L1 to 3 for L4.</param>
    /// <returns>The data codewords.</returns>
    private static int DataCodewords(int version, int level)
    {
        ReadOnlySpan<byte> groups = BlockGroups(version, level);
        return (groups[0] * groups[1]) + (groups[3] * groups[4]) + (groups[6] * groups[7]);
    }

    /// <summary>
    /// Returns the three block groups of a version at a level from <see cref="Blocks"/>.
    /// </summary>
    /// <param name="version">The version, 1 to 84.</param>
    /// <param name="level">The level, 0 for L1 to 3 for L4.</param>
    /// <returns>Nine bytes: for each group, the number of blocks, the data codewords and the check codewords of a block.</returns>
    private static ReadOnlySpan<byte> BlockGroups(int version, int level)
        => Blocks.Slice((((version - 1) * 4) + level) * 9, 9);

    /// <summary>
    /// Writes the data as segments: a run of digits as a Numeric segment when it is long enough to pay for its mode indicator and terminator, and the other
    /// characters as Byte segments. With an empty buffer the method only counts the bits.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="codewords">The data codewords to write the bits to, most significant bit first, or empty.</param>
    /// <returns>The number of bits.</returns>
    private static int WriteBits(ReadOnlySpan<char> text, Span<int> codewords)
    {
        // A Numeric segment of D digits costs a 4-bit mode indicator, 10 bits for each group of three digits and a 10-bit terminator. The same digits in a Byte
        // segment cost 8 bits each. A Numeric segment inside Byte data also starts a new Byte segment after it, a 4-bit mode indicator and a 13-bit count. So 8
        // digits, 61 bits against 64, is the shortest run that saves bits, and 5 digits, 34 bits against 40, when no Byte segment follows.
        BitWriter writer = new(codewords);

        // The start of the pending Byte segment, or -1 when there is none.
        int byteStart = -1;
        int i = 0;
        while (i < text.Length)
        {
            // The characters before the next digit join the pending Byte segment.
            int next = text[i..].IndexOfAnyInRange('0', '9');
            if (next != 0)
            {
                if (byteStart < 0)
                {
                    byteStart = i;
                }

                if (next < 0)
                {
                    break;
                }

                i += next;
            }

            int digits = text[i..].IndexOfAnyExceptInRange('0', '9');
            if (digits < 0)
            {
                digits = text.Length - i;
            }

            // A run too short for a Numeric segment joins the Byte segment whole. Each later start in the run gives a shorter run with the same end, so none of
            // them is long enough either.
            if (digits >= (i + digits == text.Length ? ShortestFinalNumericRun : ShortestNumericRun))
            {
                if (byteStart >= 0)
                {
                    writer.WriteBytes(text[byteStart..i]);
                    byteStart = -1;
                }

                writer.WriteDigits(text.Slice(i, digits));
            }
            else if (byteStart < 0)
            {
                byteStart = i;
            }

            i += digits;
        }

        if (byteStart >= 0)
        {
            writer.WriteBytes(text[byteStart..]);
        }

        return writer.Position;
    }

    /// <summary>
    /// Divides the data codewords into the blocks of the version and level, the groups in order, and writes each block followed by its check codewords. The
    /// check codewords of a block are the remainder of its data over the generator with the roots alpha^1 to alpha^k.
    /// </summary>
    /// <param name="data">The data codewords.</param>
    /// <param name="version">The version.</param>
    /// <param name="level">The level, 0 for L1 to 3 for L4.</param>
    /// <param name="blocks">The buffer the blocks are written to.</param>
    /// <returns>The number of codewords written.</returns>
    private static int AddErrorCorrection(ReadOnlySpan<int> data, int version, int level, Span<int> blocks)
    {
        ReadOnlySpan<byte> groups = BlockGroups(version, level);

        // A block takes at most 32 check codewords.
        InlineArray32<int> generatorStorage = default;

        // The data codewords run on through the blocks of every group: input is the first data codeword of the next block, and output the start of that block,
        // its check codewords after its data.
        int input = 0;
        int output = 0;
        for (int g = 0; g < 9; g += 3)
        {
            int count = groups[g];
            int dataLength = groups[g + 1];
            int checkLength = groups[g + 2];
            if (count == 0)
            {
                continue;
            }

            // The blocks of a group share one check length, so one generator serves the group. Section 5.5.2 gives its roots as alpha^1 to alpha^2t, 2t the
            // check codewords of a block.
            Span<int> generator = ((Span<int>)generatorStorage)[..checkLength];
            Field.Generator(1, generator);
            for (int b = 0; b < count; b++)
            {
                ReadOnlySpan<int> block = data.Slice(input, dataLength);
                block.CopyTo(blocks[output..]);
                Field.Remainder(block, 1, dataLength, generator, blocks.Slice(output + dataLength, checkLength));
                input += dataLength;
                output += dataLength + checkLength;
            }
        }

        return output;
    }

    /// <summary>
    /// Reorders the codewords for placement: codewords 0, 13, 26 and so on, then 1, 14, 27 and so on, to 12, 25, 38 and so on, so neighbouring codewords of a
    /// block lie apart in the symbol.
    /// </summary>
    /// <param name="codewords">The codewords in block order.</param>
    /// <param name="placed">The buffer the codewords are written to in placement order.</param>
    private static void Interleave(ReadOnlySpan<int> codewords, Span<int> placed)
    {
        int index = 0;
        for (int start = 0; start < 13; start++)
        {
            for (int c = start; c < codewords.Length; c += 13)
            {
                placed[index++] = codewords[c];
            }
        }
    }

    /// <summary>
    /// Builds the mask layers of a symbol: the alignment patterns, the finder patterns and the function information modules, which hold light until the mask is
    /// chosen, then the codeword bits in the cells left, row by row from the top left. Bit m of a cell is its module value under mask m, so a function cell is
    /// 0x0F or 0 and a data cell is its bit repeated, flipped by <see cref="MaskLayer"/>.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="codewords">The codewords in placement order.</param>
    /// <param name="cells">The cells, side squared, to write.</param>
    private static void Layout(int version, ReadOnlySpan<int> codewords, Span<byte> cells)
    {
        // A cell is unset until a pattern or a data bit claims it. Section 5.8.2 places the fixed patterns first, so the data takes only the cells they leave.
        const byte unset = 0xFF;
        int size = 21 + (2 * version);
        cells.Fill(unset);

        int spacing = Alignment[(version - 1) * 2];
        int intervals = Alignment[((version - 1) * 2) + 1];
        if (intervals != 0)
        {
            AddAlignment(cells, size, spacing, intervals);
        }

        // The tables run from the corner inward, so (x, y) of a table maps to the top left cell (x, y), the top right cell (side - 1 - x, y), the bottom right
        // cell (side - 1 - x, side - 1 - y) and the bottom left cell (x, side - 1 - y). The finder patterns overwrite any alignment cells under them.
        ReadOnlySpan<byte> finder = FinderPattern;
        ReadOnlySpan<byte> corner = CornerFinderPattern;
        for (int y = 0; y < FinderSize; y++)
        {
            for (int x = 0; x < FinderSize; x++)
            {
                byte value = finder[(y * FinderSize) + x];
                cells[(y * size) + x] = value;
                cells[(y * size) + size - 1 - x] = value;
                cells[((size - 1 - y) * size) + size - 1 - x] = value;
                cells[((size - 1 - y) * size) + x] = corner[(y * FinderSize) + x];
            }
        }

        // The function information carries the mask, so its cells are reserved light here and written after the mask is chosen. As function cells, they also
        // stay out of the masking.
        for (int i = 0; i < FunctionBits; i++)
        {
            FunctionPositions(i, size, out int x1, out int y1, out int x2, out int y2);
            cells[(y1 * size) + x1] = 0;
            cells[(y2 * size) + x2] = 0;
        }

        // The bits fill the unset cells in row order, the most significant bit of each codeword first. The count of unset cells is DataModules of the version,
        // and the codewords hold at least that many bits. The patterns above write 0 or 1, which become 0 or 0x0F, the same value under every mask.
        int bit = 0;
        int c = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++, c++)
            {
                if (cells[c] == unset)
                {
                    int value = (codewords[bit >> 3] >> (7 - (bit & 7))) & 1;
                    cells[c] = (byte)(MaskLayer(x + 1, y + 1) ^ (value * 0x0F));
                    bit++;
                }
                else
                {
                    cells[c] = (byte)(cells[c] * 0x0F);
                }
            }
        }
    }

    /// <summary>
    /// Draws the alignment patterns: dark lines on a staggered grid of the regular spacing, each with a light line beside it, and the light breaks where the
    /// lines meet the edges.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="size">The side in modules.</param>
    /// <param name="spacing">The regular distance between the lines.</param>
    /// <param name="intervals">The number of regular intervals across the side.</param>
    private static void AddAlignment(Span<byte> cells, int size, int spacing, int intervals)
    {
        // The coordinates run from the top right corner: a is the distance from the right edge and b the row. SetMirrored draws each point at (a, b) and at (b,
        // a), so each pass draws a row and its matching column together.
        //
        // The lines lie at 0, spacing, 2 x spacing and so on to intervals x spacing, and the last at side - 1. The last interval takes the side the regular
        // intervals leave.
        int remainder = size - (spacing * intervals);

        // Each line is dark in alternate stretches of one spacing, and the next line starts with the other phase. The alternation gives the stepped lines of
        // the symbol.
        int stagger = 0;
        int i = 0;
        while (i < size)
        {
            for (int j = 0; j < size; j++)
            {
                // In the regular part, a module is dark in a dark stretch or where the line crosses another line, at a multiple of the spacing. The first
                // stretch of the top line stays clear for the finder pattern at the top right. The last stretch, of the remainder length, continues the
                // alternation.
                bool dark = j + remainder < size
                    ? (((j / spacing) + stagger) % 2 == 0 && !(i == 0 && j < spacing)) || j % spacing == 0
                    : (intervals + stagger) % 2 == 0;

                // Each dark module has a light module one step down and one step left of it. Together these make the light line beside the dark line, so a
                // pattern is 2 modules wide, as section 6.1 requires.
                if (dark)
                {
                    SetMirrored(cells, size, j, i, 1);
                    if (j + 1 < size && i + 1 < size)
                    {
                        SetMirrored(cells, size, j + 1, i + 1, 0);
                    }
                }
            }

            i = i + remainder == size ? i + remainder - 1 : i + spacing;
            stagger = 1 - stagger;
        }

        // The assistant alignment patterns sit on the edges where a line meets them. Each is 3 modules along the edge and 2 deep, as section 6.1 requires. The
        // line end stays dark, and the five modules around it turn light. On the top and right edges, a pattern goes at every odd line. The phase of the bottom
        // and left edges depends on the number of lines, and there a pattern goes where the edge module before the line end is not dark.
        for (i = spacing; i <= size - 2; i += spacing)
        {
            if ((i / spacing) % 2 != 0)
            {
                SetReflected(cells, size, 0, i - 1, 0);
                SetReflected(cells, size, 0, i + 1, 0);
                SetReflected(cells, size, 1, i - 1, 0);
                SetReflected(cells, size, 1, i, 0);
                SetReflected(cells, size, 1, i + 1, 0);
                SetReflected(cells, size, i - 1, 0, 0);
                SetReflected(cells, size, i + 1, 0, 0);
                SetReflected(cells, size, i - 1, 1, 0);
                SetReflected(cells, size, i, 1, 0);
                SetReflected(cells, size, i + 1, 1, 0);
            }

            if (cells[((i - 1) * size) + 0] != 1)
            {
                SetReflected(cells, size, size - 1, i - 1, 0);
                SetReflected(cells, size, size - 2, i - 1, 0);
                SetReflected(cells, size, size - 2, i, 0);
                SetReflected(cells, size, size - 2, i + 1, 0);
                SetReflected(cells, size, size - 1, i + 1, 0);
                SetReflected(cells, size, i - 1, size - 1, 0);
                SetReflected(cells, size, i - 1, size - 2, 0);
                SetReflected(cells, size, i, size - 2, 0);
                SetReflected(cells, size, i + 1, size - 2, 0);
                SetReflected(cells, size, i + 1, size - 1, 0);
            }
        }
    }

    /// <summary>
    /// Sets the cell a column from the right edge on a row, x = side - 1 - a and y = b.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="size">The side in modules.</param>
    /// <param name="a">The distance from the right edge.</param>
    /// <param name="b">The row.</param>
    /// <param name="value">The value.</param>
    private static void SetReflected(Span<byte> cells, int size, int a, int b, byte value)
        => cells[(b * size) + size - 1 - a] = value;

    /// <summary>
    /// Sets the two cells of an alignment pattern point, which mirror each other about the diagonal from the top right corner: x = side - 1 - b with y = a, and
    /// x = side - 1 - a with y = b.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="size">The side in modules.</param>
    /// <param name="a">The first coordinate.</param>
    /// <param name="b">The second coordinate.</param>
    /// <param name="value">The value.</param>
    private static void SetMirrored(Span<byte> cells, int size, int a, int b, byte value)
    {
        SetReflected(cells, size, b, a, value);
        SetReflected(cells, size, a, b, value);
    }

    /// <summary>
    /// Returns the two positions of bit i of the function information: one beside the finder pattern at the top left or the bottom right, and one beside the
    /// finder pattern at the top right or the bottom left.
    /// </summary>
    /// <param name="i">The bit, 0 to 33.</param>
    /// <param name="size">The side in modules.</param>
    /// <param name="x1">The column of the first position.</param>
    /// <param name="y1">The row of the first position.</param>
    /// <param name="x2">The column of the second position.</param>
    /// <param name="y2">The row of the second position.</param>
    private static void FunctionPositions(int i, int size, out int x1, out int y1, out int x2, out int y2)
    {
        // Bits 0 to 8 run along row 8 from the left edge, bits 9 to 16 up column 8, bits 17 to 25 down from the top along column side - 9, and bits 26 to 33
        // along row 8 to the right edge, each with its partner by the opposite finder pattern.
        if (i < 9)
        {
            (x1, y1, x2, y2) = (i, 8, size - 1 - i, size - 9);
        }
        else if (i < 17)
        {
            int m = i - 8;
            (x1, y1, x2, y2) = (8, 8 - m, size - 9, size - 9 + m);
        }
        else if (i < 26)
        {
            int k = i - 17;
            (x1, y1, x2, y2) = (size - 9, k, 8, size - 1 - k);
        }
        else
        {
            int m = i - 25;
            (x1, y1, x2, y2) = (size - 9 + m, 8, 8 - m, size - 9);
        }
    }

    /// <summary>
    /// Returns the mask layer of a data module at one-based column a and row b: bit m is set where mask m flips the module. Mask 0 flips none, mask 1 where (a
    /// + b) mod 2 = 0, mask 2 where ((a + b) mod 3 + a mod 3) mod 2 = 0, and mask 3 where (a mod b + b mod a + a mod 3 + b mod 3) mod 2 = 0. The coordinates
    /// are one-based, so a mod b and b mod a never divide by zero.
    /// </summary>
    /// <param name="a">The one-based column.</param>
    /// <param name="b">The one-based row.</param>
    /// <returns>The mask layer, bits 1 to 3.</returns>
    private static int MaskLayer(int a, int b)
    {
        int layer = 0;
        layer |= (a + b) % 2 == 0 ? 1 << 1 : 0;
        layer |= (((a + b) % 3) + (a % 3)) % 2 == 0 ? 1 << 2 : 0;
        layer |= ((a % b) + (b % a) + (a % 3) + (b % 3)) % 2 == 0 ? 1 << 3 : 0;
        return layer;
    }

    /// <summary>
    /// Chooses the mask whose symbol scores the lowest penalty over its rows and columns. Equal scores keep the first mask.
    /// </summary>
    /// <param name="layers">The mask layers of the modules.</param>
    /// <param name="size">The side in modules.</param>
    /// <returns>The mask, 0 to 3.</returns>
    private static int ChooseMask(ReadOnlySpan<byte> layers, int size)
    {
        // A line of the largest symbol has at most 190 runs, the leading light run included.
        InlineArray256<int> runStorage = default;
        Span<int> runs = runStorage;
        int best = 0;
        int bestScore = int.MaxValue;
        for (int mask = 0; mask < MaskCount; mask++)
        {
            // Row `line` starts at line x size and steps 1; column `line` starts at line and steps size.
            int score = 0;
            for (int line = 0; line < size; line++)
            {
                score += Penalty(runs[..RunLengths(layers, line * size, 1, size, mask, runs)]);
                score += Penalty(runs[..RunLengths(layers, line, size, size, mask, runs)]);
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = mask;
            }
        }

        return best;
    }

    /// <summary>
    /// Writes the runs of a row or a column under a mask, alternately light and dark from a light run, which is empty when the line starts dark.
    /// </summary>
    /// <param name="layers">The mask layers of the modules.</param>
    /// <param name="start">The index of the first module.</param>
    /// <param name="step">The distance between two modules of the line.</param>
    /// <param name="size">The number of modules of the line.</param>
    /// <param name="mask">The mask.</param>
    /// <param name="runs">The buffer the runs are written to.</param>
    /// <returns>The number of runs.</returns>
    private static int RunLengths(ReadOnlySpan<byte> layers, int start, int step, int size, int mask, Span<int> runs)
    {
        // The first run is light, even when empty, so the even runs are light and the odd runs dark. Penalty depends on this order.
        int count = 1;
        runs[0] = 0;
        int colour = 0;
        for (int k = 0; k < size; k++)
        {
            int value = (layers[start + (k * step)] >> mask) & 1;
            if (value == colour)
            {
                runs[count - 1]++;
            }
            else
            {
                colour = value;
                runs[count++] = 1;
            }
        }

        return count;
    }

    /// <summary>
    /// Scores the runs of one line: 4 points for each module of a run of three or more, and 50 for each 1:1:1:1:3 or 3:1:1:1:1 ratio of runs that ends a line
    /// or meets a run of three or more modules beyond it.
    /// </summary>
    /// <param name="runs">The runs, alternately light and dark from a light run.</param>
    /// <returns>The penalty.</returns>
    private static int Penalty(ReadOnlySpan<int> runs)
    {
        int count = runs.Length;
        int score = 0;
        for (int j = 0; j < count; j++)
        {
            if (runs[j] >= 3)
            {
                score += 4 * runs[j];
            }
        }

        // A 1:1:1:1:3 or 3:1:1:1:1 ratio is the scan ratio of a Position Detection Pattern in section 4.2.3, so data that shows it can look like a finder
        // pattern. The ratio counts only when it is at the start or the end of the line, or when a light run of three or more modules is next to it.
        //
        // The odd runs are dark, so j steps over the dark runs only. Here dark run j is the 3 unit run, and runs j - 4 to j - 1 are the four 1 unit runs. j = 5
        // puts the ratio after the first light run alone, at the start of the line, and j + 2 >= count leaves at most the last light run after it, at the end.
        for (int j = 5; j <= count - 1; j += 2)
        {
            if (runs[j] % 3 == 0)
            {
                int unit = runs[j] / 3;
                if (runs[j - 4] == unit && runs[j - 3] == unit && runs[j - 2] == unit && runs[j - 1] == unit
                    && (j == 5 || j + 2 >= count || runs[j - 5] >= 3 || runs[j + 1] >= 3))
                {
                    score += 50;
                }
            }
        }

        // Here dark run j is the 3 unit run, and runs j + 1 to j + 4 are the four 1 unit runs. j = 1 puts the ratio at the start of the line, and j + 6 >=
        // count leaves at most the last light run after it.
        for (int j = 1; j <= count - 5; j += 2)
        {
            if (runs[j] % 3 == 0)
            {
                int unit = runs[j] / 3;
                if (runs[j + 1] == unit && runs[j + 2] == unit && runs[j + 3] == unit && runs[j + 4] == unit
                    && (j == 1 || j + 6 >= count || runs[j - 1] >= 3 || runs[j + 5] >= 3))
                {
                    score += 50;
                }
            }
        }

        return score;
    }

    /// <summary>
    /// Writes the function information: the version plus 20, the level and the mask as three 4-bit words, four 4-bit check words over GF(16) with the roots
    /// alpha^1 to alpha^4, and the bits 010101, each bit at its two positions.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="level">The level, 0 for L1 to 3 for L4.</param>
    /// <param name="mask">The mask, 0 to 3.</param>
    /// <param name="size">The side in modules.</param>
    /// <param name="modules">The modules to write.</param>
    private static void WriteFunctionInformation(int version, int level, int mask, int size, Span<bool> modules)
    {
        // The 12 bits are the version plus 20 (8 bits, 21 to 104), the level (2 bits, the encodings of Table 13) and the mask (2 bits). They split into three
        // 4-bit words, the first word holding the high bits.
        int value = ((version + 20) * 16) + (level * 4) + mask;
        InlineArray3<int> wordStorage = default;
        InlineArray4<int> generatorStorage = default;
        InlineArray4<int> checkStorage = default;
        Span<int> words = wordStorage;
        Span<int> generator = generatorStorage;
        Span<int> check = checkStorage;
        words[0] = (value >> 8) & 0xF;
        words[1] = (value >> 4) & 0xF;
        words[2] = value & 0xF;
        FunctionField.Generator(1, generator);
        FunctionField.Remainder(words, 1, 3, generator, check);

        // Bits 0 to 11 are the three data words and bits 12 to 27 the four check words, each word most significant bit first: i >> 2 selects the word and 3 -
        // (i & 3) the bit in it. Bits 28 to 33 alternate 0 and 1.
        for (int i = 0; i < FunctionBits; i++)
        {
            int bit = i < 12
                ? (words[i >> 2] >> (3 - (i & 3))) & 1
                : i < 28
                    ? (check[(i - 12) >> 2] >> (3 - ((i - 12) & 3))) & 1
                    : (i - 28) & 1;

            FunctionPositions(i, size, out int x1, out int y1, out int x2, out int y2);
            modules[(y1 * size) + x1] = bit == 1;
            modules[(y2 * size) + x2] = bit == 1;
        }
    }

    /// <summary>
    /// Writes the bits of the segments to the data codewords, most significant bit first, or only counts them when the buffer is empty.
    /// </summary>
    private ref struct BitWriter
    {
        /// <summary>
        /// The data codewords, or empty to count only.
        /// </summary>
        private readonly Span<int> codewords;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitWriter"/> struct.
        /// </summary>
        /// <param name="codewords">The data codewords, cleared, or empty to count only.</param>
        public BitWriter(Span<int> codewords)
        {
            this.codewords = codewords;
            this.Position = 0;
        }

        /// <summary>
        /// Gets the number of bits written.
        /// </summary>
        public int Position { get; private set; }

        /// <summary>
        /// Writes a Numeric segment: the mode indicator, the digits three to 10 bits as their value, and the terminator that gives the number of digits in the
        /// last group.
        /// </summary>
        /// <param name="digits">The digits.</param>
        public void WriteDigits(ReadOnlySpan<char> digits)
        {
            this.Write((int)SegmentMode.Numeric, 4);
            for (int start = 0; start < digits.Length; start += 3)
            {
                int value = 0;
                int end = Math.Min(start + 3, digits.Length);
                for (int d = start; d < end; d++)
                {
                    value = (value * 10) + (digits[d] - '0');
                }

                this.Write(value, 10);
            }

            // Table 2 gives the terminators 1111111101, 1111111110 and 1111111111 for a last group of one, two and three digits, which (length - 1) mod 3
            // selects.
            this.Write(NumericTerminator + ((digits.Length - 1) % 3), 10);
        }

        /// <summary>
        /// Writes a Byte segment: the mode indicator, the byte count and the bytes.
        /// </summary>
        /// <param name="bytes">The characters U+0000 to U+00FF as bytes.</param>
        public void WriteBytes(ReadOnlySpan<char> bytes)
        {
            this.Write((int)SegmentMode.Byte, 4);
            this.Write(bytes.Length, ByteCountBits);
            foreach (char b in bytes)
            {
                this.Write(b, 8);
            }
        }

        /// <summary>
        /// Writes the low bits of a value, the most significant first.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="count">The number of bits, at most 13.</param>
        private void Write(int value, int count)
        {
            if (!this.codewords.IsEmpty)
            {
                // Bit b of the value goes to stream bit position. position >> 3 is its codeword, and 7 - (position & 7) is its bit in that codeword, the first
                // stream bit being the most significant. The codewords start cleared, so OR sets the dark bits alone.
                for (int b = count - 1; b >= 0; b--)
                {
                    int position = this.Position + (count - 1 - b);
                    this.codewords[position >> 3] |= ((value >> b) & 1) << (7 - (position & 7));
                }
            }

            this.Position += count;
        }
    }
}
