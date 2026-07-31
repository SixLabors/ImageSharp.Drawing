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

    [Fact]
    public void Normalize_SelfIntersectingBowtie_KeepsOnlyThePositiveWindingLobe()
    {
        // The bowtie crosses itself at (50, 50), giving its two lobes opposite winding.
        // Normalization fills using positive winding, so only the positively wound lobe
        // survives; reversing the point order keeps the opposite lobe.
        PointF[] points =
        [
            new PointF(0, 0),
            new PointF(100, 100),
            new PointF(100, 0),
            new PointF(0, 100),
        ];

        IPath normalized = new Polygon(points).Normalize();
        Assert.Equal(RectangleF.FromLTRB(0, 0, 50, 100), normalized.Bounds);

        Array.Reverse(points);
        IPath reversed = new Polygon(points).Normalize();
        Assert.Equal(RectangleF.FromLTRB(50, 0, 100, 100), reversed.Bounds);
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
