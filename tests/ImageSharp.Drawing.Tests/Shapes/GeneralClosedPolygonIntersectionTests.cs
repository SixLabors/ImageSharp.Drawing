// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public class GeneralClosedPolygonIntersectionTests
    {
        private static readonly Dictionary<string, IPath> ShapesData = new Dictionary<string, IPath>
        {
            { "ellispeWithHole", new ComplexPolygon(new EllipsePolygon(new Vector2(603), 161f), new EllipsePolygon(new Vector2(603), 61f)) },
            { "largeEllipse", new EllipsePolygon(new Vector2(603), 603f - 60) },
            { "iris_0", TestShapes.IrisSegment(0) },
            { "iris_1", TestShapes.IrisSegment(1) },
            { "iris_2", TestShapes.IrisSegment(2) },
            { "iris_3", TestShapes.IrisSegment(3) },
            { "iris_4", TestShapes.IrisSegment(4) },
            { "iris_5", TestShapes.IrisSegment(5) },
            { "iris_6", TestShapes.IrisSegment(6) },
            { "scaled_300_iris_0", TestShapes.IrisSegment(300, 0) },
            { "scaled_300_iris_1", TestShapes.IrisSegment(300, 1) },
            { "scaled_300_iris_2", TestShapes.IrisSegment(300, 2) },
            { "scaled_300_iris_3", TestShapes.IrisSegment(300, 3) },
            { "scaled_300_iris_4", TestShapes.IrisSegment(300, 4) },
            { "scaled_300_iris_5", TestShapes.IrisSegment(300, 5) },
            { "scaled_300_iris_6", TestShapes.IrisSegment(300, 6) },
            { "clippedRect",   new RectangularPolygon(10, 10, 40, 40).Clip(new RectangularPolygon(20, 0, 20, 20)) },
            { "hourGlass", TestShapes.HourGlass().AsClosedPath() },
            { "BigCurve", new Polygon(new CubicBezierLineSegment(new Vector2(10, 400), new Vector2(30, 10), new Vector2(240, 30), new Vector2(300, 400))) },
            {
                "ChopCorner", new Polygon(new LinearLineSegment(
                    new Vector2(8, 8),
                    new Vector2(64, 8),
                    new Vector2(64, 64),
                    new Vector2(120, 64),
                    new Vector2(120, 120),
                    new Vector2(8, 120)))
            }
        };

        public static TheoryData<string> PolygonsTheoryData = new TheoryData<string>
        {
            { "ellispeWithHole" },
            { "largeEllipse" },
            { "iris_0" },
            { "iris_1" },
            { "iris_2" },
            { "iris_3" },
            { "iris_4" },
            { "iris_5" },
            { "iris_6" },
            { "scaled_300_iris_0" },
            { "scaled_300_iris_1" },
            { "scaled_300_iris_2" },
            { "scaled_300_iris_3" },
            { "scaled_300_iris_4" },
            { "scaled_300_iris_5" },
            { "scaled_300_iris_6" },
            { "clippedRect" },
            { "hourGlass" },
            { "ChopCorner" },
        };

        [Theory]
        [MemberData(nameof(PolygonsTheoryData))]
        public void ShapeMissingEdgeHits(string name)
        {
            IPath polygon = ShapesData[name];
            int top = (int)Math.Ceiling(polygon.Bounds.Top);
            int bottom = (int)Math.Floor(polygon.Bounds.Bottom);

            for (int y = top; y <= bottom; y++)
            {
                IEnumerable<PointF> intersections = polygon.FindIntersections(new Vector2(polygon.Bounds.Left - 1, y), new Vector2(polygon.Bounds.Right + 1, y));
                if (intersections.Count() % 2 != 0)
                {
                    Assert.True(false, $"crosssection of '{name}' at '{y}' produced {intersections.Count()} number of intersections");
                }
            }
        }

        public static TheoryData<string, int> SpecificErrors = new TheoryData<string, int>
        {
            { "ellispeWithHole", 603 },
            { "ellispeWithHole", 442 },
            { "iris_5", 694 },
            { "iris_2", 512 },
            { "scaled_300_iris_3", 135 },
            { "scaled_300_iris_0", 165 },
            { "clippedRect", 20 },
            { "clippedRect", 10 },
            { "hourGlass", 25 },
            { "hourGlass", 175 },
            { "BigCurve", 115 },
            { "ChopCorner", 64 },
        };

        [Theory]
        [MemberData(nameof(SpecificErrors))]
        public void SpecificMisses(string name, int yScanLine)
        {
            IPath polygon = ShapesData[name];

            int intersections = polygon.FindIntersections(
                new Vector2(polygon.Bounds.Left - 1, yScanLine),
                new Vector2(polygon.Bounds.Right + 1, yScanLine)).Count();

            Assert.True(intersections % 2 == 0, $"crosssection of '{name}' at '{yScanLine}' produced {intersections} intersections");
        }
    }
}
