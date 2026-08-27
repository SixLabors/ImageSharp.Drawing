// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.FontGenerator;

/// <summary>
/// The regression gate for the generated OCR-A outlines, run on every build. Each glyph asserts its
/// exact ink bounding box (the drawings dimension every extreme: the reference lines plus the T/2 cap
/// projection, and per-glyph values such as the -13.8 tail of t or the 99.9 dot top of i) plus probe
/// points that must be inside or outside the ink, catching arc sweep and construction errors the
/// bounding box cannot see. A failure means an edit moved accepted ink: an extreme drifted, a counter
/// filled in, or a stroke vanished. The expectations were transcribed from the same drawings as the
/// skeletons, so they cannot catch a misreading of the drawings themselves; full-size visual review
/// against the figures remains the acceptance gate for shape.
/// </summary>
internal static class SpecChecks
{
    /// <summary>
    /// The permitted difference in design units between an expected and a measured ink extreme. Arc
    /// flattening and the sub-quantum stroke width perturbation that keeps union edges apart move the
    /// measured extremes by fractions of a unit; the drawings dimension nothing finer than whole units.
    /// </summary>
    private const float BoundsTolerance = 4;

    /// <summary>
    /// The per-glyph expectations. Bounds are [minX, maxX, minY, maxY] of the ink in design units, or an
    /// empty array when the figure leaves an extreme underdetermined; probes are [x, y] points. Inked
    /// probes assert ink where a stroke must pass and blank probes assert holes in counters and gaps, so
    /// errors that preserve the bounding box still trip a check.
    /// </summary>
    private static readonly Dictionary<char, (float[] Bounds, float[][] Inked, float[][] Blank)> Expectations = new()
    {
        ['0'] = ([-70, 620, -70, 1010], [[275, 940], [275, 0], [0, 470], [550, 470]], [[275, 470]]),
        ['1'] = ([-70, 620, -70, 1010], [[137, 940], [275, 470], [275, 0], [550, 300]], [[450, 600]]),
        ['2'] = ([-70, 620, -70, 1010], [[275, 940], [275, 470], [275, 0], [0, 235], [550, 705]], [[100, 700], [450, 235]]),
        ['3'] = ([-70, 620, -70, 1010], [[275, 940], [275, 470], [275, 0], [550, 700]], [[100, 700], [100, 235]]),
        ['4'] = ([-1.25f, 620, -70, 1010], [[68.75f, 600], [550, 600], [275, 352.5f]], [[275, 600]]),
        ['5'] = ([-70, 620, -70, 1010], [[300, 975], [137.5f, 700], [300, 500], [550, 235], [300, -30], [0, 59]], [[300, 700], [300, 180], [-40, 940]]),
        ['6'] = ([-70, 620, -70, 1010], [[0, 500], [68.75f, 940], [275, 0], [550, 176]], [[275, 176], [275, 600]]),
        ['7'] = ([-70, 620, -70, 1010], [[0, 900], [275, 940], [550, 750], [275, 100]], [[100, 300], [450, 200]]),
        ['8'] = ([-70, 620, -70, 1010], [[275, 470], [275, 940], [275, 0], [0, 235], [550, 235]], [[275, 705], [275, 235]]),
        ['9'] = ([-70, 620, -70, 1010], [[550, 500], [481.25f, 0], [275, 940], [0, 750]], [[275, 764], [275, 300]]),
        ['A'] = ([-70, 620, -70, 1010], [[275, 940], [275, 235], [30, 100], [520, 100]], [[275, 100], [275, 500]]),
        ['B'] = ([-70, 620, -70, 1010], [[0, 470], [275, 940], [275, 470], [275, 0], [550, 700], [550, 235]], [[275, 705], [275, 235]]),
        ['C'] = ([-70, 620, -70, 1010], [[0, 470], [275, 940], [275, 0], [550, 940], [550, 0]], [[275, 470], [550, 470]]),
        ['D'] = ([-70, 620, -70, 1010], [[137.5f, 470], [275, 940], [275, 0], [550, 470]], [[340, 470], [30, 470]]),
        ['E'] = ([-70, 620, -70, 1010], [[0, 470], [275, 940], [275, 470], [275, 0], [550, 940]], [[450, 470], [275, 705], [275, 235]]),
        ['F'] = ([-70, 620, -70, 1010], [[0, 470], [275, 940], [275, 587.5f], [412, 587.5f]], [[450, 300], [275, 300], [275, 760]]),
        ['G'] = ([-70, 620, -70, 1010], [[275, 940], [0, 470], [275, 0], [550, 200], [400, 352.5f]], [[275, 600], [450, 500]]),
        ['H'] = ([-70, 620, -70, 1010], [[0, 470], [550, 470], [275, 470]], [[275, 700], [275, 235]]),
        ['I'] = ([-70, 620, -70, 1010], [[275, 470], [275, 940], [275, 0], [0, 940], [550, 0]], [[100, 470], [450, 470]]),
        ['J'] = ([-1.25f, 551.25f, -70, 1010], [[481.25f, 700], [275, 0], [68.75f, 300]], [[275, 400], [275, 700]]),
        ['K'] = ([-70, 620, -70, 1010], [[0, 470], [275, 705], [275, 235]], [[450, 470], [300, 900]]),
        ['L'] = ([-70, 620, -70, 1010], [[0, 470], [275, 0], [550, 0]], [[275, 470]]),
        ['M'] = ([-70, 620, -70, 1010], [[0, 470], [550, 470], [275, 660], [275, 530], [137, 800], [0, 990]], [[275, 300], [275, 880]]),
        ['N'] = ([-70, 620, -70, 1010], [[0, 470], [550, 470], [275, 470]], [[200, 235], [350, 705]]),
        ['O'] = ([-70, 620, -70, 1010], [[0, 470], [550, 470], [275, 940], [275, 0]], [[275, 470]]),
        ['P'] = ([-70, 620, -70, 1010], [[0, 470], [275, 940], [275, 411.25f], [550, 700]], [[275, 700], [350, 150]]),
        ['Q'] = ([-70, 620, -70, 1010], [[400, 940], [550, 470], [0, 300], [430, 25]], [[250, 550]]),
        ['R'] = ([-70, 620, -70, 1010], [[0, 470], [275, 940], [275, 587.5f], [378, 294]], [[275, 760], [150, 235]]),
        ['S'] = ([-70, 620, -70, 1010], [[275, 470], [300, 1000], [550, 822.5f], [0, 117.5f], [550, 765], [300, -60], [550, 87]], [[350, 700], [200, 200], [275, 700], [275, 200], [432, 822], [117, 117]]),
        ['T'] = ([-70, 620, -70, 1010], [[275, 470], [0, 870], [550, 870], [275, 940]], [[100, 400], [450, 400]]),
        ['U'] = ([-70, 620, -70, 1010], [[0, 470], [550, 470], [275, 0]], [[275, 470]]),
        ['V'] = ([-70, 620, -70, 1010], [[0, 800], [137.5f, 352.5f], [412.5f, 352.5f]], [[275, 600], [60, 100]]),
        ['W'] = ([-70, 620, -70, 1010], [[0, 470], [550, 470], [275, 400], [137.5f, -30], [412.5f, -30]], [[137.5f, 500], [412.5f, 500], [275, -60], [275, 800]]),
        ['X'] = ([-70, 620, -70, 1010], [[275, 470], [50, 85], [500, 855]], [[275, 100], [275, 840]]),
        ['Y'] = ([-70, 620, -70, 1010], [[275, 470], [275, 100], [0, 900], [550, 900]], [[100, 300], [450, 300]]),
        ['Z'] = ([-70, 620, -70, 1010], [[275, 940], [275, 470], [275, 0], [590, 940], [-40, 0], [590, 0]], [[60, 500], [500, 200]]),
        ['.'] = ([136.25f, 413.75f, 106.25f, 363.75f], [[275, 235]], [[275, 600]]),
        [','] = ([67.5f, 482.5f, -70, 422.5f], [[275, 352.5f], [412.5f, 100]], [[200, 100]]),
        [':'] = ([136.25f, 413.75f, 106.25f, 833.75f], [[275, 235], [275, 705]], [[275, 470]]),
        [';'] = ([67.5f, 482.5f, -70, 892.5f], [[275, 352.5f], [206, 764]], [[275, 550]]),
        ['='] = ([-70, 620, 282.5f, 716.25f], [[275, 352.5f], [275, 646.25f]], [[275, 500]]),
        ['+'] = ([-70, 620, 125, 815], [[275, 470], [275, 200], [275, 740], [0, 470], [550, 470]], [[100, 200]]),
        ['-'] = ([-70, 620, 341.25f, 540], [[275, 440], [0, 440], [550, 440]], [[275, 600]]),
        ['/'] = ([-70, 620, -70, 1010], [[275, 470]], [[100, 700], [450, 235]]),
        ['*'] = ([-70, 620, 47.5f, 892.5f], [[275, 470], [275, 150], [275, 790]], [[100, 470]]),
        ['"'] = ([-70, 620, 517.5f, 1010], [[275, 940], [37, 750], [513, 750], [68.75f, 620], [481.25f, 620]], [[275, 700]]),
        ['\''] = ([136, 414, 400, 1010], [[206.25f, 700], [275, 940], [280, 600]], [[450, 800]]),
        ['%'] = ([-70, 620, -70, 1010], [[68.75f, 880], [481.25f, 60], [275, 470]], [[450, 880], [100, 60]]),
        ['$'] = ([-70, 620, -70, 1010], [[275, 470], [275, 970], [0, 600]], [[100, 100], [450, 850]]),
        ['('] = ([-70, 620, -70, 1010], [[275, 470], [550, 940], [550, 0], [0, 470]], [[450, 470]]),
        [')'] = ([-70, 620, -70, 1010], [[275, 470], [0, 940], [0, 0], [550, 470]], [[100, 470]]),
        ['?'] = ([-70, 620, -70, 1010], [[137.5f, 0], [0, 587.5f], [275, 822.5f], [412, 900], [550, 705], [137.5f, 300]], [[275, 300], [400, 150]]),
        ['&'] = ([-70, 620, -70, 892.5f], [[275, 0], [0, 300], [206, 822.5f], [412.5f, 650], [183, 392], [550, 30]], [[450, 400], [100, 700]]),
        ['|'] = ([205, 345, -294, 1166], [[275, 0], [275, 1000], [275, -250]], [[100, 470], [275, 436]]),
        ['⑀'] = ([-70, 620, -70, 1010], [[275, 470], [550, 700], [0, 176], [275, 940], [275, 0]], [[450, 176]]),
        ['⑁'] = ([-70, 620, -70, 1010], [[550, 470], [0, 235], [-30, 470]], [[300, 150]]),
        ['⑂'] = ([-70, 620, -70, 1010], [[0, 700], [550, 700], [275, 470], [275, 100]], [[275, 700]]),
        ['█'] = ([-70, 620, -70, 1010], [[275, 470], [0, 470], [550, 470], [275, 940], [275, 0]], []),
        ['―'] = ([-225, 775, 400, 540], [[275, 470], [-200, 470], [750, 470]], [[275, 600]]),
        ['a'] = ([-70, 620, -70, 716], [[138, 646], [275, 353], [275, 0], [0, 176], [470, 400]], [[200, 176], [250, 500]]),
        ['b'] = ([-70, 620, -70, 1010], [[0, 800], [275, 646], [275, 0], [550, 300]], [[275, 300]]),
        ['c'] = ([-70, 620, -70, 716], [[275, 646], [275, 0], [0, 300]], [[275, 300], [480, 300]]),
        ['d'] = ([-70, 620, -70, 1010], [[550, 800], [275, 646], [275, 0], [0, 300]], [[275, 300]]),
        ['e'] = ([-70, 620, -70, 716], [[275, 294], [275, 646], [275, 0], [0, 470]], [[275, 450], [480, 100]]),
        ['f'] = ([-1, 620, -70, 1010], [[206, 300], [275, 646], [450, 940]], [[60, 300], [520, 646]]),
        ['g'] = ([-70, 620, -364, 716], [[275, 646], [275, 0], [550, 300], [550, -100], [300, -294]], [[275, 300], [100, -150]]),
        ['h'] = ([-70, 620, -70, 1010], [[0, 800], [0, 300], [275, 646], [545, 300]], [[275, 300]]),
        ['i'] = ([-1, 551, -70, 1069], [[275, 300], [240, 970], [275, 0], [100, 646]], [[275, 800], [100, 300]]),
        ['j'] = ([-70, 483, -364, 1069], [[413, 300], [378, 970], [200, -294], [200, 646]], [[200, 300], [200, -50]]),
        ['k'] = ([-70, 620, -70, 1010], [[0, 800], [0, 300], [300, 536], [300, 161]], [[450, 353]]),
        ['l'] = ([-1, 551, -70, 1010], [[275, 600], [275, 0], [100, 0]], [[100, 400]]),
        ['m'] = ([-85, 635, -70, 725], [[-15, 50], [-15, 600], [275, 300], [565, 50], [178, 700], [454, 700]], [[140, 300], [420, 300], [314, 690]]),
        ['n'] = ([-70, 620, -70, 716], [[0, 300], [550, 300], [300, 646]], [[275, 300]]),
        ['o'] = ([-70, 620, -70, 716], [[0, 300], [550, 300], [275, 646], [275, 0]], [[275, 300]]),
        ['p'] = ([-70, 620, -364, 745.5f], [[0, 300], [0, -200], [550, 300], [275, 675.5f], [275, -29.5f]], [[275, 300]]),
        ['q'] = ([-70, 620, -364, 716], [[550, -200], [0, 300], [275, 646], [275, 0], [550, 300]], [[275, 300]]),
        ['r'] = ([-70, 620, -70, 716], [[0, 300], [300, 646], [550, 500]], [[300, 300]]),
        ['s'] = ([-61, 616, -70, 716], [[275, 323], [300, 700], [250, -55], [535, 588], [15, 59], [560, 138]], [[275, 500], [275, 150], [422, 508]]),
        ['t'] = ([-70, 620, -70, 951], [[275, 646], [138, 300], [345, -30], [550, 118], [138, 880]], [[300, 300], [0, 300]]),
        ['u'] = ([-70, 620, -70, 716], [[0, 300], [550, 300], [275, 0]], [[275, 300]]),
        ['v'] = ([-70, 620, -70, 716], [[0, 500], [550, 500], [275, 0]], [[275, 500]]),
        ['w'] = ([-84, 634, -70, 716], [[-10, 600], [560, 600], [275, 350], [275, 411], [103, 0], [447, 0]], [[150, 500], [275, 550]]),
        ['x'] = ([-55, 605, -70, 716], [[275, 323], [480, 578]], [[275, 100], [275, 550]]),
        ['y'] = ([-70, 620, -364, 716], [[0, 600], [550, 600], [100, -294], [350, 100]], [[100, 100]]),
        ['z'] = ([-70, 620, -70, 716], [[275, 646], [275, 323], [275, 0]], [[60, 300], [490, 350]]),
    };

    /// <summary>
    /// Verifies one glyph's flattened contours against its expectations, appending any deviation to
    /// <paramref name="failures"/> as a line of glyph, feature, expected and actual values. A character
    /// with no recorded expectation is itself a failure, so a new glyph cannot land unchecked.
    /// </summary>
    /// <param name="character">The character being verified.</param>
    /// <param name="contours">The flattened outline contours in design units.</param>
    /// <param name="failures">The failure sink.</param>
    public static void Verify(char character, IReadOnlyList<IReadOnlyList<Vector2>> contours, List<string> failures)
    {
        if (!Expectations.TryGetValue(character, out (float[] Bounds, float[][] Inked, float[][] Blank) spec))
        {
            failures.Add($"U+{(int)character:X4} '{character}': no spec expectation recorded");
            return;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (IReadOnlyList<Vector2> contour in contours)
        {
            foreach (Vector2 point in contour)
            {
                minX = MathF.Min(minX, point.X);
                maxX = MathF.Max(maxX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxY = MathF.Max(maxY, point.Y);
            }
        }

        if (spec.Bounds.Length == 4)
        {
            Check(failures, character, "minX", spec.Bounds[0], minX);
            Check(failures, character, "maxX", spec.Bounds[1], maxX);
            Check(failures, character, "minY", spec.Bounds[2], minY);
            Check(failures, character, "maxY", spec.Bounds[3], maxY);
        }

        foreach (float[] point in spec.Inked)
        {
            if (!Contains(contours, new Vector2(point[0], point[1])))
            {
                failures.Add($"'{character}': expected ink at ({point[0]}, {point[1]}) but found none");
            }
        }

        foreach (float[] point in spec.Blank)
        {
            if (Contains(contours, new Vector2(point[0], point[1])))
            {
                failures.Add($"'{character}': expected no ink at ({point[0]}, {point[1]}) but found ink");
            }
        }
    }

    /// <summary>
    /// Compares one measured ink extreme against its expected value within <see cref="BoundsTolerance"/>.
    /// </summary>
    /// <param name="failures">The failure sink.</param>
    /// <param name="character">The character being verified.</param>
    /// <param name="feature">The name of the extreme being compared.</param>
    /// <param name="expected">The expected value in design units.</param>
    /// <param name="actual">The measured value in design units.</param>
    private static void Check(List<string> failures, char character, string feature, float expected, float actual)
    {
        if (MathF.Abs(expected - actual) > BoundsTolerance)
        {
            failures.Add($"'{character}': {feature} expected {expected} but measured {actual:F1}");
        }
    }

    /// <summary>
    /// Determines whether a point lies inside the outline ink by the non-zero winding rule, the fill
    /// rule the TrueType rasteriser applies to the glyph.
    /// </summary>
    /// <param name="contours">The flattened outline contours in design units.</param>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> when the point is inside the ink; otherwise <see langword="false"/>.</returns>
    private static bool Contains(IReadOnlyList<IReadOnlyList<Vector2>> contours, Vector2 point)
    {
        int winding = 0;
        foreach (IReadOnlyList<Vector2> contour in contours)
        {
            for (int i = 0; i < contour.Count; i++)
            {
                Vector2 a = contour[i];
                Vector2 b = contour[(i + 1) % contour.Count];
                if (a.Y <= point.Y)
                {
                    if (b.Y > point.Y && Cross(a, b, point) > 0)
                    {
                        winding++;
                    }
                }
                else if (b.Y <= point.Y && Cross(a, b, point) < 0)
                {
                    winding--;
                }
            }
        }

        return winding != 0;
    }

    /// <summary>
    /// Computes the z component of the cross product of the edge from <paramref name="a"/> to
    /// <paramref name="b"/> with the vector from <paramref name="a"/> to <paramref name="point"/>,
    /// positive when the point lies left of the edge.
    /// </summary>
    /// <param name="a">The edge start.</param>
    /// <param name="b">The edge end.</param>
    /// <param name="point">The point to classify.</param>
    /// <returns>The signed area term.</returns>
    private static float Cross(Vector2 a, Vector2 b, Vector2 point) =>
        ((b.X - a.X) * (point.Y - a.Y)) - ((point.X - a.X) * (b.Y - a.Y));
}
