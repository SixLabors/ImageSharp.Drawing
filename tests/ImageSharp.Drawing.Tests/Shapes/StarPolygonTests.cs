// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class StarPolygonTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void RequiresAtLeast3Verticies(int points, bool throws)
    {
        if (throws)
        {
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new StarPolygon(Vector2.Zero, points, 10f, 20f, 0));

            Assert.Equal("prongs", ex.ParamName);
        }
        else
        {
            StarPolygon p = new(Vector2.Zero, points, 10f, 20f, 0);
            Assert.NotNull(p);
        }
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(0.00001, false)]
    [InlineData(1, false)]
    public void RadiusMustBeGreaterThan0(float radius, bool throws)
    {
        if (throws)
        {
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new StarPolygon(Vector2.Zero, 3, radius, 20f, 0));
            ArgumentOutOfRangeException ex2 = Assert.Throws<ArgumentOutOfRangeException>(() => new StarPolygon(Vector2.Zero, 3, 20f, radius, 0));

            Assert.Equal("innerRadii", ex.ParamName);
            Assert.Equal("outerRadii", ex2.ParamName);
        }
        else
        {
            StarPolygon p = new(Vector2.Zero, 3, radius, radius, 0);
            Assert.NotNull(p);
        }
    }

    [Fact]
    public void GeneratesCorrectPath()
    {
        const float radius = 5;
        const float radius2 = 30;
        int pointsCount = new Random().Next(3, 20);

        StarPolygon poly = new(Vector2.Zero, pointsCount, radius, radius2, 0);

        PointF[] points = poly.Flatten().ToArray()[0].Points.ToArray();

        // calculates baselineDistance
        float baseline = Vector2.Distance(points[0], points[1]);

        // all points are exact the same distance away from the center
        Assert.Equal(pointsCount * 2, points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            int j = i - 1;
            if (j < 0)
            {
                j += points.Length;
            }

            float actual = Vector2.Distance(points[i], points[j]);
            Assert.Equal(baseline, actual, 3F);
            if (i % 2 == 1)
            {
                Assert.Equal(radius, Vector2.Distance(Vector2.Zero, points[i]), 3F);
            }
            else
            {
                Assert.Equal(radius2, Vector2.Distance(Vector2.Zero, points[i]), 3F);
            }
        }
    }

    [Fact]
    public void AngleChangesOnePointToStartAtThatPosition()
    {
        const float radius = 10;
        const float radius2 = 20;
        const double tolerance = 1e-3D;
        double anAngleDegrees = new Random().NextDouble() * 360D;
        double expectedAngleDegrees = anAngleDegrees + 90D;
        if (expectedAngleDegrees >= 360D)
        {
            expectedAngleDegrees -= 360D;
        }

        StarPolygon poly = new(Vector2.Zero, 3, radius, radius2, (float)anAngleDegrees);
        ISimplePath[] points = [.. poly.Flatten()];

        IEnumerable<double> allAngles = points[0].Points.ToArray()
            .Select(b => GeometryUtilities.RadianToDegree((float)Math.Atan2(b.Y, b.X)))
            .Select(x => x < 0 ? x + 360D : x); // normalise it from +/- 180 to 0 to 360

        Assert.Contains(allAngles, a => Math.Abs(a - expectedAngleDegrees) < tolerance);
    }
}
