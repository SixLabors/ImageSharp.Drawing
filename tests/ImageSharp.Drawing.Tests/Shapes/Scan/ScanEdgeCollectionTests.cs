// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan
{
    public class ScanEdgeCollectionTests
    {
        private ScanEdgeCollection edges;

        private static MemoryAllocator MemoryAllocator => Configuration.Default.MemoryAllocator;

        private static readonly DebugDraw DebugDraw = new DebugDraw(nameof(ScanEdgeCollectionTests));

        private void VerifyEdge(
            float y0,
            float y1,
            (FuzzyFloat X, FuzzyFloat Y) arbitraryPoint,
            int emit0,
            int emit1,
            bool edgeUp)
        {
            foreach (ScanEdge e in this.edges.Edges)
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
        public void SimplePolygon_AllEmitCases()
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

            this.edges = ScanEdgeCollection.Create(polygon, MemoryAllocator, 16);

            Assert.Equal(19, this.edges.Edges.Length);

            this.VerifyEdge(1f, 2f, (2.5f, 1.5f), 1, 2, true);
            this.VerifyEdge(1f, 3f, (3.5f, 2f), 1, 1, false);
            this.VerifyEdge(1f, 3f, (5f, 2f), 1, 1, true);
            this.VerifyEdge(1f, 2f, (6.5f, 1.5f), 1, 2, false);
            this.VerifyEdge(2f, 3f, (8.5f, 2.5f), 1, 0, false);
            this.VerifyEdge(3f, 4f, (9f, 3.5f), 1, 0, false);
            this.VerifyEdge(4f, 5f, (9.5f, 4.5f), 1, 0, false);
            this.VerifyEdge(5f, 6f, (9.5f, 5.5f), 1, 1, false);
            this.VerifyEdge(6f, 7f, (8f, 6.5f), 2, 2, false);
            this.VerifyEdge(7f, 8f, (9f, 7.5f), 1, 1, false);
            this.VerifyEdge(7f, 8f, (6.5f, 7.5f), 1, 1, true);
            this.VerifyEdge(7f, 8f, (5.5f, 7.5f), 1, 1, false);
            this.VerifyEdge(7f, 8f, (4.5f, 7.5f), 1, 1, true);
            this.VerifyEdge(7f, 8f, (3.5f, 7.5f), 1, 1, false);
            this.VerifyEdge(6f, 8f, (2f, 7f), 0, 1, true);
            this.VerifyEdge(5f, 6f, (2.5f, 5.5f), 2, 1, true);
            this.VerifyEdge(4f, 5f, (2f, 4.5f), 0, 1, true);
            this.VerifyEdge(3f, 4f, (1.5f, 3.5f), 0, 1, true);
            this.VerifyEdge(2f, 3f, (1f, 1.5f), 1, 1, true);
        }

        [Fact]
        public void ComplexPolygon()
        {
            Polygon contour = PolygonFactory.CreatePolygon(
                (1, 1), (4, 1), (4, 2), (5, 2), (5, 5), (2, 5), (2, 4), (1, 4), (1, 1));
            Polygon hole = PolygonFactory.CreatePolygon(
                (2, 2), (2, 3), (3, 3), (3, 4), (4, 4), (4, 3), (3, 2));

            ComplexPolygon polygon = contour.Clip(hole);
            DebugDraw.Polygon(polygon, 1, 100);

            this.edges = ScanEdgeCollection.Create(polygon, MemoryAllocator, 16);

            Assert.Equal(8, this.edges.Count);

            this.VerifyEdge(1, 4, (1, 2), 1, 1, true);
            this.VerifyEdge(1, 2, (4, 1.5f), 1, 2, false);
            this.VerifyEdge(4, 5, (2, 4.5f), 2, 1, true);
            this.VerifyEdge(2, 5, (5, 3f), 1, 1, false);

            this.VerifyEdge(2, 3, (2, 2.5f), 2, 2, false);
            this.VerifyEdge(2, 3, (3.5f, 2.5f), 2, 1, true);
            this.VerifyEdge(3, 4, (3, 3.5f), 1, 2, false);
            this.VerifyEdge(3, 4, (4, 3.5f), 0, 2, true);
        }

        [Fact]
        public void NumericCornerCase_C()
        {
            this.edges = ScanEdgeCollection.Create(NumericCornerCasePolygons.C, MemoryAllocator, 4);
            Assert.Equal(2, this.edges.Count);
            this.VerifyEdge(3.5f, 4f, (2f, 3.75f), 1, 1, true);
            this.VerifyEdge(3.5f, 4f, (8f, 3.75f), 1, 1, false);
        }

        [Fact]
        public void NumericCornerCase_D()
        {
            this.edges = ScanEdgeCollection.Create(NumericCornerCasePolygons.D, MemoryAllocator, 4);
            Assert.Equal(5, this.edges.Count);

            this.VerifyEdge(3.25f, 4f, (12f, 3.75f), 1, 1, true);
            this.VerifyEdge(3.25f, 3.5f, (15f, 3.375f), 1, 0, false);
            this.VerifyEdge(3.5f, 4f, (18f, 3.75f), 1, 1, false);

            // TODO: verify 2 more edges
        }

        [Fact]
        public void NumericCornerCase_H_ShouldCollapseNearZeroEdge()
        {
            this.edges = ScanEdgeCollection.Create(NumericCornerCasePolygons.H, MemoryAllocator, 4);

            Assert.Equal(3, this.edges.Count);
            this.VerifyEdge(1.75f, 2f, (15f, 1.875f), 1, 1, true);
            this.VerifyEdge(1.75f, 2.25f, (16f, 2f), 1, 1, false);

            // this places two dummy points:
            this.VerifyEdge(2f, 2.25f, (15f, 2.125f), 2, 1, true);
        }

        private static FuzzyFloat F(float value, float eps) => new FuzzyFloat(value, eps);
    }
}
