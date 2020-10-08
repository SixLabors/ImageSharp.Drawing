// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan
{
    /// <summary>
    /// See: NumericCornerCases.jpg
    /// </summary>
    internal class CornerCasePolygons
    {
        public static readonly Polygon A = PolygonTest.CreatePolygon(
            (2, 2.5f), (11, 2.5f), (11, 3.25f), (8, 3.1f), (5, 3), (2, 3));

        public static readonly Polygon B  = PolygonTest.CreatePolygon(
            (12, 2.5f), (21, 2.5f), (21, 3.2f), (18, 3.125f), (15,3), (12,3));

        public static readonly Polygon C = PolygonTest.CreatePolygon(
            (2, 3.4f), (8, 3.6f), (8, 4), (5, 3.875f), (2, 4));

        public static readonly Polygon D = PolygonTest.CreatePolygon(
            (12, 3.3f), (18, 3.6f), (18, 4), (15, 3.87f), (12, 4));

        public static readonly Polygon E = PolygonTest.CreatePolygon(
            (3, 4.4f), (4, 4.75f), (6, 4.6f), (6, 5), (2, 5));

        public static readonly Polygon F = PolygonTest.CreatePolygon(
            (13, 4.3f), (14, 4.75f), (16, 4.6f), (16, 5), (12, 5));

        public static readonly Polygon G = PolygonTest.CreatePolygon((2, 2.25f), (6, 1.87f), (10, 2.25f));

        public static Polygon GetByName(string name)
        {
            return (Polygon) typeof(CornerCasePolygons).GetField(name).GetValue(null);
        }
    }
}