// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

/// <summary>
/// The internal path tests.
/// </summary>
public class PathTests
{
    [Fact]
    public void Bounds()
    {
        LinearLineSegment seg1 = new(new PointF(0, 0), new PointF(2, 2));
        LinearLineSegment seg2 = new(new PointF(4, 4), new PointF(5, 5));

        Path path = new(seg1, seg2);

        Assert.Equal(0, path.Bounds.Left);
        Assert.Equal(5, path.Bounds.Right);
        Assert.Equal(0, path.Bounds.Top);
        Assert.Equal(5, path.Bounds.Bottom);
    }

    [Fact]
    public void SimplePath()
    {
        Path path = new(new LinearLineSegment(new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10)));
        PointF[] points = path.Flatten().Single().Points.ToArray();

        Assert.Equal(4, points.Length);
        Assert.Equal(new PointF(0, 0), points[0]);
        Assert.Equal(new PointF(10, 0), points[1]);
        Assert.Equal(new PointF(10, 10), points[2]);
        Assert.Equal(new PointF(0, 10), points[3]);
    }

    [Fact]
    public void EmptyPath_SingletonsExposeExpectedPathTypes()
    {
        Assert.Same(EmptyPath.OpenPath, Path.Empty);
        Assert.Equal(PathTypes.Open, EmptyPath.OpenPath.PathType);
        Assert.Equal(PathTypes.Closed, EmptyPath.ClosedPath.PathType);
    }

    [Fact]
    public void EmptyPath_OperationsPreserveSingletons()
    {
        Matrix4x4 transform = Matrix4x4.CreateTranslation(12, 34, 0);

        Assert.Same(EmptyPath.ClosedPath, EmptyPath.OpenPath.AsClosedPath());
        Assert.Same(EmptyPath.ClosedPath, EmptyPath.ClosedPath.AsClosedPath());
        Assert.Same(EmptyPath.OpenPath, EmptyPath.OpenPath.Transform(transform));
        Assert.Same(EmptyPath.ClosedPath, EmptyPath.ClosedPath.Transform(transform));
    }

    [Fact]
    public void EmptyPath_ExposesNoPathData()
    {
        Assert.Equal(RectangleF.Empty, EmptyPath.OpenPath.Bounds);
        Assert.Equal(RectangleF.Empty, EmptyPath.ClosedPath.Bounds);
        Assert.Empty(EmptyPath.OpenPath.Flatten());
        Assert.Empty(EmptyPath.ClosedPath.Flatten());
    }

    [Fact]
    public void EmptyPath_ToLinearGeometry_ReturnsEmptyGeometry()
    {
        LinearGeometry identity = EmptyPath.OpenPath.ToLinearGeometry(Vector2.One);
        LinearGeometry scaled = EmptyPath.ClosedPath.ToLinearGeometry(new Vector2(2F, 3F));

        Assert.Same(identity, scaled);
        Assert.Equal(RectangleF.Empty, identity.Info.Bounds);
        Assert.Equal(0, identity.Info.ContourCount);
        Assert.Equal(0, identity.Info.PointCount);
        Assert.Equal(0, identity.Info.SegmentCount);
        Assert.Equal(0, identity.Info.NonHorizontalSegmentCountPixelBoundary);
        Assert.Equal(0, identity.Info.NonHorizontalSegmentCountPixelCenter);
        Assert.Empty(identity.Contours);
        Assert.Empty(identity.Points);

        SegmentEnumerator segments = identity.GetSegments();

        Assert.False(segments.MoveNext());
    }

    [Theory]
    [InlineData("M")]
    [InlineData("M 5")]
    [InlineData("V")]
    [InlineData("H")]
    [InlineData("C 1 2")]
    [InlineData("S 6 7")]
    [InlineData("Q 3 4 5")]
    [InlineData("T")]
    [InlineData("A 1 2 3 4 5 6")]
    [InlineData("~ 7 6 5")]
    public void TryParseSvgPath_ReturnsFalseForMalformedPathData(string svgPath)
        => Assert.False(Path.TryParseSvgPath(svgPath, out _));

    [Theory]
    [InlineData("M 10 10 L 1e999 20")]
    [InlineData("M 10 10 L NaN 20")]
    [InlineData("M 10 10 L Infinity 20")]
    [InlineData("M 10 10 h 1e999")]
    [InlineData("M 10 10 v 1e999")]
    [InlineData("M 10 10 A 25 25 0 2 0 50 50")]
    [InlineData("M 10 10 A 25 25 0 0 2 50 50")]
    [InlineData("M 10 10 L")]
    [InlineData("M 10 10 Q 20 20")]
    [InlineData("M 10 10 C 20 20 30 30")]
    public void TryParseSvgPath_ReturnsFalseForInvalidPathDataBoundaries(string svgPath)
        => Assert.False(Path.TryParseSvgPath(svgPath, out _));

    [Fact]
    public void PathCollection_EnumerableConstructor_PreservesPaths()
    {
        IPath first = new RectangularPolygon(1, 2, 3, 4);
        IPath second = new RectangularPolygon(10, 20, 5, 6);

        PathCollection collection = new(new[] { first, second }.AsEnumerable());

        IPath[] paths = collection.ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Same(first, paths[0]);
        Assert.Same(second, paths[1]);
    }

    [Fact]
    public void PathCollection_Bounds_AggregatesPathBounds()
    {
        PathCollection collection = new(
            new RectangularPolygon(1, 2, 3, 4),
            new RectangularPolygon(10, 20, 5, 6));

        Assert.Equal(new RectangleF(1, 2, 14, 24), collection.Bounds);
    }

    [Fact]
    public void PathCollection_Bounds_ReturnsEmptyForEmptyCollection()
    {
        PathCollection collection = new();

        Assert.Equal(RectangleF.Empty, collection.Bounds);
    }

    [Fact]
    public void PathCollection_Transform_TransformsEachPath()
    {
        PathCollection collection = new(
            new RectangularPolygon(1, 2, 3, 4),
            new RectangularPolygon(10, 20, 5, 6));
        Matrix4x4 matrix = Matrix4x4.CreateTranslation(7, 11, 0);

        IPathCollection transformed = collection.Transform(matrix);

        Assert.NotSame(collection, transformed);
        Assert.Equal(new RectangleF(8, 13, 14, 24), transformed.Bounds);
        Assert.Equal(new RectangleF(1, 2, 14, 24), collection.Bounds);

        RectangleF[] transformedBounds = transformed.Select(x => x.Bounds).ToArray();
        Assert.Equal(2, transformedBounds.Length);
        Assert.Equal(new RectangleF(8, 13, 3, 4), transformedBounds[0]);
        Assert.Equal(new RectangleF(17, 31, 5, 6), transformedBounds[1]);
    }
}
