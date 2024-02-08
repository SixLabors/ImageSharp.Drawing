// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

internal struct VertexDistance
{
    private const double Dd = 1.0 / Constants.Misc.VertexDistanceEpsilon;
    public double X;
    public double Y;
    public double Distance;

    public VertexDistance(double x, double y)
        : this()
    {
        this.X = x;
        this.Y = y;
        this.Distance = 0;
    }

    public VertexDistance(double x, double y, double distance)
        : this()
    {
        this.X = x;
        this.Y = y;
        this.Distance = distance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Measure(VertexDistance vd)
    {
        bool ret = (this.Distance = UtilityMethods.CalcDistance(this.X, this.Y, vd.X, vd.Y)) > Constants.Misc.VertexDistanceEpsilon;
        if (!ret)
        {
            this.Distance = Dd;
        }

        return ret;
    }
}

internal static class Constants
{
    public struct Misc
    {
        public const double BezierArcAngleEpsilon = 0.01;
        public const double AffineEpsilon = 1e-14;
        public const double VertexDistanceEpsilon = 1e-14;
        public const double IntersectionEpsilon = 1.0e-30;
        public const double Pi = 3.14159265358979323846;
        public const double PiMul2 = 3.14159265358979323846 * 2;
        public const double PiDiv2 = 3.14159265358979323846 * 0.5;
        public const double PiDiv180 = 3.14159265358979323846 / 180.0;
        public const double CurveDistanceEpsilon = 1e-30;
        public const double CurveCollinearityEpsilon = 1e-30;
        public const double CurveAngleToleranceEpsilon = 0.01;
        public const int CurveRecursionLimit = 32;
        public const int PolyMaxCoord = (1 << 30) - 1;
    }
}

internal static unsafe class UtilityMethods
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CalcDistance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CalcIntersection(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy, ref double x, ref double y)
    {
        double num = ((ay - cy) * (dx - cx)) - ((ax - cx) * (dy - cy));
        double den = ((bx - ax) * (dy - cy)) - ((by - ay) * (dx - cx));

        if (Math.Abs(den) < Constants.Misc.IntersectionEpsilon)
        {
            return false;
        }

        double r = num / den;
        x = ax + (r * (bx - ax));
        y = ay + (r * (by - ay));

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CrossProduct(double x1, double y1, double x2, double y2, double x, double y) => ((x - x2) * (y2 - y1)) - ((y - y2) * (x2 - x1));
}
