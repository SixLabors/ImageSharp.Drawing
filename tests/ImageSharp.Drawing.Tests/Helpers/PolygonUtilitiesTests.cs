// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Tests.Helpers;

public class PolygonUtilitiesTests
{
    private static PointF[] CreateTestPoints()
        => PolygonFactory.CreatePointArray(
            (10, 0),
            (20, 0),
            (20, 30),
            (10, 30),
            (10, 20),
            (0, 20),
            (0, 10),
            (10, 10),
            (10, 0));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnsureOrientation_Positive(bool isPositive)
    {
        PointF[] expected = CreateTestPoints();
        PointF[] polygon = expected.CloneArray();

        if (!isPositive)
        {
            polygon.AsSpan().Reverse();
        }

        PolygonUtilities.EnsureOrientation(polygon, 1);

        Assert.Equal(expected, polygon);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnsureOrientation_Negative(bool isNegative)
    {
        PointF[] expected = CreateTestPoints();
        expected.AsSpan().Reverse();

        PointF[] polygon = expected.CloneArray();

        if (!isNegative)
        {
            polygon.AsSpan().Reverse();
        }

        PolygonUtilities.EnsureOrientation(polygon, -1);

        Assert.Equal(expected, polygon);
    }

    public static TheoryData<(float X, float Y), (float X, float Y), (float X, float Y), (float X, float Y), (float X, float Y)?> LineSegmentToLineSegment_Data { get; } =
        new()
        {
                { (0, 0), (2, 3), (1, 3), (1, 0), (1, 1.5f) },
                { (3, 1), (3, 3), (3, 2), (4, 2), (3, 2) },
                { (1, -3), (3, -1), (3, -4), (2, -2), (2, -2) },
                { (0, 0), (2, 1), (2, 1.0001f), (5, 2), (2, 1) }, // Robust to inaccuracies
                { (0, 0), (2, 3), (1, 3), (1, 2), null },
                { (-3, 3), (-1, 3), (-3, 2), (-1, 2), null },
                { (-4, 3), (-4, 1), (-5, 3), (-5, 1), null },
                { (0, 0), (4, 1), (4, 1), (8, 2), null }, // Collinear intersections are ignored
                { (0, 0), (4, 1), (4, 1.0001f), (8, 2), null }, // Collinear intersections are ignored
        };

    [Theory]
    [MemberData(nameof(LineSegmentToLineSegment_Data))]
    public void LineSegmentToLineSegmentNoCollinear(
        (float X, float Y) a0,
        (float X, float Y) a1,
        (float X, float Y) b0,
        (float X, float Y) b1,
        (float X, float Y)? expected)
    {
        Vector2 ip = default;

        bool result = PolygonUtilities.LineSegmentToLineSegmentIgnoreCollinear(P(a0), P(a1), P(b0), P(b1), ref ip);
        Assert.Equal(result, expected.HasValue);
        if (expected.HasValue)
        {
            Assert.Equal(P(expected.Value), ip, new ApproximateFloatComparer(1e-3f));
        }

        static Vector2 P((float X, float Y) p) => new(p.X, p.Y);
    }
}
