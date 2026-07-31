// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests;

public class RegionTests
{
    [Fact]
    public void DefaultConstructor_CreatesEmptyRegion()
    {
        Region region = new();

        Assert.True(region.IsEmpty);
        Assert.Equal(Rectangle.Empty, region.Bounds);
        Assert.Empty(region.Rectangles);
        Assert.False(region.Contains(0, 0));
        Assert.False(region.Intersects(new Rectangle(0, 0, 10, 10)));
    }

    [Fact]
    public void RectangleConstructor_ContainsRectangle()
    {
        Region region = new(new Rectangle(10, 20, 30, 40));

        Assert.False(region.IsEmpty);
        Assert.Equal(new Rectangle(10, 20, 30, 40), region.Bounds);
        Rectangle single = Assert.Single(region.Rectangles);
        Assert.Equal(new Rectangle(10, 20, 30, 40), single);
    }

    [Fact]
    public void CopyConstructor_CopiesAreaAndIsIndependent()
    {
        Region source = new(new Rectangle(0, 0, 10, 10));
        Region copy = new(source);

        Assert.Equal(source.Rectangles, copy.Rectangles);

        copy.Add(new Rectangle(20, 0, 10, 10));

        Assert.Single(source.Rectangles);
        Assert.Equal(2, copy.Rectangles.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 3)]
    [InlineData(3, -5)]
    public void Add_NonPositiveRectangle_DoesNotChangeRegion(int width, int height)
    {
        Region region = new();
        region.Add(new Rectangle(10, 10, width, height));

        Assert.True(region.IsEmpty);
        Assert.Equal(Rectangle.Empty, region.Bounds);
    }

    [Fact]
    public void Add_StackedRectanglesWithSameWidth_MergeIntoOne()
    {
        Region region = new(new Rectangle(0, 0, 10, 5));
        region.Add(new Rectangle(0, 5, 10, 5));

        Rectangle single = Assert.Single(region.Rectangles);
        Assert.Equal(new Rectangle(0, 0, 10, 10), single);
    }

    [Fact]
    public void Add_OverlappingRectangles_NormalizesIntoBands()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));
        region.Add(new Rectangle(5, 5, 10, 10));

        Assert.Equal(Rectangle.FromLTRB(0, 0, 15, 15), region.Bounds);
        Assert.Equal(
            new[]
            {
                Rectangle.FromLTRB(0, 0, 10, 5),
                Rectangle.FromLTRB(0, 5, 15, 10),
                Rectangle.FromLTRB(5, 10, 15, 15),
            },
            region.Rectangles);
    }

    [Fact]
    public void Add_DisjointRectangles_PreservesIslands()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));
        region.Add(new Rectangle(100, 100, 10, 10));

        Assert.Equal(2, region.Rectangles.Count);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 110, 110), region.Bounds);
        Assert.True(region.Contains(5, 5));
        Assert.True(region.Contains(105, 105));
        Assert.False(region.Contains(50, 50));
    }

    [Fact]
    public void Contains_IsInclusiveOfLeftTopAndExclusiveOfRightBottom()
    {
        Region region = new(new Rectangle(10, 10, 10, 10));

        Assert.True(region.Contains(new Point(10, 10)));
        Assert.True(region.Contains(19, 19));
        Assert.False(region.Contains(20, 10));
        Assert.False(region.Contains(10, 20));
        Assert.False(region.Contains(9, 10));
        Assert.False(region.Contains(10, 9));
    }

    [Fact]
    public void Intersects_TouchingEdges_DoNotIntersect()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));

        Assert.True(region.Intersects(new Rectangle(9, 9, 10, 10)));
        Assert.False(region.Intersects(new Rectangle(10, 0, 10, 10)));
        Assert.False(region.Intersects(new Rectangle(0, 10, 10, 10)));
        Assert.False(region.Intersects(new Rectangle(0, 0, 0, 10)));
    }

    [Fact]
    public void IntersectRectangle_ClipsRegion()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));
        region.Add(new Rectangle(100, 100, 10, 10));

        bool result = region.Intersect(new Rectangle(5, 5, 20, 20));

        Assert.True(result);
        Rectangle single = Assert.Single(region.Rectangles);
        Assert.Equal(Rectangle.FromLTRB(5, 5, 10, 10), single);
        Assert.Equal(Rectangle.FromLTRB(5, 5, 10, 10), region.Bounds);
    }

    [Fact]
    public void IntersectRectangle_Disjoint_ClearsRegionAndReturnsFalse()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));

        bool result = region.Intersect(new Rectangle(50, 50, 10, 10));

        Assert.False(result);
        Assert.True(region.IsEmpty);
    }

    [Fact]
    public void IntersectRectangle_EmptyRectangle_ClearsRegionAndReturnsFalse()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));

        bool result = region.Intersect(Rectangle.Empty);

        Assert.False(result);
        Assert.True(region.IsEmpty);
    }

    [Fact]
    public void IntersectRegion_KeepsOnlySharedArea()
    {
        Region first = new(new Rectangle(0, 0, 10, 10));
        first.Add(new Rectangle(20, 0, 10, 10));

        Region second = new(new Rectangle(5, 0, 20, 10));

        bool result = first.Intersect(second);

        Assert.True(result);
        Assert.Equal(
            new[]
            {
                Rectangle.FromLTRB(5, 0, 10, 10),
                Rectangle.FromLTRB(20, 0, 25, 10),
            },
            first.Rectangles);
    }

    [Fact]
    public void IntersectRegion_Disjoint_ClearsRegionAndReturnsFalse()
    {
        Region first = new(new Rectangle(0, 0, 10, 10));
        Region second = new(new Rectangle(50, 50, 10, 10));

        bool result = first.Intersect(second);

        Assert.False(result);
        Assert.True(first.IsEmpty);
    }

    [Fact]
    public void IntersectRegion_WithEmpty_ClearsRegionAndReturnsFalse()
    {
        Region first = new(new Rectangle(0, 0, 10, 10));

        bool result = first.Intersect(new Region());

        Assert.False(result);
        Assert.True(first.IsEmpty);
    }

    [Fact]
    public void Clear_RemovesAllArea()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));
        region.Clear();

        Assert.True(region.IsEmpty);
        Assert.Equal(Rectangle.Empty, region.Bounds);
        Assert.Empty(region.Rectangles);
    }

    [Fact]
    public void ToPath_EmptyRegion_HasEmptyBounds()
    {
        Region region = new();
        IPath path = region.ToPath();

        Assert.Equal(0, path.Bounds.Width * path.Bounds.Height);
    }

    [Fact]
    public void ToPath_SingleRectangle_MatchesRectangleBounds()
    {
        Region region = new(new Rectangle(10, 20, 30, 40));
        IPath path = region.ToPath();

        Assert.Equal(new RectangleF(10, 20, 30, 40), path.Bounds);
    }

    [Fact]
    public void ToPath_MultipleRectangles_MatchesRegionBounds()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));
        region.Add(new Rectangle(5, 5, 10, 10));

        IPath path = region.ToPath();

        Assert.Equal((RectangleF)region.Bounds, path.Bounds);
    }

    [Fact]
    public void ToPath_IsCachedUntilTheRegionChanges()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));

        IPath first = region.ToPath();
        IPath second = region.ToPath();
        Assert.Same(first, second);

        region.Add(new Rectangle(20, 0, 10, 10));
        IPath third = region.ToPath();
        Assert.NotSame(first, third);
    }

    [Fact]
    public void ToPath_DisjointIslands_ProducesFigurePerIsland()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));
        region.Add(new Rectangle(100, 0, 10, 10));

        IPath path = region.ToPath();

        Assert.Equal(Rectangle.FromLTRB(0, 0, 110, 10), (Rectangle)path.Bounds);
    }
}
