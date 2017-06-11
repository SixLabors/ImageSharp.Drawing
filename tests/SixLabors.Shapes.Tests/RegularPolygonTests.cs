using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using SixLabors.Primitives;
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
                ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RegularPolygon(Vector2.Zero, points, 10f, 0));

                Assert.Equal("verticies", ex.ParamName);
            }
            else
            {
                RegularPolygon p = new RegularPolygon(Vector2.Zero, points, 10f, 0);
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
                ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RegularPolygon(Vector2.Zero, 3, radius, 0));

                Assert.Equal("radius", ex.ParamName);
            }
            else
            {
                RegularPolygon p = new RegularPolygon(Vector2.Zero, 3, radius, 0);
                Assert.NotNull(p);
            }
        }

        [Fact]
        public void GeneratesCorrectPath()
        {
            float radius = 10;
            int pointsCount = new Random().Next(3, 20);

            RegularPolygon poly = new RegularPolygon(Vector2.Zero, pointsCount, radius, 0);

            var points = poly.Flatten().ToArray()[0].Points;

            // calcualte baselineDistance
            float baseline = Vector2.Distance(points[0], points[1]);

            // all points are extact the same distance away from the center
            for (int i = 0; i < points.Count; i++)
            {
                int j = i - 1;
                if (i == 0)
                {
                    j = points.Count - 1;
                }

                float actual = Vector2.Distance(points[i], points[j]);
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

            RegularPolygon poly = new RegularPolygon(Vector2.Zero, 3, radius, (float)anAngle);
            var points = poly.Flatten().ToArray()[0].Points;

            IEnumerable<double> allAngles = points.Select(b => Math.Atan2(b.Y, b.X))
                .Select(x => x < 0 ? x + TwoPI : x); // normalise it from +/- PI to 0 to TwoPI
            
            Assert.Contains(allAngles, a => Math.Abs(a - anAngle) > 0.000001);
        }

        [Fact]
        public void TriangleMissingIntersectionsDownCenter()
        {

            RegularPolygon poly = new SixLabors.Shapes.RegularPolygon(50, 50, 3, 30);
            PointF[] points = poly.FindIntersections(new PointF(0, 50), new PointF(100, 50)).ToArray();

            Assert.Equal(2, points.Length);
        }

        [Fact]
        public void ClippingCornerShouldReturn2Points()
        {
            RegularPolygon poly = new RegularPolygon(50, 50, 7, 30, -(float)Math.PI);
            PointF[] points = poly.FindIntersections(new PointF(0, 20), new PointF(100, 20)).ToArray();

            Assert.Equal(2, points.Length);
        }
    }
}
