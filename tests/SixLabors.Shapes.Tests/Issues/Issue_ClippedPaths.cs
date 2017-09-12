using SixLabors.Primitives;
using System;
using System.Collections.Generic;
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

            var clippedPath = simplePath.Clip(hole1);
            var outline = clippedPath.GenerateOutline(5, new[] { 1f });

            Assert.False(outline.Contains(new PointF(74, 97)));
        }
    }
}
