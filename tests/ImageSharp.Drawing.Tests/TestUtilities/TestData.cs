using System;
using System.Linq;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    internal static class TestData
    {
        private const float Inf = 10000;
        
        public static Polygon CreatePolygon(params (float x, float y)[] coords) 
            => new Polygon(new LinearLineSegment(CreatePoints(coords)));

        public static (PointF Start, PointF End) CreateHorizontalLine(float y) 
            => (new PointF(-Inf, y), new PointF(Inf, y));

        public static PointF[] CreatePoints(params (float x, float y)[] coords) =>
            coords.Select(c => new PointF(c.x, c.y)).ToArray();

        public static T[] CloneArray<T>(this T[] points)
        {
            T[] result = new T[points.Length];
            Array.Copy(points, result, points.Length);
            return result;
        }
    }
}