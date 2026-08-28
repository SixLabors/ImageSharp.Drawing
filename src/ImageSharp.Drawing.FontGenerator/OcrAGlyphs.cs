// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.FontGenerator;

/// <summary>
/// The OCR-A glyph skeletons transcribed from the character drawings of FIPS PUB 32 (1974), Figures II-11
/// through II-96, using the Size I dimensions of Table II-3. The drawings dimension stroke centerlines
/// directly on the 0.0, W and H reference lines, so ink projects a half stroke beyond them at every
/// terminal: round terminals get round caps and square terminals get projecting square caps, both ending
/// T/2 past the centerline endpoint. Labelled corner radii are outer ink radii, so the centerline radii
/// used here are those values less T/2.
/// </summary>
internal static class OcrAGlyphs
{
    /// <summary>
    /// Design units per em. One em equals the 10 characters-per-inch pitch of 0.1 inch, so one unit is
    /// 0.0001 inch and Table II-3 dimensions map directly: W = 550, H = 940, T = 140.
    /// </summary>
    public const ushort UnitsPerEm = 1000;

    /// <summary>
    /// The nominal centerline width W of Table II-3 in design units.
    /// </summary>
    public const float W = 550;

    /// <summary>
    /// The nominal centerline height H of Table II-3 in design units.
    /// </summary>
    public const float H = 940;

    /// <summary>
    /// The nominal stroke width T of Table II-3 in design units.
    /// </summary>
    public const float T = 140;

    /// <summary>
    /// The centerline radius of the r1 vertex arcs of Table II-3 at the C, D and O sides; the callouts
    /// point at the dashed centerlines, so the table values are centerline radii.
    /// </summary>
    public const float R1 = 248;

    /// <summary>
    /// The centerline radius of the r2 vertex arcs of Table II-3 at the O top and bottom.
    /// </summary>
    public const float R2 = 111;

    /// <summary>
    /// The centerline radius of the r3 corner arcs of Table II-3 at the Q bowl corners.
    /// </summary>
    public const float R3 = 100;

    /// <summary>
    /// The centerline radius of the r4 corner arcs of Table II-3 joining the S bars to its diagonal.
    /// </summary>
    public const float R4 = 87;

    /// <summary>
    /// The centerline radius of corners the drawings label 3/2 T.
    /// </summary>
    public const float Corner3T2 = 210;

    /// <summary>
    /// The centerline radius of corners the drawings label T.
    /// </summary>
    public const float CornerT = T;

    /// <summary>
    /// The centerline radius of corners the drawings label 1/2 T.
    /// </summary>
    public const float CornerHalfT = T / 2;

    /// <summary>
    /// The lower case x height of Figures II-68 through II-93, whose dimensions are given directly in
    /// thousandths of an inch.
    /// </summary>
    public const float X = 646;

    /// <summary>
    /// The lower case descender tail centerline of Figures II-68 through II-93; the ink reaches -294.
    /// </summary>
    public const float D = -224;

    /// <summary>
    /// The centerline radius of the 13.8 corner arcs of the lower case drawings, measured on the outer
    /// ink.
    /// </summary>
    public const float LowerR = 138 - (T / 2);

    /// <summary>
    /// Gets the glyph skeletons keyed by character. Each glyph is a list of strokes; a stroke is a point
    /// list in design units with the y axis up, closed when the first and last points are equal. A
    /// <see cref="float.NaN"/> in an x position starts an arc segment: the next three values are the arc
    /// radius (negative to flip the sweep direction; positive sweeps counter-clockwise) and the end point.
    /// Two consecutive <see cref="float.NaN"/> values mark a rounded corner: the next three values are the
    /// centerline corner radius and the corner vertex, and the generator replaces the vertex with a
    /// tangent arc computed from the neighbouring segments.
    /// </summary>
    public static IReadOnlyDictionary<char, float[][]> Skeletons { get; } = new Dictionary<char, float[][]>
    {
        // Figure II-20. A rounded rectangle with 1/2 T corners.
        ['0'] =
        [
            [
                W / 2, H,
                float.NaN, float.NaN, CornerHalfT, W, H,
                float.NaN, float.NaN, CornerHalfT, W, 0,
                float.NaN, float.NaN, CornerHalfT, 0, 0,
                float.NaN, float.NaN, CornerHalfT, 0, H,
                W / 2, H,
            ],
        ],

        // Figure II-11. A flag to 1/2 W with a square stem corner, a full-width base bar with a round
        // join into the riser, and the riser cap at 3/8 H.
        ['1'] =
        [
            [0, H, W / 2, H, W / 2, 0],
            [0, 0, W, 0, W, 3 * H / 8],
        ],

        // Figure II-12. Three 1/2 T centerline bends, a square corner at the base, and round caps at the
        // top-left and bottom-right.
        ['2'] =
        [
            [
                0, H,
                float.NaN, float.NaN, CornerHalfT, W, H,
                float.NaN, float.NaN, CornerHalfT, W, H / 2,
                float.NaN, float.NaN, CornerHalfT, 0, H / 2,
                0, 0,
                W, 0,
            ],
        ],

        // Figure II-13. Two runs sharing the middle arm at 1/2 H: 1/2 T bends at the outer right corners
        // and T bends where the arm turns, whose outer arcs pinch at the right notch.
        ['3'] =
        [
            [
                0, H,
                float.NaN, float.NaN, CornerHalfT, W, H,
                float.NaN, float.NaN, CornerT, W, H / 2,
                W / 4, H / 2,
            ],
            [
                W / 4, H / 2,
                float.NaN, float.NaN, CornerT, W, H / 2,
                float.NaN, float.NaN, CornerHalfT, W, 0,
                0, 0,
            ],
        ],

        // Figure II-14. A vertical left arm at 1/8 W to the bar at 3/8 H, and a right stem at 7/8 W ink,
        // topping out at 7/8 H; the spacing reference is corrected by delta X of +1/16 W.
        ['4'] =
        [
            [W / 8, H, W / 8, 3 * H / 8, W, 3 * H / 8],
            [7 * W / 8, 0, 7 * W / 8, 7 * H / 8],
        ],

        // Figure II-15. Bars on H and 1/2 H off a stem at 1/4 W with sharp corners, 1/2 T corners onto
        // the right side, and the base run bending at 1/4 W through a T corner up to the 1/16 H tail cap.
        ['5'] =
        [
            [
                W, H, W / 4, H, W / 4, H / 2,
                float.NaN, float.NaN, CornerHalfT, W, H / 2,
                float.NaN, float.NaN, CornerHalfT, W, 0,
                float.NaN, float.NaN, CornerT, W / 4, 0,
                0, H / 16,
            ],
        ],

        // Figure II-16. A spine on the 1/8 W line with the top bar capped at 1/2 W and a closed bowl
        // between the ink reference lines up to 3/8 H.
        ['6'] =
        [
            [0, 290, 0, H, W / 8, H],
            [W / 2, 0, W, 0, W, 3 * H / 8, 0, 3 * H / 8, 0, 0, W / 2, 0],
        ],

        // Figure II-17. A hook cap at 15/16 H arcing onto the top bar, a square corner at the top right,
        // and the diagonal stepping through 5/8 H and 3/8 H onto the lower stem.
        ['7'] =
        [
            [0, 15 * H / 16, 0, H, W / 2, H],
            [W / 4, H, W, H, W, 5 * H / 8],
            [W, 5 * H / 8, W / 2, 3 * H / 8, W / 2, 0],
        ],

        // Figure II-18. Two closed boxes: the narrow upper bowl between 1/4 W and 3/4 W and the full-width
        // lower bowl.
        ['8'] =
        [
            [
                W / 2, H / 2, W / 4, H / 2, W / 4, H, 3 * W / 4, H, 3 * W / 4, H / 2, W / 2, H / 2,
            ],
            [
                W / 2, H / 2,
                float.NaN, float.NaN, CornerHalfT, W, H / 2,
                float.NaN, float.NaN, CornerHalfT, W, 0,
                float.NaN, float.NaN, CornerHalfT, 0, 0,
                float.NaN, float.NaN, CornerHalfT, 0, H / 2,
                W / 2, H / 2,
            ],
        ],

        // Figure II-19. The digit six rotated a half turn: a spine on the 7/8 W line with the base bar on
        // the baseline capped at 1/2 W, and a closed bowl down to 5/8 H.
        ['9'] =
        [
            [W, 650, W, 0, 7 * W / 8, 0],
            [W / 2, H, 0, H, 0, 5 * H / 8, W, 5 * H / 8, W, H, W / 2, H],
        ],

        // Figure II-25. Diagonals meeting at the apex with a crossbar at 1/4 H ending on the legs.
        ['A'] =
        [
            [0, 0, W / 2, H, W, 0],
            [W / 8, H / 4, 7 * W / 8, H / 4],
        ],

        // Figure II-26. A square-cut stem and two bowls whose right corners round at 3/2 T.
        ['B'] =
        [
            [0, 0, 0, H],
            [
                0, H,
                float.NaN, float.NaN, Corner3T2, W, H,
                float.NaN, float.NaN, Corner3T2, W, H / 2,
                0, H / 2,
            ],
            [
                0, H / 2,
                float.NaN, float.NaN, Corner3T2, W, H / 2,
                float.NaN, float.NaN, Corner3T2, W, 0,
                0, 0,
            ],
        ],

        // Figure II-27. Bars turning at 3/8 W through T corners into 1:2 diagonals tangent to the r1
        // vertex arc, whose leftmost centerline point rides the left reference line at 1/2 H.
        ['C'] =
        [
            [
                W, H,
                float.NaN, float.NaN, CornerT, 3 * W / 8, H,
                26.6f, 580.7f,
                float.NaN, R1, 26.6f, 359.3f,
                float.NaN, float.NaN, CornerT, 3 * W / 8, 0,
                W, 0,
            ],
        ],

        // Figure II-28. Overhanging bars turning at 5/8 W into the r1 vertex arc on the right reference
        // line, with an inset stem at 1/4 W.
        ['D'] =
        [
            [
                0, H,
                float.NaN, float.NaN, CornerT, 5 * W / 8, H,
                523.4f, 580.7f,
                float.NaN, -R1, 523.4f, 359.3f,
                float.NaN, float.NaN, CornerT, 5 * W / 8, 0,
                0, 0,
            ],
            [W / 4, 0, W / 4, H],
        ],

        // Figure II-29. One mitered run from the top bar cap around the square stem corners to the bottom
        // bar cap, with the middle bar to 1/2 W.
        ['E'] =
        [
            [W, H, 0, H, 0, 0, W, 0],
            [0, H / 2, W / 2, H / 2],
        ],

        // Figure II-30. A mitered run from the top bar cap through the square top corner down to the stem
        // base cap; the middle bar sits at 5/8 H and ends at 3/4 W.
        ['F'] =
        [
            [W, H, 0, H, 0, 0],
            [0, 5 * H / 8, 3 * W / 4, 5 * H / 8],
        ],

        // Figure II-31. One stroke from the top-right cap through 3/2 T bends into the straight left side
        // between 3/8 H and 5/8 H, around the base through T corners, up the right side to 3/8 H, and
        // inward to the spur cap at 5/8 W.
        ['G'] =
        [
            [
                W, H,
                float.NaN, float.NaN, CornerT, W / 2, H,
                float.NaN, float.NaN, Corner3T2, 0, 5 * H / 8,
                float.NaN, float.NaN, CornerT, 0, 0,
                float.NaN, float.NaN, CornerT, W, 0,
                W, 3 * H / 8,
                5 * W / 8, 3 * H / 8,
            ],
        ],

        // Figure II-32.
        ['H'] =
        [
            [0, 0, 0, H],
            [W, 0, W, H],
            [0, H / 2, W, H / 2],
        ],

        // Figure II-33.
        ['I'] =
        [
            [0, H, W, H],
            [W / 2, H, W / 2, 0],
            [0, 0, W, 0],
        ],

        // Figure II-34. The stem on the right turns through a semicircular bowl into a riser ending at
        // 3/8 H on the left.
        ['J'] =
        [
            [
                7 * W / 8, H,
                float.NaN, float.NaN, CornerT, 7 * W / 8, 0,
                float.NaN, float.NaN, CornerT, W / 8, 0,
                W / 8, 3 * H / 8,
            ],
        ],

        // Figure II-35. Both diagonals spring from the stem at 1/2 H toward the corners.
        ['K'] =
        [
            [0, 0, 0, H],
            [0, H / 2, W, H],
            [0, H / 2, W, 0],
        ],

        // Figure II-36. Round caps on the stem top and bar end; the base corner miters square.
        ['L'] =
        [
            [0, H, 0, 0, W, 0],
        ],

        // Figure II-37. Edge stems, square at the top and round at the base; each vee arm leaves the stem
        // with a butt face whose upper corner sits on the stem's inner top corner and ends in the round
        // bulb cap at 11/16 H.
        ['M'] =
        [
            [0, 0, 0, H / 2],
            [0, H / 4, 0, H],
            [W, 0, W, H / 2],
            [W, H / 4, W, H],
            [0, H, W / 8, H, W / 2, 11 * H / 16, 7 * W / 8, H, W, H],
            [W / 2, 11 * H / 16, W / 2, 5 * H / 8],
        ],

        // Figure II-38. One run: round cap up the left stem, a short top bar to 1/8 W, the diagonal to
        // 7/8 W on the baseline, a short bottom bar, and the right stem to a round cap; the two bar
        // corners miter square.
        ['N'] =
        [
            [0, 0, 0, H, W / 8, H, 7 * W / 8, 0, W, 0, W, H],
        ],

        // Figure II-39. Four diagonals joined by the r2 vertex arcs at the top and bottom, tangent at
        // 3/8 W and 5/8 W, and the r1 vertex arcs on the sides, tangent at 7/16 H and 9/16 H.
        ['O'] =
        [
            [
                206.25f, 916.2f,
                float.NaN, -R2, 343.75f, 916.2f,
                W - 7.1f, 9 * H / 16,
                float.NaN, -R1, W - 7.1f, 7 * H / 16,
                343.75f, 23.8f,
                float.NaN, -R2, 206.25f, 23.8f,
                7.1f, 7 * H / 16,
                float.NaN, -R1, 7.1f, 9 * H / 16,
                206.25f, 916.2f,
            ],
        ],

        // Figure II-40. A stem with a square bowl closing at 7/16 H, its right corners rounded at T.
        ['P'] =
        [
            [0, 0, 0, H / 2],
            [0, H / 4, 0, H],
            [0, H, W / 2, H],
            [
                W / 4, H,
                float.NaN, float.NaN, CornerT, W, H,
                float.NaN, float.NaN, CornerT, W, 7 * H / 16,
                0, 7 * H / 16,
            ],
        ],

        // Figure II-41. A slanted bowl: straight sides on the reference lines between 3/8 H and 5/8 H,
        // slanted top and bottom runs, r3 corners top-right and bottom-left, 3/2 T corners at the other
        // two bends, and a tail from 3/8 H through 3/4 W on the baseline to a foot cap at the right edge.
        ['Q'] =
        [
            [
                0, 5 * H / 8,
                float.NaN, float.NaN, R3, W, 1053.3f,
                W, 3 * H / 8,
                157.2f, 25.2f,
                float.NaN, -97, 0, 97,
                0, 5 * H / 8,
            ],
            [W / 2, 3 * H / 8, 415, 0, W, 0],
        ],

        // Figure II-42. The P bowl closing at 5/8 H with a leg from 3/8 W on the bowl to the corner.
        ['R'] =
        [
            [0, 0, 0, H / 2],
            [0, H / 4, 0, H],
            [0, H, W / 2, H],
            [
                W / 4, H,
                float.NaN, float.NaN, CornerT, W, H,
                float.NaN, float.NaN, CornerT, W, 5 * H / 8,
                0, 5 * H / 8,
            ],
            [3 * W / 8, 5 * H / 8, W, 0],
        ],

        // Figure II-43. Point-symmetric: terminal curls to 7/8 H and 1/8 H, runs along the top and bottom
        // reference lines, short sides on the vertical reference lines, and r4 corners onto the middle
        // diagonal.
        ['S'] =
        [
            [
                W, 7 * H / 8,
                float.NaN, H / 8, W - (H / 8), H,
                float.NaN, float.NaN, R4, -91.7f, H,
                float.NaN, float.NaN, R4, 641.7f, 0,
                H / 8, 0,
                float.NaN, -(H / 8), 0, H / 8,
            ],
        ],

        // Figure II-44. The bar carries short drops to caps at 7/8 H at both ends.
        ['T'] =
        [
            [0, 7 * H / 8, 0, H],
            [0, H, W, H],
            [W, H, W, 7 * H / 8],
            [W / 2, H, W / 2, 0],
        ],

        // Figure II-45. A flat bottom run with T corners.
        ['U'] =
        [
            [
                0, H,
                float.NaN, float.NaN, CornerT, 0, 0,
                float.NaN, float.NaN, CornerT, W, 0,
                W, H,
            ],
        ],

        // Figure II-46. Vertical drops to 3/4 H before the diagonals meet at the rounded base vertex.
        ['V'] =
        [
            [0, H, 0, 3 * H / 4, W / 2, 0, W, 3 * H / 4, W, H],
        ],

        // Figure II-47. Three verticals: outer stems to H and a middle stroke capped at 5/8 H, joined by
        // U valleys whose 1/2 T corners put their tangents on the 1/8 W and 3/8 W ticks.
        ['W'] =
        [
            [0, H, 0, H / 8, W / 8, 0, (W / 2) - (T / 2), 0],
            [W, H, W, H / 8, 7 * W / 8, 0, (W / 2) + (T / 2), 0],
            [W / 2, 0, W / 2, H / 2],
            [W / 2, 3 * H / 8, W / 2, 5 * H / 8],
        ],

        // Figure II-48. Diagonals corner to corner.
        ['X'] =
        [
            [0, 0, W, H],
            [0, H, W, 0],
        ],

        // Figure II-49. Arm stubs dropping to 7/8 H, the junction at 1/2 H, and the stem to the baseline.
        ['Y'] =
        [
            [0, H, 0, 7 * H / 8, W / 2, H / 2, W / 2, 0],
            [W, H, W, 7 * H / 8, W / 2, H / 2],
        ],

        // Figure II-50. The top bar has a round cap on the left and a square end projecting 1/2 T past
        // the diagonal vertex on the right; the bottom bar mirrors it. The diagonal runs vertex to vertex
        // with its caps buried under the square ends.
        ['Z'] =
        [
            [
                0, 1010,
                620, 1010,
                620, 921,
                122, 70,
                W, 70,
                float.NaN, -70, 620, 0,
                float.NaN, -70, W, -70,
                -70, -70,
                -70, 19,
                427.9f, 870,
                0, 870,
                float.NaN, -70, -70, H,
                float.NaN, -70, 0, 1010,
            ],
        ],

        // Figure II-51. A square centerline between 3/16 H and 5/16 H; the drawing's Y value keeps the
        // ink clear of the baseline.
        ['.'] = [.. SquareMark(3 * W / 8, 3 * H / 16, 5 * W / 8, 5 * H / 16)],

        // Figure II-52. A bar at 3/8 H from 1/4 W turning down at 3/4 W to a foot on the baseline.
        [','] =
        [
            [W / 4, 3 * H / 8, 3 * W / 4, 3 * H / 8, 3 * W / 4, 0],
        ],

        // Figure II-53. Squares between 3/16 H and 5/16 H and between 11/16 H and 13/16 H.
        [':'] =
        [
            .. SquareMark(3 * W / 8, 3 * H / 16, 5 * W / 8, 5 * H / 16),
            .. SquareMark(3 * W / 8, 11 * H / 16, 5 * W / 8, 13 * H / 16),
        ],

        // Figure II-54. The comma elbow with a square between 3/4 H and 7/8 H.
        [';'] =
        [
            [W / 4, 3 * H / 8, 3 * W / 4, 3 * H / 8, 3 * W / 4, 0],
            .. SquareMark(W / 4, 3 * H / 4, W / 2, 7 * H / 8),
        ],

        // Figure II-55. Bars at Y = 3/8 H and 11/16 H.
        ['='] =
        [
            [0, 3 * H / 8, W, 3 * H / 8],
            [0, 11 * H / 16, W, 11 * H / 16],
        ],

        // Figure II-56. A cross centered at 1/2 H whose vertical bar spans W in total.
        ['+'] =
        [
            [0, H / 2, W, H / 2],
            [W / 2, (H / 2) - (W / 2), W / 2, (H / 2) + (W / 2)],
        ],

        // Figure II-66. A closed centerline loop between Y = 7/16 H and 1/2 H across the full width with
        // 1/2 T corners.
        ['-'] =
        [
            [0, 7 * H / 16, 0, H / 2, W, H / 2, W, 7 * H / 16, 0, 7 * H / 16],
            [80, 15 * H / 32, W - 80, 15 * H / 32],
        ],

        // Figure II-57. A diagonal corner to corner.
        ['/'] =
        [
            [0, 0, W, H],
        ],

        // Figure II-58. A vertical arm between 1/8 H and 7/8 H crossed by two diagonals ending at 1/4 H
        // and 3/4 H on the reference lines.
        ['*'] =
        [
            [W / 2, H / 8, W / 2, 7 * H / 8],
            [0, H / 4, W, 3 * H / 4],
            [0, 3 * H / 4, W, H / 4],
        ],

        // Figure II-59. Single ink outline: flat top spanning the full width plus the 1/2 T side
        // overhangs, tapered sides parallel to the vee centerlines, round prong tips at 5/8 H, and the
        // centre notch floor T below the top ink.
        ['"'] =
        [
            [
                -70, 1010, 620, 1010, 620, 933.2f, 549.96f, 574.1f,
                float.NaN, -70, 412.54f, 574.1f,
                354.83f, 870, 195.17f, 870, 137.46f, 574.1f,
                float.NaN, -70, 0.04f, 574.1f,
                -70, 933.2f, -70, 1010,
            ],
        ],

        // Figure II-65. A vertical left arm to a round vertex at 1/2 H with the right arm rising back to
        // the top line, capped flat by a short bar.
        ['\''] =
        [
            [3 * W / 8, H, 5 * W / 8, H],
            [3 * W / 8, 900, 3 * W / 8, H / 2],
            [3 * W / 8, H / 2, 332.05f, 900],
        ],

        // Figure II-62. Centerline squares top-left and bottom-right with the slash from 1/8 H to 7/8 H.
        ['%'] =
        [
            [0, H / 8, W, 7 * H / 8],
            .. SquareMark(0, 7 * H / 8, W / 4, H),
            .. SquareMark(3 * W / 4, 0, W, H / 8),
        ],

        // Figure II-67. A squared S body with bars at 1/4 H, 1/2 H and 3/4 H crossed by a centre stem
        // spanning the full character height.
        ['$'] =
        [
            [W, 3 * H / 4, 0, 3 * H / 4, 0, H / 2, W, H / 2, W, H / 4, 0, H / 4],
            [W / 2, 3 * H / 4, W / 2, H],
            [W / 2, H / 4, W / 2, 0],
        ],

        // Figure II-60. A spine at 1/2 W with bars reaching the right edge and a spur to the left edge at
        // 1/2 H.
        ['('] =
        [
            [
                W, H,
                float.NaN, float.NaN, CornerHalfT, W / 2, H,
                W / 2, 610,
                float.NaN, -CornerT, 135, H / 2,
                0, H / 2,
            ],
            [
                W, 0,
                float.NaN, float.NaN, CornerHalfT, W / 2, 0,
                W / 2, 330,
                float.NaN, CornerT, 135, H / 2,
                0, H / 2,
            ],
        ],

        // Figure II-61. The mirrored closing form.
        [')'] =
        [
            [
                0, H,
                float.NaN, float.NaN, CornerHalfT, W / 2, H,
                W / 2, 610,
                float.NaN, CornerT, 415, H / 2,
                W, H / 2,
            ],
            [
                0, 0,
                float.NaN, float.NaN, CornerHalfT, W / 2, 0,
                W / 2, 330,
                float.NaN, -CornerT, 415, H / 2,
                W, H / 2,
            ],
        ],

        // Figure II-63. A slanted hairpin from a cap at 5/8 H around the top-right, descending to a drop
        // cap at 1/4 H, with the dot at 1/4 W on the baseline.
        ['?'] =
        [
            [0, 5 * H / 8, 3 * W / 4, H, W, 7 * H / 8, W, 3 * H / 4, W / 4, 3 * H / 8, W / 4, H / 4],
            [W / 4, 0, (W / 4) + 0.1f, 0],
        ],

        // Figure II-64. One run: from the cap at (W, 1/4 H) down to the baseline vertex at 1/2 W, around
        // the T fillet at the origin, up and diagonally through the self crossing to 3/4 W at 5/8 H, over
        // the top bar at 7/8 H between two T fillets, and down the left side into the closing diagonal to
        // the cap at (W, 0).
        ['&'] =
        [
            [
                W, H / 4,
                W / 2, 0,
                float.NaN, float.NaN, CornerT, 0, 0,
                0, H / 4,
                3 * W / 4, 5 * H / 8,
                float.NaN, float.NaN, CornerT, 3 * W / 4, 7 * H / 8,
                float.NaN, float.NaN, CornerT, 0, 7 * H / 8,
                0, 5 * H / 8,
                W, 0,
            ],
        ],

        // Figure II-24. The Long Vertical Mark: length L = 1460 with square-cut ends, tied to no
        // baseline, rising from the descender depth through the line box. The break is real ink, centred
        // on the bar, splitting it into equal segments.
        ['|'] =
        [
            [W / 2, D - (T / 2), W / 2, 410.75f],
            [W / 2, 461.25f, W / 2, D - (T / 2) + 1460],
        ],

        // Figure II-21. Symbol Hook: one run with square corners from the 3/8 H cap on the left, along
        // the baseline, up the 1/2 W stem, across the top, and down to the 5/8 H cap on the right.
        ['⑀'] =
        [
            [0, 3 * H / 8, 0, 0, W / 2, 0, W / 2, H, W, H, W, 5 * H / 8],
        ],

        // Figure II-23. Symbol Chair: a full-height right stroke, a seat at 1/2 H turning square down the
        // left front leg to the baseline.
        ['⑁'] =
        [
            [W, 0, W, H],
            [W, H / 2, 0, H / 2, 0, 0],
        ],

        // Figure II-22. Symbol Fork: prongs to 1/2 H joined by a square-ended bar, with the stem
        // continuing to the baseline.
        ['⑂'] =
        [
            [0, H, 0, H / 2],
            [W, H, W, H / 2],
            [0, H / 2, W, H / 2],
            [W / 2, H / 2, W / 2, 0],
        ],

        // Figure II-95. Character Erase: solid ink covering the character cell, inside the figure's MIN
        // and MAX rectangles.
        ['█'] =
        [
            [-70, -70, 620, -70, 620, 1010, -70, 1010, -70, -70],
        ],

        // Figure II-96. Group Erase: a thin continuous line spanning the cell so adjacent marks join.
        ['―'] =
        [
            [-225, H / 2, W + 225, H / 2],
        ],

        // Figure II-68. An arm capped at 13.8 turning through a 13.8 corner into the 2 degree right side
        // ending in the 7.0 foot, with a low octagonal bowl to 35.3 whose bottom flat ends at 34.4 in a
        // cut up to the side.
        ['a'] =
        [
            [138, X, float.NaN, float.NaN, 138, 527.4f, X, W, 0],
            [
                537.7f, 353,
                float.NaN, float.NaN, 138, 0, 353,
                float.NaN, float.NaN, 138, 0, 0,
                float.NaN, float.NaN, 138, 344, 0,
                546, 118,
            ],
        ],

        // Figure II-69. An ascender stem with the bowl attached on its right.
        ['b'] =
        [
            [0, 0, 0, H],
            [
                0, 380,
                float.NaN, float.NaN, 138, 0, 470,
                float.NaN, float.NaN, 138, 206, X,
                float.NaN, float.NaN, 138, 413, X,
                float.NaN, float.NaN, 138, W, 529,
                float.NaN, float.NaN, 138, W, 118,
                float.NaN, float.NaN, 138, 413, 0,
                float.NaN, float.NaN, 138, 206, 0,
                float.NaN, float.NaN, 138, 0, 176,
                0, 266,
            ],
        ],

        // Figure II-70. The open bowl with caps at 3/4 of the width.
        ['c'] =
        [
            [
                W, X,
                float.NaN, float.NaN, 138, 206, X,
                float.NaN, float.NaN, 138, 0, 470,
                float.NaN, float.NaN, 138, 0, 176,
                float.NaN, float.NaN, 138, 206, 0,
                W, 0,
            ],
        ],

        // Figure II-71. The mirrored b.
        ['d'] =
        [
            [W, 0, W, H],
            [
                W, 380,
                float.NaN, float.NaN, 138, W, 470,
                float.NaN, float.NaN, 138, 344, X,
                float.NaN, float.NaN, 138, 138, X,
                float.NaN, float.NaN, 138, 0, 529,
                float.NaN, float.NaN, 138, 0, 118,
                float.NaN, float.NaN, 138, 138, 0,
                float.NaN, float.NaN, 138, 344, 0,
                float.NaN, float.NaN, 138, W, 176,
                W, 266,
            ],
        ],

        // Figure II-72. A full-width bar at 29.4 with the shell opening bottom-right.
        ['e'] =
        [
            [0, 294, W, 294],
            [
                W, 294,
                float.NaN, float.NaN, 138, W, 529,
                float.NaN, float.NaN, 138, 413, X,
                float.NaN, float.NaN, 138, 138, X,
                float.NaN, float.NaN, 138, 0, 529,
                float.NaN, float.NaN, 138, 0, 118,
                float.NaN, float.NaN, 138, 138, 0,
                300, 0,
            ],
            [200, 0, W, 0],
        ],

        // Figure II-73. A stem leaning 2 degrees from 30.9 at the base, hooking right through the 17.6
        // corner to a cap at 48.1, crossed at the x line; the spacing reference is corrected by delta X
        // of +3.4.
        ['f'] =
        [
            [206, 0, float.NaN, float.NaN, 176, 206, H, W, H],
            [69, X, 413, X],
        ],

        // Figure II-74. The bowl with a right stem descending to a hooked tail ending at 13.8.
        ['g'] =
        [
            [
                W, X,
                float.NaN, float.NaN, 138, W, -176,
                float.NaN, float.NaN, 138, 413, -294,
                69, -294,
            ],
            [
                W, 380,
                float.NaN, float.NaN, 138, W, 470,
                float.NaN, float.NaN, 138, 344, X,
                float.NaN, float.NaN, 138, 138, X,
                float.NaN, float.NaN, 138, 0, 529,
                float.NaN, float.NaN, 138, 0, 176,
                float.NaN, float.NaN, 138, 138, 59,
                float.NaN, float.NaN, 138, 344, 59,
                float.NaN, float.NaN, 138, W, 176,
                W, 266,
            ],
        ],

        // Figure II-75. An ascender stem with the arch springing at 47.0; the arch geometry repeats
        // Figure II-81, so the shoulder is the same tangent arc into the 2 degree leg.
        ['h'] =
        [
            [0, 0, 0, H],
            [
                0, 470,
                float.NaN, float.NaN, 138, W / 2, X,
                394.1f, X,
                float.NaN, -138, 532.1f, 512.8f,
                W, 0,
            ],
        ],

        // Figure II-76. A flag capped at 6.9 into the stem at 27.5, a base bar from 6.9 to 48.1, and the
        // dot: a closed centerline square between 20.6 and 27.5 and between 94.0 and 99.9.
        ['i'] =
        [
            [69, X, W / 2, X, W / 2, 0],
            [69, 0, 481, 0],
            .. SquareMark(206, 940, W / 2, 999),
        ],

        // Figure II-77. A flag capped at 13.8 into the stem at 41.3, a hook with 13.8 corners whose run
        // sits at -22.4 rising to a terminal on the left reference line, and the dot square between 34.4
        // and 41.3; the spacing reference is corrected by delta X of -6.9.
        ['j'] =
        [
            [138, X, 413, X],
            [
                413, X,
                float.NaN, float.NaN, 138, 413, -294,
                138, -294,
                float.NaN, -138, 0, -176,
            ],
            .. SquareMark(344, 940, 413, 999),
        ],

        // Figure II-78. Diagonals from the stem at 35.3 toward the x line and the baseline corner.
        ['k'] =
        [
            [0, 0, 0, H],
            [0, 235, 481, X],
            [138, 353, W, 0],
        ],

        // Figure II-79. The ascender stem with a base bar.
        ['l'] =
        [
            [69, H, W / 2, H, W / 2, 0],
            [69, 0, 481, 0],
        ],

        // Figure II-80. Stems lean two degrees from baseline centres 1.5 and 56.5; the r100 and r97
        // arches crest at 13.8 and 41.3 on the x line and run down straight tangents into the next leg
        // at 52.9.
        ['m'] =
        [
            [-15, 0, -15, X],
            [-15, 529, 112.4f, 631.1f, float.NaN, -100, W / 2, 553.1f],
            [W / 2, 553.1f, W / 2, 0],
            [W / 2, 529, 383.4f, 627.4f, float.NaN, -97, 545.5f, 559.1f, 565, 0],
        ],

        // Figure II-81. The left stem with the arch springing at 47.0.
        ['n'] =
        [
            [0, 0, 0, X],
            [
                0, 470,
                float.NaN, float.NaN, 138, W / 2, X,
                394.1f, X,
                float.NaN, -138, 532.1f, 512.8f,
                W, 0,
            ],
        ],

        // Figure II-82. An octagonal bowl: the centerline carries the drawing's corner cuts between 13.8
        // and 41.3 horizontally and 11.8 and 52.9 vertically, leaving the counter angled while the joins
        // round the outer corners.
        ['o'] =
        [
            [
                W / 2, X,
                float.NaN, float.NaN, 138, 413, X,
                float.NaN, float.NaN, 138, W, 529,
                float.NaN, float.NaN, 138, W, 118,
                float.NaN, float.NaN, 138, 413, 0,
                float.NaN, float.NaN, 138, 138, 0,
                float.NaN, float.NaN, 138, 0, 118,
                float.NaN, float.NaN, 138, 0, 529,
                float.NaN, float.NaN, 138, 138, X,
                W / 2, X,
            ],
        ],

        // Figure II-83. The bowl with a left stem rising to 67.55 and descending to the tail depth.
        ['p'] =
        [
            [0, -294, 0, 675.5f],
            [
                0, 470,
                float.NaN, float.NaN, 138, 206, 675.5f,
                float.NaN, float.NaN, 138, 344, 675.5f,
                float.NaN, float.NaN, 138, W, 498,
                float.NaN, float.NaN, 138, W, 148,
                float.NaN, float.NaN, 138, 344, -29.5f,
                float.NaN, float.NaN, 138, 206, -29.5f,
                0, 176,
            ],
        ],

        // Figure II-84. The mirrored p.
        ['q'] =
        [
            [W, -294, W, X],
            [
                W, 470,
                float.NaN, float.NaN, 138, 344, X,
                float.NaN, float.NaN, 138, 138, X,
                float.NaN, float.NaN, 138, 0, 529,
                float.NaN, float.NaN, 138, 0, 118,
                float.NaN, float.NaN, 138, 138, 0,
                float.NaN, float.NaN, 138, 344, 0,
                W, 176,
            ],
        ],

        // Figure II-85. The stem with an arm curling down to a cap at 41.1.
        ['r'] =
        [
            [0, 0, 0, X],
            [
                0, 411,
                float.NaN, float.NaN, 138, W / 2, X,
                412, X,
                float.NaN, -138, W, 508,
                W, 470,
            ],
        ],

        // Figure II-86. The scaled S form: terminal curls of 13.8 radius, sides on the vertical reference
        // lines, and the middle diagonal.
        ['s'] =
        [
            [
                535, 588,
                float.NaN, 138, 422.6f, X,
                147.7f, X,
                float.NaN, 138, 15, 470,
                465.7f, 271,
                float.NaN, -141.6f, 408.4f, 0,
                127.4f, 0,
                float.NaN, -138, 15, 59,
            ],
        ],

        // Figure II-87. A stem rising to 88.1, crossed at the x line, with the base turning through a
        // U-arc dipping to -13.8 ink and rising to a tip at 11.8.
        ['t'] =
        [
            [0, X, 481, X],
            [
                138, 881, 138, 138,
                float.NaN, 138, 276, 0,
                413.5f, 0,
                float.NaN, 138, W, 118,
            ],
        ],

        // Figure II-88. The n form flipped, with the right stem running to its own base cap.
        ['u'] =
        [
            [W, 0, W, X],
            [
                W, 176,
                float.NaN, float.NaN, 138, W / 2, 0,
                155.9f, 0,
                float.NaN, -138, 17.9f, 133.2f,
                0, X,
            ],
        ],

        // Figure II-89. Legs to a short base flat between 23.2 and 31.8.
        ['v'] =
        [
            [0, X, 0, 529, 232, 0, 318, 0, W, 529, W, X],
        ],

        // Figure II-90. Outer stems leaning 2 degrees bending at 23.5 into flat valley bottoms between
        // 6.9 and 13.8 and between 41.3 and 48.1, with the centre peak at 41.1.
        ['w'] =
        [
            [-14, X, 0, 235, 69, 0, 138, 0, W / 2, 235, 413, 0, 481, 0, W, 235, W + 14, X],
            [W / 2, 235, W / 2, 411],
        ],

        // Figure II-91. Diagonals between 1.5 and 53.5.
        ['x'] =
        [
            [15, 0, 535, X],
            [15, X, 535, 0],
        ],

        // Figure II-92. The left arm meets the descending right diagonal, which hooks left at the tail.
        ['y'] =
        [
            [0, X, 0, 529, 232, 0],
            [W, X, W, 529, 189, -294, 0, -294],
        ],

        // Figure II-93. The Z form at x height with round caps on all bar ends.
        ['z'] =
        [
            [34, X, W, X, W, 588, 0, 59, 0, 0, W, 0],
        ],
    };

    /// <summary>
    /// Gets, per character, the indices of strokes whose terminals are square: they receive projecting
    /// square caps that extend T/2 past the centerline endpoint, mirroring how round caps project. Mixed
    /// terminals on one path are drawn as two overlapping strokes, one per cap style.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> SquareStrokes { get; } = new Dictionary<char, int[]>
    {
        ['B'] = [0],
        ['M'] = [1, 3],
        ['T'] = [1],
        ['P'] = [1, 2],
        ['R'] = [1, 2],
        ['⑂'] = [2],
    };

    /// <summary>
    /// Gets, per character, the indices of strokes cut off exactly at their endpoints with no cap
    /// projection: the attachment strokes whose butt faces sit on another stroke's ink corners.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> ButtStrokes { get; } = new Dictionary<char, int[]>
    {
        ['W'] = [2],
        ['|'] = [0, 1],
        ['―'] = [0],
        ['%'] = [1, 2, 7, 8],
        ['.'] = [0, 1],
        [':'] = [0, 1, 6, 7],
        [';'] = [1, 2],
        ['a'] = [1],
        ['b'] = [1],
        ['d'] = [1],
        ['e'] = [1],
        ['g'] = [1],
        ['i'] = [2, 3],
        ['j'] = [2, 3],
    };

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with butt ends and sharp miter joins: the
    /// vees whose drawings dimension the outer vertex at the exact miter point.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> MiterStrokes { get; } = new Dictionary<char, int[]>
    {
        ['M'] = [4],
    };

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with round terminal caps and sharp miter
    /// joins: paths whose drawings show square outer corners between straight runs.
    /// </summary>
    public static IReadOnlyDictionary<char, int[]> MiterRoundStrokes { get; } = new Dictionary<char, int[]>
    {
        ['1'] = [0],
        ['2'] = [0],
        ['4'] = [0],
        ['5'] = [0],
        ['7'] = [1],
        ['E'] = [0],
        ['F'] = [0],
        ['G'] = [0],
        ['L'] = [0],
        ['N'] = [0],
        ['⑀'] = [0],
        ['⑁'] = [1],
    };

    /// <summary>
    /// Gets per-stroke width overrides keyed by character and stroke index, used for the dots and marks
    /// whose ink is shallower than a full stroke. The value zero marks a stroke that is already a filled
    /// ink polygon rather than a centerline to stroke.
    /// </summary>
    public static IReadOnlyDictionary<(char Character, int Stroke), float> StrokeWidthOverrides { get; } =
        new Dictionary<(char Character, int Stroke), float>
        {
            [('.', 0)] = 257.5f,
            [('.', 1)] = 277.5f,
            [(':', 0)] = 257.5f,
            [(':', 1)] = 277.5f,
            [(':', 6)] = 257.5f,
            [(':', 7)] = 277.5f,
            [(';', 1)] = 257.5f,
            [(';', 2)] = 277.5f,
            [('%', 1)] = 257.5f,
            [('%', 2)] = 277.5f,
            [('%', 7)] = 257.5f,
            [('%', 8)] = 277.5f,
            [('█', 0)] = 0,
            [('Z', 0)] = 0,
            [('"', 0)] = 0,
            [('i', 2)] = 199,
            [('i', 3)] = 209,
            [('j', 2)] = 199,
            [('j', 3)] = 209,
        };

    /// <summary>
    /// Gets the design definition the generator consumes.
    /// </summary>
    public static FontDesign Design { get; } = new()
    {
        Name = "OcrA",
        FamilyName = "SixLabors OCRA",
        DataSummary = "Built clean-room from the dimensioned character drawings of FIPS PUB 32 (1974).",
        UnitsPerEm = UnitsPerEm,
        H = H,
        W = W,
        Descender = D,
        DefaultStrokeWidth = T,
        CutTerminals = false,
        Skeletons = Skeletons,
        SquareStrokes = SquareStrokes,
        ButtStrokes = ButtStrokes,
        MiterStrokes = MiterStrokes,
        MiterRoundStrokes = MiterRoundStrokes,
        StrokeWidths = new Dictionary<char, float>(),
        StrokeWidthOverrides = StrokeWidthOverrides,
        Expectations = SpecChecks.OcrA,
    };

    /// <summary>
    /// Builds the six strokes of a rounded square mark of full stroke ink: two butt-cut bars crossing to
    /// form the body and four round dots supplying the corner radii. Loop-free so the outline union never
    /// sees a degenerate inner offset.
    /// </summary>
    /// <param name="minX">The centerline left edge.</param>
    /// <param name="minY">The centerline bottom edge.</param>
    /// <param name="maxX">The centerline right edge.</param>
    /// <param name="maxY">The centerline top edge.</param>
    /// <returns>The strokes.</returns>
    private static float[][] SquareMark(float minX, float minY, float maxX, float maxY) =>
    [
        [minX, (minY + maxY) / 2, maxX, (minY + maxY) / 2],
        [(minX + maxX) / 2, minY, (minX + maxX) / 2, maxY],
        [minX, minY, minX + 0.1f, minY],
        [maxX, minY, maxX + 0.1f, minY],
        [minX, maxY, minX + 0.1f, maxY],
        [maxX, maxY, maxX + 0.1f, maxY],
    ];
}
