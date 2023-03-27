// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
    internal static class ClipperUtils
    {
        public const float DefaultArcTolerance = .25F;
        public const float FloatingPointTolerance = 1e-4F;
        public const float DefaultMinimumEdgeLength = .1F;

        // TODO: rename to Pow2
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
                return new();
            }

            if (radiusY <= 0)
            {
                radiusY = radiusX;
            }

            if (steps <= 2)
            {
                steps = (int)MathF.Ceiling(MathF.PI * MathF.Sqrt((radiusX + radiusY) / 2));
            }

            float si = MathF.Sin(2 * MathF.PI / steps);
            float co = MathF.Cos(2 * MathF.PI / steps);
            float dx = co, dy = si;
            PathF result = new(steps) { new Vector2(center.X + radiusX, center.Y) };
            for (int i = 1; i < steps; ++i)
            {
                result.Add(new Vector2(center.X + (radiusX * dx), center.Y + (radiusY * dy)));
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

            // TODO: review tolerence.
            => MathF.Abs(value) <= FloatingPointTolerance;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PerpendicDistFromLineSqrd(Vector2 pt, Vector2 line1, Vector2 line2)
        {
            float a = pt.X - line1.X;
            float b = pt.Y - line1.Y;
            float c = line2.X - line1.X;
            float d = line2.Y - line1.Y;
            if (c == 0 && d == 0)
            {
                return 0;
            }

            return Sqr((a * d) - (c * b)) / ((c * c) + (d * d));
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
            float dy1 = ln1b.Y - ln1a.Y;
            float dx1 = ln1b.X - ln1a.X;
            float dy2 = ln2b.Y - ln2a.Y;
            float dx2 = ln2b.X - ln2a.X;
            float cp = (dy1 * dx2) - (dy2 * dx1);
            if (cp == 0F)
            {
                ip = default;
                return false;
            }

            float qx = (dx1 * ln1a.Y) - (dy1 * ln1a.X);
            float qy = (dx2 * ln2a.Y) - (dy2 * ln2a.X);
            ip = new Vector2(((dx1 * qy) - (dx2 * qx)) / cp, ((dy1 * qy) - (dy2 * qx)) / cp);
            return ip.X != float.MaxValue && ip.Y != float.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetIntersectPoint(Vector2 ln1a, Vector2 ln1b, Vector2 ln2a, Vector2 ln2b, out Vector2 ip)
        {
            float dy1 = ln1b.Y - ln1a.Y;
            float dx1 = ln1b.X - ln1a.X;
            float dy2 = ln2b.Y - ln2a.Y;
            float dx2 = ln2b.X - ln2a.X;
            float q1 = (dy1 * ln1a.X) - (dx1 * ln1a.Y);
            float q2 = (dy2 * ln2a.X) - (dx2 * ln2a.Y);
            float cross_prod = (dy1 * dx2) - (dy2 * dx1);
            if (cross_prod == 0F)
            {
                ip = default;
                return false;
            }

            ip = new Vector2(((dx2 * q1) - (dx1 * q2)) / cross_prod, ((dy2 * q1) - (dy1 * q2)) / cross_prod);
            return true;
        }

        public static Vector2 GetClosestPtOnSegment(Vector2 offPt, Vector2 seg1, Vector2 seg2)
        {
            if (seg1.X == seg2.X && seg1.Y == seg2.Y)
            {
                return seg1;
            }

            float dx = seg2.X - seg1.X;
            float dy = seg2.Y - seg1.Y;
            float q = (((offPt.X - seg1.X) * dx) + ((offPt.Y - seg1.Y) * dy)) / ((dx * dx) + (dy * dy));
            if (q < 0)
            {
                q = 0;
            }
            else if (q > 1)
            {
                q = 1;
            }

            return new Vector2(seg1.X + (q * dx), seg1.Y + (q * dy));
        }

        public static PathF ReversePath(PathF path)
        {
            path.Reverse();
            return path;
        }
    }
}
