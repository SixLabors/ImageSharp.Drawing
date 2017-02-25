using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    public class GeneralClosedPolygonIntersectionTests
    {

        static Dictionary<string, IPath> shapes = new Dictionary<string, IPath> {
            {"ellispeWithHole", new ComplexPolygon(new SixLabors.Shapes.Ellipse(new Vector2(603), 161f), new SixLabors.Shapes.Ellipse(new Vector2(603), 61f)) },
            { "largeEllipse", new SixLabors.Shapes.Ellipse(new Vector2(603), 603f-60) },
            { "iris_2", Shapes.IrisSegment(2) },
            { "iris_5", Shapes.IrisSegment(5) }
        };

        public static IEnumerable<object[]> polygonsTheoryData => shapes.Keys.Select(x => new object[] { x });

        [Theory]
        [MemberData(nameof(polygonsTheoryData))]
        public void ShapeMissingEdgeHits(string name)
        {
            var polygon = shapes[name];
            var top = (int)Math.Ceiling(polygon.Bounds.Top);
            var bottom = (int)Math.Floor(polygon.Bounds.Bottom);

            for (var y = top; y <= bottom; y++)
            {
                var intersections = polygon.FindIntersections(new Vector2(polygon.Bounds.Left - 1, y), new Vector2(polygon.Bounds.Right + 1, y));

                Assert.True(intersections.Count() % 2 == 0, $"crosssections at '{y}' produced odd number of intersections");
            }
        }

        public static TheoryData<string, int> specificErrors = new TheoryData<string, int>
        {
            { "ellispeWithHole", 603 },
            { "iris_5", 694 },
            { "iris_2", 512 }
        };

        [Theory]
        [MemberData(nameof(specificErrors))]
        public void SpecificMisses(string name, int yScanLine)
        {
            var polygon = shapes[name];

            var intersections = polygon.FindIntersections(new Vector2(polygon.Bounds.Left - 1, yScanLine), new Vector2(polygon.Bounds.Right + 1, yScanLine));

            Assert.True(intersections.Count() % 2 == 0, $"crosssections at '{yScanLine}' produced odd number of intersections");
        }
    }
}
