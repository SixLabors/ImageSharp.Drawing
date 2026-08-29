// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.FontGenerator;

/// <summary>
/// The OCR-B glyph outlines, set against ECMA-11, the origin standard for OCR-B: each outline is
/// placed on the printed 4:1 character sheet of the standard and squared to the dimensions its
/// tables give. Coordinates are on the 0.0001 inch design grid shared with OCR-A: x = 275 at the
/// cell centre, baseline at zero. The vertical line, the pre-printed long vertical mark, the
/// continuous underline and the character erase are exact primitives, because the standard
/// dimensions those four in millimetres instead of drawing them. Entries whose stroke width is zero
/// are filled ink outlines; primitives are centerline strokes.
/// </summary>
internal static class OcrBGlyphs
{
    /// <summary>
    /// Design units per em before the em scale, equal to the 0.1 inch pitch.
    /// </summary>
    public const ushort UnitsPerEm = 1000;

    /// <summary>
    /// The nominal centerline height of digit eight, the tallest character: 0.094 inch.
    /// </summary>
    public const float H = 940;

    /// <summary>
    /// The nominal centerline width of digit zero, the widest character: 0.055 inch.
    /// </summary>
    public const float W = 550;

    /// <summary>
    /// The nominal stroke width for digits, capitals and most symbols: 0.014 inch.
    /// </summary>
    public const float T = 140;

    /// <summary>
    /// The nominal stroke width for all small letters plus #, % and @: 0.012 inch.
    /// </summary>
    public const float TSmall = 120;

    /// <summary>
    /// The descender centerline depth. ECMA-11 section 4.1 gives 0,60 mm below the baseline as
    /// the typical descender depth.
    /// </summary>
    public const float D = -236;

    /// <summary>
    /// One grid square of the reference drawings in design units: 0.02 mm.
    /// </summary>
    public const float Square = 7.874f;

    /// <summary>
    /// Design units per millimetre. The design grid is the 0,0001 inch grid.
    /// </summary>
    public const float PerMillimetre = 10000F / 25.4F;

    /// <summary>
    /// Dimension C of ECMA-11 section 4.1: a small letter stands 1,83 mm above the baseline.
    /// </summary>
    public const float SmallLetterHeight = 1.83F * PerMillimetre;

    /// <summary>
    /// Dimension B of ECMA-11 section 4.1: a capital letter and a digit both stand 2,46 mm above the
    /// baseline. The standard gives the two one dimension.
    /// </summary>
    public const float CapitalHeight = 2.46F * PerMillimetre;

    /// <summary>
    /// Dimension A of ECMA-11 section 4.1: an ascender stands 2,60 mm above the baseline.
    /// </summary>
    public const float AscenderHeight = 2.60F * PerMillimetre;

    /// <summary>
    /// Dimension D of ECMA-11 section 4.1: a descender hangs 0,60 mm below the baseline.
    /// </summary>
    public const float DescenderDepth = 0.60F * PerMillimetre;

    /// <summary>
    /// Gets the glyph outlines and the dimensional primitives keyed by character, in the
    /// stroke token format that <c>BuildStroke</c> in Program.cs documents.
    /// </summary>
    public static IReadOnlyDictionary<char, float[][]> Skeletons { get; } = new Dictionary<char, float[][]>
    {
        ['!'] =
        [
            [
                188.8f, 1237.5f, 213.2f, 462.4f, 360.6f, 462.4f, 389.2f, 1237.5f, 188.8f, 1237.5f,
            ],
            [
                174.8f, 197.5f, 174.8f, 7, 375.2f, 7, 375.2f, 197.5f, 174.8f, 197.5f,
            ],
        ],

        ['"'] =
        [
            [
                100.3f, 745.6f, 156.2f, 1212.3f, -38.6f, 1212.3f, -38.6f, 745.6f, 100.3f, 745.6f,
            ],
            [
                534.4f, 745.6f, 588.6f, 1212.3f, 393.8f, 1212.3f, 393.8f, 745.6f, 534.4f, 745.6f,
            ],
        ],

        ['#'] =
        [
            [
                218.7f, 831.5f, 288.3f, 1168.5f, 171.3f, 1168.5f, 103.1f, 831.5f, -12.5f, 831.5f,
                -42.1f, 719.1f, 82.4f, 719.1f, 23.1f, 436.6f, -42.1f, 436.6f, -70.2f, 324.2f,
                -0.6f, 324.2f, -70.2f, 0, 51.3f, 0, 120.9f, 324.2f, 329.8f, 324.2f,
                264.6f, 0, 387.6f, 0, 451.3f, 324.2f, 562.5f, 324.2f, 590.6f, 436.6f,
                470.6f, 436.6f, 529.9f, 719.1f, 590.6f, 719.1f, 620.2f, 831.5f, 552.1f, 831.5f,
                620.2f, 1168.5f, 498.7f, 1168.5f, 430.6f, 831.5f, 218.7f, 831.5f,
            ],
            [
                140.2f, 437.5f, 197.9f, 711.5f, 408.4f, 711.5f, 349.1f, 437.5f, 140.2f, 437.5f,
            ],
        ],

        ['$'] =
        [
            [
                213, 691, float.NaN, float.NaN, float.NaN, float.NaN, 143.2f, 717.1f, 56.3f, 730.1f,
                56.3f, 829.4f, float.NaN, float.NaN, float.NaN, float.NaN, 56.3f, 923.9f, 141.6f, 938.5f,
                213, 938.5f, 213, 691,
            ],
            [
                338.6f, 490.7f, float.NaN, float.NaN, float.NaN, float.NaN, 406.8f, 466.3f, 493.7f, 440.3f,
                493.7f, 347.5f, float.NaN, float.NaN, float.NaN, float.NaN, 493.7f, 254.7f, 413, 228.6f,
                338.6f, 222.1f, 338.6f, 490.7f,
            ],
            [
                336.7f, 1150.8f, 214.8f, 1150.8f, 214.8f, 1073.4f, float.NaN, float.NaN, float.NaN, float.NaN,
                64.2f, 1060.7f, -92.4f, 1003.8f, -92.4f, 817.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                -92.4f, 641.8f, 82.3f, 578.6f, 214.8f, 535.9f, 214.8f, 229.2f, 193.7f, 229.2f,
                float.NaN, float.NaN, float.NaN, float.NaN, 124.4f, 229.2f, 59.7f, 251.3f, 62.7f, 336.7f,
                -92.4f, 336.7f, -92.4f, 306.7f, float.NaN, float.NaN, float.NaN, float.NaN, -92.4f, 161.2f,
                65.7f, 80.6f, 186.2f, 80.6f, 214.8f, 80.6f, 214.8f, 0, 336.7f, 0,
                336.7f, 80.6f, float.NaN, float.NaN, float.NaN, float.NaN, 481.3f, 93.3f, 642.4f, 188.1f,
                642.4f, 358.8f, float.NaN, float.NaN, float.NaN, float.NaN, 642.4f, 540.6f, 476.7f, 602.3f,
                336.7f, 651.3f, 336.7f, 924.8f, float.NaN, float.NaN, float.NaN, float.NaN, 413.5f, 924.8f,
                463.2f, 913.7f, 458.7f, 817.3f, 613.7f, 817.3f, 613.7f, 844.1f, float.NaN, float.NaN,
                float.NaN, float.NaN, 613.7f, 988, 449.6f, 1062.3f, 336.7f, 1073.4f, 336.7f, 1150.8f,
            ],
        ],

        ['%'] =
        [
            [
                97.4f, 1124.8f, float.NaN, float.NaN, float.NaN, float.NaN, -0.9f, 1124.8f, -70.5f, 1046.9f,
                -70.5f, 938.5f, float.NaN, float.NaN, float.NaN, float.NaN, -70.5f, 839.2f, -3.9f, 756.7f,
                97.4f, 756.7f, float.NaN, float.NaN, float.NaN, float.NaN, 198.7f, 756.7f, 265.2f, 839.2f,
                265.2f, 938.5f, float.NaN, float.NaN, float.NaN, float.NaN, 265.2f, 1046.9f, 195.6f, 1124.8f,
                97.4f, 1124.8f,
            ],
            [
                95.6f, 1036.2f, float.NaN, float.NaN, float.NaN, float.NaN, 157.2f, 1036.2f, 157.2f, 988.9f,
                157.2f, 938.5f, float.NaN, float.NaN, float.NaN, float.NaN, 157.2f, 897.2f, 157.2f, 845.3f,
                95.6f, 845.3f, float.NaN, float.NaN, float.NaN, float.NaN, 34, 845.3f, 34, 897.2f,
                34, 938.5f, float.NaN, float.NaN, float.NaN, float.NaN, 34, 988.9f, 34, 1036.2f,
                95.6f, 1036.2f,
            ],
            [
                460.7f, 365, float.NaN, float.NaN, float.NaN, float.NaN, 360.9f, 365, 292.8f, 284.8f,
                292.8f, 173.1f, float.NaN, float.NaN, float.NaN, float.NaN, 292.8f, 70.8f, 359.3f, -14.2f,
                460.7f, -14.2f, float.NaN, float.NaN, float.NaN, float.NaN, 562, -14.2f, 628.5f, 70.8f,
                628.5f, 173.1f, float.NaN, float.NaN, float.NaN, float.NaN, 628.5f, 284.8f, 558.9f, 365,
                460.7f, 365,
            ],
            [
                455.4f, 271.8f, float.NaN, float.NaN, float.NaN, float.NaN, 517, 271.8f, 517, 225.9f,
                517, 177, float.NaN, float.NaN, float.NaN, float.NaN, 517, 137, 517, 86.6f,
                455.4f, 86.6f, float.NaN, float.NaN, float.NaN, float.NaN, 393.8f, 86.6f, 393.8f, 137,
                393.8f, 177, float.NaN, float.NaN, float.NaN, float.NaN, 393.8f, 225.9f, 393.8f, 271.8f,
                455.4f, 271.8f,
            ],
            [
                550, 1145.3f, -60.5f, 85, -60.5f, 0, 23.6f, 0, 626.3f, 1054.1f,
                626.3f, 1145.3f, 550, 1145.3f,
            ],
        ],

        ['&'] =
        [
            [
                159.7f, 698, float.NaN, float.NaN, float.NaN, float.NaN, 125.4f, 747.2f, 78.3f, 820.3f,
                78.3f, 883.8f, float.NaN, float.NaN, float.NaN, float.NaN, 78.3f, 972.8f, 134, 1034.7f,
                212.5f, 1034.7f, float.NaN, float.NaN, float.NaN, float.NaN, 279.6f, 1034.7f, 345.2f, 1001.4f,
                345.2f, 917.2f, float.NaN, float.NaN, float.NaN, float.NaN, 345.2f, 829.8f, 209.7f, 747.2f,
                159.7f, 698,
            ],
            [
                355.6f, 156.2f, float.NaN, float.NaN, float.NaN, float.NaN, 323.7f, 134.9f, 285.9f, 125.1f,
                249.5f, 125.1f, float.NaN, float.NaN, float.NaN, float.NaN, 143.4f, 125.1f, 51, 195.4f,
                51, 318.1f, float.NaN, float.NaN, float.NaN, float.NaN, 51, 381.9f, 94.9f, 445.7f,
                138.9f, 485, 355.6f, 156.2f,
            ],
            [
                454.9f, 0, 638.9f, 0, 532.9f, 155.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                590.3f, 238.3f, 625.6f, 401.9f, 625.6f, 500.4f, 625.6f, 527.4f, 474.1f, 527.4f,
                474.1f, 490.8f, float.NaN, float.NaN, float.NaN, float.NaN, 474.1f, 420.9f, 465.2f, 349.5f,
                446.1f, 282.7f, 244.5f, 581.4f, 331.3f, 651.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                415.2f, 718, 513.8f, 813.3f, 513.8f, 934, float.NaN, float.NaN, float.NaN, float.NaN,
                513.8f, 1092.8f, 365.2f, 1177, 235.7f, 1177, float.NaN, float.NaN, float.NaN, float.NaN,
                94.4f, 1177, -64.5f, 1086.5f, -64.5f, 913.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                -64.5f, 806.9f, 4.7f, 691, 59.1f, 608.4f, float.NaN, float.NaN, float.NaN, float.NaN,
                -32.1f, 543.2f, -101.2f, 428.9f, -101.2f, 306.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                -101.2f, 101.7f, 42.9f, -14.3f, 222.5f, -14.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                294.6f, -14.3f, 360.8f, -1.6f, 424, 42.9f, 454.9f, 0,
            ],
        ],

        ['\''] =
        [
            [
                198.2f, 628.7f, 351.8f, 628.7f, 383.5f, 1177.6f, 166.6f, 1177.6f, 198.2f, 628.7f,
            ],
        ],

        ['('] =
        [
            [
                362.9f, 1161.8f, float.NaN, float.NaN, float.NaN, float.NaN, 189, 1047.4f, 54.5f, 801.2f,
                54.5f, 580.1f, float.NaN, float.NaN, float.NaN, float.NaN, 54.5f, 360.6f, 189, 112.9f,
                362.9f, 0, 475.5f, 0, 475.5f, 112.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                319.1f, 153.7f, 205.1f, 421.8f, 205.1f, 580.1f, float.NaN, float.NaN, float.NaN, float.NaN,
                205.1f, 738.5f, 319.1f, 1006.6f, 475.5f, 1048.9f, 475.5f, 1161.8f, 362.9f, 1161.8f,
            ],
        ],

        [')'] =
        [
            [
                70.5f, 1161.8f, 70.5f, 1048.9f, float.NaN, float.NaN, float.NaN, float.NaN, 224, 1006.6f,
                338.4f, 738.5f, 338.4f, 580.1f, float.NaN, float.NaN, float.NaN, float.NaN, 338.4f, 421.8f,
                224, 153.7f, 70.5f, 112.9f, 70.5f, 0, 182, 0, float.NaN, float.NaN,
                float.NaN, float.NaN, 354.3f, 112.9f, 487.5f, 360.6f, 487.5f, 580.1f, float.NaN, float.NaN,
                float.NaN, float.NaN, 487.5f, 801.2f, 354.3f, 1047.4f, 182, 1161.8f, 70.5f, 1161.8f,
            ],
        ],

        ['*'] =
        [
            [
                329.8f, 590.9f, 351.2f, 886.3f, 195.8f, 886.3f, 209.5f, 590.9f, -35.7f, 700.9f,
                -98.2f, 543.7f, 165.3f, 457.3f, -6.8f, 232.6f, 116.6f, 139.9f, 276.5f, 381.9f,
                425.8f, 139.9f, 564.4f, 232.6f, 380.1f, 457.3f, 648.2f, 534.3f, 581.2f, 700.9f,
                329.8f, 590.9f,
            ],
        ],

        ['+'] =
        [
            [
                204.9f, 634.7f, -63.3f, 634.7f, -63.3f, 467.5f, 204.9f, 467.5f, 204.9f, 123.4f,
                343.7f, 123.4f, 343.7f, 467.5f, 613.3f, 467.5f, 613.3f, 634.7f, 343.7f, 634.7f,
                343.7f, 936.6f, 204.9f, 936.6f, 204.9f, 634.7f,
            ],
        ],

        [','] =
        [
            [
                297.8f, 276.4f, 3.1f, -270.2f, 169.9f, -270.2f, 560.9f, 157.7f, 560.9f, 276.4f,
                297.8f, 276.4f,
            ],
        ],

        ['-'] =
        [
            [
                -67.7f, 544.6f, 617.7f, 544.6f, 617.7f, 739.3f, -67.7f, 739.3f, -67.7f, 544.6f,
            ],
        ],

        ['.'] =
        [
            [
                119.4f, 269.3f, 119.4f, 0, 430.7f, 0, 430.7f, 269.3f, 119.4f, 269.3f,
            ],
        ],

        ['/'] =
        [
            [
                423.3f, 1195.7f, -24.5f, 0, 128.3f, 0, 580.5f, 1195.7f, 423.3f, 1195.7f,
            ],
        ],

        ['0'] =
        [
            [
                279, 1137, float.NaN, float.NaN, float.NaN, float.NaN, 130.9f, 1137, 11.5f, 1120.1f,
                -39.9f, 968.2f, float.NaN, float.NaN, float.NaN, float.NaN, -80.7f, 847, -88.3f, 704.3f,
                -88.3f, 576.9f, float.NaN, float.NaN, float.NaN, float.NaN, -88.3f, 440.4f, -80.7f, 151.9f,
                19, 53.7f, float.NaN, float.NaN, float.NaN, float.NaN, 75, -1.5f, 191.3f, -13.8f,
                279, -13.8f, float.NaN, float.NaN, float.NaN, float.NaN, 365.2f, -13.8f, 483, -1.5f,
                537.5f, 53.7f, float.NaN, float.NaN, float.NaN, float.NaN, 638.7f, 151.9f, 646.3f, 440.4f,
                646.3f, 576.9f, float.NaN, float.NaN, float.NaN, float.NaN, 646.3f, 704.3f, 638.7f, 847,
                597.9f, 968.2f, float.NaN, float.NaN, float.NaN, float.NaN, 546.5f, 1120.1f, 425.6f, 1137,
                279, 1137,
            ],
            [
                279, 1008.6f, float.NaN, float.NaN, float.NaN, float.NaN, 399.9f, 1008.6f, 454.3f, 1003.8f,
                474, 882.1f, float.NaN, float.NaN, float.NaN, float.NaN, 487.6f, 795.2f, 490.6f, 653,
                490.6f, 562.9f, float.NaN, float.NaN, float.NaN, float.NaN, 490.6f, 479.1f, 486.1f, 223.1f,
                443.7f, 163, float.NaN, float.NaN, float.NaN, float.NaN, 415, 121.9f, 330.4f, 120.3f,
                279, 120.3f, float.NaN, float.NaN, float.NaN, float.NaN, 227.6f, 120.3f, 143, 121.9f,
                114.3f, 163, float.NaN, float.NaN, float.NaN, float.NaN, 70.4f, 223.1f, 67.4f, 479.1f,
                67.4f, 562.9f, float.NaN, float.NaN, float.NaN, float.NaN, 67.4f, 653, 68.9f, 795.2f,
                84, 882.1f, float.NaN, float.NaN, float.NaN, float.NaN, 103.7f, 1003.8f, 158.1f, 1008.6f,
                279, 1008.6f,
            ],
        ],

        ['1'] =
        [
            [
                323.7f, 956.3f, 323.7f, 0, 470, 0, 470, 1137.6f, 312.3f, 1137.6f,
                60.9f, 906.3f, 60.9f, 706.3f, 323.7f, 956.3f,
            ],
        ],

        ['2'] =
        [
            [
                95.9f, 146.6f, 95.9f, 170, float.NaN, float.NaN, float.NaN, float.NaN, 95.9f, 301,
                212.4f, 393.1f, 308.1f, 460.1f, float.NaN, float.NaN, float.NaN, float.NaN, 452.5f, 558.4f,
                594, 637.9f, 594, 843.8f, float.NaN, float.NaN, float.NaN, float.NaN, 594, 1052.8f,
                437.8f, 1155.8f, 253.6f, 1155.8f, float.NaN, float.NaN, float.NaN, float.NaN, 156.4f, 1155.8f,
                42.9f, 1124.6f, -36.7f, 1060.6f, -36.7f, 887.5f, float.NaN, float.NaN, float.NaN, float.NaN,
                45.8f, 951.4f, 172.6f, 1009.2f, 277.2f, 1009.2f, float.NaN, float.NaN, float.NaN, float.NaN,
                362.7f, 1009.2f, 442.2f, 929.6f, 442.2f, 837.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                442.2f, 725.3f, 322.9f, 653.5f, 243.3f, 600.5f, float.NaN, float.NaN, float.NaN, float.NaN,
                37, 461.7f, -55.8f, 354.1f, -55.8f, 85.8f, -55.8f, 0, 580.7f, 0,
                580.7f, 146.6f, 95.9f, 146.6f,
            ],
        ],

        ['3'] =
        [
            [
                580.7f, 1141.7f, -65.2f, 1141.7f, -65.2f, 994.3f, 388.4f, 994.3f, 113.9f, 694.7f,
                113.9f, 570.8f, 231.3f, 570.8f, float.NaN, float.NaN, float.NaN, float.NaN, 360.5f, 570.8f,
                472.1f, 512.8f, 472.1f, 360.7f, float.NaN, float.NaN, float.NaN, float.NaN, 472.1f, 196,
                342.9f, 133.3f, 207.8f, 133.3f, float.NaN, float.NaN, float.NaN, float.NaN, 110.9f, 133.3f,
                18.5f, 156.8f, -65.2f, 214.8f, -65.2f, 53.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                24.3f, 12.5f, 112.4f, -14.1f, 210.8f, -14.1f, float.NaN, float.NaN, float.NaN, float.NaN,
                425.1f, -14.1f, 623.3f, 101.9f, 623.3f, 356, float.NaN, float.NaN, float.NaN, float.NaN,
                623.3f, 558.3f, 495.5f, 685.3f, 312.1f, 694.7f, 580.7f, 981.7f, 580.7f, 1141.7f,
            ],
        ],

        ['4'] =
        [
            [
                481, 387.5f, 481, 616.3f, 327.6f, 616.3f, 327.6f, 387.5f, 65.4f, 387.5f,
                393.1f, 1110.6f, 235.2f, 1110.6f, -83.5f, 408.8f, -83.5f, 244.1f, 327.6f, 244.1f,
                327.6f, 0, 481, 0, 481, 244.1f, 613.5f, 244.1f, 613.5f, 387.5f,
                481, 387.5f,
            ],
        ],

        ['5'] =
        [
            [
                137.2f, 994.3f, 552.9f, 994.3f, 552.9f, 1141.7f, 0.2f, 1141.7f, -33, 611.6f,
                153.8f, 625.7f, float.NaN, float.NaN, float.NaN, float.NaN, 242.6f, 632, 427.9f, 611.6f,
                427.9f, 409.3f, float.NaN, float.NaN, float.NaN, float.NaN, 427.9f, 192.9f, 232.1f, 133.3f,
                55.9f, 133.3f, -33, 133.3f, -33, -14.1f, 18.2f, -14.1f, float.NaN, float.NaN,
                float.NaN, float.NaN, 153.8f, -14.1f, 583, 22, 583, 407.7f, float.NaN, float.NaN,
                float.NaN, float.NaN, 583, 671.2f, 381.2f, 773.1f, 155.3f, 773.1f, 119.1f, 773.1f,
                137.2f, 994.3f,
            ],
        ],

        ['6'] =
        [
            [
                337, 1150.6f, 60.4f, 817.1f, float.NaN, float.NaN, float.NaN, float.NaN, -47, 687.5f,
                -93.8f, 551.6f, -93.8f, 377.7f, float.NaN, float.NaN, float.NaN, float.NaN, -93.8f, 132.8f,
                40.7f, -14.2f, 276.5f, -14.2f, float.NaN, float.NaN, float.NaN, float.NaN, 488.1f, -14.2f,
                643.8f, 134.3f, 643.8f, 361.9f, float.NaN, float.NaN, float.NaN, float.NaN, 643.8f, 562.6f,
                506.3f, 739.7f, 306.7f, 739.7f, float.NaN, float.NaN, float.NaN, float.NaN, 256.9f, 739.7f,
                211.5f, 730.2f, 167.7f, 709.6f, 166.2f, 712.8f, 238.7f, 812.4f, float.NaN, float.NaN,
                float.NaN, float.NaN, 323.4f, 926.2f, 427.7f, 1049.4f, 532, 1150.6f, 337, 1150.6f,
            ],
            [
                272, 594.8f, float.NaN, float.NaN, float.NaN, float.NaN, 397.4f, 594.8f, 488.1f, 500.4f,
                488.1f, 362, float.NaN, float.NaN, float.NaN, float.NaN, 488.1f, 218.8f, 402, 124.4f,
                275, 124.4f, float.NaN, float.NaN, float.NaN, float.NaN, 145, 124.4f, 61.9f, 223.7f,
                61.9f, 363.7f, float.NaN, float.NaN, float.NaN, float.NaN, 61.9f, 495.5f, 149.5f, 594.8f,
                272, 594.8f,
            ],
        ],

        ['7'] =
        [
            [
                452.6f, 998.5f, float.NaN, float.NaN, float.NaN, float.NaN, 448.1f, 957.5f, 419.4f, 922.9f,
                392.3f, 891.4f, 337.9f, 825.3f, float.NaN, float.NaN, float.NaN, float.NaN, 184, 636.3f,
                72.3f, 437.8f, 72.3f, 184.3f, 72.3f, 0, 227.8f, 0, 227.8f, 184.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, 227.8f, 453.6f, 336.4f, 590.6f, 496.4f, 787.5f,
                float.NaN, float.NaN, float.NaN, float.NaN, 529.6f, 830, 586.9f, 902.4f, 609.6f, 951.2f,
                float.NaN, float.NaN, float.NaN, float.NaN, 636.7f, 1011.1f, 638.3f, 1061.5f, 638.3f, 1127.6f,
                638.3f, 1146.5f, -84.6f, 1146.5f, -84.6f, 998.5f, 452.6f, 998.5f,
            ],
        ],

        ['8'] =
        [
            [
                271, 121.9f, float.NaN, float.NaN, float.NaN, float.NaN, 164.4f, 121.9f, 54.8f, 176.3f,
                54.8f, 309, float.NaN, float.NaN, float.NaN, float.NaN, 54.8f, 435.4f, 159.8f, 492.9f,
                271, 555.3f, float.NaN, float.NaN, float.NaN, float.NaN, 380.7f, 492.9f, 487.2f, 435.4f,
                487.2f, 309, float.NaN, float.NaN, float.NaN, float.NaN, 487.2f, 176.3f, 377.6f, 121.9f,
                271, 121.9f,
            ],
            [
                271, 1150.5f, float.NaN, float.NaN, float.NaN, float.NaN, 118.1f, 1150.5f, -51.4f, 1080.6f,
                -51.4f, 900.5f, float.NaN, float.NaN, float.NaN, float.NaN, -51.4f, 770.1f, 40.1f, 695.6f,
                134.5f, 631.9f, float.NaN, float.NaN, float.NaN, float.NaN, -0.4f, 563.6f, -93.4f, 467.3f,
                -93.4f, 299.7f, float.NaN, float.NaN, float.NaN, float.NaN, -93.4f, 69.9f, 71.6f, -14,
                271, -14, float.NaN, float.NaN, float.NaN, float.NaN, 468.9f, -14, 635.4f, 69.9f,
                635.4f, 299.7f, float.NaN, float.NaN, float.NaN, float.NaN, 635.4f, 467.3f, 542.4f, 563.6f,
                406, 631.9f, float.NaN, float.NaN, float.NaN, float.NaN, 501.9f, 695.6f, 593.4f, 770.1f,
                593.4f, 900.5f, float.NaN, float.NaN, float.NaN, float.NaN, 593.4f, 1080.6f, 423.9f, 1150.5f,
                271, 1150.5f,
            ],
            [
                264, 1004.5f, float.NaN, float.NaN, float.NaN, float.NaN, 339.7f, 1004.5f, 437, 981.2f,
                437, 886.5f, float.NaN, float.NaN, float.NaN, float.NaN, 437, 798, 333.5f, 737.5f,
                264, 709.5f, float.NaN, float.NaN, float.NaN, float.NaN, 194.5f, 737.5f, 91, 798,
                91, 886.5f, float.NaN, float.NaN, float.NaN, float.NaN, 91, 981.2f, 188.3f, 1004.5f,
                264, 1004.5f,
            ],
        ],

        ['9'] =
        [
            [
                214.2f, 0, float.NaN, float.NaN, float.NaN, float.NaN, 401.9f, 205.8f, 643.7f, 477.2f,
                643.7f, 754.7f, float.NaN, float.NaN, float.NaN, float.NaN, 643.7f, 993.3f, 510.1f, 1155.5f,
                278.8f, 1155.5f, float.NaN, float.NaN, float.NaN, float.NaN, 56.5f, 1155.5f, -93.7f, 1005.8f,
                -93.7f, 775, float.NaN, float.NaN, float.NaN, float.NaN, -93.7f, 569.2f, 50.5f, 410.1f,
                248.7f, 410.1f, float.NaN, float.NaN, float.NaN, float.NaN, 293.8f, 410.1f, 340.3f, 421,
                376.4f, 439.7f, 379.4f, 436.6f, 380.9f, 438.2f, float.NaN, float.NaN, float.NaN, float.NaN,
                278.8f, 276, 143.6f, 135.7f, 8.4f, 0, 214.2f, 0,
            ],
            [
                278.8f, 1008.9f, float.NaN, float.NaN, float.NaN, float.NaN, 406.4f, 1008.9f, 489, 916.9f,
                489, 782.8f, float.NaN, float.NaN, float.NaN, float.NaN, 489, 651.8f, 403.4f, 556.7f,
                278.8f, 556.7f, float.NaN, float.NaN, float.NaN, float.NaN, 151.1f, 556.7f, 61, 647.1f,
                61, 779.7f, float.NaN, float.NaN, float.NaN, float.NaN, 61, 924.7f, 140.6f, 1008.9f,
                278.8f, 1008.9f,
            ],
        ],

        [':'] =
        [
            [
                135, 818, 135, 569.9f, 421.1f, 569.9f, 421.1f, 818, 135, 818,
            ],
            [
                123.5f, 248.2f, 123.5f, 0, 418.5f, 0, 418.5f, 248.2f, 123.5f, 248.2f,
            ],
        ],

        [';'] =
        [
            [
                231.3f, 238, -5.2f, -272.7f, 180.6f, -272.7f, 546.2f, 238, 231.3f, 238,
            ],
            [
                243.9f, 789.6f, 243.9f, 550.1f, 508.6f, 550.1f, 508.6f, 789.6f, 243.9f, 789.6f,
            ],
        ],

        ['<'] =
        [
            [
                626.9f, 313, 120.2f, 645.1f, 626.9f, 977.1f, 626.9f, 1174.3f, -104.9f, 664.1f,
                -104.9f, 620.9f, 626.9f, 112.4f, 626.9f, 313,
            ],
        ],

        ['='] =
        [
            [
                -59, 297.1f, 609, 297.1f, 609, 428.6f, -59, 428.6f, -59, 297.1f,
            ],
            [
                -59, 642, 609, 642, 609, 777.6f, -59, 777.6f, -59, 642,
            ],
        ],

        ['>'] =
        [
            [
                653, 661.5f, -67, 1169.7f, -67, 973.3f, 431.6f, 642.6f, -67, 311.8f,
                -67, 112, 653, 618.5f, 653, 661.5f,
            ],
        ],

        ['?'] =
        [
            [
                385.1f, 396.1f, 385.1f, 425.2f, float.NaN, float.NaN, float.NaN, float.NaN, 385.1f, 478.6f,
                397.3f, 509.3f, 432.5f, 541.6f, float.NaN, float.NaN, float.NaN, float.NaN, 521.2f, 628.9f,
                576.2f, 701.7f, 576.2f, 835.9f, float.NaN, float.NaN, float.NaN, float.NaN, 576.2f, 1010.5f,
                446.2f, 1110.7f, 290.3f, 1110.7f, float.NaN, float.NaN, float.NaN, float.NaN, 84, 1110.7f,
                -18.5f, 1004, -18.5f, 795.4f, -18.5f, 756.6f, 139, 756.6f, 139, 777.7f,
                float.NaN, float.NaN, float.NaN, float.NaN, 139, 881.1f, 178.7f, 958.7f, 287.3f, 958.7f,
                float.NaN, float.NaN, float.NaN, float.NaN, 371.3f, 958.7f, 418.7f, 897.3f, 418.7f, 813.2f,
                float.NaN, float.NaN, float.NaN, float.NaN, 418.7f, 738.9f, 389.7f, 701.7f, 342.3f, 654.8f,
                float.NaN, float.NaN, float.NaN, float.NaN, 314.8f, 630.5f, 259.8f, 573.9f, 244.5f, 541.6f,
                float.NaN, float.NaN, float.NaN, float.NaN, 227.6f, 507.7f, 229.2f, 472.1f, 229.2f, 433.3f,
                229.2f, 396.1f, 385.1f, 396.1f,
            ],
            [
                193.7f, 203.9f, 193.7f, 0, 404.2f, 0, 404.2f, 203.9f, 193.7f, 203.9f,
            ],
        ],

        ['@'] =
        [
            [
                366.5f, 613.4f, 247.5f, 613.4f, 247.5f, 570.6f, 226.7f, 589.6f, float.NaN, float.NaN,
                float.NaN, float.NaN, 198.4f, 613.4f, 165.7f, 621.3f, 139, 621.3f, float.NaN, float.NaN,
                float.NaN, float.NaN, -42.5f, 621.3f, -70.7f, 467.6f, -70.7f, 378.8f, -70.7f, 220.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, -70.7f, 80.8f, 2.1f, -14.3f, 139, -14.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, 195.5f, -14.3f, 250.5f, 7.9f, 286.2f, 58.6f,
                292.1f, 69.7f, 305.5f, 46, float.NaN, float.NaN, float.NaN, float.NaN, 332.3f, -3.2f,
                373.9f, -14.3f, 426, -14.3f, float.NaN, float.NaN, float.NaN, float.NaN, 595.5f, -14.3f,
                620.8f, 144.2f, 620.8f, 291.6f, 620.8f, 599.1f, float.NaN, float.NaN, float.NaN, float.NaN,
                620.8f, 874.9f, 525.6f, 1074.6f, 266.8f, 1074.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                72, 1074.6f, -70.7f, 987.4f, -70.7f, 779.8f, -70.7f, 765.5f, 48.2f, 765.5f,
                48.2f, 794.1f, float.NaN, float.NaN, float.NaN, float.NaN, 48.2f, 908.2f, 158.3f, 952.5f,
                266.8f, 952.5f, float.NaN, float.NaN, float.NaN, float.NaN, 436.4f, 952.5f, 501.8f, 828.9f,
                501.8f, 648.2f, 501.8f, 309.1f, float.NaN, float.NaN, float.NaN, float.NaN, 501.8f, 191.8f,
                489.9f, 107.8f, 426, 107.8f, float.NaN, float.NaN, float.NaN, float.NaN, 370.9f, 107.8f,
                366.5f, 164.8f, 366.5f, 204.5f, 366.5f, 613.4f,
            ],
            [
                55.1f, 376.2f, float.NaN, float.NaN, float.NaN, float.NaN, 55.1f, 444.7f, 83.9f, 500.2f,
                143.1f, 500.2f, float.NaN, float.NaN, float.NaN, float.NaN, 219.5f, 500.2f, 248.4f, 444.7f,
                248.4f, 376.2f, 248.4f, 212.9f, float.NaN, float.NaN, float.NaN, float.NaN, 248.4f, 146,
                219.5f, 97, 143.1f, 97, float.NaN, float.NaN, float.NaN, float.NaN, 82.5f, 97,
                55.1f, 146, 55.1f, 212.9f, 55.1f, 376.2f,
            ],
        ],

        ['A'] =
        [
            [
                386.4f, 451.4f, 163.5f, 451.4f, 274.2f, 903.5f, 277.2f, 903.5f, 386.4f, 451.4f,
            ],
            [
                420.8f, 309.1f, 494.1f, 0, 649.7f, 0, 373, 1074.9f, 177, 1074.9f,
                -99.7f, 0, 55.8f, 0, 127.6f, 309.1f, 420.8f, 309.1f,
            ],
        ],

        ['B'] =
        [
            [
                56.4f, 910.4f, 243.7f, 910.4f, float.NaN, float.NaN, float.NaN, float.NaN, 343.5f, 910.4f,
                463.2f, 893.3f, 463.2f, 762.3f, float.NaN, float.NaN, float.NaN, float.NaN, 463.2f, 634.5f,
                363.4f, 623.6f, 265.2f, 623.6f, 56.4f, 623.6f, 56.4f, 910.4f,
            ],
            [
                56.4f, 476.3f, 262.1f, 476.3f, float.NaN, float.NaN, float.NaN, float.NaN, 383.4f, 476.3f,
                487.8f, 449.1f, 487.8f, 305.5f, float.NaN, float.NaN, float.NaN, float.NaN, 487.8f, 169.4f,
                383.4f, 142.1f, 269.8f, 142.1f, 56.4f, 142.1f, 56.4f, 476.3f,
            ],
            [
                304.7f, 0, float.NaN, float.NaN, float.NaN, float.NaN, 538.7f, 0, 641.6f, 154.3f,
                641.6f, 314.9f, float.NaN, float.NaN, float.NaN, float.NaN, 641.6f, 417.8f, 589.4f, 528.5f,
                488, 551.9f, 488, 555, float.NaN, float.NaN, float.NaN, float.NaN, 570, 584.6f,
                617.7f, 665.7f, 617.7f, 754.5f, float.NaN, float.NaN, float.NaN, float.NaN, 617.7f, 988.4f,
                431.4f, 1057, 242.1f, 1057, -84.3f, 1057, -84.3f, 0, 304.7f, 0,
            ],
        ],

        ['C'] =
        [
            [
                572, 837.6f, float.NaN, float.NaN, float.NaN, float.NaN, 572, 992.4f, 446.1f, 1096.1f,
                307, 1096.1f, float.NaN, float.NaN, float.NaN, float.NaN, 42, 1096.1f, -30.4f, 786.5f,
                -30.4f, 537.7f, float.NaN, float.NaN, float.NaN, float.NaN, -30.4f, 301.5f, 42, -14.4f,
                307, -14.4f, float.NaN, float.NaN, float.NaN, float.NaN, 446.1f, -14.4f, 572, 92.5f,
                572, 253.7f, 422.9f, 253.7f, float.NaN, float.NaN, float.NaN, float.NaN, 418.5f, 183.5f,
                363.5f, 135.6f, 301.2f, 135.6f, float.NaN, float.NaN, float.NaN, float.NaN, 140.5f, 135.6f,
                118.8f, 416.4f, 118.8f, 545.6f, float.NaN, float.NaN, float.NaN, float.NaN, 118.8f, 673.3f,
                143.4f, 946.1f, 301.2f, 946.1f, float.NaN, float.NaN, float.NaN, float.NaN, 363.5f, 946.1f,
                418.5f, 903, 422.9f, 837.6f, 572, 837.6f,
            ],
        ],

        ['D'] =
        [
            [
                -68.6f, 0, 51.8f, 0, float.NaN, float.NaN, float.NaN, float.NaN, 361.3f, 0,
                586.4f, 186.4f, 586.4f, 509, float.NaN, float.NaN, float.NaN, float.NaN, 586.4f, 830.1f,
                347.2f, 1061.9f, 31.4f, 1061.9f, -68.6f, 1061.9f, -68.6f, 0,
            ],
            [
                103.6f, 914.6f, float.NaN, float.NaN, float.NaN, float.NaN, 300.9f, 905.3f, 426.9f, 703.2f,
                426.9f, 512.1f, float.NaN, float.NaN, float.NaN, float.NaN, 426.9f, 311.7f, 310, 142.5f,
                103.6f, 147.2f, 103.6f, 914.6f,
            ],
        ],

        ['E'] =
        [
            [
                98.9f, 925.5f, 563.6f, 925.5f, 563.6f, 1074.5f, -59.1f, 1074.5f, -59.1f, 0,
                582, 0, 582, 149, 98.9f, 149, 98.9f, 486.5f, 503.8f, 486.5f,
                503.8f, 635.5f, 98.9f, 635.5f, 98.9f, 925.5f,
            ],
        ],

        ['F'] =
        [
            [
                151.9f, 933.3f, 536.9f, 933.3f, 536.9f, 1083.5f, -4.9f, 1083.5f, -4.9f, 0,
                151.9f, 0, 151.9f, 490.6f, 462.3f, 490.6f, 462.3f, 640.9f, 151.9f, 640.9f,
                151.9f, 933.3f,
            ],
        ],

        ['G'] =
        [
            [
                469.2f, 429.3f, 469.2f, 161.2f, float.NaN, float.NaN, float.NaN, float.NaN, 429.7f, 145.2f,
                388.6f, 135.6f, 343, 135.6f, float.NaN, float.NaN, float.NaN, float.NaN, 114.8f, 135.6f,
                88.9f, 367, 88.9f, 556.9f, float.NaN, float.NaN, float.NaN, float.NaN, 88.9f, 761.2f,
                125.4f, 946.3f, 344.5f, 946.3f, float.NaN, float.NaN, float.NaN, float.NaN, 412.9f, 946.3f,
                454, 928.7f, 469.2f, 864.9f, 625.9f, 864.9f, 625.9f, 877.7f, float.NaN, float.NaN,
                float.NaN, float.NaN, 625.9f, 987.8f, 495.1f, 1096.3f, 344.5f, 1096.3f, float.NaN, float.NaN,
                float.NaN, float.NaN, 23.5f, 1096.3f, -67.8f, 833, -67.8f, 542.5f, float.NaN, float.NaN,
                float.NaN, float.NaN, -67.8f, 264.9f, 17.4f, -14.4f, 329.3f, -14.4f, float.NaN, float.NaN,
                float.NaN, float.NaN, 428.2f, -14.4f, 534.7f, 11.2f, 625.9f, 51.1f, 625.9f, 579.3f,
                256.2f, 579.3f, 256.2f, 429.3f, 469.2f, 429.3f,
            ],
        ],

        ['H'] =
        [
            [
                453.1f, 472.3f, 453.1f, 0, 615.4f, 0, 615.4f, 1049.9f, 453.1f, 1049.9f,
                453.1f, 617.9f, 96.9f, 617.9f, 96.9f, 1049.9f, -65.4f, 1049.9f, -65.4f, 0,
                96.9f, 0, 96.9f, 472.3f, 453.1f, 472.3f,
            ],
        ],

        ['I'] =
        [
            [
                338.7f, 925.5f, 483.2f, 925.5f, 483.2f, 1074.5f, 45.4f, 1074.5f, 45.4f, 925.5f,
                191.4f, 925.5f, 191.4f, 149, 16.8f, 149, 16.8f, 0, 513.3f, 0,
                513.3f, 149, 338.7f, 149, 338.7f, 925.5f,
            ],
        ],

        ['J'] =
        [
            [
                396.9f, 1065.9f, 396.9f, 323.8f, float.NaN, float.NaN, float.NaN, float.NaN, 396.9f, 231.1f,
                404.5f, 133.6f, 287.1f, 133.6f, float.NaN, float.NaN, float.NaN, float.NaN, 216.4f, 133.6f,
                180.3f, 176.1f, 180.3f, 246.8f, 180.3f, 345.9f, 25.4f, 345.9f, 25.4f, 254.7f,
                float.NaN, float.NaN, float.NaN, float.NaN, 25.4f, 91.2f, 127.7f, -14.1f, 288.6f, -14.1f,
                float.NaN, float.NaN, float.NaN, float.NaN, 455.6f, -14.1f, 551.9f, 84.9f, 551.9f, 294,
                551.9f, 1065.9f, 396.9f, 1065.9f,
            ],
        ],

        ['K'] =
        [
            [
                37.2f, 631.1f, 37.2f, 1067.1f, -117.1f, 1067.1f, -117.1f, 0, 37.2f, 0,
                37.2f, 478.5f, 467.1f, 0, 679.9f, 0, 160, 558.7f, 649.9f, 1067.1f,
                449.2f, 1067.1f, 37.2f, 631.1f,
            ],
        ],

        ['L'] =
        [
            [
                85.4f, 147.3f, 85.4f, 1062.3f, -65.6f, 1062.3f, -65.6f, 0, 593.8f, 0,
                593.8f, 147.3f, 85.4f, 147.3f,
            ],
        ],

        ['M'] =
        [
            [
                283.6f, 648.1f, 280.4f, 648.1f, 157.9f, 1053.8f, -88.8f, 1053.8f, -88.8f, 0,
                71, 0, 71, 878.2f, 74.1f, 878.2f, 218.4f, 341.9f, 345.6f, 341.9f,
                489.9f, 878.2f, 493, 878.2f, 493, 0, 652.8f, 0, 652.8f, 1053.8f,
                406.1f, 1053.8f, 283.6f, 648.1f,
            ],
        ],

        ['N'] =
        [
            [
                475.8f, 290.4f, 472.7f, 290.4f, 114.1f, 1058.4f, -69.9f, 1058.4f, -69.9f, 0,
                74.2f, 0, 74.2f, 800.8f, 77.3f, 800.8f, 448.2f, 0, 619.9f, 0,
                619.9f, 1058.4f, 475.8f, 1058.4f, 475.8f, 290.4f,
            ],
        ],

        ['O'] =
        [
            [
                279, 1101, float.NaN, float.NaN, float.NaN, float.NaN, 15.7f, 1101, -88.4f, 764.4f,
                -88.4f, 544.9f, float.NaN, float.NaN, float.NaN, float.NaN, -88.4f, 306.1f, 14.2f, -14.4f,
                279, -14.4f, float.NaN, float.NaN, float.NaN, float.NaN, 543.8f, -14.4f, 646.4f, 306.1f,
                646.4f, 544.9f, float.NaN, float.NaN, float.NaN, float.NaN, 646.4f, 764.4f, 542.3f, 1101,
                279, 1101,
            ],
            [
                279, 964.9f, float.NaN, float.NaN, float.NaN, float.NaN, 461.2f, 964.9f, 488.7f, 682.6f,
                488.7f, 539, float.NaN, float.NaN, float.NaN, float.NaN, 488.7f, 398.7f, 458.1f, 126.3f,
                279, 126.3f, float.NaN, float.NaN, float.NaN, float.NaN, 98.4f, 126.3f, 69.3f, 398.7f,
                69.3f, 539, float.NaN, float.NaN, float.NaN, float.NaN, 69.3f, 682.6f, 95.3f, 964.9f,
                279, 964.9f,
            ],
        ],

        ['P'] =
        [
            [
                -91.4f, 0, 67.4f, 0, 67.4f, 438.8f, 250.9f, 438.8f, float.NaN, float.NaN,
                float.NaN, float.NaN, 445.1f, 438.8f, 616.3f, 525, 616.3f, 746, float.NaN, float.NaN,
                float.NaN, float.NaN, 616.3f, 967, 469.8f, 1062.6f, 261.7f, 1062.6f, -91.4f, 1062.6f,
                -91.4f, 0,
            ],
            [
                74.4f, 579.1f, 74.4f, 908.3f, 262.5f, 908.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                381.2f, 908.3f, 464.5f, 881.6f, 464.5f, 742.1f, float.NaN, float.NaN, float.NaN, float.NaN,
                464.5f, 613.6f, 370.4f, 579.1f, 261, 579.1f, 74.4f, 579.1f,
            ],
        ],

        ['Q'] =
        [
            [
                501.5f, 0, 690.6f, 0, 510.6f, 238.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                570.1f, 331.4f, 579.3f, 452.9f, 579.3f, 561.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                579.3f, 789, 504.5f, 1084, 229.9f, 1084, float.NaN, float.NaN, float.NaN, float.NaN,
                -43.1f, 1084, -119.4f, 789, -119.4f, 561.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                -119.4f, 329.8f, -34, 55.2f, 229.9f, 55.2f, float.NaN, float.NaN, float.NaN, float.NaN,
                297, 55.2f, 364.2f, 78.9f, 411.5f, 119.9f, 501.5f, 0,
            ],
            [
                324.8f, 224.3f, float.NaN, float.NaN, float.NaN, float.NaN, 299.6f, 197.4f, 261.9f, 189.6f,
                228.9f, 189.6f, float.NaN, float.NaN, float.NaN, float.NaN, 56.1f, 189.6f, 30.9f, 437.3f,
                30.9f, 565.1f, float.NaN, float.NaN, float.NaN, float.NaN, 30.9f, 696.1f, 54.5f, 921.7f,
                228.9f, 921.7f, float.NaN, float.NaN, float.NaN, float.NaN, 403.3f, 921.7f, 426.9f, 696.1f,
                426.9f, 565.1f, float.NaN, float.NaN, float.NaN, float.NaN, 426.9f, 487.8f, 426.9f, 426.2f,
                409.6f, 369.4f, 299.6f, 508.3f, 107.9f, 508.3f, 324.8f, 224.3f,
            ],
        ],

        ['R'] =
        [
            [
                71.7f, 445.5f, 200.9f, 445.5f, 445.2f, 0, 631.2f, 0, 352.2f, 478.8f,
                float.NaN, float.NaN, float.NaN, float.NaN, 490.9f, 504.1f, 565, 626.2f, 565, 761,
                float.NaN, float.NaN, float.NaN, float.NaN, 565, 973.4f, 415.3f, 1074.9f, 219.8f, 1074.9f,
                -90.6f, 1074.9f, -90.6f, 0, 71.7f, 0, 71.7f, 445.5f,
            ],
            [
                83.3f, 594.5f, 83.3f, 925.8f, 198, 925.8f, float.NaN, float.NaN, float.NaN, float.NaN,
                309.8f, 925.8f, 404.6f, 908.4f, 404.6f, 767.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                404.6f, 626.2f, 326.6f, 594.5f, 199.6f, 594.5f, 83.3f, 594.5f,
            ],
        ],

        ['S'] =
        [
            [
                269.5f, -14.5f, float.NaN, float.NaN, float.NaN, float.NaN, 438.6f, -14.5f, 594.1f, 98.1f,
                594.1f, 289.5f, float.NaN, float.NaN, float.NaN, float.NaN, 594.1f, 474.5f, 474.8f, 551.7f,
                335.9f, 622.5f, 245.3f, 667.5f, float.NaN, float.NaN, float.NaN, float.NaN, 186.4f, 696.5f,
                127.6f, 738.3f, 127.6f, 817.1f, float.NaN, float.NaN, float.NaN, float.NaN, 127.6f, 920.1f,
                203.1f, 953.8f, 284.6f, 953.8f, float.NaN, float.NaN, float.NaN, float.NaN, 366.1f, 953.8f,
                434.1f, 907.2f, 428, 815.5f, 583.5f, 815.5f, 583.5f, 836.4f, float.NaN, float.NaN,
                float.NaN, float.NaN, 583.5f, 1000.5f, 428, 1105, 287.6f, 1105, float.NaN, float.NaN,
                float.NaN, float.NaN, 118.5f, 1105, -28, 990.8f, -28, 802.6f, float.NaN, float.NaN,
                float.NaN, float.NaN, -28, 640.2f, 89.8f, 571, 206.1f, 511.5f, float.NaN, float.NaN,
                float.NaN, float.NaN, 322.3f, 453.6f, 438.6f, 406.9f, 438.6f, 287.9f, float.NaN, float.NaN,
                float.NaN, float.NaN, 438.6f, 181.8f, 357.1f, 136.7f, 269.5f, 136.7f, float.NaN, float.NaN,
                float.NaN, float.NaN, 172.9f, 136.7f, 104.9f, 199.5f, 103.4f, 302.4f, -52.1f, 302.4f,
                -52.1f, 268.6f, float.NaN, float.NaN, float.NaN, float.NaN, -52.1f, 88.5f, 115.5f, -14.5f,
                269.5f, -14.5f,
            ],
        ],

        ['T'] =
        [
            [
                351.2f, 941.1f, 622, 941.1f, 622, 1092.6f, -72, 1092.6f, -72, 941.1f,
                198.8f, 941.1f, 198.8f, 0, 351.2f, 0, 351.2f, 941.1f,
            ],
        ],

        ['U'] =
        [
            [
                -92.2f, 1087.3f, -92.2f, 364, float.NaN, float.NaN, float.NaN, float.NaN, -92.2f, 128.3f,
                17.9f, -14.4f, 268, -14.4f, float.NaN, float.NaN, float.NaN, float.NaN, 516.5f, -14.4f,
                628.2f, 128.3f, 628.2f, 364, 628.2f, 1087.3f, 466.2f, 1087.3f, 466.2f, 375.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, 466.2f, 222.9f, 430, 136.3f, 268, 136.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, 106, 136.3f, 69.8f, 222.9f, 69.8f, 375.3f,
                69.8f, 1087.3f, -92.2f, 1087.3f,
            ],
        ],

        ['V'] =
        [
            [
                277.2f, 159.2f, 274.3f, 159.2f, 52.4f, 1058.4f, -98, 1058.4f, 169.2f, 0,
                377.9f, 0, 648.1f, 1058.4f, 497.7f, 1058.4f, 277.2f, 159.2f,
            ],
        ],

        ['W'] =
        [
            [
                219.4f, 660.5f, 112.6f, 279.7f, 109.5f, 279.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                80.1f, 518.3f, 60, 774.3f, 60, 982.9f, 60, 1071.4f, -99.4f, 1071.4f,
                -99.4f, 982.9f, float.NaN, float.NaN, float.NaN, float.NaN, -99.4f, 875.4f, -87, 595.7f,
                -65.4f, 469.3f, 13.6f, 0, 186.9f, 0, 278.2f, 372.9f, 281.3f, 372.9f,
                371.1f, 0, 544.4f, 0, 623.3f, 469.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                645, 595.7f, 657.4f, 875.4f, 657.4f, 982.9f, 657.4f, 1071.4f, 498, 1071.4f,
                498, 982.9f, float.NaN, float.NaN, float.NaN, float.NaN, 498, 774.3f, 477.8f, 518.3f,
                448.4f, 279.7f, 445.3f, 279.7f, 338.6f, 660.5f, 219.4f, 660.5f,
            ],
        ],

        ['X'] =
        [
            [
                265, 691.6f, 75.3f, 1058.4f, -106.8f, 1058.4f, 181.5f, 544.8f, -106.8f, 0,
                75.3f, 0, 265, 401.2f, 453.2f, 0, 636.8f, 0, 360.6f, 544.8f,
                636.8f, 1058.4f, 453.2f, 1058.4f, 265, 691.6f,
            ],
        ],

        ['Y'] =
        [
            [
                275, 668.4f, 86.4f, 1071.4f, -76.4f, 1071.4f, 201.6f, 500.9f, 201.6f, 0,
                349.9f, 0, 349.9f, 500.9f, 626.4f, 1071.4f, 463.6f, 1071.4f, 275, 668.4f,
            ],
        ],

        ['Z'] =
        [
            [
                146.3f, 147.3f, 533.2f, 938.5f, 533.2f, 1062.3f, -19.3f, 1062.3f, -19.3f, 915,
                364.6f, 915, -32.9f, 111.2f, -32.9f, 0, 582.8f, 0, 582.8f, 147.3f,
                146.3f, 147.3f,
            ],
        ],

        ['['] =
        [
            [
                147.2f, 1064.3f, 543.4f, 1064.3f, 543.4f, 1219, -7.4f, 1219, -7.4f, 0,
                543.4f, 0, 543.4f, 154.6f, 147.2f, 154.6f, 147.2f, 1064.3f,
            ],
        ],

        ['\\'] =
        [
            [
                -49.6f, 1197.4f, 426.7f, 0, 587.6f, 0, 114.5f, 1197.4f, -49.6f, 1197.4f,
            ],
        ],

        [']'] =
        [
            [
                402.1f, 154.6f, 18.2f, 154.6f, 18.2f, 0, 551.8f, 0, 551.8f, 1219,
                18.2f, 1219, 18.2f, 1064.3f, 402.1f, 1064.3f, 402.1f, 154.6f,
            ],
        ],

        ['^'] =
        [
            [
                308.6f, 1180.1f, 239.8f, 1180.1f, -86.2f, 651.4f, 6.5f, 573.3f, 274.2f, 877.5f,
                541.9f, 573.3f, 636.2f, 651.4f, 308.6f, 1180.1f,
            ],
        ],

        ['_'] =
        [
            [
                -77.7f, -311, 607.7f, -311, 607.7f, -125.8f, -77.7f, -125.8f, -77.7f, -311,
            ],
        ],

        ['`'] =
        [
            [
                296, 1160.8f, 113.4f, 1160.8f, 288.9f, 904.1f, 425.9f, 904.1f, 296, 1160.8f,
            ],
        ],

        ['a'] =
        [
            [
                438.8f, 363.6f, 438.8f, 337.6f, float.NaN, float.NaN, float.NaN, float.NaN, 438.8f, 211.7f,
                372.3f, 105.9f, 234.7f, 105.9f, float.NaN, float.NaN, float.NaN, float.NaN, 159.1f, 105.9f,
                103.1f, 150.4f, 103.1f, 230.1f, float.NaN, float.NaN, float.NaN, float.NaN, 103.1f, 331.4f,
                192.3f, 363.6f, 277, 363.6f, 438.8f, 363.6f,
            ],
            [
                434.8f, 0, 586, 0, 586, 493.5f, float.NaN, float.NaN, float.NaN, float.NaN,
                586, 726, 503.8f, 806.7f, 291, 806.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                155.9f, 806.7f, 0.3f, 768.7f, -9.9f, 593.2f, 141.2f, 593.2f, float.NaN, float.NaN,
                float.NaN, float.NaN, 141.2f, 670.7f, 220.5f, 683.3f, 298.3f, 683.3f, float.NaN, float.NaN,
                float.NaN, float.NaN, 423.1f, 683.3f, 434.8f, 616.9f, 434.8f, 526.7f, 434.8f, 498.3f,
                298.3f, 498.3f, float.NaN, float.NaN, float.NaN, float.NaN, 136.8f, 498.3f, -42.2f, 436.6f,
                -42.2f, 230.9f, float.NaN, float.NaN, float.NaN, float.NaN, -42.2f, 69.6f, 79.6f, -14.2f,
                222, -14.2f, float.NaN, float.NaN, float.NaN, float.NaN, 299.8f, -14.2f, 377.6f, 17.4f,
                434.8f, 75.9f, 434.8f, 0,
            ],
        ],

        ['b'] =
        [
            [
                282, 678.7f, float.NaN, float.NaN, float.NaN, float.NaN, 440.5f, 678.7f, 489.6f, 518.6f,
                489.6f, 386.3f, float.NaN, float.NaN, float.NaN, float.NaN, 489.6f, 253.9f, 425.8f, 111.8f,
                273.8f, 111.8f, float.NaN, float.NaN, float.NaN, float.NaN, 113.6f, 111.8f, 54.7f, 257.2f,
                54.7f, 394.4f, float.NaN, float.NaN, float.NaN, float.NaN, 54.7f, 539.8f, 116.9f, 678.7f,
                282, 678.7f,
            ],
            [
                -116.9f, 0, 46.6f, 0, 46.6f, 80.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                105.3f, 17.4f, 191, -14.3f, 276.7f, -14.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                503.7f, -14.3f, 632.2f, 171.3f, 632.2f, 383.8f, float.NaN, float.NaN, float.NaN, float.NaN,
                632.2f, 598, 532.2f, 808.9f, 287.8f, 808.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                202.1f, 808.9f, 105.3f, 778.8f, 51.4f, 707.4f, 46.6f, 707.4f, 46.6f, 1154.7f,
                -116.9f, 1154.7f, -116.9f, 0,
            ],
        ],

        ['c'] =
        [
            [
                571.6f, 590.5f, float.NaN, float.NaN, float.NaN, float.NaN, 549.8f, 748.6f, 434.9f, 822.8f,
                299.7f, 822.8f, float.NaN, float.NaN, float.NaN, float.NaN, 87.5f, 822.8f, -21.6f, 624.4f,
                -21.6f, 408.2f, float.NaN, float.NaN, float.NaN, float.NaN, -21.6f, 190.4f, 71.5f, -14.5f,
                286.6f, -14.5f, float.NaN, float.NaN, float.NaN, float.NaN, 421.8f, -14.5f, 571.6f, 75.8f,
                571.6f, 246.9f, 421.8f, 246.9f, float.NaN, float.NaN, float.NaN, float.NaN, 421.8f, 177.5f,
                360.8f, 124.2f, 296.8f, 124.2f, float.NaN, float.NaN, float.NaN, float.NaN, 155.8f, 124.2f,
                128.2f, 282.3f, 128.2f, 409.8f, float.NaN, float.NaN, float.NaN, float.NaN, 128.2f, 542.1f,
                158.7f, 684.1f, 304.1f, 684.1f, float.NaN, float.NaN, float.NaN, float.NaN, 359.3f, 684.1f,
                418.9f, 648.6f, 421.8f, 590.5f, 571.6f, 590.5f,
            ],
        ],

        ['d'] =
        [
            [
                270.6f, 110.7f, float.NaN, float.NaN, float.NaN, float.NaN, 109.8f, 110.7f, 56.1f, 266.2f,
                56.1f, 402.3f, float.NaN, float.NaN, float.NaN, float.NaN, 56.1f, 528.7f, 109.8f, 672.8f,
                259.2f, 672.8f, float.NaN, float.NaN, float.NaN, float.NaN, 421.7f, 672.8f, 483.5f, 546.5f,
                483.5f, 402.3f, float.NaN, float.NaN, float.NaN, float.NaN, 483.5f, 266.2f, 428.2f, 110.7f,
                270.6f, 110.7f,
            ],
            [
                498.7f, 0, 661.2f, 0, 661.2f, 1144.9f, 498.7f, 1144.9f, 498.7f, 704.6f,
                495.6f, 704.6f, float.NaN, float.NaN, float.NaN, float.NaN, 443.5f, 773.8f, 348.9f, 802.1f,
                262.1f, 802.1f, float.NaN, float.NaN, float.NaN, float.NaN, 25.5f, 802.1f, -78.6f, 603.9f,
                -78.6f, 391.6f, float.NaN, float.NaN, float.NaN, float.NaN, -78.6f, 176.1f, 36.5f, -14.2f,
                273.2f, -14.2f, float.NaN, float.NaN, float.NaN, float.NaN, 353.6f, -14.2f, 443.5f, 26.7f,
                498.7f, 81.8f, 498.7f, 0,
            ],
        ],

        ['e'] =
        [
            [
                602.4f, 368.1f, 602.4f, 417.8f, float.NaN, float.NaN, float.NaN, float.NaN, 602.4f, 651.4f,
                512.3f, 816.3f, 280.6f, 816.3f, float.NaN, float.NaN, float.NaN, float.NaN, 47.4f, 816.3f,
                -60.3f, 633.8f, -60.3f, 395.3f, float.NaN, float.NaN, float.NaN, float.NaN, -60.3f, 145.7f,
                65.1f, -14.4f, 299.8f, -14.4f, float.NaN, float.NaN, float.NaN, float.NaN, 414.9f, -14.4f,
                566.9f, 27.2f, 574.3f, 179.3f, 423.8f, 179.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                407.5f, 128, 351.5f, 123.2f, 307.2f, 123.2f, float.NaN, float.NaN, float.NaN, float.NaN,
                171.4f, 123.2f, 84.3f, 222.5f, 91.7f, 368.1f, 602.4f, 368.1f,
            ],
            [
                100.3f, 507, float.NaN, float.NaN, float.NaN, float.NaN, 106.4f, 617.4f, 196.1f, 685,
                291.8f, 685, float.NaN, float.NaN, float.NaN, float.NaN, 392.2f, 685, 468.2f, 617.4f,
                469.7f, 507, 100.3f, 507,
            ],
        ],

        ['f'] =
        [
            [
                347.1f, 789.6f, 347.1f, 827.4f, float.NaN, float.NaN, float.NaN, float.NaN, 347.1f, 981.9f,
                375.6f, 1011.8f, 515.3f, 1011.8f, 548, 1011.8f, 548, 1147.4f, 472.5f, 1147.4f,
                float.NaN, float.NaN, float.NaN, float.NaN, 270.2f, 1147.4f, 200.4f, 1027.6f, 200.4f, 827.4f,
                200.4f, 789.6f, -3.3f, 789.6f, -3.3f, 654.1f, 200.4f, 654.1f, 200.4f, 0,
                347.1f, 0, 347.1f, 654.1f, 548, 654.1f, 548, 789.6f, 347.1f, 789.6f,
            ],
        ],

        ['g'] =
        [
            [
                273.5f, 160.8f, float.NaN, float.NaN, float.NaN, float.NaN, 116.8f, 160.8f, 76.5f, 286.3f,
                76.5f, 418, float.NaN, float.NaN, float.NaN, float.NaN, 76.5f, 541.9f, 115.2f, 672.1f,
                265.4f, 672.1f, float.NaN, float.NaN, float.NaN, float.NaN, 418.8f, 672.1f, 465.7f, 552.9f,
                465.7f, 422.7f, float.NaN, float.NaN, float.NaN, float.NaN, 465.7f, 298.8f, 422, 160.8f,
                273.5f, 160.8f,
            ],
            [
                636, 785.7f, 474.5f, 785.7f, 474.5f, 715.1f, 471.3f, 715.1f, 472.9f, 715.1f,
                float.NaN, float.NaN, float.NaN, float.NaN, 425.9f, 777.9f, 333.4f, 799.8f, 255, 799.8f,
                float.NaN, float.NaN, float.NaN, float.NaN, 40.2f, 799.8f, -64.9f, 635.2f, -64.9f, 434.4f,
                float.NaN, float.NaN, float.NaN, float.NaN, -64.9f, 225.8f, 30.8f, 47, 258.1f, 47,
                float.NaN, float.NaN, float.NaN, float.NaN, 344.3f, 47, 400.8f, 72.1f, 468.2f, 125.5f,
                474.5f, 130.2f, 474.5f, 14.1f, float.NaN, float.NaN, float.NaN, float.NaN, 474.5f, -108.2f,
                444.7f, -150.6f, 262.8f, -150.6f, float.NaN, float.NaN, float.NaN, float.NaN, 209.5f, -150.6f,
                124.8f, -142.7f, 129.5f, -72.1f, -31.9f, -72.1f, -31.9f, -89.4f, float.NaN, float.NaN,
                float.NaN, float.NaN, -31.9f, -254.1f, 140.5f, -271.3f, 264.4f, -271.3f, float.NaN, float.NaN,
                float.NaN, float.NaN, 472.9f, -271.3f, 636, -214.9f, 636, 26.7f, 636, 785.7f,
            ],
        ],

        ['h'] =
        [
            [
                110.8f, 1154.3f, -50.3f, 1154.3f, -50.3f, 0, 110.8f, 0, 110.8f, 424.9f,
                float.NaN, float.NaN, float.NaN, float.NaN, 110.8f, 558.1f, 131.1f, 659.6f, 290.6f, 659.6f,
                float.NaN, float.NaN, float.NaN, float.NaN, 404.8f, 659.6f, 425.2f, 591.4f, 425.2f, 491.5f,
                425.2f, 0, 586.3f, 0, 586.3f, 478.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                586.3f, 661.2f, 515.9f, 808.7f, 314.1f, 808.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                240.6f, 808.7f, 160.8f, 781.7f, 110.8f, 729.4f, 110.8f, 1154.3f,
            ],
        ],

        ['i'] =
        [
            [
                89.1f, 789.6f, 89.1f, 641.5f, 332.9f, 641.5f, 332.9f, 0, 489, 0,
                489, 789.6f, 89.1f, 789.6f,
            ],
            [
                264.4f, 1175.9f, 264.4f, 969.5f, 466.9f, 969.5f, 466.9f, 1175.9f, 264.4f, 1175.9f,
            ],
        ],

        ['j'] =
        [
            [
                172.1f, 781.3f, 172.1f, 634.7f, 344.3f, 634.7f, 344.3f, 51.5f, float.NaN, float.NaN,
                float.NaN, float.NaN, 344.3f, -4.7f, 344.3f, -57.7f, 313.9f, -90.4f, float.NaN, float.NaN,
                float.NaN, float.NaN, 277.7f, -129.4f, 234.3f, -123.2f, 179.3f, -123.2f, 76.6f, -123.2f,
                76.6f, -269.8f, 185.1f, -269.8f, float.NaN, float.NaN, float.NaN, float.NaN, 412.2f, -269.8f,
                493.3f, -185.6f, 493.3f, 43.7f, 493.3f, 781.3f, 172.1f, 781.3f,
            ],
            [
                289.9f, 1149.3f, 289.9f, 951.2f, 483.4f, 951.2f, 483.4f, 1149.3f, 289.9f, 1149.3f,
            ],
        ],

        ['k'] =
        [
            [
                71.5f, 1136.5f, -87.4f, 1136.5f, -87.4f, 0, 71.5f, 0, 71.5f, 365.3f,
                403.3f, 0, 610.1f, 0, 207.3f, 443.4f, 576.2f, 782.1f, 366.3f, 782.1f,
                71.5f, 502.7f, 71.5f, 1136.5f,
            ],
        ],

        ['l'] =
        [
            [
                202.4f, 1182.9f, 52.8f, 1182.9f, 52.8f, 355.8f, float.NaN, float.NaN, float.NaN, float.NaN,
                52.8f, 126.7f, 110.9f, 0, 328.8f, 0, 463.9f, 0, 463.9f, 139.7f,
                366.6f, 139.7f, float.NaN, float.NaN, float.NaN, float.NaN, 247.5f, 139.7f, 202.4f, 178.7f,
                202.4f, 313.6f, 202.4f, 1182.9f,
            ],
        ],

        ['m'] =
        [
            [
                56.7f, 781.7f, -90.7f, 781.7f, -90.7f, 0, 56.7f, 0, 56.7f, 510.2f,
                float.NaN, float.NaN, float.NaN, float.NaN, 56.7f, 575.8f, 58.1f, 675.6f, 141.2f, 675.6f,
                float.NaN, float.NaN, float.NaN, float.NaN, 194.1f, 675.6f, 201.3f, 619.5f, 201.3f, 575.8f,
                201.3f, 0, 348.7f, 0, 348.7f, 510.2f, float.NaN, float.NaN, float.NaN, float.NaN,
                348.7f, 575.8f, 345.8f, 675.6f, 431.7f, 675.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                490.4f, 675.6f, 493.3f, 624.1f, 493.3f, 575.8f, 493.3f, 0, 640.7f, 0,
                640.7f, 575.8f, float.NaN, float.NaN, float.NaN, float.NaN, 640.7f, 691.2f, 583.4f, 795.8f,
                467.5f, 795.8f, float.NaN, float.NaN, float.NaN, float.NaN, 411.7f, 795.8f, 363, 777.1f,
                322.9f, 734.9f, float.NaN, float.NaN, float.NaN, float.NaN, 292.9f, 778.6f, 248.5f, 795.8f,
                198.4f, 795.8f, float.NaN, float.NaN, float.NaN, float.NaN, 169.8f, 795.8f, 144, 789.5f,
                119.7f, 777.1f, float.NaN, float.NaN, float.NaN, float.NaN, 96.8f, 764.6f, 75.3f, 747.4f,
                56.7f, 724, 56.7f, 781.7f,
            ],
        ],

        ['n'] =
        [
            [
                78.6f, 778.7f, -82.6f, 778.7f, -82.6f, 0, 78.6f, 0, 78.6f, 405.7f,
                float.NaN, float.NaN, float.NaN, float.NaN, 78.6f, 551.8f, 144.4f, 659.1f, 302.4f, 659.1f,
                float.NaN, float.NaN, float.NaN, float.NaN, 410.4f, 659.1f, 457.4f, 603.1f, 457.4f, 500.5f,
                457.4f, 0, 618.6f, 0, 618.6f, 489.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                618.6f, 669.9f, 505.9f, 792.7f, 319.6f, 792.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                233.6f, 792.7f, 131.8f, 757, 81.8f, 691.7f, 78.6f, 691.7f, 78.6f, 778.7f,
            ],
        ],

        ['o'] =
        [
            [
                275, 800.3f, float.NaN, float.NaN, float.NaN, float.NaN, 45.2f, 800.3f, -92.4f, 635.5f,
                -92.4f, 397, float.NaN, float.NaN, float.NaN, float.NaN, -92.4f, 164.8f, 42.2f, -14.1f,
                275, -14.1f, float.NaN, float.NaN, float.NaN, float.NaN, 507.8f, -14.1f, 642.4f, 164.8f,
                642.4f, 397, float.NaN, float.NaN, float.NaN, float.NaN, 642.4f, 635.5f, 503.3f, 800.3f,
                275, 800.3f,
            ],
            [
                268, 679.5f, float.NaN, float.NaN, float.NaN, float.NaN, 428.4f, 679.5f, 486, 541.4f,
                486, 395.4f, float.NaN, float.NaN, float.NaN, float.NaN, 486, 249.5f, 436.2f, 106.7f,
                268, 106.7f, float.NaN, float.NaN, float.NaN, float.NaN, 99.8f, 106.7f, 50, 249.5f,
                50, 395.4f, float.NaN, float.NaN, float.NaN, float.NaN, 50, 541.4f, 107.6f, 679.5f,
                268, 679.5f,
            ],
        ],

        ['p'] =
        [
            [
                275.2f, 664.6f, float.NaN, float.NaN, float.NaN, float.NaN, 444, 664.6f, 483.8f, 519.2f,
                483.8f, 373.8f, float.NaN, float.NaN, float.NaN, float.NaN, 483.8f, 233.3f, 432.9f, 97.8f,
                275.2f, 97.8f, float.NaN, float.NaN, float.NaN, float.NaN, 114.4f, 97.8f, 61.9f, 238.2f,
                61.9f, 382, float.NaN, float.NaN, float.NaN, float.NaN, 61.9f, 524.1f, 114.4f, 664.6f,
                275.2f, 664.6f,
            ],
            [
                47.9f, 770.7f, -116.1f, 770.7f, -116.1f, -266.1f, 47.9f, -266.1f, 47.9f, 72.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, 102, 15.4f, 203.9f, -13.8f, 278.7f, -13.8f,
                float.NaN, float.NaN, float.NaN, float.NaN, 511.2f, -13.8f, 633.8f, 175.4f, 633.8f, 384.6f,
                float.NaN, float.NaN, float.NaN, float.NaN, 633.8f, 595.3f, 509.6f, 784.5f, 272.4f, 784.5f,
                float.NaN, float.NaN, float.NaN, float.NaN, 180, 784.5f, 127.5f, 756.8f, 59, 704.5f,
                47.9f, 698.4f, 47.9f, 770.7f,
            ],
        ],

        ['q'] =
        [
            [
                277.7f, 667.6f, float.NaN, float.NaN, float.NaN, float.NaN, 430.1f, 667.6f, 486.9f, 529.3f,
                486.9f, 397.5f, float.NaN, float.NaN, float.NaN, float.NaN, 486.9f, 248, 438.2f, 109.8f,
                266.4f, 109.8f, float.NaN, float.NaN, float.NaN, float.NaN, 94.5f, 109.8f, 58.8f, 252.8f,
                58.8f, 394.3f, float.NaN, float.NaN, float.NaN, float.NaN, 58.8f, 535.8f, 114, 667.6f,
                277.7f, 667.6f,
            ],
            [
                667.9f, 781.9f, 500.9f, 781.9f, 500.9f, 703.8f, float.NaN, float.NaN, float.NaN, float.NaN,
                437.6f, 763.2f, 355, 795.9f, 265.8f, 795.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                21, 795.9f, -94.2f, 593, -94.2f, 377.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                -94.2f, 163.9f, 35.5f, -14, 267.4f, -14, float.NaN, float.NaN, float.NaN, float.NaN,
                355, -14, 442.5f, 15.6f, 500.9f, 78, 500.9f, -270, 667.9f, -270,
                667.9f, 781.9f,
            ],
        ],

        ['r'] =
        [
            [
                119.9f, 791.4f, -39.8f, 791.4f, -39.8f, 0, 119.9f, 0, 119.9f, 409.1f,
                float.NaN, float.NaN, float.NaN, float.NaN, 119.9f, 529.2f, 150.9f, 683.9f, 302.8f, 683.9f,
                float.NaN, float.NaN, float.NaN, float.NaN, 371.1f, 683.9f, 400.5f, 619.2f, 400.5f, 557.6f,
                400.5f, 533.9f, 560.2f, 533.9f, 560.2f, 565.5f, float.NaN, float.NaN, float.NaN, float.NaN,
                560.2f, 706.1f, 461, 805.6f, 324.5f, 805.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                242.4f, 805.6f, 171.1f, 769.2f, 123, 704.5f, 119.9f, 704.5f, 119.9f, 791.4f,
            ],
        ],

        ['s'] =
        [
            [
                544.8f, 597, 544.8f, 620.8f, float.NaN, float.NaN, float.NaN, float.NaN, 544.8f, 747.9f,
                405.2f, 809.8f, 271.4f, 809.8f, float.NaN, float.NaN, float.NaN, float.NaN, 156.5f, 809.8f,
                -16.6f, 768.5f, -16.6f, 582.7f, float.NaN, float.NaN, float.NaN, float.NaN, -16.6f, 500.2f,
                18.3f, 425.5f, 91, 392.2f, float.NaN, float.NaN, float.NaN, float.NaN, 150.6f, 362,
                243.7f, 338.2f, 307.7f, 325.5f, float.NaN, float.NaN, float.NaN, float.NaN, 367.4f, 314.4f,
                427, 301.7f, 427, 220.7f, float.NaN, float.NaN, float.NaN, float.NaN, 427, 128.6f,
                310.6f, 109.6f, 248.1f, 109.6f, float.NaN, float.NaN, float.NaN, float.NaN, 200.1f, 109.6f,
                109.9f, 117.5f, 107, 211.2f, -42.8f, 211.2f, float.NaN, float.NaN, float.NaN, float.NaN,
                -42.8f, 34.9f, 109.9f, -14.3f, 245.2f, -14.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                386.3f, -14.3f, 576.8f, 28.6f, 576.8f, 220.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                576.8f, 435.1f, 376.1f, 457.3f, 229.2f, 489, float.NaN, float.NaN, float.NaN, float.NaN,
                173.9f, 500.2f, 133.2f, 522.4f, 133.2f, 590.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                133.2f, 671.6f, 217.5f, 685.9f, 271.4f, 685.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                316.5f, 685.9f, 392.1f, 668.5f, 395, 597, 544.8f, 597,
            ],
        ],

        ['t'] =
        [
            [
                301.1f, 1020.3f, 151.6f, 978, 151.6f, 799.3f, 2, 799.3f, 2, 664.5f,
                151.6f, 664.5f, 151.6f, 286.8f, float.NaN, float.NaN, float.NaN, float.NaN, 151.6f, 111.3f,
                219.8f, -14.1f, 395.5f, -14.1f, float.NaN, float.NaN, float.NaN, float.NaN, 450.6f, -14.1f,
                514.5f, 3.1f, 561, 32.9f, 561, 181.8f, float.NaN, float.NaN, float.NaN, float.NaN,
                520.3f, 148.9f, 449.2f, 120.7f, 398.4f, 120.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                312.7f, 120.7f, 301.1f, 197.5f, 301.1f, 271.1f, 301.1f, 664.5f, 561, 664.5f,
                561, 799.3f, 301.1f, 799.3f, 301.1f, 1020.3f,
            ],
        ],

        ['u'] =
        [
            [
                475.4f, 0, 640.1f, 0, 640.1f, 781.7f, 475.4f, 781.7f, 475.4f, 385.4f,
                float.NaN, float.NaN, float.NaN, float.NaN, 475.4f, 227.8f, 414.7f, 120.1f, 237.2f, 120.1f,
                float.NaN, float.NaN, float.NaN, float.NaN, 123.7f, 120.1f, 88.6f, 163.8f, 88.6f, 269.9f,
                88.6f, 781.7f, -76.1f, 781.7f, -76.1f, 269.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                -76.1f, 99.9f, 27.8f, -14, 208.5f, -14, float.NaN, float.NaN, float.NaN, float.NaN,
                301.2f, -14, 413.1f, 15.6f, 475.4f, 87.4f, 475.4f, 0,
            ],
        ],

        ['v'] =
        [
            [
                66.6f, 772.1f, -101.2f, 772.1f, 166.8f, 0, 383.2f, 0, 651.2f, 772.1f,
                483.4f, 772.1f, 275.7f, 131, 272.8f, 131, 66.6f, 772.1f,
            ],
        ],

        ['w'] =
        [
            [
                67.6f, 780.8f, -88, 780.8f, 6.9f, 0, 186.1f, 0, 273.5f, 319.5f,
                276.5f, 319.5f, 363.9f, 0, 547.6f, 0, 638, 780.8f, 482.4f, 780.8f,
                429.1f, 202.6f, 426.1f, 202.6f, 349.1f, 478.4f, 199.4f, 478.4f, 123.9f, 219.7f,
                120.9f, 219.7f, 67.6f, 780.8f,
            ],
        ],

        ['x'] =
        [
            [
                122.2f, 780.8f, -77.2f, 780.8f, 186.8f, 392.7f, -77.2f, 0, 92.1f, 0,
                267.1f, 285.2f, 442.1f, 0, 627.1f, 0, 353.2f, 397.4f, 599.9f, 780.8f,
                429.2f, 780.8f, 277.1f, 519, 122.2f, 780.8f,
            ],
        ],

        ['y'] =
        [
            [
                291, 347.1f, 74.6f, 790.4f, -108.4f, 790.4f, 214, 189.3f, 83.3f, -60,
                float.NaN, float.NaN, float.NaN, float.NaN, 58.6f, -105.7f, 23.7f, -137.3f, -30, -137.3f,
                -53.2f, -137.3f, -53.2f, -272.9f, -19.8f, -272.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                109.5f, -272.9f, 158.8f, -224, 219.8f, -101, 664.3f, 790.4f, 507.5f, 790.4f,
                291, 347.1f,
            ],
        ],

        ['z'] =
        [
            [
                553.7f, 815.6f, 2.2f, 815.6f, 2.2f, 675.6f, 382.4f, 675.6f, -24.8f, 169.3f,
                -24.8f, 0, 586.8f, 0, 586.8f, 140, 142, 140, 553.7f, 654.4f,
                553.7f, 815.6f,
            ],
        ],

        ['{'] =
        [
            [
                -58.8f, 505.3f, 15.6f, 505.3f, float.NaN, float.NaN, float.NaN, float.NaN, 111.4f, 505.3f,
                152.7f, 488.1f, 152.7f, 389.5f, 152.7f, 286.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                152.7f, 92.3f, 280, 0, 458.4f, 0, 608.8f, 0, 608.8f, 147,
                509.6f, 147, float.NaN, float.NaN, float.NaN, float.NaN, 400.6f, 147, 322.9f, 153.3f,
                322.9f, 286.3f, 322.9f, 389.5f, float.NaN, float.NaN, float.NaN, float.NaN, 322.9f, 450.5f,
                304.7f, 535, 248.6f, 561.6f, 208.9f, 578.8f, 248.6f, 597.6f, float.NaN, float.NaN,
                float.NaN, float.NaN, 304.7f, 624.2f, 322.9f, 708.6f, 322.9f, 768.1f, 322.9f, 872.9f,
                float.NaN, float.NaN, float.NaN, float.NaN, 322.9f, 1005.9f, 400.6f, 1012.1f, 509.6f, 1012.1f,
                608.8f, 1012.1f, 608.8f, 1159.2f, 458.4f, 1159.2f, float.NaN, float.NaN, float.NaN, float.NaN,
                280, 1159.2f, 152.7f, 1065.3f, 152.7f, 872.9f, 152.7f, 768.1f, float.NaN, float.NaN,
                float.NaN, float.NaN, 152.7f, 671.1f, 111.4f, 652.3f, 15.6f, 652.3f, -58.8f, 652.3f,
                -58.8f, 505.3f,
            ],
        ],

        ['}'] =
        [
            [
                614.3f, 657.3f, 539.6f, 657.3f, float.NaN, float.NaN, float.NaN, float.NaN, 441.7f, 657.3f,
                401.8f, 676.2f, 401.8f, 773.9f, 401.8f, 879.5f, float.NaN, float.NaN, float.NaN, float.NaN,
                401.8f, 1073.4f, 274, 1168, 93.1f, 1168, -56.3f, 1168, -56.3f, 1019.8f,
                43.3f, 1019.8f, float.NaN, float.NaN, float.NaN, float.NaN, 152.8f, 1019.8f, 230.9f, 1013.5f,
                230.9f, 879.5f, 230.9f, 773.9f, float.NaN, float.NaN, float.NaN, float.NaN, 230.9f, 714,
                249.1f, 628.9f, 305.6f, 602.1f, 345.4f, 583.2f, 305.6f, 565.9f, float.NaN, float.NaN,
                float.NaN, float.NaN, 249.1f, 539.1f, 230.9f, 453.9f, 230.9f, 392.5f, 230.9f, 288.4f,
                float.NaN, float.NaN, float.NaN, float.NaN, 230.9f, 154.5f, 152.8f, 148.2f, 43.3f, 148.2f,
                -56.3f, 148.2f, -56.3f, 0, 93.1f, 0, float.NaN, float.NaN, float.NaN, float.NaN,
                274, 0, 401.8f, 93, 401.8f, 288.4f, 401.8f, 392.5f, float.NaN, float.NaN,
                float.NaN, float.NaN, 401.8f, 491.8f, 441.7f, 509.1f, 539.6f, 509.1f, 614.3f, 509.1f,
                614.3f, 657.3f,
            ],
        ],

        ['~'] =
        [
            [
                109.2f, 910.2f, float.NaN, float.NaN, float.NaN, float.NaN, 109.2f, 943.8f, 119.9f, 994.3f,
                161.1f, 994.3f, float.NaN, float.NaN, float.NaN, float.NaN, 190.1f, 994.3f, 220.6f, 969.8f,
                242, 954.5f, float.NaN, float.NaN, float.NaN, float.NaN, 287.8f, 919.4f, 338.2f, 896.4f,
                397.7f, 896.4f, float.NaN, float.NaN, float.NaN, float.NaN, 516.7f, 896.4f, 574.7f, 1008.1f,
                574.7f, 1113.6f, 432.8f, 1113.6f, float.NaN, float.NaN, float.NaN, float.NaN, 432.8f, 1083,
                423.6f, 1032.6f, 379.4f, 1032.6f, float.NaN, float.NaN, float.NaN, float.NaN, 347.3f, 1032.6f,
                321.4f, 1058.6f, 286.3f, 1083, float.NaN, float.NaN, float.NaN, float.NaN, 254.2f, 1107.5f,
                213, 1133.5f, 155, 1133.5f, float.NaN, float.NaN, float.NaN, float.NaN, 32.9f, 1133.5f,
                -32.7f, 1041.7f, -32.7f, 925.5f, -32.7f, 910.2f, 109.2f, 910.2f,
            ],
        ],

        ['\u00a3'] =
        [
            [
                289.9f, 532.2f, 490.6f, 532.2f, 490.6f, 681.1f, 322.6f, 681.1f, 370.2f, 907.6f,
                float.NaN, float.NaN, float.NaN, float.NaN, 383.6f, 966.3f, 402.9f, 1024.9f, 468.3f, 1024.9f,
                float.NaN, float.NaN, float.NaN, float.NaN, 511.4f, 1024.9f, 547.1f, 993.2f, 567.9f, 955.2f,
                613.9f, 955.2f, 613.9f, 1108.8f, float.NaN, float.NaN, float.NaN, float.NaN, 573.8f, 1151.6f,
                527.7f, 1173.8f, 469.8f, 1173.8f, float.NaN, float.NaN, float.NaN, float.NaN, 307.8f, 1173.8f,
                243.8f, 1024.9f, 212.6f, 879.1f, 169.5f, 681.1f, -22.2f, 681.1f, -22.2f, 532.2f,
                136.8f, 532.2f, 55.1f, 148.9f, -51.9f, 148.9f, -51.9f, 0, 613.9f, 0,
                613.9f, 148.9f, 206.7f, 148.9f, 289.9f, 532.2f,
            ],
        ],

        ['\u00a4'] =
        [
            [
                -102.2f, 770.1f, 15.5f, 654.2f, float.NaN, float.NaN, float.NaN, float.NaN, -26.2f, 584.1f,
                -29.2f, 532.2f, -29.2f, 491, float.NaN, float.NaN, float.NaN, float.NaN, -29.2f, 448.3f,
                -26.2f, 396.5f, 15.5f, 324.8f, -102.2f, 208.9f, -0.9f, 108.3f, 110.8f, 228.7f,
                float.NaN, float.NaN, float.NaN, float.NaN, 139.1f, 199.8f, 224.1f, 178.4f, 271.7f, 178.4f,
                float.NaN, float.NaN, float.NaN, float.NaN, 317.9f, 178.4f, 402.8f, 199.8f, 431.1f, 228.7f,
                542.9f, 108.3f, 644.2f, 208.9f, 526.5f, 324.8f, float.NaN, float.NaN, float.NaN, float.NaN,
                566.7f, 396.5f, 571.2f, 448.3f, 571.2f, 491, float.NaN, float.NaN, float.NaN, float.NaN,
                571.2f, 532.2f, 566.7f, 584.1f, 526.5f, 654.2f, 644.2f, 770.1f, 542.9f, 872.3f,
                431.1f, 750.3f, float.NaN, float.NaN, float.NaN, float.NaN, 402.8f, 780.8f, 317.9f, 800.6f,
                271.7f, 800.6f, float.NaN, float.NaN, float.NaN, float.NaN, 224.1f, 800.6f, 139.1f, 780.8f,
                110.8f, 750.3f, -0.9f, 872.3f, -102.2f, 770.1f,
            ],
            [
                271.7f, 304.9f, float.NaN, float.NaN, float.NaN, float.NaN, 174.9f, 304.9f, 98.9f, 389.7f,
                98.9f, 491.8f, float.NaN, float.NaN, float.NaN, float.NaN, 98.9f, 590.7f, 174.9f, 675.5f,
                271.7f, 675.5f, float.NaN, float.NaN, float.NaN, float.NaN, 365.6f, 675.5f, 443, 590.7f,
                443, 491.8f, float.NaN, float.NaN, float.NaN, float.NaN, 443, 389.7f, 365.6f, 304.9f,
                271.7f, 304.9f,
            ],
        ],

        ['\u00a5'] =
        [
            [
                692, 1153.8f, 493.2f, 1153.8f, 271.8f, 843.2f, 48.8f, 1153.8f, -150, 1153.8f,
                187.8f, 686.3f, 187.8f, 497.7f, -98.3f, 497.7f, -98.3f, 348.7f, 187.8f, 348.7f,
                187.8f, 0, 354.2f, 0, 354.2f, 348.7f, 641.9f, 348.7f, 641.9f, 497.7f,
                354.2f, 497.7f, 354.2f, 686.3f, 692, 1153.8f,
            ],
        ],

        ['\u00a7'] =
        [
            [
                252.5f, 637.5f, float.NaN, float.NaN, float.NaN, float.NaN, 296.3f, 632.9f, 341.7f, 628.3f,
                384.1f, 622.2f, float.NaN, float.NaN, float.NaN, float.NaN, 456.7f, 610, 489.9f, 573.3f,
                479.4f, 533.5f, float.NaN, float.NaN, float.NaN, float.NaN, 473.3f, 515.2f, 467.3f, 490.7f,
                453.6f, 470.8f, float.NaN, float.NaN, float.NaN, float.NaN, 411.3f, 477, 361.4f, 489.2f,
                306.9f, 490.7f, float.NaN, float.NaN, float.NaN, float.NaN, 264.6f, 492.2f, 204.1f, 498.4f,
                143.6f, 506, float.NaN, float.NaN, float.NaN, float.NaN, 111.8f, 510.6f, 90.6f, 538.1f,
                90.6f, 570.2f, float.NaN, float.NaN, float.NaN, float.NaN, 90.6f, 611.5f, 110.3f, 635.9f,
                134.5f, 663.5f, float.NaN, float.NaN, float.NaN, float.NaN, 167.8f, 649.7f, 208.6f, 643.6f,
                252.5f, 637.5f,
            ],
            [
                -54.9f, 250.7f, float.NaN, float.NaN, float.NaN, float.NaN, -25.5f, 44.3f, 114, -13.8f,
                307.8f, -13.8f, float.NaN, float.NaN, float.NaN, float.NaN, 576.5f, -13.8f, 625, 172.7f,
                625, 230.8f, float.NaN, float.NaN, float.NaN, float.NaN, 625, 275.2f, 629.4f, 333.3f,
                558.9f, 405.1f, float.NaN, float.NaN, float.NaN, float.NaN, 592.7f, 440.3f, 625, 477,
                625, 561, float.NaN, float.NaN, float.NaN, float.NaN, 625, 704.7f, 475.2f, 770.5f,
                310.7f, 776.6f, float.NaN, float.NaN, float.NaN, float.NaN, 159.5f, 782.7f, 131.6f, 836.2f,
                131.6f, 889.7f, float.NaN, float.NaN, float.NaN, float.NaN, 131.6f, 943.2f, 197.7f, 989.1f,
                293.1f, 989.1f, float.NaN, float.NaN, float.NaN, float.NaN, 391.5f, 989.1f, 447.3f, 952.4f,
                447.3f, 880.5f, 598.5f, 880.5f, float.NaN, float.NaN, float.NaN, float.NaN, 598.5f, 1019.6f,
                486.9f, 1132.8f, 299, 1132.8f, float.NaN, float.NaN, float.NaN, float.NaN, 59.6f, 1132.8f,
                -19.7f, 963.1f, -19.7f, 895.8f, float.NaN, float.NaN, float.NaN, float.NaN, -19.7f, 807.2f,
                3.8f, 761.3f, 34.7f, 730.7f, float.NaN, float.NaN, float.NaN, float.NaN, 9.7f, 704.7f,
                -54.9f, 668, -54.9f, 574.8f, float.NaN, float.NaN, float.NaN, float.NaN, -54.9f, 388.3f,
                109.5f, 353.1f, 272.5f, 351.6f, float.NaN, float.NaN, float.NaN, float.NaN, 406.2f, 350.1f,
                473.7f, 322.6f, 473.7f, 240, float.NaN, float.NaN, float.NaN, float.NaN, 473.7f, 157.5f,
                368, 129.9f, 281.3f, 129.9f, float.NaN, float.NaN, float.NaN, float.NaN, 197.7f, 129.9f,
                131.6f, 157.5f, 96.3f, 250.7f, -54.9f, 250.7f,
            ],
        ],

        ['\u00a8'] =
        [
            [
                491, 974.2f, 491, 1227, 351.6f, 1227, 351.6f, 974.2f, 491, 974.2f,
            ],
            [
                207.9f, 974.2f, 207.9f, 1227, 67, 1227, 67, 974.2f, 207.9f, 974.2f,
            ],
        ],

        ['\u00b4'] =
        [
            [
                253.9f, 1195.7f, 125, 931.2f, 262.4f, 931.2f, 435.1f, 1195.7f, 253.9f, 1195.7f,
            ],
        ],

        ['\u00b8'] =
        [
            [
                381.2f, -13.5f, 259.4f, -13.5f, 168.8f, -293.8f, 317.6f, -293.8f, 381.2f, -13.5f,
            ],
        ],

        ['\u00c4'] =
        [
            [
                417, 309.1f, 490.3f, 0, 646.1f, 0, 369.1f, 1074.9f, 172.9f, 1074.9f,
                -104.1f, 0, 51.6f, 0, 123.5f, 309.1f, 417, 309.1f,
            ],
            [
                374, 417.6f, 168.1f, 417.6f, 270.4f, 883.7f, 273.1f, 883.7f, 374, 417.6f,
            ],
            [
                633.3f, 951.3f, 633.3f, 1162.2f, 500.6f, 1162.2f, 500.6f, 951.3f, 633.3f, 951.3f,
            ],
            [
                48.6f, 951.3f, 48.6f, 1162.2f, -89.6f, 1162.2f, -89.6f, 951.3f, 48.6f, 951.3f,
            ],
        ],

        ['\u00c5'] =
        [
            [
                634.9f, 0, 448.9f, 696.2f, float.NaN, float.NaN, float.NaN, float.NaN, 523.9f, 741.5f,
                564.4f, 803.4f, 568.9f, 894, float.NaN, float.NaN, float.NaN, float.NaN, 564.4f, 1069.2f,
                415.9f, 1119.1f, 269, 1119.1f, float.NaN, float.NaN, float.NaN, float.NaN, 117.6f, 1119.1f,
                -30.9f, 1069.2f, -30.9f, 894, float.NaN, float.NaN, float.NaN, float.NaN, -30.9f, 803.4f,
                9.6f, 741.5f, 83.1f, 696.2f, -96.9f, 0, 51.6f, 0, 98.1f, 197.8f,
                439.9f, 197.8f, 486.4f, 0, 634.9f, 0,
            ],
            [
                406.9f, 333.8f, 131.1f, 333.8f, 206, 653.9f, 269, 649.4f, 332, 653.9f,
                406.9f, 333.8f,
            ],
            [
                262, 1002.8f, float.NaN, float.NaN, float.NaN, float.NaN, 340.8f, 1002.8f, 431.9f, 980.1f,
                431.9f, 898.6f, float.NaN, float.NaN, float.NaN, float.NaN, 431.9f, 798.9f, 348.5f, 770.2f,
                262, 770.2f, float.NaN, float.NaN, float.NaN, float.NaN, 175.5f, 770.2f, 92.1f, 798.9f,
                92.1f, 898.6f, float.NaN, float.NaN, float.NaN, float.NaN, 92.1f, 980.1f, 183.2f, 1002.8f,
                262, 1002.8f,
            ],
        ],

        ['\u00c6'] =
        [
            [
                289.7f, 394.4f, 130, 394.4f, 224, 896.1f, 259.8f, 896.1f, 289.7f, 394.4f,
            ],
            [
                298.6f, 268.7f, 315, 0, 622.4f, 0, 622.4f, 146, 444.8f, 146,
                421, 479.9f, 611.9f, 479.9f, 611.9f, 625.9f, 410.5f, 625.9f, 389.6f, 907,
                611.9f, 907, 611.9f, 1052.9f, 118.1f, 1052.9f, -83.3f, 0, 52.5f, 0,
                104.7f, 268.7f, 298.6f, 268.7f,
            ],
        ],

        ['\u00d1'] =
        [
            [
                469.5f, 218.6f, 466.5f, 218.6f, 119.1f, 793.6f, -59.1f, 793.6f, -59.1f, 0,
                80.5f, 0, 80.5f, 600.4f, 83.5f, 600.4f, 442.8f, 0, 609.1f, 0,
                609.1f, 793.6f, 469.5f, 793.6f, 469.5f, 218.6f,
            ],
            [
                110.6f, 949.6f, float.NaN, float.NaN, float.NaN, float.NaN, 110.6f, 984.4f, 121, 1036.7f,
                161.1f, 1036.7f, float.NaN, float.NaN, float.NaN, float.NaN, 189.3f, 1036.7f, 219, 1011.3f,
                239.8f, 995.5f, float.NaN, float.NaN, float.NaN, float.NaN, 284.3f, 959.1f, 333.3f, 935.3f,
                391.2f, 935.3f, float.NaN, float.NaN, float.NaN, float.NaN, 507.1f, 935.3f, 565, 1050.9f,
                565, 1160.2f, 425.4f, 1160.2f, float.NaN, float.NaN, float.NaN, float.NaN, 425.4f, 1128.6f,
                416.5f, 1076.3f, 373.4f, 1076.3f, float.NaN, float.NaN, float.NaN, float.NaN, 342.2f, 1076.3f,
                317, 1103.2f, 282.8f, 1128.6f, float.NaN, float.NaN, float.NaN, float.NaN, 251.7f, 1153.9f,
                211.6f, 1180.8f, 155.2f, 1180.8f, float.NaN, float.NaN, float.NaN, float.NaN, 36.4f, 1180.8f,
                -29, 1085.8f, -29, 965.4f, -29, 949.6f, 110.6f, 949.6f,
            ],
        ],

        ['\u00d6'] =
        [
            [
                281.7f, 913.8f, float.NaN, float.NaN, float.NaN, float.NaN, 48.2f, 913.8f, -74.4f, 678.8f,
                -74.4f, 456.1f, float.NaN, float.NaN, float.NaN, float.NaN, -74.4f, 235, 48.2f, -13.9f,
                281.7f, -13.9f, float.NaN, float.NaN, float.NaN, float.NaN, 515.3f, -13.9f, 636.4f, 235,
                636.4f, 456.1f, float.NaN, float.NaN, float.NaN, float.NaN, 636.4f, 678.8f, 515.3f, 913.8f,
                281.7f, 913.8f,
            ],
            [
                281.7f, 784.5f, float.NaN, float.NaN, float.NaN, float.NaN, 435.4f, 784.5f, 484.2f, 612.5f,
                484.2f, 462.8f, float.NaN, float.NaN, float.NaN, float.NaN, 484.2f, 324.3f, 435.4f, 128.4f,
                281.7f, 128.4f, float.NaN, float.NaN, float.NaN, float.NaN, 128, 128.4f, 77.8f, 324.3f,
                77.8f, 462.8f, float.NaN, float.NaN, float.NaN, float.NaN, 77.8f, 612.5f, 128, 784.5f,
                281.7f, 784.5f,
            ],
            [
                633.1f, 943.6f, 633.1f, 1149.3f, 495.5f, 1149.3f, 495.5f, 943.6f, 633.1f, 943.6f,
            ],
            [
                45.5f, 943.6f, 45.5f, 1149.3f, -92.1f, 1149.3f, -92.1f, 943.6f, 45.5f, 943.6f,
            ],
        ],

        ['\u00d8'] =
        [
            [
                275, 1026.9f, float.NaN, float.NaN, float.NaN, float.NaN, 18.1f, 1026.9f, -83.5f, 746.5f,
                -83.5f, 563.5f, float.NaN, float.NaN, float.NaN, float.NaN, -83.5f, 364.5f, 16.6f, 97.4f,
                275, 97.4f, float.NaN, float.NaN, float.NaN, float.NaN, 533.4f, 97.4f, 633.5f, 364.5f,
                633.5f, 563.5f, float.NaN, float.NaN, float.NaN, float.NaN, 633.5f, 746.5f, 531.9f, 1026.9f,
                275, 1026.9f,
            ],
            [
                275, 913.5f, float.NaN, float.NaN, float.NaN, float.NaN, 452.8f, 913.5f, 479.7f, 678.3f,
                479.7f, 558.6f, float.NaN, float.NaN, float.NaN, float.NaN, 479.7f, 441.7f, 449.8f, 214.7f,
                275, 214.7f, float.NaN, float.NaN, float.NaN, float.NaN, 98.7f, 214.7f, 70.3f, 441.7f,
                70.3f, 558.6f, float.NaN, float.NaN, float.NaN, float.NaN, 70.3f, 678.3f, 95.7f, 913.5f,
                275, 913.5f,
            ],
            [
                373.5f, 1138.8f, 69.4f, 14.5f, 176.5f, -14.4f, 480.6f, 1109.8f, 373.5f, 1138.8f,
            ],
        ],

        ['\u00dc'] =
        [
            [
                -88.4f, 868.4f, -88.4f, 349.5f, float.NaN, float.NaN, float.NaN, float.NaN, -88.4f, 123.2f,
                29.1f, -13.9f, 296, -13.9f, float.NaN, float.NaN, float.NaN, float.NaN, 561.2f, -13.9f,
                680.4f, 123.2f, 680.4f, 349.5f, 680.4f, 868.4f, 507.5f, 868.4f, 507.5f, 360.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, 507.5f, 214, 468.9f, 130.9f, 296, 130.9f,
                float.NaN, float.NaN, float.NaN, float.NaN, 123.1f, 130.9f, 84.5f, 214, 84.5f, 360.3f,
                84.5f, 868.4f, -88.4f, 868.4f,
            ],
            [
                122.8f, 1028.1f, 122.8f, 1137.4f, -84.2f, 1137.4f, -84.2f, 1028.1f, 122.8f, 1028.1f,
            ],
            [
                637, 1028.1f, 637, 1137.4f, 436.3f, 1137.4f, 436.3f, 1028.1f, 637, 1028.1f,
            ],
        ],

        ['\u00df'] =
        [
            [
                274.4f, 529.7f, float.NaN, float.NaN, float.NaN, float.NaN, 423.1f, 529.7f, 465.6f, 450.5f,
                465.6f, 304.5f, float.NaN, float.NaN, float.NaN, float.NaN, 465.6f, 175.5f, 417.1f, 133.6f,
                297.2f, 133.6f, 280.5f, 133.6f, 280.5f, 0, 313.9f, 0, float.NaN, float.NaN,
                float.NaN, float.NaN, 491.4f, 0, 622, 122.7f, 622, 313.8f, float.NaN, float.NaN,
                float.NaN, float.NaN, 622, 448.9f, 567.3f, 562.3f, 441.4f, 607.4f, float.NaN, float.NaN,
                float.NaN, float.NaN, 530.9f, 658.7f, 587.1f, 741, 587.1f, 851.3f, float.NaN, float.NaN,
                float.NaN, float.NaN, 587.1f, 1045.5f, 417.1f, 1151.1f, 247.1f, 1151.1f, float.NaN, float.NaN,
                float.NaN, float.NaN, 51.3f, 1151.1f, -91.4f, 1014.4f, -91.4f, 806.2f, -91.4f, 0,
                65, 0, 65, 753.4f, float.NaN, float.NaN, float.NaN, float.NaN, 65, 901,
                89.2f, 1017.5f, 256.2f, 1017.5f, float.NaN, float.NaN, float.NaN, float.NaN, 368.5f, 1017.5f,
                430.7f, 956.9f, 430.7f, 837.3f, float.NaN, float.NaN, float.NaN, float.NaN, 430.7f, 727,
                376.1f, 675.7f, 274.4f, 675.7f, 250.1f, 675.7f, 250.1f, 529.7f, 274.4f, 529.7f,
            ],
        ],

        ['\u00e5'] =
        [
            [
                595.9f, 471.3f, float.NaN, float.NaN, float.NaN, float.NaN, 595.9f, 542.2f, 604.9f, 675.2f,
                474.3f, 712.9f, float.NaN, float.NaN, float.NaN, float.NaN, 538.9f, 758.2f, 565.9f, 806.6f,
                565.9f, 886.6f, float.NaN, float.NaN, float.NaN, float.NaN, 565.9f, 1054.3f, 427.8f, 1119.2f,
                283.7f, 1119.2f, float.NaN, float.NaN, float.NaN, float.NaN, 139.6f, 1119.2f, 1.5f, 1054.3f,
                1.5f, 886.6f, float.NaN, float.NaN, float.NaN, float.NaN, 1.5f, 806.6f, 28.6f, 758.2f,
                93.1f, 712.9f, float.NaN, float.NaN, float.NaN, float.NaN, 31.6f, 684.2f, -3, 635.9f,
                -13.5f, 566.4f, 141.1f, 566.4f, float.NaN, float.NaN, float.NaN, float.NaN, 141.1f, 640.4f,
                222.2f, 654, 301.7f, 654, float.NaN, float.NaN, float.NaN, float.NaN, 429.3f, 654,
                441.3f, 589.1f, 441.3f, 503, 441.3f, 475.8f, 301.7f, 475.8f, float.NaN, float.NaN,
                float.NaN, float.NaN, 136.6f, 475.8f, -46.5f, 416.9f, -46.5f, 220.5f, float.NaN, float.NaN,
                float.NaN, float.NaN, -46.5f, 66.5f, 78.1f, -13.6f, 223.7f, -13.6f, float.NaN, float.NaN,
                float.NaN, float.NaN, 303.2f, -13.6f, 382.8f, 16.6f, 441.3f, 72.5f, 441.3f, 0,
                595.9f, 0, 595.9f, 471.3f,
            ],
            [
                438.4f, 359.5f, 438.4f, 332.3f, float.NaN, float.NaN, float.NaN, float.NaN, 438.4f, 208.4f,
                370.4f, 102.7f, 229.7f, 102.7f, float.NaN, float.NaN, float.NaN, float.NaN, 152.4f, 102.7f,
                95.2f, 148, 95.2f, 226.6f, float.NaN, float.NaN, float.NaN, float.NaN, 95.2f, 326.3f,
                186.4f, 359.5f, 273, 359.5f, 438.4f, 359.5f,
            ],
            [
                290, 1007.5f, float.NaN, float.NaN, float.NaN, float.NaN, 375.1f, 1007.5f, 453.9f, 974.2f,
                453.9f, 886.6f, float.NaN, float.NaN, float.NaN, float.NaN, 453.9f, 786.9f, 373.5f, 764.3f,
                290, 764.3f, float.NaN, float.NaN, float.NaN, float.NaN, 206.6f, 764.3f, 126.2f, 786.9f,
                126.2f, 886.6f, float.NaN, float.NaN, float.NaN, float.NaN, 126.2f, 974.2f, 205, 1007.5f,
                290, 1007.5f,
            ],
        ],

        ['\u00e6'] =
        [
            [
                338.2f, 477.9f, 338.2f, 523.1f, float.NaN, float.NaN, float.NaN, float.NaN, 338.2f, 597.2f,
                338.2f, 682.7f, 417.7f, 682.7f, float.NaN, float.NaN, float.NaN, float.NaN, 500.3f, 682.7f,
                500.3f, 597.2f, 500.3f, 523.1f, 500.3f, 477.9f, 338.2f, 477.9f,
            ],
            [
                209.9f, 359.1f, 209.9f, 260.7f, float.NaN, float.NaN, float.NaN, float.NaN, 209.9f, 202.7f,
                203.9f, 110.7f, 129.7f, 110.7f, float.NaN, float.NaN, float.NaN, float.NaN, 55.5f, 110.7f,
                49.5f, 186.5f, 49.5f, 236.5f, float.NaN, float.NaN, float.NaN, float.NaN, 49.5f, 343,
                108.5f, 359.1f, 199.3f, 359.1f, 209.9f, 359.1f,
            ],
            [
                630.7f, 355.4f, 630.7f, 504.2f, float.NaN, float.NaN, float.NaN, float.NaN, 630.7f, 660.8f,
                612.5f, 798.6f, 427.9f, 798.6f, float.NaN, float.NaN, float.NaN, float.NaN, 371.9f, 798.6f,
                305.3f, 775.1f, 276.5f, 718.7f, float.NaN, float.NaN, float.NaN, float.NaN, 250.8f, 782.9f,
                181.2f, 798.6f, 120.6f, 798.6f, float.NaN, float.NaN, float.NaN, float.NaN, -5, 798.6f,
                -80.7f, 709.3f, -80.7f, 580.9f, 49.5f, 580.9f, float.NaN, float.NaN, float.NaN, float.NaN,
                49.5f, 629.5f, 76.7f, 676.4f, 132.7f, 676.4f, float.NaN, float.NaN, float.NaN, float.NaN,
                209.9f, 676.4f, 209.9f, 593.4f, 209.9f, 512, 209.9f, 477.6f, 197.8f, 477.6f,
                float.NaN, float.NaN, float.NaN, float.NaN, 5.6f, 477.6f, -80.7f, 440, -80.7f, 222.3f,
                float.NaN, float.NaN, float.NaN, float.NaN, -80.7f, 93.9f, -32.3f, -14.1f, 107, -14.1f,
                float.NaN, float.NaN, float.NaN, float.NaN, 176.6f, -14.1f, 246.2f, 11, 276.5f, 79.9f,
                float.NaN, float.NaN, float.NaN, float.NaN, 302.2f, 9.4f, 364.3f, -14.1f, 433.9f, -14.1f,
                float.NaN, float.NaN, float.NaN, float.NaN, 564.1f, -14.1f, 630.7f, 68.9f, 630.7f, 198.9f,
                630.7f, 234.9f, 497.5f, 234.9f, float.NaN, float.NaN, float.NaN, float.NaN, 497.5f, 178.5f,
                489.9f, 114.3f, 423.3f, 114.3f, float.NaN, float.NaN, float.NaN, float.NaN, 343.1f, 114.3f,
                343.1f, 205.1f, 343.1f, 267.8f, 343.1f, 355.4f, 630.7f, 355.4f,
            ],
        ],

        ['\u00f8'] =
        [
            [
                599.3f, 905.5f, 479.5f, 905.5f, 409.4f, 769, float.NaN, float.NaN, float.NaN, float.NaN,
                368.5f, 783, 321.8f, 789.2f, 278, 790.7f, float.NaN, float.NaN, float.NaN, float.NaN,
                56, 790.7f, -76.9f, 627.9f, -76.9f, 392.3f, float.NaN, float.NaN, float.NaN, float.NaN,
                -76.9f, 274.4f, -38.9f, 150.4f, 50.2f, 66.7f, -55, -139.5f, 63.3f, -139.5f,
                139.3f, 9.3f, float.NaN, float.NaN, float.NaN, float.NaN, 181.6f, -4.7f, 226.9f, -14,
                278, -14, float.NaN, float.NaN, float.NaN, float.NaN, 502.9f, -14, 632.9f, 162.8f,
                632.9f, 392.3f, float.NaN, float.NaN, float.NaN, float.NaN, 632.9f, 502.4f, 584.7f, 637.2f,
                498.5f, 707, 599.3f, 905.5f,
            ],
            [
                449.4f, 578.3f, float.NaN, float.NaN, float.NaN, float.NaN, 489.7f, 525.6f, 502.1f, 448.1f,
                502.1f, 390.7f, float.NaN, float.NaN, float.NaN, float.NaN, 502.1f, 246.5f, 452.5f, 105.4f,
                285, 105.4f, float.NaN, float.NaN, float.NaN, float.NaN, 250.9f, 105.4f, 224.5f, 116.3f,
                202.8f, 127.1f, 449.4f, 578.3f,
            ],
            [
                111.3f, 184, float.NaN, float.NaN, float.NaN, float.NaN, 67.9f, 239.9f, 67.9f, 319.8f,
                67.9f, 388.4f, float.NaN, float.NaN, float.NaN, float.NaN, 67.9f, 537, 125.3f, 677.5f,
                285, 677.5f, float.NaN, float.NaN, float.NaN, float.NaN, 309.8f, 677.5f, 336.2f, 672.7f,
                361, 658.3f, 111.3f, 184,
            ],
        ],

        ['\u0132'] =
        [
            [
                54.4f, 1065.6f, -106.2f, 1065.6f, -106.2f, 0, 54.4f, 0, 54.4f, 1065.6f,
            ],
            [
                503.5f, 303.3f, float.NaN, float.NaN, float.NaN, float.NaN, 503.5f, 238.9f, 497.3f, 136.7f,
                395.9f, 136.7f, float.NaN, float.NaN, float.NaN, float.NaN, 347.6f, 136.7f, 299.2f, 146.2f,
                254, 161.9f, 254, 0, 410, -12.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                503.5f, -18.9f, 590.9f, 11, 634.6f, 100.6f, float.NaN, float.NaN, float.NaN, float.NaN,
                664.2f, 160.3f, 664.2f, 240.5f, 664.2f, 303.3f, 664.2f, 1065.6f, 503.5f, 1065.6f,
                503.5f, 303.3f,
            ],
        ],

        ['\u0133'] =
        [
            [
                93, 771.6f, -56.4f, 771.6f, -56.4f, 7, 93, 7, 93, 771.6f,
            ],
            [
                447.2f, 764.6f, 447.2f, 80.9f, float.NaN, float.NaN, float.NaN, float.NaN, 447.2f, -67.2f,
                434.2f, -132.8f, 281.9f, -132.8f, 194.9f, -132.8f, 194.9f, -264, 303.7f, -264,
                float.NaN, float.NaN, float.NaN, float.NaN, 548.7f, -264, 596.6f, -148, 596.6f, 90,
                596.6f, 764.6f, 447.2f, 764.6f,
            ],
            [
                428.3f, 1152.4f, 428.3f, 952.6f, 646.9f, 952.6f, 646.9f, 1152.4f, 428.3f, 1152.4f,
            ],
            [
                -104.7f, 1152.4f, -104.7f, 952.6f, 113.9f, 952.6f, 113.9f, 1152.4f, -104.7f, 1152.4f,
            ],
        ],

        ['|'] =
        [
            [275, -90, 275, 1170],
        ],

        ['\u2223'] =
        [
            [275, -190, 275, 514.75f],
            [275, 565.25f, 275, 1270],
        ],

        ['\u2015'] =
        [
            [-309, -454, 859, -454],
        ],

        ['\u2588'] =
        [
            [-99, -51, 649, -51, 649, 1090, -99, 1090, -99, -51],
        ],
    };

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with projecting square caps.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> SquareStrokes { get; } = new Dictionary<char, int[]>
    {
    };

    /// <summary>
    /// Gets, per character, the indices of strokes cut off exactly at their endpoints.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> ButtStrokes { get; } = new Dictionary<char, int[]>
    {
        ['|'] = [0],
        ['\u2223'] = [0, 1],
        ['\u2015'] = [0],
    };

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with butt ends and sharp miter joins.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> MiterStrokes { get; } = new Dictionary<char, int[]>
    {
    };

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with round caps and sharp miter joins.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> MiterRoundStrokes { get; } = new Dictionary<char, int[]>
    {
    };

    /// <summary>
    /// Gets the stroke width per character. Zero marks a character whose entry is filled ink
    /// outlines fitted to the standard.
    /// </summary>
    public static IReadOnlyDictionary<char, float> StrokeWidths { get; } = new Dictionary<char, float>
    {
        ['!'] = 0,
        ['"'] = 0,
        ['#'] = 0,
        ['$'] = 0,
        ['%'] = 0,
        ['&'] = 0,
        ['\''] = 0,
        ['('] = 0,
        [')'] = 0,
        ['*'] = 0,
        ['+'] = 0,
        [','] = 0,
        ['-'] = 0,
        ['.'] = 0,
        ['/'] = 0,
        ['0'] = 0,
        ['1'] = 0,
        ['2'] = 0,
        ['3'] = 0,
        ['4'] = 0,
        ['5'] = 0,
        ['6'] = 0,
        ['7'] = 0,
        ['8'] = 0,
        ['9'] = 0,
        [':'] = 0,
        [';'] = 0,
        ['<'] = 0,
        ['='] = 0,
        ['>'] = 0,
        ['?'] = 0,
        ['@'] = 0,
        ['A'] = 0,
        ['B'] = 0,
        ['C'] = 0,
        ['D'] = 0,
        ['E'] = 0,
        ['F'] = 0,
        ['G'] = 0,
        ['H'] = 0,
        ['I'] = 0,
        ['J'] = 0,
        ['K'] = 0,
        ['L'] = 0,
        ['M'] = 0,
        ['N'] = 0,
        ['O'] = 0,
        ['P'] = 0,
        ['Q'] = 0,
        ['R'] = 0,
        ['S'] = 0,
        ['T'] = 0,
        ['U'] = 0,
        ['V'] = 0,
        ['W'] = 0,
        ['X'] = 0,
        ['Y'] = 0,
        ['Z'] = 0,
        ['['] = 0,
        ['\\'] = 0,
        [']'] = 0,
        ['^'] = 0,
        ['_'] = 0,
        ['`'] = 0,
        ['a'] = 0,
        ['b'] = 0,
        ['c'] = 0,
        ['d'] = 0,
        ['e'] = 0,
        ['f'] = 0,
        ['g'] = 0,
        ['h'] = 0,
        ['i'] = 0,
        ['j'] = 0,
        ['k'] = 0,
        ['l'] = 0,
        ['m'] = 0,
        ['n'] = 0,
        ['o'] = 0,
        ['p'] = 0,
        ['q'] = 0,
        ['r'] = 0,
        ['s'] = 0,
        ['t'] = 0,
        ['u'] = 0,
        ['v'] = 0,
        ['w'] = 0,
        ['x'] = 0,
        ['y'] = 0,
        ['z'] = 0,
        ['{'] = 0,
        ['}'] = 0,
        ['~'] = 0,
        ['\u00a3'] = 0,
        ['\u00a4'] = 0,
        ['\u00a5'] = 0,
        ['\u00a7'] = 0,
        ['\u00a8'] = 0,
        ['\u00b4'] = 0,
        ['\u00b8'] = 0,
        ['\u00c4'] = 0,
        ['\u00c5'] = 0,
        ['\u00c6'] = 0,
        ['\u00d1'] = 0,
        ['\u00d6'] = 0,
        ['\u00d8'] = 0,
        ['\u00dc'] = 0,
        ['\u00df'] = 0,
        ['\u00e5'] = 0,
        ['\u00e6'] = 0,
        ['\u00f8'] = 0,
        ['\u0132'] = 0,
        ['\u0133'] = 0,
    };

    /// <summary>
    /// Gets per-stroke width overrides keyed by character and stroke index. The value zero
    /// marks a stroke that is already a filled ink polygon rather than a centerline to stroke.
    /// </summary>
    public static IReadOnlyDictionary<(char Character, int Stroke), float> StrokeWidthOverrides { get; } =
        new Dictionary<(char Character, int Stroke), float>
        {
            [('\u2588', 0)] = 0,
        };

    /// <summary>
    /// Gets the design definition the generator consumes.
    /// </summary>
    public static FontDesign Design { get; } = new()
    {
        Name = "OcrB",
        FamilyName = "SixLabors OCRB",
        DataSummary = "Built clean-room from the character sheet and dimension tables of ECMA-11.",
        UnitsPerEm = UnitsPerEm,
        H = H,
        W = W,
        Descender = D,
        DefaultStrokeWidth = T,
        SmallLetterHeight = SmallLetterHeight,
        CapitalHeight = CapitalHeight,
        AscenderHeight = AscenderHeight,
        DescenderDepth = DescenderDepth,
        NormalizationExceptions = string.Empty,
        DrawnCapitalHeight = 1050,

        // The reference OCR-B digitization prints its capital ink 678 units above the baseline per em, so
        // the scale lands ours there and a point size means the same glyph size in both.
        EmScale = 678F / CapitalHeight,
        DrawnSmallLetterHeight = 780.6F,
        DrawnAscenderHeight = 1154.2F,
        DrawnDescenderDepth = 266.7F,
        CutTerminals = false,
        Skeletons = Skeletons,
        SquareStrokes = SquareStrokes,
        ButtStrokes = ButtStrokes,
        MiterStrokes = MiterStrokes,
        MiterRoundStrokes = MiterRoundStrokes,
        StrokeWidths = StrokeWidths,
        StrokeWidthOverrides = StrokeWidthOverrides,
        Expectations = SpecChecks.OcrB,
    };
}
