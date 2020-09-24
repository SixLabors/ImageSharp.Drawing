// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Shapes.Scan;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan
{
    public class ScanEdgeCollectionTests
    {
        private ScanEdgeCollection _edges;

        private static MemoryAllocator MemoryAllocator => Configuration.Default.MemoryAllocator;
        
        private static readonly TolerantComparer DefaultComparer = new TolerantComparer(0.001f);
        
        private static readonly DebugDraw DebugDraw = new DebugDraw(nameof(ScanEdgeCollectionTests));

        private void VerifyEdge(float y0, float y1, (float X, float Y) arbitraryPoint, int emit0, int emit1, bool edgeUp)
            => VerifyEdge(y0, y1, arbitraryPoint, emit0, emit1, edgeUp, DefaultComparer);

        private void VerifyEdge(float y0,
            float y1,
            (float X, float Y) arbitraryPoint,
            int emit0,
            int emit1,
            bool edgeUp,
            in TolerantComparer comparer)
        {

            foreach (ScanEdge e in _edges.Edges)
            {
                if (comparer.AreEqual(y0, e.Y0) && comparer.AreEqual(y1, e.Y1))
                {
                    // x = P*y + Q
                    bool containsPoint = comparer.AreEqual(arbitraryPoint.X, e.P * arbitraryPoint.Y + e.Q);
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
            var polygon = PolygonTest.CreatePolygon(
                (1, 2), (2, 2), (3, 1), (4, 3), (6, 1), (7, 2), (8, 2), (9, 3), 
                (9, 4), (10, 5), (9, 6), (8, 6), (8, 7), (9,7), (9, 8),
                (7, 8), (6, 7), (5, 8), (4, 7), (3, 8), (2, 8),
                (2, 6), (3, 5), (2, 5), (2, 4), (1, 3)
            );
            DebugDraw.Polygon(polygon, 1, 100);
            
            _edges = ScanEdgeCollection.Create(polygon, MemoryAllocator, DefaultComparer);
            
            Assert.Equal(19, _edges.Edges.Length);
            
            VerifyEdge(1f, 2f, (2.5f, 1.5f), 0, 2, true);
            VerifyEdge(1f, 3f, (3.5f, 2f), 0, 0, false);
            VerifyEdge(1f, 3f, (5f, 2f), 0, 0, true);
            VerifyEdge(1f, 2f, (6.5f, 1.5f), 0, 2, false);
            VerifyEdge(2f, 3f, (8.5f, 2.5f), 1, 0, false);
            VerifyEdge(3f, 4f, (9f, 3.5f), 1, 0, false);
            VerifyEdge(4f, 5f, (9.5f, 4.5f), 1, 0, false);
            VerifyEdge(5f, 6f, (9.5f, 5.5f), 1, 1, false);
            VerifyEdge(6f, 7f, (8f, 6.5f), 2, 2, false);
            VerifyEdge(7f, 8f, (9f, 7.5f), 1, 1, false);
            VerifyEdge(7f, 8f, (6.5f, 7.5f), 0, 1, true);
            VerifyEdge(7f, 8f, (5.5f, 7.5f), 0, 0, false);
            VerifyEdge(7f, 8f, (4.5f, 7.5f), 0, 0, true);
            VerifyEdge(7f, 8f, (3.5f, 7.5f), 0, 1, false);
            VerifyEdge(6f, 8f, (2f, 7f), 0, 1, true);
            VerifyEdge(5f, 6f, (2.5f, 5.5f), 2, 1, true);
            VerifyEdge(4f, 5f, (2f, 4.5f), 0, 1, true);
            VerifyEdge(3f, 4f, (1.5f, 3.5f), 0, 1, true);
            VerifyEdge(2f, 3f, (1f, 1.5f), 1, 1, true);
        }

        [Fact]
        public void YDifferenceUnderTreshold_HorizontalEdgeExcluded()
        {
            // (0, 2) -> (10, 1.1) counts as horizontal with Epsilon = 1.0
            var polygon = PolygonTest.CreatePolygon((0, 2), (10, 1.1f), (10, 4));
            DebugDraw.Polygon(polygon, 1, 50);
            
            var comparer = new TolerantComparer(1f);
            _edges = ScanEdgeCollection.Create(polygon, MemoryAllocator, comparer);
            
            Assert.Equal(2, _edges.Edges.Length);
            VerifyEdge(1, 4, (10, 2), 1, 0, false);
            VerifyEdge(2, 4, (5, 3), 1, 0, true);
        }

        [Fact]
        public void YDifferenceOverTreshold_EdgeIncludedAsNonHorizontal()
        {
            // (0, 2) -> (10, 0.9) counts as NON-horizontal with Epsilon = 1.0
            var polygon = PolygonTest.CreatePolygon((0, 2), (10, 0.9f), (10, 4));
            DebugDraw.Polygon(polygon, 1, 50);
            
            var comparer = new TolerantComparer(1f);
            _edges = ScanEdgeCollection.Create(polygon, MemoryAllocator, comparer);
            
            Assert.Equal(3, _edges.Edges.Length);
            VerifyEdge(0.9f, 2, (5, 1.45f), 1, 0, true);
            VerifyEdge(1, 4, (10, 2), 0, 0, false);
            VerifyEdge(2, 4, (5, 3), 1, 0, true);
        }

        [Fact]
        public void Create_ComplexPolygon()
        {
            
        }
        
        private static PointF P(float x, float y) => new PointF(x, y);
    }
}