using SixLabors.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SixLabors.Shapes.Tests.Issues
{
    public class Issue_ClippedPaths
    {
        [Fact]
        public void ClippedTriangle()
        {
            Polygon simplePath = new Polygon(new LinearLineSegment(
                           new PointF(10, 10),
                           new PointF(200, 150),
                           new PointF(50, 300)));

            Polygon hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(93, 85),
                            new PointF(65, 137)));

            IPath clippedPath = simplePath.Clip(hole1);
            IPath outline = clippedPath.GenerateOutline(5, new[] { 1f });

            Assert.False(outline.Contains(new PointF(74, 97)));
        }

        [Fact]
        public void ClippedTriangleGapInIntersections()
        {
            Polygon simplePath = new Polygon(new LinearLineSegment(
                           new PointF(10, 10),
                           new PointF(200, 150),
                           new PointF(50, 300)));

            Polygon hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(93, 85),
                            new PointF(65, 137)));

            IPath clippedPath = simplePath.Clip(hole1);
            IPath outline = clippedPath.GenerateOutline(5, new[] { 1f });
            PointF[] buffer = new PointF[20];

            PointF start = new PointF(outline.Bounds.Left - 1, 102);
            PointF end = new PointF(outline.Bounds.Right + 1, 102);

            int matches = outline.FindIntersections(start, end, buffer, 0);
            int maxIndex = buffer.Select((x, i) => new { x, i }).Where(x => x.x.X > 0).Select(x=>x.i).Last();
            Assert.Equal(matches-1, maxIndex);
        }
    }
}
