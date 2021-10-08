// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Linq;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    /// <summary>
    /// The internal path tests.
    /// </summary>
    public class PathTests
    {
        [Fact]
        public void Bounds()
        {
            var seg1 = new LinearLineSegment(new PointF(0, 0), new PointF(2, 2));
            var seg2 = new LinearLineSegment(new PointF(4, 4), new PointF(5, 5));

            var path = new Path(seg1, seg2);

            Assert.Equal(0, path.Bounds.Left);
            Assert.Equal(5, path.Bounds.Right);
            Assert.Equal(0, path.Bounds.Top);
            Assert.Equal(5, path.Bounds.Bottom);
        }

        [Fact]
        public void SimplePath()
        {
            var path = new Path(new LinearLineSegment(new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10)));
            System.Collections.Generic.IReadOnlyList<PointF> points = path.Flatten().Single().Points.ToArray();

            Assert.Equal(4, points.Count);
            Assert.Equal(new PointF(0, 0), points[0]);
            Assert.Equal(new PointF(10, 0), points[1]);
            Assert.Equal(new PointF(10, 10), points[2]);
            Assert.Equal(new PointF(0, 10), points[3]);
        }
    }
}
