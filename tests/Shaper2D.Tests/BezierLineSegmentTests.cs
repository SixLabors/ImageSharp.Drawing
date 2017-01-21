using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Shaper2D.Tests
{
    public class BezierLineSegmentTests
    {
        [Fact]
        public void SingleSegmentConstructor()
        {
            var segment = new BezierLineSegment(new Point(0, 0), new Point(10, 0), new Point(10, 0), new Point(20, 0));
            var points = segment.Flatten();
            Assert.Equal(51, points.Length);
            Assert.Contains(new Point(0, 0), points);
            Assert.Contains(new Point(10, 0), points);
            Assert.Contains(new Point(20, 0), points);
        }

        [Fact]
        public void MustHaveAtleast4Points()
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => new BezierLineSegment(new[] { new Point(0, 0) }));
        }
    }
}
