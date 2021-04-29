// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing.Utilities
{
    internal static class Intersect
    {
        public static bool LineSegmentToLineSegment(PointF a0, PointF a1, PointF b0, PointF b1, ref PointF intersectionPoint)
        {
            float dax = a1.X - a0.X;
            float day = a1.Y - a0.Y;
            float dbx = b1.X - b0.X;
            float dby = b1.Y - b0.Y;

            float s = ((-day * (a0.X - b0.X)) + (dax * (a0.Y - b0.Y))) / ((-dbx * day) + (dax * dby));
            float t = ((dbx * (a0.Y - b0.Y)) - (dby * (a0.X - b0.X))) / ((-dbx * day) + (dax * dby));

            if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
            {
                intersectionPoint.X = a0.X + (t * dax);
                intersectionPoint.Y = a0.Y + (t * day);
                return true;
            }

            return false;
        }
    }
}
