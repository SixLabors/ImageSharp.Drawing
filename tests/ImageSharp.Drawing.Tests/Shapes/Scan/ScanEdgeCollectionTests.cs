// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan;

public class ScanEdgeCollectionTests
{
    private static MemoryAllocator MemoryAllocator => Configuration.Default.MemoryAllocator;

    private static readonly DebugDraw DebugDraw = new(nameof(ScanEdgeCollectionTests));

    private static void VerifyEdge(
        ScanEdgeCollection edges,
        float y0,
        float y1,
        (FuzzyFloat X, FuzzyFloat Y) arbitraryPoint,
        int emit0,
        int emit1,
        bool edgeUp)
    {
        foreach (ScanEdge e in edges.Edges)
        {
            if (y0 == e.Y0 && y1 == e.Y1)
            {
                bool containsPoint = arbitraryPoint.X.Equals(e.GetX(arbitraryPoint.Y));
                if (containsPoint)
                {
                    Assert.Equal(emit0, e.EmitV0);
                    Assert.Equal(emit1, e.EmitV1);
                    Assert.Equal(edgeUp, e.EdgeUp);

                    // Found the edge
                    return;
                }
            }
        }

        Assert.True(false, $"Failed to find edge {y0}->{y1} with {arbitraryPoint}");
    }

    [Fact]
    [ValidateDisposedMemoryAllocations]
    public void SimplePolygon_AllEmitCases()
    {
        static void RunTest()
        {
            // see: SimplePolygon_AllEmitCases.png
            Polygon polygon = PolygonFactory.CreatePolygon(
                (1, 2),
                (2, 2),
                (3, 1),
                (4, 3),
                (6, 1),
                (7, 2),
                (8, 2),
                (9, 3),
                (9, 4),
                (10, 5),
                (9, 6),
                (8, 6),
                (8, 7),
                (9, 7),
                (9, 8),
                (7, 8),
                (6, 7),
                (5, 8),
                (4, 7),
                (3, 8),
                (2, 8),
                (2, 6),
                (3, 5),
                (2, 5),
                (2, 4),
                (1, 3));

            DebugDraw.Polygon(polygon, 1, 100);

            using ScanEdgeCollection edges = ScanEdgeCollection.Create(polygon, MemoryAllocator, 16);

            Assert.Equal(19, edges.Edges.Length);

            VerifyEdge(edges, 1f, 2f, (2.5f, 1.5f), 1, 2, true);
            VerifyEdge(edges, 1f, 3f, (3.5f, 2f), 1, 1, false);
            VerifyEdge(edges, 1f, 3f, (5f, 2f), 1, 1, true);
            VerifyEdge(edges, 1f, 2f, (6.5f, 1.5f), 1, 2, false);
            VerifyEdge(edges, 2f, 3f, (8.5f, 2.5f), 1, 0, false);
            VerifyEdge(edges, 3f, 4f, (9f, 3.5f), 1, 0, false);
            VerifyEdge(edges, 4f, 5f, (9.5f, 4.5f), 1, 0, false);
            VerifyEdge(edges, 5f, 6f, (9.5f, 5.5f), 1, 1, false);
            VerifyEdge(edges, 6f, 7f, (8f, 6.5f), 2, 2, false);
            VerifyEdge(edges, 7f, 8f, (9f, 7.5f), 1, 1, false);
            VerifyEdge(edges, 7f, 8f, (6.5f, 7.5f), 1, 1, true);
            VerifyEdge(edges, 7f, 8f, (5.5f, 7.5f), 1, 1, false);
            VerifyEdge(edges, 7f, 8f, (4.5f, 7.5f), 1, 1, true);
            VerifyEdge(edges, 7f, 8f, (3.5f, 7.5f), 1, 1, false);
            VerifyEdge(edges, 6f, 8f, (2f, 7f), 0, 1, true);
            VerifyEdge(edges, 5f, 6f, (2.5f, 5.5f), 2, 1, true);
            VerifyEdge(edges, 4f, 5f, (2f, 4.5f), 0, 1, true);
            VerifyEdge(edges, 3f, 4f, (1.5f, 3.5f), 0, 1, true);
            VerifyEdge(edges, 2f, 3f, (1f, 1.5f), 1, 1, true);
        }

        FeatureTestRunner.RunWithHwIntrinsicsFeature(RunTest, HwIntrinsics.AllowAll | HwIntrinsics.DisableAVX | HwIntrinsics.DisableSSE41 | HwIntrinsics.DisableArm64AdvSimd);
    }

    [Fact]
    public void ComplexPolygon()
    {
        Polygon contour = PolygonFactory.CreatePolygon(
            (1, 1), (4, 1), (4, 2), (5, 2), (5, 5), (2, 5), (2, 4), (1, 4), (1, 1));
        Polygon hole = PolygonFactory.CreatePolygon(
            (2, 2), (2, 3), (3, 3), (3, 4), (4, 4), (4, 3), (3, 2));

        IPath polygon = contour.Clip(hole);
        DebugDraw.Polygon(polygon, 1, 100);

        using ScanEdgeCollection edges = ScanEdgeCollection.Create(polygon, MemoryAllocator, 16);

        Assert.Equal(8, edges.Count);

        VerifyEdge(edges, 1, 4, (1, 2), 1, 1, true);
        VerifyEdge(edges, 1, 2, (4, 1.5f), 1, 2, false);
        VerifyEdge(edges, 4, 5, (2, 4.5f), 2, 1, true);
        VerifyEdge(edges, 2, 5, (5, 3f), 1, 1, false);

        VerifyEdge(edges, 2, 3, (2, 2.5f), 2, 2, false);
        VerifyEdge(edges, 2, 3, (3.5f, 2.5f), 2, 1, true);
        VerifyEdge(edges, 3, 4, (3, 3.5f), 1, 2, false);
        VerifyEdge(edges, 3, 4, (4, 3.5f), 0, 2, true);
    }

    [Fact]
    public void NumericCornerCase_C()
    {
        using ScanEdgeCollection edges = ScanEdgeCollection.Create(NumericCornerCasePolygons.C, MemoryAllocator, 4);
        Assert.Equal(2, edges.Count);
        VerifyEdge(edges, 3.5f, 4f, (2f, 3.75f), 1, 1, true);
        VerifyEdge(edges, 3.5f, 4f, (8f, 3.75f), 1, 1, false);
    }

    [Fact]
    public void NumericCornerCase_D()
    {
        using ScanEdgeCollection edges = ScanEdgeCollection.Create(NumericCornerCasePolygons.D, MemoryAllocator, 4);
        Assert.Equal(5, edges.Count);

        VerifyEdge(edges, 3.25f, 4f, (12f, 3.75f), 1, 1, true);
        VerifyEdge(edges, 3.25f, 3.5f, (15f, 3.375f), 1, 0, false);
        VerifyEdge(edges, 3.5f, 4f, (18f, 3.75f), 1, 1, false);

        // TODO: verify 2 more edges
    }

    [Fact]
    public void NumericCornerCase_H_ShouldCollapseNearZeroEdge()
    {
        using ScanEdgeCollection edges = ScanEdgeCollection.Create(NumericCornerCasePolygons.H, MemoryAllocator, 4);

        Assert.Equal(3, edges.Count);
        VerifyEdge(edges, 1.75f, 2f, (15f, 1.875f), 1, 1, true);
        VerifyEdge(edges, 1.75f, 2.25f, (16f, 2f), 1, 1, false);

        // this places two dummy points:
        VerifyEdge(edges, 2f, 2.25f, (15f, 2.125f), 2, 1, true);
    }

    private static FuzzyFloat F(float value, float eps) => new(value, eps);
}
