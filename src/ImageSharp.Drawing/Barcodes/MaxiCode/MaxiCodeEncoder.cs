// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes MaxiCode symbols: 144 six-bit codewords in 33 rows of 30 hexagonal modules around a circular finder. The primary message of 10 data and 10 check
/// codewords carries the mode and, in modes 2 and 3, the structured carrier message of postal code, country code and class of service. The secondary message
/// carries the data in two interleaved Reed-Solomon blocks. The code set search finds the shortest sequence of code set latches, shifts and characters.
/// </summary>
internal static class MaxiCodeEncoder
{
    /// <summary>
    /// The longest data the secondary message can hold: 93 codewords of digits in numeric shift, 9 digits in 6 codewords.
    /// </summary>
    private const int MaximumDataLength = 138;

    /// <summary>
    /// The number of module positions in a row.
    /// </summary>
    private const int Columns = 30;

    /// <summary>
    /// The number of rows.
    /// </summary>
    private const int Rows = 33;

    /// <summary>
    /// The number of codewords in a symbol, the primary and secondary messages with their check codewords.
    /// </summary>
    private const int CodewordCount = 144;

    /// <summary>
    /// The number of data codewords of the primary message. The first is the mode.
    /// </summary>
    private const int PrimaryDataCodewords = 10;

    /// <summary>
    /// The number of check codewords of the primary message.
    /// </summary>
    private const int PrimaryCheckCodewords = 10;

    /// <summary>
    /// The index of the first secondary data codeword, after the primary data and check codewords.
    /// </summary>
    private const int SecondaryStart = PrimaryDataCodewords + PrimaryCheckCodewords;

    /// <summary>
    /// The number of secondary data codewords with standard error correction, modes 2, 3, 4 and 6.
    /// </summary>
    private const int StandardSecondaryLength = 84;

    /// <summary>
    /// The number of check codewords of one secondary block with standard error correction.
    /// </summary>
    private const int StandardSecondaryCheckCodewords = 20;

    /// <summary>
    /// The number of check codewords of one secondary block with enhanced error correction, mode 5.
    /// </summary>
    private const int EnhancedSecondaryCheckCodewords = 28;

    /// <summary>
    /// The length of a code set the search has not reached at a character.
    /// </summary>
    private const int Unreached = int.MaxValue;

    /// <summary>
    /// The number of code sets, A to E.
    /// </summary>
    private const int CodeSetCount = 5;

    /// <summary>
    /// The positions the code set search keeps, a power of two so a position maps to its row with a mask. The longest operation reads 9 characters back.
    /// </summary>
    private const int RingLength = 16;

    /// <summary>
    /// The codeword that latches from code set C, D or E to code set A.
    /// </summary>
    private const int LatchA = 58;

    /// <summary>
    /// The codeword of the numeric shift, NS, which carries nine digits in the five codewords after it.
    /// </summary>
    private const int NumericShiftCodeword = 31;

    /// <summary>
    /// The codeword that shifts two characters into code set A from B, 2 SHIFT A.
    /// </summary>
    private const int TwoShiftACodeword = 56;

    /// <summary>
    /// The codeword that shifts three characters into code set A from B, 3 SHIFT A.
    /// </summary>
    private const int ThreeShiftACodeword = 57;

    /// <summary>
    /// The codeword that shifts one character into code set B from A, SHIFT B, or into code set A from B, SHIFT A.
    /// </summary>
    private const int ShiftCodeword = 59;

    /// <summary>
    /// The codeword that shifts one character into code set C.
    /// </summary>
    private const int ShiftCCodeword = 60;

    /// <summary>
    /// The codeword that shifts one character into code set D.
    /// </summary>
    private const int ShiftDCodeword = 61;

    /// <summary>
    /// The codeword that shifts one character into code set E.
    /// </summary>
    private const int ShiftECodeword = 62;

    /// <summary>
    /// The data codewords of the secondary message of modes 2 and 3.
    /// </summary>
    private const int CarrierCapacity = 84;

    /// <summary>
    /// The data codewords of mode 5 after the mode codeword.
    /// </summary>
    private const int EnhancedCapacity = 77;

    /// <summary>
    /// The data codewords of modes 4 and 6 after the mode codeword.
    /// </summary>
    private const int StandardCapacity = 93;

    /// <summary>
    /// The field of the Reed-Solomon codes: GF(64) with the polynomial x^6 + x + 1, 67 decimal.
    /// </summary>
    private static readonly GaloisField Field = new(64, 67);

    /// <summary>
    /// The code sets of Table 3, in the row and column order of the latch, pad and value tables.
    /// </summary>
    private enum CodeSet : byte
    {
        /// <summary>
        /// Code set A.
        /// </summary>
        A,

        /// <summary>
        /// Code set B.
        /// </summary>
        B,

        /// <summary>
        /// Code set C.
        /// </summary>
        C,

        /// <summary>
        /// Code set D.
        /// </summary>
        D,

        /// <summary>
        /// Code set E.
        /// </summary>
        E,
    }

    /// <summary>
    /// A set of code sets, bit n for the code set of value n in <see cref="CodeSet"/>.
    /// </summary>
    [Flags]
    private enum CodeSets : byte
    {
        /// <summary>
        /// No code set.
        /// </summary>
        None = 0,

        /// <summary>
        /// Code set A.
        /// </summary>
        A = 1,

        /// <summary>
        /// Code set B.
        /// </summary>
        B = 2,

        /// <summary>
        /// Code set C.
        /// </summary>
        C = 4,

        /// <summary>
        /// Code set D.
        /// </summary>
        D = 8,

        /// <summary>
        /// Code set E.
        /// </summary>
        E = 16,
    }

    /// <summary>
    /// The operations of the code set search, in the order the search tries them. Equal lengths keep the first operation found. The character operations follow
    /// the code sets A to E in order.
    /// </summary>
    private enum Operation : byte
    {
        /// <summary>
        /// The numeric shift: nine digits in six codewords.
        /// </summary>
        NumericShift,

        /// <summary>
        /// One character of code set A in one codeword.
        /// </summary>
        CharacterA,

        /// <summary>
        /// One character of code set B in one codeword.
        /// </summary>
        CharacterB,

        /// <summary>
        /// One character of code set C in one codeword.
        /// </summary>
        CharacterC,

        /// <summary>
        /// One character of code set D in one codeword.
        /// </summary>
        CharacterD,

        /// <summary>
        /// One character of code set E in one codeword.
        /// </summary>
        CharacterE,

        /// <summary>
        /// SHIFT A and one character of code set A.
        /// </summary>
        ShiftA,

        /// <summary>
        /// 2 SHIFT A and two characters of code set A.
        /// </summary>
        ShiftA2,

        /// <summary>
        /// 3 SHIFT A and three characters of code set A.
        /// </summary>
        ShiftA3,

        /// <summary>
        /// SHIFT B and one character of code set B.
        /// </summary>
        ShiftB,

        /// <summary>
        /// The shift into code set C and one character of code set C.
        /// </summary>
        ShiftC,

        /// <summary>
        /// The shift into code set D and one character of code set D.
        /// </summary>
        ShiftD,

        /// <summary>
        /// The shift into code set E and one character of code set E.
        /// </summary>
        ShiftE,
    }

    /// <summary>
    /// Gets the codewords of the latch into each code set, row, from each code set, column, written in the code set it latches from: a latch A or B is one
    /// codeword, and a lock into C, D or E is the shift into that code set followed by the lock. 0 marks no codeword.
    /// </summary>
    private static ReadOnlySpan<byte> LatchCodes =>
    [
        0, 0, 63, 0, 58, 0, 58, 0, 58, 0,
        63, 0, 0, 0, 63, 0, 63, 0, 63, 0,
        60, 60, 60, 60, 0, 0, 60, 60, 60, 60,
        61, 61, 61, 61, 61, 61, 0, 0, 61, 61,
        62, 62, 62, 62, 62, 62, 62, 62, 0, 0,
    ];

    /// <summary>
    /// Gets the pad codeword of each code set, 0 where the code set has none.
    /// </summary>
    private static ReadOnlySpan<byte> PadCodes => [33, 33, 0, 0, 28];

    /// <summary>
    /// Gets the dark modules of the orientation patterns around the finder, as row x 30 + column.
    /// </summary>
    private static ReadOnlySpan<short> OrientationModules => [28, 29, 280, 281, 311, 457, 488, 500, 530, 670, 700, 677, 707];

    /// <summary>
    /// Gets the module of each codeword bit, as row x 30 + column: bit b of codeword c, the most significant bit first, is entry 6c + b.
    /// </summary>
    private static ReadOnlySpan<short> ModuleMap =>
    [
        469, 529, 286, 316, 347, 346, 673, 672, 703, 702, 647, 676, 283, 282, 313, 312, 370, 610, 618, 379,
        378, 409, 408, 439, 705, 704, 559, 589, 588, 619, 458, 518, 640, 701, 675, 674, 285, 284, 315, 314,
        310, 340, 531, 289, 288, 319, 349, 348, 456, 486, 517, 516, 471, 470, 369, 368, 399, 398, 429, 428,
        549, 548, 579, 578, 609, 608, 649, 648, 679, 678, 709, 708, 639, 638, 669, 668, 699, 698, 279, 278,
        309, 308, 339, 338, 381, 380, 411, 410, 441, 440, 561, 560, 591, 590, 621, 620, 547, 546, 577, 576,
        607, 606, 367, 366, 397, 396, 427, 426, 291, 290, 321, 320, 351, 350, 651, 650, 681, 680, 711, 710,
        1, 0, 31, 30, 61, 60, 3, 2, 33, 32, 63, 62, 5, 4, 35, 34, 65, 64, 7, 6,
        37, 36, 67, 66, 9, 8, 39, 38, 69, 68, 11, 10, 41, 40, 71, 70, 13, 12, 43, 42,
        73, 72, 15, 14, 45, 44, 75, 74, 17, 16, 47, 46, 77, 76, 19, 18, 49, 48, 79, 78,
        21, 20, 51, 50, 81, 80, 23, 22, 53, 52, 83, 82, 25, 24, 55, 54, 85, 84, 27, 26,
        57, 56, 87, 86, 117, 116, 147, 146, 177, 176, 115, 114, 145, 144, 175, 174, 113, 112, 143, 142,
        173, 172, 111, 110, 141, 140, 171, 170, 109, 108, 139, 138, 169, 168, 107, 106, 137, 136, 167, 166,
        105, 104, 135, 134, 165, 164, 103, 102, 133, 132, 163, 162, 101, 100, 131, 130, 161, 160, 99, 98,
        129, 128, 159, 158, 97, 96, 127, 126, 157, 156, 95, 94, 125, 124, 155, 154, 93, 92, 123, 122,
        153, 152, 91, 90, 121, 120, 151, 150, 181, 180, 211, 210, 241, 240, 183, 182, 213, 212, 243, 242,
        185, 184, 215, 214, 245, 244, 187, 186, 217, 216, 247, 246, 189, 188, 219, 218, 249, 248, 191, 190,
        221, 220, 251, 250, 193, 192, 223, 222, 253, 252, 195, 194, 225, 224, 255, 254, 197, 196, 227, 226,
        257, 256, 199, 198, 229, 228, 259, 258, 201, 200, 231, 230, 261, 260, 203, 202, 233, 232, 263, 262,
        205, 204, 235, 234, 265, 264, 207, 206, 237, 236, 267, 266, 297, 296, 327, 326, 357, 356, 295, 294,
        325, 324, 355, 354, 293, 292, 323, 322, 353, 352, 277, 276, 307, 306, 337, 336, 275, 274, 305, 304,
        335, 334, 273, 272, 303, 302, 333, 332, 271, 270, 301, 300, 331, 330, 361, 360, 391, 390, 421, 420,
        363, 362, 393, 392, 423, 422, 365, 364, 395, 394, 425, 424, 383, 382, 413, 412, 443, 442, 385, 384,
        415, 414, 445, 444, 387, 386, 417, 416, 447, 446, 477, 476, 507, 506, 537, 536, 475, 474, 505, 504,
        535, 534, 473, 472, 503, 502, 533, 532, 455, 454, 485, 484, 515, 514, 453, 452, 483, 482, 513, 512,
        451, 450, 481, 480, 511, 510, 541, 540, 571, 570, 601, 600, 543, 542, 573, 572, 603, 602, 545, 544,
        575, 574, 605, 604, 563, 562, 593, 592, 623, 622, 565, 564, 595, 594, 625, 624, 567, 566, 597, 596,
        627, 626, 657, 656, 687, 686, 717, 716, 655, 654, 685, 684, 715, 714, 653, 652, 683, 682, 713, 712,
        637, 636, 667, 666, 697, 696, 635, 634, 665, 664, 695, 694, 633, 632, 663, 662, 693, 692, 631, 630,
        661, 660, 691, 690, 721, 720, 751, 750, 781, 780, 723, 722, 753, 752, 783, 782, 725, 724, 755, 754,
        785, 784, 727, 726, 757, 756, 787, 786, 729, 728, 759, 758, 789, 788, 731, 730, 761, 760, 791, 790,
        733, 732, 763, 762, 793, 792, 735, 734, 765, 764, 795, 794, 737, 736, 767, 766, 797, 796, 739, 738,
        769, 768, 799, 798, 741, 740, 771, 770, 801, 800, 743, 742, 773, 772, 803, 802, 745, 744, 775, 774,
        805, 804, 747, 746, 777, 776, 807, 806, 837, 836, 867, 866, 897, 896, 835, 834, 865, 864, 895, 894,
        833, 832, 863, 862, 893, 892, 831, 830, 861, 860, 891, 890, 829, 828, 859, 858, 889, 888, 827, 826,
        857, 856, 887, 886, 825, 824, 855, 854, 885, 884, 823, 822, 853, 852, 883, 882, 821, 820, 851, 850,
        881, 880, 819, 818, 849, 848, 879, 878, 817, 816, 847, 846, 877, 876, 815, 814, 845, 844, 875, 874,
        813, 812, 843, 842, 873, 872, 811, 810, 841, 840, 871, 870, 901, 900, 931, 930, 961, 960, 903, 902,
        933, 932, 963, 962, 905, 904, 935, 934, 965, 964, 907, 906, 937, 936, 967, 966, 909, 908, 939, 938,
        969, 968, 911, 910, 941, 940, 971, 970, 913, 912, 943, 942, 973, 972, 915, 914, 945, 944, 975, 974,
        917, 916, 947, 946, 977, 976, 919, 918, 949, 948, 979, 978, 921, 920, 951, 950, 981, 980, 923, 922,
        953, 952, 983, 982, 925, 924, 955, 954, 985, 984, 927, 926, 957, 956, 987, 986, 58, 89, 88, 118,
        149, 148, 178, 209, 208, 238, 269, 268, 298, 329, 328, 358, 389, 388, 418, 449, 448, 478, 509, 508,
        538, 569, 568, 598, 629, 628, 658, 689, 688, 718, 749, 748, 778, 809, 808, 838, 869, 868, 898, 929,
        928, 958, 989, 988,
    ];

    /// <summary>
    /// Gets the value of each byte in each code set of Table 3, at index code set x 256 + byte, or 0xFF where the code set has no such byte. Code sets A to E
    /// follow in turn, 256 bytes each. A byte that two values of one code set stand for takes the later value.
    /// </summary>
    private static ReadOnlySpan<byte> SetValues =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 28, 29, 30, 255,
        32, 255, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 255, 255, 255, 255, 255,
        255, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 28, 29, 30, 255,
        47, 53, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 48, 255, 49, 50,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 51, 37, 38, 39, 40, 41,
        52, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 42, 43, 44, 45, 46,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 32, 54, 34, 35, 36,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 28, 29, 30, 255,
        59, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 37, 255, 38, 255, 255, 255,
        255, 39, 40, 41, 255, 42, 255, 255, 255, 43, 44, 255, 45, 46, 47, 255,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 32, 33, 34, 35, 36,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 28, 29, 30, 255,
        59, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 47, 48, 49, 50, 51, 52,
        53, 54, 55, 56, 57, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 37, 255, 255, 255, 255, 255, 255, 38, 255, 255, 39, 255, 255, 255, 40,
        41, 255, 255, 255, 42, 255, 255, 43, 44, 255, 255, 45, 255, 255, 255, 46,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 32, 33, 34, 35, 36,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 30, 32, 33, 34, 35,
        59, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 36,
        37, 255, 38, 39, 40, 41, 42, 43, 255, 44, 255, 255, 255, 45, 46, 255,
        255, 255, 255, 255, 255, 255, 47, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    ];

    /// <summary>
    /// Builds a MaxiCode symbol.
    /// </summary>
    /// <param name="text">The text, characters U+0000 to U+00FF.</param>
    /// <param name="mode">The mode, or <see langword="null"/> for mode 5 when the data fits and mode 4 otherwise.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text is empty, has a character above U+00FF, breaks a rule of the structured carrier message, or does not
    /// fit.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, MaxiCodeMode? mode, double xDimension, ref BarcodeSymbol symbol)
    {
        if (text.IsEmpty)
        {
            throw new ArgumentException("MaxiCode needs at least one character.", nameof(text));
        }

        int invalid = text.IndexOfAnyExceptInRange('\0', 'ÿ');
        if (invalid >= 0)
        {
            throw new ArgumentException($"MaxiCode encodes U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.", nameof(text));
        }

        bool carrier = mode is MaxiCodeMode.StructuredCarrierNumeric or MaxiCodeMode.StructuredCarrierAlphanumeric;
        InlineArray10<int> primaryStorage = default;
        Span<int> primary = primaryStorage;
        scoped ReadOnlySpan<char> data = text;
        InlineArray256<char> carrierDataStorage = default;
        Span<char> carrierData = ((Span<char>)carrierDataStorage)[..MaximumDataLength];
        if (carrier)
        {
            data = carrierData[..ParseStructuredCarrier(text, mode!.Value, primary, carrierData)];
        }

        if (data.Length > MaximumDataLength)
        {
            throw new ArgumentException($"MaxiCode holds at most {MaximumDataLength} characters of data; got {data.Length}.", nameof(text));
        }

        // No mode holds more than StandardCapacity codewords, and the closing latch takes one more. The search writes no longer message, and the capacity check
        // below rejects it.
        InlineArray128<int> messageStorage = default;
        Span<int> message = ((Span<int>)messageStorage)[..(StandardCapacity + 1)];
        int messageLength = EncodeMessage(data, message, out CodeSet endCodeSet);
        MaxiCodeMode chosen = mode ?? (messageLength <= EnhancedCapacity ? MaxiCodeMode.FullErrorCorrection : MaxiCodeMode.Standard);
        int capacity = chosen switch
        {
            MaxiCodeMode.StructuredCarrierNumeric or MaxiCodeMode.StructuredCarrierAlphanumeric => CarrierCapacity,
            MaxiCodeMode.FullErrorCorrection => EnhancedCapacity,
            _ => StandardCapacity,
        };

        if (messageLength > capacity)
        {
            throw new ArgumentException($"A mode {(int)chosen} MaxiCode holds at most {capacity} codewords of data.", nameof(text));
        }

        // A message that ends in a code set without a pad codeword latches back to code set A when the capacity of the chosen mode leaves room to pad.
        int pad = PadCodes[(int)endCodeSet];
        if (pad == 0 && messageLength < capacity)
        {
            message[messageLength++] = LatchA;
            pad = PadCodes[(int)CodeSet.A];
        }

        InlineArray256<int> codewordsStorage = default;
        Span<int> codewords = ((Span<int>)codewordsStorage)[..CodewordCount];
        int secondaryLength;
        if (carrier)
        {
            primary.CopyTo(codewords);
            secondaryLength = CarrierCapacity;
            Span<int> secondary = codewords.Slice(SecondaryStart, secondaryLength);
            secondary.Fill(pad);
            message[..messageLength].CopyTo(secondary);
        }
        else
        {
            // The mode is the first codeword and the message follows it, through the 9 remaining data codewords of the primary message and on into the
            // secondary message.
            secondaryLength = capacity + 1 - PrimaryDataCodewords;
            codewords[..PrimaryDataCodewords].Fill(pad);
            codewords.Slice(SecondaryStart, secondaryLength).Fill(pad);
            codewords[0] = (int)chosen;
            int head = Math.Min(messageLength, PrimaryDataCodewords - 1);
            message[..head].CopyTo(codewords[1..]);
            message[head..messageLength].CopyTo(codewords[SecondaryStart..]);
        }

        AddCheckCodewords(codewords, secondaryLength);

        // Section 4.11.5: "1W on the left and right sides" and "1Y on the top and bottom", W being X and Y the row pitch, √3/2 X.
        Span<bool> modules = symbol.BeginHexagonal(Columns, Rows, 1F, (float)(Math.Sqrt(3) / 2), xDimension);
        ReadOnlySpan<short> map = ModuleMap;
        for (int i = 0; i < CodewordCount; i++)
        {
            for (int b = 0; b < 6; b++)
            {
                if (((codewords[i] >> (5 - b)) & 1) == 1)
                {
                    modules[map[(i * 6) + b]] = true;
                }
            }
        }

        foreach (short module in OrientationModules)
        {
            modules[module] = true;
        }
    }

    /// <summary>
    /// Reads the structured carrier message of a mode 2 or 3 symbol from the head of the text and writes the primary data codewords. The text is the postal
    /// code, the 3-digit country code and the 3-digit class of service, each followed by a group separator, then the secondary data. A head of "[)>", RS, "01",
    /// GS and two digits stays at the front of the secondary data.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="mode">The mode, <see cref="MaxiCodeMode.StructuredCarrierNumeric"/> or <see cref="MaxiCodeMode.StructuredCarrierAlphanumeric"/>.</param>
    /// <param name="primary">The buffer the 10 primary data codewords are written to.</param>
    /// <param name="buffer">The buffer the secondary data is assembled in.</param>
    /// <returns>The length of the secondary data written to <paramref name="buffer"/>.</returns>
    private static int ParseStructuredCarrier(ReadOnlySpan<char> text, MaxiCodeMode mode, Span<int> primary, Span<char> buffer)
    {
        ReadOnlySpan<char> header = [];
        ReadOnlySpan<char> rest = text;
        if (text.Length >= 9 && text.StartsWith("[)>\u001E01\u001D") && char.IsAsciiDigit(text[7]) && char.IsAsciiDigit(text[8]))
        {
            header = text[..9];
            rest = text[9..];
        }

        if (!TryNextField(ref rest, out ReadOnlySpan<char> postalCode)
            || !TryNextField(ref rest, out ReadOnlySpan<char> countryCode)
            || !TryNextField(ref rest, out ReadOnlySpan<char> serviceClass))
        {
            throw new ArgumentException(
                "A mode 2 or 3 MaxiCode starts with the postal code, the country code and the class of service, each followed by a group separator.",
                nameof(text));
        }

        bool numeric = mode == MaxiCodeMode.StructuredCarrierNumeric;
        if (numeric && (postalCode.Length is 0 or > 9 || postalCode.ContainsAnyExceptInRange('0', '9')))
        {
            throw new ArgumentException("A mode 2 postal code is 1 to 9 digits.", nameof(text));
        }

        if (!numeric)
        {
            if (postalCode.Length is 0 or > 6)
            {
                throw new ArgumentException("A mode 3 postal code is 1 to 6 characters.", nameof(text));
            }

            foreach (char c in postalCode)
            {
                if (c is not (' ' or (>= '"' and <= ':') or (>= 'A' and <= 'Z')))
                {
                    throw new ArgumentException($"A mode 3 postal code carries spaces, \" to : and A to Z; got '{c}'.", nameof(text));
                }
            }
        }

        if (countryCode.Length != 3 || countryCode.ContainsAnyExceptInRange('0', '9'))
        {
            throw new ArgumentException("The country code is three digits.", nameof(text));
        }

        if (serviceClass.Length != 3 || serviceClass.ContainsAnyExceptInRange('0', '9'))
        {
            throw new ArgumentException("The class of service is three digits.", nameof(text));
        }

        // The postal code takes 36 bits. A numeric code is its length in 6 bits and its value in 30 bits, a 5-digit United States code padded with "0000" to 9
        // digits. An alphanumeric code is 6 characters of code set A, cut or padded with spaces.
        long postal = 0;
        if (numeric)
        {
            InlineArray10<char> digitsStorage = default;
            Span<char> digits = ((Span<char>)digitsStorage)[..9];
            int length = postalCode.Length;
            postalCode.CopyTo(digits);
            if (countryCode.SequenceEqual("840") && length == 5)
            {
                "0000".CopyTo(digits[5..]);
                length = 9;
            }

            long value = 0;
            foreach (char digit in digits[..length])
            {
                value = (value * 10) + (digit - '0');
            }

            postal = ((long)length << 30) | value;
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                char c = i < postalCode.Length ? postalCode[i] : ' ';
                postal = (postal << 6) | (uint)Value(CodeSet.A, c);
            }
        }

        int country = int.Parse(countryCode, System.Globalization.CultureInfo.InvariantCulture);
        int service = int.Parse(serviceClass, System.Globalization.CultureInfo.InvariantCulture);

        // The primary data is one 60-bit number, least significant bits first: the mode in bits 0 to 3, the postal code in bits 4 to 39, the country code in
        // bits 40 to 49 and the class of service in bits 50 to 59. Primary codeword i carries bits 6i to 6i + 5.
        long primaryBits = (long)mode | (postal << 4) | ((long)country << 40) | ((long)service << 50);
        for (int i = 0; i < primary.Length; i++)
        {
            primary[i] = (int)((primaryBits >> (6 * i)) & 63);
        }

        header.CopyTo(buffer);
        if (header.Length + rest.Length > buffer.Length)
        {
            throw new ArgumentException($"MaxiCode holds at most {MaximumDataLength} characters of data.", nameof(text));
        }

        rest.CopyTo(buffer[header.Length..]);
        return header.Length + rest.Length;
    }

    /// <summary>
    /// Takes the field before the next group separator off the text.
    /// </summary>
    /// <param name="rest">The text, advanced past the separator.</param>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when the text has a group separator; otherwise, <see langword="false"/>.</returns>
    private static bool TryNextField(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> field)
    {
        int separator = rest.IndexOf('\u001D');
        if (separator < 0)
        {
            field = [];
            return false;
        }

        field = rest[..separator];
        rest = rest[(separator + 1)..];
        return true;
    }

    /// <summary>
    /// Finds the shortest codeword sequence for the data: for each character and each code set, the shortest length that ends in that code set, with the
    /// operation and the earlier code set that reach it, then a walk back from the end.
    /// </summary>
    /// <param name="data">The data, at most <see cref="MaximumDataLength"/> characters.</param>
    /// <param name="message">The buffer the codewords are written to. A message longer than the buffer is not written.</param>
    /// <param name="endCodeSet">The code set the message ends in.</param>
    /// <returns>The number of codewords.</returns>
    private static int EncodeMessage(ReadOnlySpan<char> data, Span<int> message, out CodeSet endCodeSet)
    {
        // The code sets the data needs beyond A. FS, GS, RS and space are in every code set.
        CodeSets needed = CodeSets.None;
        foreach (char c in data)
        {
            if (c is (< (char)28 or > (char)30) and not ' ')
            {
                for (CodeSet set = CodeSet.B; set <= CodeSet.E; set++)
                {
                    if (Has(set, c))
                    {
                        needed |= (CodeSets)(1 << (int)set);
                    }
                }
            }
        }

        // Code sets with a pad codeword come first, so they win ties.
        InlineArray5<CodeSet> priorityStorage = default;
        Span<CodeSet> priority = priorityStorage;
        int priorityCount = 0;
        priority[priorityCount++] = CodeSet.A;
        ReadOnlySpan<CodeSet> order = [CodeSet.B, CodeSet.E, CodeSet.C, CodeSet.D];
        foreach (CodeSet set in order)
        {
            if (needed.HasFlag((CodeSets)(1 << (int)set)))
            {
                priority[priorityCount++] = set;
            }
        }

        priority = priority[..priorityCount];

        // The lengths and best earlier code sets of the last RingLength characters, row (n mod RingLength) for character n, so row RingLength - 1 holds the
        // start before the first character, all zero. The longest operation reads 9 characters back, which the ring keeps.
        InlineArray128<int> bestLengthStorage = default;
        Span<int> bestLength = ((Span<int>)bestLengthStorage)[..(RingLength * CodeSetCount)];
        InlineArray128<CodeSet> bestOriginStorage = default;
        Span<CodeSet> bestOrigin = ((Span<CodeSet>)bestOriginStorage)[..(RingLength * CodeSetCount)];

        // The operation and the earlier code set that reach each character in each code set, for the walk back. The data is at most MaximumDataLength
        // characters.
        InlineArray256<InlineArray5<Step>> pathStorage = default;
        Span<InlineArray5<Step>> path = ((Span<InlineArray5<Step>>)pathStorage)[..data.Length];

        int digits = 0;
        int inA = 0;
        for (int n = 0; n < data.Length; n++)
        {
            int c = data[n];
            digits = c is >= '0' and <= '9' ? digits + 1 : 0;
            inA = Has(CodeSet.A, c) ? inA + 1 : 0;

            Span<int> lengthRow = bestLength.Slice((n & (RingLength - 1)) * CodeSetCount, CodeSetCount);
            Span<CodeSet> originRow = bestOrigin.Slice((n & (RingLength - 1)) * CodeSetCount, CodeSetCount);
            foreach (CodeSet state in priority)
            {
                int best = Unreached;
                for (Operation op = Operation.NumericShift; op <= Operation.ShiftE; op++)
                {
                    if (!Applies(op, state, needed) || !Can(op, c, digits, inA))
                    {
                        continue;
                    }

                    int m = (n - Intake(op)) & (RingLength - 1);
                    CodeSet origin = bestOrigin[(m * CodeSetCount) + (int)state];
                    long length = (long)bestLength[(m * CodeSetCount) + (int)origin] + LatchLength(state, origin) + Output(op);
                    if (best > length)
                    {
                        best = (int)length;
                        path[n][(int)state] = new Step(op, origin);
                    }
                }

                lengthRow[(int)state] = best;
            }

            foreach (CodeSet state in priority)
            {
                long originLength = (long)lengthRow[(int)CodeSet.A] + LatchLength(state, CodeSet.A);
                CodeSet origin = CodeSet.A;
                foreach (CodeSet candidate in priority[1..])
                {
                    long length = (long)lengthRow[(int)candidate] + LatchLength(state, candidate);
                    if (length < originLength)
                    {
                        originLength = length;
                        origin = candidate;
                    }
                }

                originRow[(int)state] = origin;
            }
        }

        Span<int> last = bestLength.Slice(((data.Length - 1) & (RingLength - 1)) * CodeSetCount, CodeSetCount);
        CodeSet endState = CodeSet.A;
        int total = Unreached;
        foreach (CodeSet state in priority)
        {
            if (last[(int)state] < total)
            {
                total = last[(int)state];
                endState = state;
            }
        }

        endCodeSet = endState;
        if (total > message.Length)
        {
            return total;
        }

        // The walk back writes each operation and the latch before it from the end of the message.
        int position = total;
        CodeSet current = endState;
        int index = data.Length;
        InlineArray6<int> outputStorage = default;
        Span<int> output = outputStorage;
        while (index > 0)
        {
            Step step = path[index - 1][(int)current];
            index -= Intake(step.Operation);
            int count = Encode(step.Operation, data, index, output);
            int latchCount = LatchLength(current, step.Prior);
            position -= latchCount + count;
            ReadOnlySpan<byte> latch = LatchCodes.Slice((((int)current * CodeSetCount) + (int)step.Prior) * 2, latchCount);
            for (int i = 0; i < latch.Length; i++)
            {
                message[position + i] = latch[i];
            }

            output[..count].CopyTo(message[(position + latchCount)..]);
            current = step.Prior;
        }

        return total;
    }

    /// <summary>
    /// Returns whether an operation is open in a code set. The characters of a code set are open in it. In B, the shifts into A are open. In A, the shift into
    /// B is open. In every other code set, the shifts into C, D and E are open. The numeric shift is open everywhere.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <param name="state">The code set, which is A or a code set the data needs.</param>
    /// <param name="needed">The code sets beyond A that the data needs.</param>
    /// <returns><see langword="true"/> when the operation is open.</returns>
    private static bool Applies(Operation op, CodeSet state, CodeSets needed)
        => op switch
        {
            Operation.NumericShift => true,
            Operation.CharacterA => state == CodeSet.A,
            Operation.CharacterB => state == CodeSet.B,
            Operation.CharacterC => state == CodeSet.C,
            Operation.CharacterD => state == CodeSet.D,
            Operation.CharacterE => state == CodeSet.E,
            Operation.ShiftA or Operation.ShiftA2 or Operation.ShiftA3 => state == CodeSet.B,
            Operation.ShiftB => needed.HasFlag(CodeSets.B) && state == CodeSet.A,
            Operation.ShiftC => needed.HasFlag(CodeSets.C) && state != CodeSet.C,
            Operation.ShiftD => needed.HasFlag(CodeSets.D) && state != CodeSet.D,
            _ => needed.HasFlag(CodeSets.E) && state != CodeSet.E,
        };

    /// <summary>
    /// Returns whether an operation takes the characters ending at the current one.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <param name="c">The current character.</param>
    /// <param name="digits">The number of digits ending at the current character.</param>
    /// <param name="inA">The number of characters of code set A ending at the current character.</param>
    /// <returns><see langword="true"/> when the operation takes them.</returns>
    private static bool Can(Operation op, int c, int digits, int inA)
        => op switch
        {
            Operation.NumericShift => digits >= 9,
            Operation.CharacterA => Has(CodeSet.A, c),
            Operation.CharacterB or Operation.ShiftB => Has(CodeSet.B, c),
            Operation.CharacterC or Operation.ShiftC => Has(CodeSet.C, c),
            Operation.CharacterD or Operation.ShiftD => Has(CodeSet.D, c),
            Operation.CharacterE or Operation.ShiftE => Has(CodeSet.E, c),
            Operation.ShiftA => inA >= 1,
            Operation.ShiftA2 => inA >= 2,
            _ => inA >= 3,
        };

    /// <summary>
    /// Returns the number of characters an operation takes.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <returns>The number of characters.</returns>
    private static int Intake(Operation op)
        => op switch
        {
            Operation.NumericShift => 9,
            Operation.ShiftA2 => 2,
            Operation.ShiftA3 => 3,
            _ => 1,
        };

    /// <summary>
    /// Returns the number of codewords an operation writes.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <returns>The number of codewords.</returns>
    private static int Output(Operation op)
        => op switch
        {
            Operation.NumericShift => 6,
            Operation.CharacterA or Operation.CharacterB or Operation.CharacterC or Operation.CharacterD or Operation.CharacterE => 1,
            Operation.ShiftA2 => 3,
            Operation.ShiftA3 => 4,
            _ => 2,
        };

    /// <summary>
    /// Writes the codewords of an operation.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <param name="data">The data.</param>
    /// <param name="n">The first character the operation takes.</param>
    /// <param name="output">The buffer the codewords are written to.</param>
    /// <returns>The number of codewords.</returns>
    private static int Encode(Operation op, ReadOnlySpan<char> data, int n, Span<int> output)
    {
        switch (op)
        {
            case Operation.NumericShift:
            {
                // NS: nine digits as a 30-bit number in five codewords.
                int value = 0;
                foreach (char digit in data.Slice(n, 9))
                {
                    value = (value * 10) + (digit - '0');
                }

                output[0] = NumericShiftCodeword;
                for (int i = 0; i < 5; i++)
                {
                    output[1 + i] = (value >> (24 - (6 * i))) & 63;
                }

                return 6;
            }

            case >= Operation.CharacterA and <= Operation.CharacterE:
                output[0] = Value((CodeSet)(op - Operation.CharacterA), data[n]);
                return 1;
            case Operation.ShiftA:
                output[0] = ShiftCodeword;
                output[1] = Value(CodeSet.A, data[n]);
                return 2;
            case Operation.ShiftA2:
                output[0] = TwoShiftACodeword;
                output[1] = Value(CodeSet.A, data[n]);
                output[2] = Value(CodeSet.A, data[n + 1]);
                return 3;
            case Operation.ShiftA3:
                output[0] = ThreeShiftACodeword;
                output[1] = Value(CodeSet.A, data[n]);
                output[2] = Value(CodeSet.A, data[n + 1]);
                output[3] = Value(CodeSet.A, data[n + 2]);
                return 4;
            case Operation.ShiftB:
                output[0] = ShiftCodeword;
                output[1] = Value(CodeSet.B, data[n]);
                return 2;
            case Operation.ShiftC:
                output[0] = ShiftCCodeword;
                output[1] = Value(CodeSet.C, data[n]);
                return 2;
            case Operation.ShiftD:
                output[0] = ShiftDCodeword;
                output[1] = Value(CodeSet.D, data[n]);
                return 2;
            default:
                output[0] = ShiftECodeword;
                output[1] = Value(CodeSet.E, data[n]);
                return 2;
        }
    }

    /// <summary>
    /// Adds the check codewords: 10 after the 10 primary data codewords, and for the secondary message one block of its odd codewords and one of its even
    /// codewords, each with 20 check codewords in modes 2, 3, 4 and 6 and 28 in mode 5, interleaved. Every block uses the generator with roots alpha^1 to
    /// alpha^k.
    /// </summary>
    /// <param name="codewords">The 144 codewords, primary data at 0 and secondary data at 20.</param>
    /// <param name="secondaryLength">The number of secondary data codewords, 84 or 68.</param>
    private static void AddCheckCodewords(Span<int> codewords, int secondaryLength)
    {
        // A secondary block takes 20 or 28 check codewords, the most any code here takes.
        InlineArray32<int> generatorStorage = default;
        InlineArray32<int> checkStorage = default;
        Span<int> generator = generatorStorage;
        Field.Generator(1, generator[..PrimaryCheckCodewords]);
        Field.Remainder(codewords, 1, PrimaryDataCodewords, generator[..PrimaryCheckCodewords], codewords.Slice(PrimaryDataCodewords, PrimaryCheckCodewords));

        // The secondary message deals its codewords over two blocks, the odd and the even. The 84 codewords of standard error correction take 20 check
        // codewords a block, and the 68 of mode 5 take 28.
        int checkCount = secondaryLength == StandardSecondaryLength ? StandardSecondaryCheckCodewords : EnhancedSecondaryCheckCodewords;
        Field.Generator(1, generator[..checkCount]);
        Field.InterleavedRemainders(
            codewords.Slice(SecondaryStart, secondaryLength + (checkCount * 2)),
            secondaryLength,
            2,
            generator[..checkCount],
            ((Span<int>)checkStorage)[..checkCount]);
    }

    /// <summary>
    /// Returns the length of the latch into a code set from another.
    /// </summary>
    /// <param name="to">The code set latched into.</param>
    /// <param name="from">The code set latched from.</param>
    /// <returns>The number of codewords: 0 within a code set, 1 into A or B, 2 into C, D or E.</returns>
    private static int LatchLength(CodeSet to, CodeSet from) => to == from ? 0 : to <= CodeSet.B ? 1 : 2;

    /// <summary>
    /// Returns whether a code set holds a byte.
    /// </summary>
    /// <param name="set">The code set.</param>
    /// <param name="c">The byte.</param>
    /// <returns><see langword="true"/> when the code set holds it.</returns>
    private static bool Has(CodeSet set, int c) => SetValues[((int)set << 8) | c] != 0xFF;

    /// <summary>
    /// Returns the value of a byte in a code set.
    /// </summary>
    /// <param name="set">The code set.</param>
    /// <param name="c">The byte.</param>
    /// <returns>The value.</returns>
    private static int Value(CodeSet set, int c) => SetValues[((int)set << 8) | c];

    /// <summary>
    /// One step of the code set search: the operation that reaches a character in a code set, and the code set the operation starts from.
    /// </summary>
    private readonly struct Step
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Step"/> struct.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="prior">The code set before the operation.</param>
        public Step(Operation operation, CodeSet prior)
        {
            this.Operation = operation;
            this.Prior = prior;
        }

        /// <summary>
        /// Gets the operation.
        /// </summary>
        public Operation Operation { get; }

        /// <summary>
        /// Gets the code set before the operation, which a latch leaves when it differs from the code set of the step.
        /// </summary>
        public CodeSet Prior { get; }
    }
}
