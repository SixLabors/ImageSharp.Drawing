// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

internal static class ClipperUtils
{
    public const float DefaultArcTolerance = .25F;
    public const float FloatingPointTolerance = 1e-05F;
    public const float DefaultMinimumEdgeLength = .1F;

    // TODO: rename to Pow2?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sqr(float value) => value * value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Area(PathF path)
    {
        // https://en.wikipedia.org/wiki/Shoelace_formula
        float a = 0F;
        if (path.Count < 3)
        {
            return a;
        }

        Vector2 prevPt = path[path.Count - 1];
        for (int i = 0; i < path.Count; i++)
        {
            Vector2 pt = path[i];
            a += (prevPt.Y + pt.Y) * (prevPt.X - pt.X);
            prevPt = pt;
        }

        return a * .5F;
    }

    public static PathF StripDuplicates(PathF path, bool isClosedPath)
    {
        int cnt = path.Count;
        PathF result = new(cnt);
        if (cnt == 0)
        {
            return result;
        }

        Vector2 lastPt = path[0];
        result.Add(lastPt);
        for (int i = 1; i < cnt; i++)
        {
            if (lastPt != path[i])
            {
                lastPt = path[i];
                result.Add(lastPt);
            }
        }

        if (isClosedPath && lastPt == result[0])
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    public static PathF Ellipse(Vector2 center, float radiusX, float radiusY = 0, int steps = 0)
    {
        if (radiusX <= 0)
        {
            return [];
        }

        if (radiusY <= 0)
        {
            radiusY = radiusX;
        }

        if (steps <= 2)
        {
            steps = (int)MathF.Ceiling(MathF.PI * MathF.Sqrt((radiusX + radiusY) * .5F));
        }

        float si = MathF.Sin(2 * MathF.PI / steps);
        float co = MathF.Cos(2 * MathF.PI / steps);
        float dx = co, dy = si;
        PathF result = new(steps) { new Vector2(center.X + radiusX, center.Y) };
        Vector2 radiusXY = new(radiusX, radiusY);
        for (int i = 1; i < steps; ++i)
        {
            result.Add(center + (radiusXY * new Vector2(dx, dy)));
            float x = (dx * co) - (dy * si);
            dy = (dy * co) + (dx * si);
            dx = x;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(Vector2 vec1, Vector2 vec2)
        => Vector2.Dot(vec1, vec2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CrossProduct(Vector2 vec1, Vector2 vec2)
        => (vec1.Y * vec2.X) - (vec2.Y * vec1.X);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CrossProduct(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        => ((pt2.X - pt1.X) * (pt3.Y - pt2.Y)) - ((pt2.Y - pt1.Y) * (pt3.X - pt2.X));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        => Vector2.Dot(pt2 - pt1, pt3 - pt2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAlmostZero(float value)
        => MathF.Abs(value) <= FloatingPointTolerance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PerpendicDistFromLineSqrd(Vector2 pt, Vector2 line1, Vector2 line2)
    {
        Vector2 ab = pt - line1;
        Vector2 cd = line2 - line1;
        if (cd == Vector2.Zero)
        {
            return 0;
        }

        return Sqr(CrossProduct(cd, ab)) / DotProduct(cd, cd);
    }

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
        else
        {
            return (CrossProduct(seg1a, seg2a, seg2b) * CrossProduct(seg1b, seg2a, seg2b) < 0)
                && (CrossProduct(seg2a, seg1a, seg1b) * CrossProduct(seg2b, seg1a, seg1b) < 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool GetIntersectPt(Vector2 ln1a, Vector2 ln1b, Vector2 ln2a, Vector2 ln2b, out Vector2 ip)
    {
        Vector2 dxy1 = ln1b - ln1a;
        Vector2 dxy2 = ln2b - ln2a;
        float cp = CrossProduct(dxy1, dxy2);
        if (cp == 0F)
        {
            ip = default;
            return false;
        }

        float qx = CrossProduct(ln1a, dxy1);
        float qy = CrossProduct(ln2a, dxy2);

        ip = ((dxy1 * qy) - (dxy2 * qx)) / cp;
        return ip != new Vector2(float.MaxValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetIntersectPoint(Vector2 ln1a, Vector2 ln1b, Vector2 ln2a, Vector2 ln2b, out Vector2 ip)
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

    public static PathF ReversePath(PathF path)
    {
        path.Reverse();
        return path;
    }
}
