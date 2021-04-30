// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Utilities
{
    internal static class Intersect
    {
        private const float Eps = 1e-3f;
        private const float MinusEps = -Eps;
        private const float OnePlusEps = 1 + Eps;

        public static bool LineSegmentToLineSegmentIgnoreCollinear(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, ref Vector2 intersectionPoint)
        {
            float dax = a1.X - a0.X;
            float day = a1.Y - a0.Y;
            float dbx = b1.X - b0.X;
            float dby = b1.Y - b0.Y;

            float crossD = (-dbx * day) + (dax * dby);

            if (crossD > MinusEps && crossD < Eps)
            {
                return false;
            }

            float s = ((-day * (a0.X - b0.X)) + (dax * (a0.Y - b0.Y))) / crossD;
            float t = ((dbx * (a0.Y - b0.Y)) - (dby * (a0.X - b0.X))) / crossD;

            if (s > MinusEps && s < OnePlusEps && t > MinusEps && t < OnePlusEps)
            {
                intersectionPoint.X = a0.X + (t * dax);
                intersectionPoint.Y = a0.Y + (t * day);
                return true;
            }

            return false;
        }
    }
}
