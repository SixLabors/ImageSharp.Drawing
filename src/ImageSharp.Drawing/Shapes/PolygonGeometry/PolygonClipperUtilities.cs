// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

internal static class PolygonClipperUtilities
{
    /// <summary>
    /// Computes the signed area of a path using the shoelace formula.
    /// </summary>
    /// <remarks>
    /// Positive values indicate clockwise orientation in screen space.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SignedArea(PathF path)
    {
        // https://en.wikipedia.org/wiki/Shoelace_formula
        float a = 0F;
        if (path.Count < 3)
        {
            return a;
        }

        // Sum over edges (prev -> current).
        Vector2 prevPt = path[^1];
        for (int i = 0; i < path.Count; i++)
        {
            Vector2 pt = path[i];
            a += (prevPt.Y + pt.Y) * (prevPt.X - pt.X);
            prevPt = pt;
        }

        return a * .5F;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(Vector2 vec1, Vector2 vec2)
        => Vector2.Dot(vec1, vec2);

    /// <summary>
    /// Returns the dot product of the segments (pt1->pt2) and (pt2->pt3).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        => Vector2.Dot(pt2 - pt1, pt3 - pt2);

    /// <summary>
    /// Returns the 2D cross product magnitude of <paramref name="vec1" /> and <paramref name="vec2" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CrossProduct(Vector2 vec1, Vector2 vec2)
        => (vec1.Y * vec2.X) - (vec2.Y * vec1.X);

    /// <summary>
    /// Returns the cross product of the segments (pt1->pt2) and (pt2->pt3).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CrossProduct(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        => ((pt2.X - pt1.X) * (pt3.Y - pt2.Y)) - ((pt2.Y - pt1.Y) * (pt3.X - pt2.X));

    /// <summary>
    /// Returns the squared perpendicular distance from a point to a line segment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PerpendicDistFromLineSqrd(Vector2 pt, Vector2 line1, Vector2 line2)
    {
        Vector2 ab = pt - line1;
        Vector2 cd = line2 - line1;
        if (cd == Vector2.Zero)
        {
            return 0;
        }

        float cross = CrossProduct(cd, ab);
        return (cross * cross) / DotProduct(cd, cd);
    }

    /// <summary>
    /// Returns true when two segments intersect.
    /// </summary>
    /// <param name="seg1a">First endpoint of segment 1.</param>
    /// <param name="seg1b">Second endpoint of segment 1.</param>
    /// <param name="seg2a">First endpoint of segment 2.</param>
    /// <param name="seg2b">Second endpoint of segment 2.</param>
    /// <param name="inclusive">If true, allows shared endpoints; if false, requires a proper intersection.</param>
    public static bool SegsIntersect(Vector2 seg1a, Vector2 seg1b, Vector2 seg2a, Vector2 seg2b, bool inclusive = false)
    {
        if (inclusive)
        {
            float res1 = CrossProduct(seg1a, seg2a, seg2b);
            float res2 = CrossProduct(seg1b, seg2a, seg2b);
            if (res1 * res2 > 0)
            {
                return false;
            }

            float res3 = CrossProduct(seg2a, seg1a, seg1b);
            float res4 = CrossProduct(seg2b, seg1a, seg1b);
            if (res3 * res4 > 0)
            {
                return false;
            }

            // ensure NOT collinear
            return res1 != 0 || res2 != 0 || res3 != 0 || res4 != 0;
        }

        return (CrossProduct(seg1a, seg2a, seg2b) * CrossProduct(seg1b, seg2a, seg2b) < 0)
            && (CrossProduct(seg2a, seg1a, seg1b) * CrossProduct(seg2b, seg1a, seg1b) < 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetLineIntersectPoint(Vector2 ln1a, Vector2 ln1b, Vector2 ln2a, Vector2 ln2b, out Vector2 ip)
    {
        Vector2 dxy1 = ln1b - ln1a;
        Vector2 dxy2 = ln2b - ln2a;
        float det = CrossProduct(dxy1, dxy2);
        if (det == 0F)
        {
            ip = default;
            return false;
        }

        float t = (((ln1a.X - ln2a.X) * dxy2.Y) - ((ln1a.Y - ln2a.Y) * dxy2.X)) / det;

        // Clamp intersection to the segment endpoints.
        if (t <= 0F)
        {
            ip = ln1a;
        }
        else if (t >= 1F)
        {
            ip = ln1b;
        }
        else
        {
            ip = ln1a + (t * dxy1);
        }

        return true;
    }

    /// <summary>
    /// Returns the closest point on a segment to an external point.
    /// </summary>
    /// <param name="offPt">The point to project onto the segment.</param>
    /// <param name="seg1">First endpoint of the segment.</param>
    /// <param name="seg2">Second endpoint of the segment.</param>
    public static Vector2 GetClosestPtOnSegment(Vector2 offPt, Vector2 seg1, Vector2 seg2)
    {
        if (seg1 == seg2)
        {
            return seg1;
        }

        Vector2 dxy = seg2 - seg1;
        Vector2 oxy = (offPt - seg1) * dxy;
        float q = (oxy.X + oxy.Y) / DotProduct(dxy, dxy);

        if (q < 0)
        {
            q = 0;
        }
        else if (q > 1)
        {
            q = 1;
        }

        return seg1 + (dxy * q);
    }
}
