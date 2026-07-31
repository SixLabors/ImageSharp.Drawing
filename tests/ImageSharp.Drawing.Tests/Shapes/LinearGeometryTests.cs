// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class LinearGeometryTests
{
    private static LinearGeometry CreateLPolyline()
        => LinearGeometry.CreateOpenPolyline(
        [
            new PointF(0, 0),
            new PointF(10, 0),
            new PointF(10, 10),
        ]);

    private static LinearGeometry CreateClosedSquare()
    {
        PointF[] points =
        [
            new PointF(0, 0),
            new PointF(10, 0),
            new PointF(10, 10),
            new PointF(0, 10),
        ];
        RectangleF bounds = RectangleF.FromLTRB(0, 0, 10, 10);

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = bounds,
                ContourCount = 1,
                PointCount = points.Length,
                SegmentCount = points.Length,
            },
            [new LinearContour
            {
                PointStart = 0,
                PointCount = points.Length,
                Bounds = bounds,
                SegmentStart = 0,
                SegmentCount = points.Length,
                IsClosed = true,
            }
            ],
            points);
    }

    [Fact]
    public void CreateOpenPolyline_PopulatesInfoAndContours()
    {
        LinearGeometry geometry = CreateLPolyline();

        Assert.Equal(1, geometry.Info.ContourCount);
        Assert.Equal(3, geometry.Info.PointCount);
        Assert.Equal(2, geometry.Info.SegmentCount);
        Assert.Equal(RectangleF.FromLTRB(0, 0, 10, 10), geometry.Info.Bounds);

        LinearContour contour = Assert.Single(geometry.Contours);
        Assert.False(contour.IsClosed);
        Assert.Equal(3, geometry.Points.Count);
    }

    [Fact]
    public void CreateOpenPolyline_AppliesScale()
    {
        LinearGeometry geometry = LinearGeometry.CreateOpenPolyline(
            [new PointF(1, 2), new PointF(3, 4)],
            new Vector2(2, 10));

        Assert.Equal(new PointF(2, 20), geometry.Points[0]);
        Assert.Equal(new PointF(6, 40), geometry.Points[1]);
    }

    [Fact]
    public void CreateOpenPolyline_FewerThanTwoPoints_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => LinearGeometry.CreateOpenPolyline([new PointF(0, 0)]));

    [Fact]
    public void GetSegments_YieldsEachSegmentInOrder()
    {
        LinearGeometry geometry = CreateLPolyline();
        List<(PointF Start, PointF End)> segments = [];

        SegmentEnumerator enumerator = geometry.GetSegments();
        while (enumerator.MoveNext())
        {
            segments.Add((enumerator.Current.Start, enumerator.Current.End));
        }

        Assert.Equal(
        [
            (new PointF(0, 0), new PointF(10, 0)),
            (new PointF(10, 0), new PointF(10, 10)),
        ],
            segments);
    }

    [Fact]
    public void ComputeLength_SumsSegmentLengths()
        => Assert.Equal(20F, CreateLPolyline().ComputeLength());

    [Fact]
    public void ComputeArea_OpenRunWithThreePoints_UsesShoelaceOfPointRun()
        => Assert.Equal(50F, CreateLPolyline().ComputeArea());

    [Fact]
    public void ComputeArea_ClosedSquare_ReturnsEnclosedArea()
        => Assert.Equal(100F, CreateClosedSquare().ComputeArea());

    [Fact]
    public void Contains_OpenContour_NeverContains()
    {
        LinearGeometry geometry = CreateLPolyline();

        Assert.False(geometry.Contains(new PointF(9, 1), IntersectionRule.NonZero));
        Assert.False(geometry.Contains(new PointF(9, 1), IntersectionRule.EvenOdd));
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(0, 0, true)]
    [InlineData(15, 5, false)]
    [InlineData(-1, 5, false)]
    public void Contains_ClosedContour_UsesWinding(float x, float y, bool expected)
    {
        LinearGeometry geometry = CreateClosedSquare();

        Assert.Equal(expected, geometry.Contains(new PointF(x, y), IntersectionRule.NonZero));
        Assert.Equal(expected, geometry.Contains(new PointF(x, y), IntersectionRule.EvenOdd));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 5, 0)]
    [InlineData(15, 10, 5)]
    [InlineData(20, 10, 10)]
    public void TryGetPathPointAtDistance_WithinLength_ReturnsPointOnPolyline(float distance, float x, float y)
    {
        LinearGeometry geometry = CreateLPolyline();

        Assert.True(geometry.TryGetPathPointAtDistance(distance, out PathPoint pathPoint));
        Assert.Equal(new PointF(x, y), pathPoint.Point);
    }

    [Fact]
    public void TryGetPathPointAtDistance_BeyondOpenPolyline_ReturnsFalse()
    {
        LinearGeometry geometry = CreateLPolyline();

        Assert.False(geometry.TryGetPathPointAtDistance(25, out _));
        Assert.False(geometry.TryGetPathPointAtDistance(-1, out _));
        Assert.False(geometry.TryGetPathPointAtDistance(float.NaN, out _));
    }

    [Fact]
    public void TryGetPathPointAtDistanceUnbounded_ExtrapolatesAlongEndTangent()
    {
        LinearGeometry geometry = CreateLPolyline();

        Assert.True(geometry.TryGetPathPointAtDistanceUnbounded(25, out PathPoint pathPoint));
        Assert.Equal(new PointF(10, 15), pathPoint.Point);
    }

    [Fact]
    public void TryGetPathPointAtDistanceUnbounded_NegativeDistance_ExtrapolatesBeforeStart()
    {
        LinearGeometry geometry = CreateLPolyline();

        Assert.True(geometry.TryGetPathPointAtDistanceUnbounded(-5, out PathPoint pathPoint));
        Assert.Equal(new PointF(-5, 0), pathPoint.Point);
    }

    [Fact]
    public void TryGetSegment_ReturnsSubPathBetweenDistances()
    {
        LinearGeometry geometry = CreateLPolyline();

        Assert.True(geometry.TryGetSegment(5, 15, false, out IPath segment));
        Assert.Equal(RectangleF.FromLTRB(5, 0, 10, 5), segment.Bounds);
    }
}
