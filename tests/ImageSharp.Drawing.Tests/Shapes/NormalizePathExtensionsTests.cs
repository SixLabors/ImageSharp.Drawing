// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class NormalizePathExtensionsTests
{
    [Fact]
    public void Normalize_SimpleRectangle_PreservesBounds()
    {
        IPath path = new RectanglePolygon(10, 20, 30, 40);

        IPath normalized = path.Normalize();

        Assert.Equal(path.Bounds, normalized.Bounds);
    }

    /// <summary>
    /// Checks that normalization keeps both lobes in either vertex order.
    /// </summary>
    /// <param name="reverse">Whether to reverse the contour before normalization.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Normalize_SelfIntersectingBowtie_KeepsBothLobes(bool reverse)
    {
        // The lobes meet at (50, 50). Their winding counts are +1 and -1.
        PointF[] points =
        [
            new PointF(0, 0),
            new PointF(100, 100),
            new PointF(100, 0),
            new PointF(0, 100),
        ];

        if (reverse)
        {
            // Reversing the vertices swaps the winding signs of the two lobes.
            // Both counts remain nonzero, so both lobes must remain filled.
            Array.Reverse(points);
        }

        ComplexPolygon normalized = Assert.IsType<ComplexPolygon>(new Polygon(points).Normalize());
        Assert.Collection(
            normalized.Paths.OrderBy(x => x.Bounds.Left),
            left => Assert.Equal(RectangleF.FromLTRB(0, 0, 50, 100), left.Bounds),
            right => Assert.Equal(RectangleF.FromLTRB(50, 0, 100, 100), right.Bounds));
    }

    [Fact]
    public void Normalize_OverlappingContours_MergesIntoOneArea()
    {
        IPath combined = new ComplexPolygon(
            new RectanglePolygon(0, 0, 60, 60),
            new RectanglePolygon(40, 0, 60, 60));

        IPath normalized = combined.Normalize();

        Assert.Equal(RectangleF.FromLTRB(0, 0, 100, 60), normalized.Bounds);
    }
}
