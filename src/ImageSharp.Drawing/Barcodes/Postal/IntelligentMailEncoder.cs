// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the United States Postal Service Intelligent Mail barcode, from USPS-B-3200 revision H. Section 2.2 converts the routing code and the
/// tracking code to a binary value, generates an 11-bit frame check sequence on it, divides it into ten codewords, converts the codewords to 13-bit characters
/// and maps the character bits to the 65 bars.
/// </summary>
internal static class IntelligentMailEncoder
{
    /// <summary>
    /// The number of bars in a symbol.
    /// </summary>
    public const int BarCount = 65;

    /// <summary>
    /// The number of digits in the tracking code.
    /// </summary>
    public const int TrackingLength = 20;

    /// <summary>
    /// The number of codewords, and of characters, in a symbol.
    /// </summary>
    public const int CharacterCount = 10;

    /// <summary>
    /// The number of entries in the table of characters with five bits set, Table 19.
    /// </summary>
    private const int FiveOf13Length = 1287;

    /// <summary>
    /// The number of entries in the table of characters with two bits set, Table 20.
    /// </summary>
    private const int TwoOf13Length = 78;

    /// <summary>
    /// The generator polynomial of the frame check sequence, section 2.2.2.
    /// </summary>
    private const int GeneratorPolynomial = 0xF35;

    /// <summary>
    /// The dimensions of Figure 6, which gives every vertical dimension as a minimum and a maximum from the centerline: a full bar of 0.125 to 0.165 inch, a
    /// tracker of 0.039 to 0.057 inch and an ascender or descender bar of 0.083 to 0.111 inch. The metrics are the middle of each range: a full bar of 0.145
    /// inch, a tracker of 0.048 inch and an extender of 0.0485 inch above or below it. The bar width of 0.020 inch, the pitch of 22 bars per inch and the clear
    /// zone of 0.125 inch on each end are the POSTNET dimensions. Section 2.3.2 gives a clear zone of 0.028 inch above and below the bars. Section 2.4.1 keeps
    /// the human readable information at least 0.028 inch below the bars, and section 2.4.2 aligns "the left edge of the leftmost digit" with the leftmost bar.
    /// </summary>
    public static readonly FourStateMetrics Metrics = new(
        UspsPostalEncoder.NominalXDimension,
        UspsPostalEncoder.RunUnit,
        UspsPostalEncoder.BarUnits,
        FourStateMetrics.SpaceUnitsForPitch(
            UspsPostalEncoder.Pitch,
            UspsPostalEncoder.NominalXDimension,
            UspsPostalEncoder.RunUnit,
            UspsPostalEncoder.BarUnits),
        (float)((0.145 - 0.048) / 2 / 0.020),
        (float)(0.048 / 0.020),
        (float)((0.145 - 0.048) / 2 / 0.020),
        UspsPostalEncoder.ClearZone,
        (float)(0.028 / 0.020),
        BarcodeTextSide.BelowBars,
        (float)(0.028 / 0.020),
        BarcodeTextAlignment.Left,
        UspsPostalEncoder.Pitch,
        false)
    {
        MinimumXDimension = UspsPostalEncoder.MinimumXDimension,
        MaximumXDimension = UspsPostalEncoder.MaximumXDimension,
    };

    /// <summary>
    /// Gets Table 19, the characters with five of their 13 bits set, which a codeword below <see cref="FiveOf13Length"/> selects. The code of Table 18 orders
    /// them: every such 13-bit value is visited in ascending order, a value and its bit reversal fill the next two entries from the start, and a value that is
    /// its own reversal fills the next entry from the end.
    /// </summary>
    private static ReadOnlySpan<ushort> FiveOf13 =>
    [
        31, 7936, 47, 7808, 55, 7552, 59, 7040, 61, 6016, 62, 3968, 79, 7744, 87, 7488, 91, 6976, 93, 5952, 94, 3904, 103, 7360, 107,
        6848, 109, 5824, 110, 3776, 115, 6592, 117, 5568, 118, 3520, 121, 5056, 122, 3008, 124, 1984, 143, 7712, 151, 7456, 155, 6944, 157, 5920,
        158, 3872, 167, 7328, 171, 6816, 173, 5792, 174, 3744, 179, 6560, 181, 5536, 182, 3488, 185, 5024, 186, 2976, 188, 1952, 199, 7264, 203,
        6752, 205, 5728, 206, 3680, 211, 6496, 213, 5472, 214, 3424, 217, 4960, 218, 2912, 220, 1888, 227, 6368, 229, 5344, 230, 3296, 233, 4832,
        234, 2784, 236, 1760, 241, 4576, 242, 2528, 244, 1504, 248, 992, 271, 7696, 279, 7440, 283, 6928, 285, 5904, 286, 3856, 295, 7312, 299,
        6800, 301, 5776, 302, 3728, 307, 6544, 309, 5520, 310, 3472, 313, 5008, 314, 2960, 316, 1936, 327, 7248, 331, 6736, 333, 5712, 334, 3664,
        339, 6480, 341, 5456, 342, 3408, 345, 4944, 346, 2896, 348, 1872, 355, 6352, 357, 5328, 358, 3280, 361, 4816, 362, 2768, 364, 1744, 369,
        4560, 370, 2512, 372, 1488, 376, 976, 391, 7216, 395, 6704, 397, 5680, 398, 3632, 403, 6448, 405, 5424, 406, 3376, 409, 4912, 410, 2864,
        412, 1840, 419, 6320, 421, 5296, 422, 3248, 425, 4784, 426, 2736, 428, 1712, 433, 4528, 434, 2480, 436, 1456, 440, 944, 451, 6256, 453,
        5232, 454, 3184, 457, 4720, 458, 2672, 460, 1648, 465, 4464, 466, 2416, 468, 1392, 472, 880, 481, 4336, 482, 2288, 484, 1264, 488, 752,
        527, 7688, 535, 7432, 539, 6920, 541, 5896, 542, 3848, 551, 7304, 555, 6792, 557, 5768, 558, 3720, 563, 6536, 565, 5512, 566, 3464, 569,
        5000, 570, 2952, 572, 1928, 583, 7240, 587, 6728, 589, 5704, 590, 3656, 595, 6472, 597, 5448, 598, 3400, 601, 4936, 602, 2888, 604, 1864,
        611, 6344, 613, 5320, 614, 3272, 617, 4808, 618, 2760, 620, 1736, 625, 4552, 626, 2504, 628, 1480, 632, 968, 647, 7208, 651, 6696, 653,
        5672, 654, 3624, 659, 6440, 661, 5416, 662, 3368, 665, 4904, 666, 2856, 668, 1832, 675, 6312, 677, 5288, 678, 3240, 681, 4776, 682, 2728,
        684, 1704, 689, 4520, 690, 2472, 692, 1448, 696, 936, 707, 6248, 709, 5224, 710, 3176, 713, 4712, 714, 2664, 716, 1640, 721, 4456, 722,
        2408, 724, 1384, 728, 872, 737, 4328, 738, 2280, 740, 1256, 775, 7192, 779, 6680, 781, 5656, 782, 3608, 787, 6424, 789, 5400, 790, 3352,
        793, 4888, 794, 2840, 796, 1816, 803, 6296, 805, 5272, 806, 3224, 809, 4760, 810, 2712, 812, 1688, 817, 4504, 818, 2456, 820, 1432, 824,
        920, 835, 6232, 837, 5208, 838, 3160, 841, 4696, 842, 2648, 844, 1624, 849, 4440, 850, 2392, 852, 1368, 865, 4312, 866, 2264, 868, 1240,
        899, 6200, 901, 5176, 902, 3128, 905, 4664, 906, 2616, 908, 1592, 913, 4408, 914, 2360, 916, 1336, 929, 4280, 930, 2232, 932, 1208, 961,
        4216, 962, 2168, 964, 1144, 1039, 7684, 1047, 7428, 1051, 6916, 1053, 5892, 1054, 3844, 1063, 7300, 1067, 6788, 1069, 5764, 1070, 3716, 1075, 6532,
        1077, 5508, 1078, 3460, 1081, 4996, 1082, 2948, 1084, 1924, 1095, 7236, 1099, 6724, 1101, 5700, 1102, 3652, 1107, 6468, 1109, 5444, 1110, 3396, 1113,
        4932, 1114, 2884, 1116, 1860, 1123, 6340, 1125, 5316, 1126, 3268, 1129, 4804, 1130, 2756, 1132, 1732, 1137, 4548, 1138, 2500, 1140, 1476, 1159, 7204,
        1163, 6692, 1165, 5668, 1166, 3620, 1171, 6436, 1173, 5412, 1174, 3364, 1177, 4900, 1178, 2852, 1180, 1828, 1187, 6308, 1189, 5284, 1190, 3236, 1193,
        4772, 1194, 2724, 1196, 1700, 1201, 4516, 1202, 2468, 1204, 1444, 1219, 6244, 1221, 5220, 1222, 3172, 1225, 4708, 1226, 2660, 1228, 1636, 1233, 4452,
        1234, 2404, 1236, 1380, 1249, 4324, 1250, 2276, 1287, 7188, 1291, 6676, 1293, 5652, 1294, 3604, 1299, 6420, 1301, 5396, 1302, 3348, 1305, 4884, 1306,
        2836, 1308, 1812, 1315, 6292, 1317, 5268, 1318, 3220, 1321, 4756, 1322, 2708, 1324, 1684, 1329, 4500, 1330, 2452, 1332, 1428, 1347, 6228, 1349, 5204,
        1350, 3156, 1353, 4692, 1354, 2644, 1356, 1620, 1361, 4436, 1362, 2388, 1377, 4308, 1378, 2260, 1411, 6196, 1413, 5172, 1414, 3124, 1417, 4660, 1418,
        2612, 1420, 1588, 1425, 4404, 1426, 2356, 1441, 4276, 1442, 2228, 1473, 4212, 1474, 2164, 1543, 7180, 1547, 6668, 1549, 5644, 1550, 3596, 1555, 6412,
        1557, 5388, 1558, 3340, 1561, 4876, 1562, 2828, 1564, 1804, 1571, 6284, 1573, 5260, 1574, 3212, 1577, 4748, 1578, 2700, 1580, 1676, 1585, 4492, 1586,
        2444, 1603, 6220, 1605, 5196, 1606, 3148, 1609, 4684, 1610, 2636, 1617, 4428, 1618, 2380, 1633, 4300, 1634, 2252, 1667, 6188, 1669, 5164, 1670, 3116,
        1673, 4652, 1674, 2604, 1681, 4396, 1682, 2348, 1697, 4268, 1698, 2220, 1729, 4204, 1730, 2156, 1795, 6172, 1797, 5148, 1798, 3100, 1801, 4636, 1802,
        2588, 1809, 4380, 1810, 2332, 1825, 4252, 1826, 2204, 1857, 4188, 1858, 2140, 1921, 4156, 1922, 2108, 2063, 7682, 2071, 7426, 2075, 6914, 2077, 5890,
        2078, 3842, 2087, 7298, 2091, 6786, 2093, 5762, 2094, 3714, 2099, 6530, 2101, 5506, 2102, 3458, 2105, 4994, 2106, 2946, 2119, 7234, 2123, 6722, 2125,
        5698, 2126, 3650, 2131, 6466, 2133, 5442, 2134, 3394, 2137, 4930, 2138, 2882, 2147, 6338, 2149, 5314, 2150, 3266, 2153, 4802, 2154, 2754, 2161, 4546,
        2162, 2498, 2183, 7202, 2187, 6690, 2189, 5666, 2190, 3618, 2195, 6434, 2197, 5410, 2198, 3362, 2201, 4898, 2202, 2850, 2211, 6306, 2213, 5282, 2214,
        3234, 2217, 4770, 2218, 2722, 2225, 4514, 2226, 2466, 2243, 6242, 2245, 5218, 2246, 3170, 2249, 4706, 2250, 2658, 2257, 4450, 2258, 2402, 2273, 4322,
        2311, 7186, 2315, 6674, 2317, 5650, 2318, 3602, 2323, 6418, 2325, 5394, 2326, 3346, 2329, 4882, 2330, 2834, 2339, 6290, 2341, 5266, 2342, 3218, 2345,
        4754, 2346, 2706, 2353, 4498, 2354, 2450, 2371, 6226, 2373, 5202, 2374, 3154, 2377, 4690, 2378, 2642, 2385, 4434, 2401, 4306, 2435, 6194, 2437, 5170,
        2438, 3122, 2441, 4658, 2442, 2610, 2449, 4402, 2465, 4274, 2497, 4210, 2567, 7178, 2571, 6666, 2573, 5642, 2574, 3594, 2579, 6410, 2581, 5386, 2582,
        3338, 2585, 4874, 2586, 2826, 2595, 6282, 2597, 5258, 2598, 3210, 2601, 4746, 2602, 2698, 2609, 4490, 2627, 6218, 2629, 5194, 2630, 3146, 2633, 4682,
        2641, 4426, 2657, 4298, 2691, 6186, 2693, 5162, 2694, 3114, 2697, 4650, 2705, 4394, 2721, 4266, 2753, 4202, 2819, 6170, 2821, 5146, 2822, 3098, 2825,
        4634, 2833, 4378, 2849, 4250, 2881, 4186, 2945, 4154, 3079, 7174, 3083, 6662, 3085, 5638, 3086, 3590, 3091, 6406, 3093, 5382, 3094, 3334, 3097, 4870,
        3107, 6278, 3109, 5254, 3110, 3206, 3113, 4742, 3121, 4486, 3139, 6214, 3141, 5190, 3145, 4678, 3153, 4422, 3169, 4294, 3203, 6182, 3205, 5158, 3209,
        4646, 3217, 4390, 3233, 4262, 3265, 4198, 3331, 6166, 3333, 5142, 3337, 4630, 3345, 4374, 3361, 4246, 3393, 4182, 3457, 4150, 3587, 6158, 3589, 5134,
        3593, 4622, 3601, 4366, 3617, 4238, 3649, 4174, 3713, 4142, 3841, 4126, 4111, 7681, 4119, 7425, 4123, 6913, 4125, 5889, 4135, 7297, 4139, 6785, 4141,
        5761, 4147, 6529, 4149, 5505, 4153, 4993, 4167, 7233, 4171, 6721, 4173, 5697, 4179, 6465, 4181, 5441, 4185, 4929, 4195, 6337, 4197, 5313, 4201, 4801,
        4209, 4545, 4231, 7201, 4235, 6689, 4237, 5665, 4243, 6433, 4245, 5409, 4249, 4897, 4259, 6305, 4261, 5281, 4265, 4769, 4273, 4513, 4291, 6241, 4293,
        5217, 4297, 4705, 4305, 4449, 4359, 7185, 4363, 6673, 4365, 5649, 4371, 6417, 4373, 5393, 4377, 4881, 4387, 6289, 4389, 5265, 4393, 4753, 4401, 4497,
        4419, 6225, 4421, 5201, 4425, 4689, 4483, 6193, 4485, 5169, 4489, 4657, 4615, 7177, 4619, 6665, 4621, 5641, 4627, 6409, 4629, 5385, 4633, 4873, 4643,
        6281, 4645, 5257, 4649, 4745, 4675, 6217, 4677, 5193, 4739, 6185, 4741, 5161, 4867, 6169, 4869, 5145, 5127, 7173, 5131, 6661, 5133, 5637, 5139, 6405,
        5141, 5381, 5155, 6277, 5157, 5253, 5187, 6213, 5251, 6181, 5379, 6165, 5635, 6157, 6151, 7171, 6155, 6659, 6163, 6403, 6179, 6275, 6211, 5189, 4681,
        4433, 4321, 3142, 2634, 2386, 2274, 1612, 1364, 1252, 856, 744, 496,
    ];

    /// <summary>
    /// Gets Table 20, the characters with two of their 13 bits set, in the order of the code of Table 18. A codeword of <see cref="FiveOf13Length"/> or more
    /// selects entry codeword minus <see cref="FiveOf13Length"/>.
    /// </summary>
    private static ReadOnlySpan<ushort> TwoOf13 =>
    [
        3, 6144, 5, 5120, 6, 3072, 9, 4608, 10, 2560, 12, 1536, 17, 4352, 18, 2304, 20, 1280, 24, 768, 33, 4224, 34, 2176, 36,
        1152, 40, 640, 48, 384, 65, 4160, 66, 2112, 68, 1088, 72, 576, 80, 320, 96, 192, 129, 4128, 130, 2080, 132, 1056, 136, 544,
        144, 288, 257, 4112, 258, 2064, 260, 1040, 264, 528, 513, 4104, 514, 2056, 516, 1032, 1025, 4100, 1026, 2052, 2049, 4098, 4097, 2050, 1028,
        520, 272, 160,
    ];

    /// <summary>
    /// Gets Table 22, the bar to character mapping, as four values per bar from the left: the character and the bit of the descender, then the character and
    /// the bit of the ascender. Characters A to J are 0 to 9.
    /// </summary>
    private static ReadOnlySpan<byte> BarMap =>
    [
        7, 2, 4, 3, 1, 10, 0, 0, 9, 12, 2, 8, 5, 5, 6, 11, 8, 9, 3, 1, 0, 1, 5, 12, 2, 5, 1, 8, 4, 4, 9, 11, 6, 3, 8, 10, 3, 9, 7, 6,
        5, 11, 1, 4, 8, 5, 2, 12, 9, 10, 0, 2, 7, 1, 6, 7, 3, 6, 4, 9, 0, 3, 8, 6, 6, 4, 2, 7, 1, 1, 9, 9, 7, 10, 5, 2, 4, 0, 3, 8,
        6, 2, 0, 4, 8, 11, 1, 0, 9, 8, 3, 12, 2, 6, 7, 7, 5, 1, 4, 10, 1, 12, 6, 9, 7, 3, 8, 0, 5, 8, 9, 7, 4, 6, 2, 10, 3, 4, 0, 5,
        8, 4, 5, 7, 7, 11, 1, 9, 6, 0, 9, 6, 0, 6, 4, 8, 2, 1, 3, 2, 5, 9, 8, 12, 4, 11, 6, 1, 9, 5, 7, 4, 3, 3, 1, 2, 0, 7, 2, 0,
        1, 3, 4, 1, 6, 10, 3, 5, 8, 7, 9, 4, 2, 11, 5, 6, 0, 8, 7, 12, 4, 2, 8, 1, 5, 10, 3, 0, 9, 3, 0, 9, 6, 5, 2, 4, 7, 8, 1, 7,
        5, 0, 4, 5, 2, 3, 0, 10, 6, 12, 9, 2, 3, 11, 1, 6, 8, 8, 7, 9, 5, 4, 0, 11, 1, 5, 2, 2, 9, 1, 4, 12, 8, 3, 6, 6, 7, 0, 3, 7,
        4, 7, 7, 5, 0, 12, 1, 11, 2, 9, 9, 0, 6, 8, 5, 3, 3, 10, 8, 2,
    ];

    /// <summary>
    /// Encodes the digits into bar states.
    /// </summary>
    /// <param name="text">The tracking code followed by the routing code, already validated.</param>
    /// <param name="states">The buffer that receives the 65 bar states.</param>
    public static void Encode(ReadOnlySpan<char> text, Span<FourState> states)
    {
        UInt128 binary = Binary(text[..TrackingLength], text[TrackingLength..]);
        int fcs = FrameCheckSequence(binary);
        InlineArray10<int> codewordStorage = default;
        Span<int> codewords = codewordStorage;
        Codewords(binary, fcs, codewords);
        InlineArray10<int> characterStorage = default;
        Span<int> characters = characterStorage;
        Characters(codewords, fcs, characters);
        Bars(characters, states);
    }

    /// <summary>
    /// Step 1, section 2.2.1: converts the routing code to a value by Table 4, then multiplies the value by 10 and adds the first tracking digit, multiplies by
    /// 5 and adds the second, and multiplies by 10 and adds each of the remaining 18.
    /// </summary>
    /// <param name="tracking">The 20 tracking digits.</param>
    /// <param name="routing">The 0, 5, 9 or 11 routing digits.</param>
    /// <returns>The binary data, which fills the rightmost 102 bits.</returns>
    public static UInt128 Binary(ReadOnlySpan<char> tracking, ReadOnlySpan<char> routing)
    {
        UInt128 value = 0;
        for (int i = 0; i < routing.Length; i++)
        {
            value = (value * 10U) + (uint)(routing[i] - '0');
        }

        value += routing.Length switch
        {
            5 => 1UL,
            9 => 100001UL,
            11 => 1000100001UL,
            _ => 0UL,
        };

        value = (value * 10U) + (uint)(tracking[0] - '0');
        value = (value * 5U) + (uint)(tracking[1] - '0');
        for (int i = 2; i < tracking.Length; i++)
        {
            value = (value * 10U) + (uint)(tracking[i] - '0');
        }

        return value;
    }

    /// <summary>
    /// Step 2, section 2.2.2: the 11-bit frame check sequence of the rightmost 102 bits, by the code of Table 17. The data is 13 bytes of which the first byte
    /// carries 6 bits.
    /// </summary>
    /// <param name="data">The binary data.</param>
    /// <returns>The frame check sequence.</returns>
    public static int FrameCheckSequence(UInt128 data)
    {
        int fcs = 0x7FF;
        int word = ((int)(data >> 96) & 0xFF) << 5;
        for (int bit = 2; bit < 8; bit++)
        {
            fcs = Shift(fcs, word);
            word <<= 1;
        }

        for (int byteIndex = 1; byteIndex < 13; byteIndex++)
        {
            word = ((int)(data >> (8 * (12 - byteIndex))) & 0xFF) << 3;
            for (int bit = 0; bit < 8; bit++)
            {
                fcs = Shift(fcs, word);
                word <<= 1;
            }
        }

        return fcs;

        static int Shift(int fcs, int word)
            => (((fcs ^ word) & 0x400) != 0 ? (fcs << 1) ^ GeneratorPolynomial : fcs << 1) & 0x7FF;
    }

    /// <summary>
    /// Steps 3 and 4, sections 2.2.3 and 2.2.4: the rightmost codeword J is the value modulo 636, codewords I to B are successive remainders modulo 1365 and
    /// codeword A is what remains. J is then doubled, and A gains 659 when the most significant bit of the frame check sequence is set.
    /// </summary>
    /// <param name="data">The binary data.</param>
    /// <param name="fcs">The frame check sequence.</param>
    /// <param name="codewords">The buffer that receives the ten codewords, A first.</param>
    public static void Codewords(UInt128 data, int fcs, Span<int> codewords)
    {
        codewords[9] = (int)(data % 636U) * 2;
        data /= 636U;
        for (int i = 8; i >= 1; i--)
        {
            codewords[i] = (int)(data % 1365U);
            data /= 1365U;
        }

        codewords[0] = (int)data;
        if ((fcs & 0x400) != 0)
        {
            codewords[0] += 659;
        }
    }

    /// <summary>
    /// Step 5, section 2.2.5: a codeword below 1287 selects its character from Table 19 and any other codeword selects entry codeword minus 1287 from Table 20.
    /// Then, by Table 21, each of the frame check sequence bits 0 to 9 that is set negates the 13 bits of characters A to J in turn.
    /// </summary>
    /// <param name="codewords">The ten codewords.</param>
    /// <param name="fcs">The frame check sequence.</param>
    /// <param name="characters">The buffer that receives the ten characters, A first.</param>
    public static void Characters(ReadOnlySpan<int> codewords, int fcs, Span<int> characters)
    {
        for (int i = 0; i < CharacterCount; i++)
        {
            int codeword = codewords[i];
            characters[i] = codeword < FiveOf13Length ? FiveOf13[codeword] : TwoOf13[codeword - FiveOf13Length];
            if (((fcs >> i) & 1) != 0)
            {
                characters[i] ^= 0x1FFF;
            }
        }
    }

    /// <summary>
    /// Step 6, section 2.2.6: every bar takes its descender from one character bit and its ascender from another, by Table 22.
    /// </summary>
    /// <param name="characters">The ten characters.</param>
    /// <param name="states">The buffer that receives the 65 bar states.</param>
    public static void Bars(ReadOnlySpan<int> characters, Span<FourState> states)
    {
        for (int bar = 0; bar < BarCount; bar++)
        {
            int entry = bar * 4;
            FourState descender = ((characters[BarMap[entry]] >> BarMap[entry + 1]) & 1) != 0 ? FourState.Descender : FourState.Tracker;
            FourState ascender = ((characters[BarMap[entry + 2]] >> BarMap[entry + 3]) & 1) != 0 ? FourState.Ascender : FourState.Tracker;
            states[bar] = ascender | descender;
        }
    }
}
