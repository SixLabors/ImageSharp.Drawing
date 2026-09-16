// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

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

    [Fact]
    public void PathConstructor_ZeroAreaPath_CreatesEmptyRegion()
    {
        Polygon line = new([new PointF(0, 5), new PointF(10, 5), new PointF(20, 5)]);
        Region region = new(line, IntersectionRule.NonZero);

        Assert.True(region.IsEmpty);
        Assert.Equal(Rectangle.Empty, region.Bounds);
        Assert.Empty(region.Rectangles);
    }

    [Fact]
    public void PathConstructor_IntegerRectangle_MatchesRectangle()
    {
        Region region = new(new RectanglePolygon(10, 20, 30, 40), IntersectionRule.NonZero);

        Rectangle single = Assert.Single(region.Rectangles);
        Assert.Equal(new Rectangle(10, 20, 30, 40), single);
        Assert.Equal(new Rectangle(10, 20, 30, 40), region.Bounds);
    }

    [Fact]
    public void PathConstructor_FractionalRectangle_SelectsPixelsByCentre()
    {
        // Columns 10 to 39 have centres inside [10.25, 40.25). Rows 21 to 60 have centres inside [20.75, 60.75).
        Region region = new(new RectanglePolygon(10.25F, 20.75F, 30, 40), IntersectionRule.NonZero);

        Rectangle single = Assert.Single(region.Rectangles);
        Assert.Equal(Rectangle.FromLTRB(10, 21, 40, 61), single);
        Assert.Equal(Rectangle.FromLTRB(10, 21, 40, 61), region.Bounds);
    }

    [Theory]
    [InlineData(IntersectionRule.NonZero)]
    [InlineData(IntersectionRule.EvenOdd)]
    public void PathConstructor_Triangle_MatchesPathAtPixelCentres(IntersectionRule intersectionRule)
    {
        Polygon triangle = new([new PointF(0.25F, 0.25F), new PointF(20.25F, 0.25F), new PointF(0.25F, 40.25F)]);

        AssertMatchesPathAtPixelCentres(triangle, intersectionRule);
    }

    [Theory]
    [InlineData(0F, 0F, IntersectionRule.NonZero)]
    [InlineData(0F, 0F, IntersectionRule.EvenOdd)]
    [InlineData(-7.3F, -13.3F, IntersectionRule.NonZero)]
    public void PathConstructor_ConcavePolygon_MatchesPathAtPixelCentres(float offsetX, float offsetY, IntersectionRule intersectionRule)
        => AssertMatchesPathAtPixelCentres(CreateConcavePolygon(offsetX, offsetY), intersectionRule);

    [Theory]
    [InlineData(IntersectionRule.NonZero, true)]
    [InlineData(IntersectionRule.EvenOdd, false)]
    public void PathConstructor_NestedRectanglesSameWinding_FollowIntersectionRule(IntersectionRule intersectionRule, bool centreFilled)
    {
        // Both parts wind the same way, so the inner rectangle has winding number two.
        ComplexPolygon nested = new(new RectanglePolygon(0.25F, 0.25F, 40, 40), new RectanglePolygon(10.25F, 10.25F, 20, 20));

        AssertMatchesPathAtPixelCentres(nested, intersectionRule);
        Assert.Equal(centreFilled, new Region(nested, intersectionRule).Contains(20, 20));
    }

    [Fact]
    public void ContainsRegion_LShape_RequiresFullCoverage()
    {
        Region shape = new(new Rectangle(0, 0, 10, 20));
        shape.Add(new Rectangle(10, 10, 10, 10));

        Assert.True(shape.Contains(new Region(shape)));
        Assert.True(shape.Contains(new Region(new Rectangle(0, 0, 10, 20))));
        Assert.True(shape.Contains(new Region(new Rectangle(2, 12, 16, 6))));
        Assert.False(shape.Contains(new Region(new Rectangle(12, 2, 5, 5))));
        Assert.False(shape.Contains(new Region(new Rectangle(5, 5, 10, 10))));
        Assert.False(shape.Contains(new Region(new Rectangle(0, 0, 10, 21))));
    }

    [Fact]
    public void ContainsRegion_EmptyRegions_ReturnFalse()
    {
        Region shape = new(new Rectangle(0, 0, 10, 10));

        Assert.False(shape.Contains(new Region()));
        Assert.False(new Region().Contains(shape));
        Assert.False(new Region().Contains(new Region()));
    }

    [Fact]
    public void IntersectsRegion_RequiresSharedArea()
    {
        Region shape = new(new Rectangle(0, 0, 10, 20));
        shape.Add(new Rectangle(10, 10, 10, 10));

        Assert.True(shape.Intersects(new Region(new Rectangle(9, 9, 2, 2))));
        Assert.False(shape.Intersects(new Region(new Rectangle(12, 2, 5, 5))));
        Assert.False(shape.Intersects(new Region(new Rectangle(10, 0, 10, 10))));
        Assert.False(shape.Intersects(new Region(new Rectangle(20, 10, 5, 5))));
        Assert.False(shape.Intersects(new Region(new Rectangle(0, 20, 5, 5))));
        Assert.False(shape.Intersects(new Region()));
        Assert.False(new Region().Intersects(shape));
    }

    [Fact]
    public void IntersectsRegion_InterleavedIslands_DoNotIntersect()
    {
        Region first = new(new Rectangle(0, 0, 5, 10));
        first.Add(new Rectangle(10, 0, 5, 10));
        Region second = new(new Rectangle(5, 0, 5, 10));
        second.Add(new Rectangle(15, 0, 5, 10));

        Assert.False(first.Intersects(second));
        Assert.False(second.Intersects(first));

        second.Add(new Rectangle(4, 0, 1, 10));

        Assert.True(first.Intersects(second));
        Assert.True(second.Intersects(first));
    }

    [Fact]
    public void ContainsAndIntersectsRegion_MatchPixelMembership()
    {
        Region shape = new(CreateConcavePolygon(0, 0), IntersectionRule.NonZero);
        Region shifted = new(CreateConcavePolygon(3.3F, 5.75F), IntersectionRule.NonZero);
        Region insideLowerHalf = new(new RectanglePolygon(2.25F, 30.25F, 5, 5), IntersectionRule.NonZero);
        Region insideNotch = new(new RectanglePolygon(8.25F, 2.25F, 4, 4), IntersectionRule.NonZero);

        AssertRegionRelationsMatchPixelMembership(shape, shifted);
        AssertRegionRelationsMatchPixelMembership(shape, insideLowerHalf);
        AssertRegionRelationsMatchPixelMembership(insideLowerHalf, shape);
        AssertRegionRelationsMatchPixelMembership(shape, insideNotch);
    }

    /// <summary>
    /// Creates a polygon with a V-shaped notch in its top edge. Its slanted edges have slopes of one half,
    /// so no scanline centre crossing lands on a pixel centre.
    /// </summary>
    /// <param name="offsetX">The horizontal offset applied to every vertex.</param>
    /// <param name="offsetY">The vertical offset applied to every vertex.</param>
    private static Polygon CreateConcavePolygon(float offsetX, float offsetY)
        => new(
        [
            new PointF(0.25F + offsetX, 0.25F + offsetY),
            new PointF(10.25F + offsetX, 20.25F + offsetY),
            new PointF(20.25F + offsetX, 0.25F + offsetY),
            new PointF(20.25F + offsetX, 40.25F + offsetY),
            new PointF(0.25F + offsetX, 40.25F + offsetY)
        ]);

    /// <summary>
    /// Asserts that a region built from a path holds exactly the pixels whose centres the path contains,
    /// and that its bounds are the union of its rectangles.
    /// </summary>
    /// <param name="path">The path to convert.</param>
    /// <param name="intersectionRule">The fill rule.</param>
    private static void AssertMatchesPathAtPixelCentres(IPath path, IntersectionRule intersectionRule)
    {
        Region region = new(path, intersectionRule);
        RectangleF pathBounds = path.Bounds;
        int left = (int)MathF.Floor(pathBounds.Left) - 1;
        int top = (int)MathF.Floor(pathBounds.Top) - 1;
        int right = (int)MathF.Ceiling(pathBounds.Right) + 1;
        int bottom = (int)MathF.Ceiling(pathBounds.Bottom) + 1;

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                bool expected = path.Contains(new PointF(x + 0.5F, y + 0.5F), intersectionRule, Vector2.One);
                Assert.True(expected == region.Contains(x, y), $"Pixel ({x}, {y}) expected {expected}.");
            }
        }

        Rectangle expectedBounds = Rectangle.Empty;
        foreach (Rectangle rectangle in region.Rectangles)
        {
            expectedBounds = expectedBounds.IsEmpty ? rectangle : Rectangle.Union(expectedBounds, rectangle);
        }

        Assert.Equal(expectedBounds, region.Bounds);
    }

    /// <summary>
    /// Asserts that region containment and intersection agree with per-pixel membership.
    /// </summary>
    /// <param name="first">The region whose <c>Contains</c> and <c>Intersects</c> are tested.</param>
    /// <param name="second">The region passed as the argument.</param>
    private static void AssertRegionRelationsMatchPixelMembership(Region first, Region second)
    {
        Rectangle bounds = Rectangle.Union(first.Bounds, second.Bounds);
        bool anyShared = false;
        bool secondCovered = !second.IsEmpty;

        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                bool inFirst = first.Contains(x, y);
                bool inSecond = second.Contains(x, y);
                anyShared |= inFirst && inSecond;
                secondCovered &= !inSecond || inFirst;
            }
        }

        Assert.Equal(anyShared, first.Intersects(second));
        Assert.Equal(anyShared, second.Intersects(first));
        Assert.Equal(secondCovered, first.Contains(second));
    }
}
