using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using System.Numerics;

    public class RegularPolygonTests
    {

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        [InlineData(4, false)]
        public void RequiresAtleast3Verticies(int points, bool throws)
        {
            if (throws)
            {
                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RegularPolygon(Vector2.Zero, points, 10f, 0));

                Assert.Equal("verticies", ex.ParamName);
            }
            else
            {
                var p = new RegularPolygon(Vector2.Zero, points, 10f, 0);
                Assert.NotNull(p);
            }
        }

        [Theory]
        [InlineData(-1, true)]
        [InlineData(0, true)]
        [InlineData(0.00001, false)]
        [InlineData(1, false)]
        public void RadiusMustBeGreateThan0(float radius, bool throws)
        {
            if (throws)
            {
                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RegularPolygon(Vector2.Zero, 3, radius, 0));

                Assert.Equal("radius", ex.ParamName);
            }
            else
            {
                var p = new RegularPolygon(Vector2.Zero, 3, radius, 0);
                Assert.NotNull(p);
            }
        }

        [Fact]
        public void GeneratesCorrectPath()
        {
            float radius = 10;
            int pointsCount = new Random().Next(3, 20);

            var poly = new RegularPolygon(Vector2.Zero, pointsCount, radius, 0);
            var path = poly.AsPath();

            var points = path.Flatten();

            // calcualte baselineDistance
            var baseline = Vector2.Distance(points[0], points[1]);

            // all points are extact the same distance away from the center
            for (var i = 0; i < points.Length; i++)
            {
                var j = i - 1;
                if (i == 0)
                {
                    j = points.Length - 1;
                }

                var actual = Vector2.Distance(points[i], points[j]);
                Assert.Equal(baseline, actual, 3);
                Assert.Equal(radius, Vector2.Distance(Vector2.Zero, points[i]), 3);
            }
        }

        [Fact]
        public void AngleChangesOnePointToStartAtThatPosition()
        {
            const double TwoPI = 2 * Math.PI;
            float radius = 10;
            double anAngle = new Random().NextDouble() * TwoPI;

            var poly = new RegularPolygon(Vector2.Zero, 3, radius, (float)anAngle);
            var path = poly.AsPath();
            var points = path.Flatten();

            var allAngles = points.Select(b => Math.Atan2(b.Y, b.X))
                .Select(x => x < 0 ? x + TwoPI : x); // normalise it from +/- PI to 0 to TwoPI
            
            Assert.Contains(allAngles, a => Math.Abs(a - anAngle) > 0.000001);
        }

        [Fact]
        public void TriangleMissingIntersectionsDownCenter()
        {

            var poly = new SixLabors.Shapes.RegularPolygon(50, 50, 3, 30);
            var points = poly.FindIntersections(new Vector2(0, 50), new Vector2(100, 50)).ToArray();

            Assert.Equal(2, points.Length);
        }
    }
}
