// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    internal static class PolygonTest
    {
        private const float Inf = 10000;
        
        private static readonly IBrush TestBrush = Brushes.Solid(Color.Red);
        
        private static readonly IPen GridPen = Pens.Solid(Color.Aqua, 0.5f);

        public static Polygon CreatePolygon(params (float x, float y)[] coords)
            => new Polygon(new LinearLineSegment(CreatePoints(coords)))
            {
                // The default epsilon is too large for test code, we prefer the vertices not to be changed
                RemoveCloseAndCollinearPoints = false
            };

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