// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests;

public class RegularPolygonTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void RequiresAtLeast3Vertices(int points, bool throws)
    {
        if (throws)
        {
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RegularPolygon(Vector2.Zero, points, 10f, 0));

            Assert.Equal("vertices", ex.ParamName);
        }
        else
        {
            RegularPolygon p = new(Vector2.Zero, points, 10f, 0);
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
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RegularPolygon(Vector2.Zero, 3, radius, 0));

            Assert.Equal("radius", ex.ParamName);
        }
        else
        {
            RegularPolygon p = new(Vector2.Zero, 3, radius, 0);
            Assert.NotNull(p);
        }
    }

    [Fact]
    public void GeneratesCorrectPath()
    {
        const float Radius = 10;
        int pointsCount = new Random().Next(3, 20);

        RegularPolygon poly = new(Vector2.Zero, pointsCount, Radius, 0);

        IReadOnlyList<PointF> points = poly.Flatten().ToArray()[0].Points.ToArray();

        // calculates baselineDistance
        float baseline = Vector2.Distance(points[0], points[1]);

        // all points are exact the same distance away from the center
        for (int i = 0; i < points.Count; i++)
        {
            int j = i - 1;
            if (i == 0)
            {
                j = points.Count - 1;
            }

            float actual = Vector2.Distance(points[i], points[j]);
            Assert.Equal(baseline, actual, 3F);
            Assert.Equal(Radius, Vector2.Distance(Vector2.Zero, points[i]), 3F);
        }
    }

    [Fact]
    public void AngleChangesOnePointToStartAtThatPosition()
    {
        const float Radius = 10;
        double anAngleDegrees = new Random().NextDouble() * 360D;

        RegularPolygon poly = new(Vector2.Zero, 3, Radius, (float)anAngleDegrees);
        IReadOnlyList<PointF> points = poly.Flatten().ToArray()[0].Points.ToArray();

        IEnumerable<double> allAngles = points.Select(b => GeometryUtilities.RadianToDegree((float)Math.Atan2(b.Y, b.X)))
            .Select(x => x < 0 ? x + 360D : x); // normalise it from +/- 180 to 0 to 360

        Assert.Contains(allAngles, a => Math.Abs(a - anAngleDegrees) < 0.000001);
    }
}
