using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using SixLabors.Primitives;
    using System.Numerics;

    public class BezierLineSegmentTests
    {
        [Fact]
        public void SingleSegmentConstructor()
        {
            CubicBezierLineSegment segment = new CubicBezierLineSegment(new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 0), new Vector2(20, 0));
            var points = segment.Flatten();
            Assert.Contains(new Vector2(0, 0), points);
            Assert.Contains(new Vector2(10, 0), points);
            Assert.Contains(new Vector2(20, 0), points);
        }

        [Fact]
        public void MustHaveAtleast4Points()
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierLineSegment(new[] { new PointF(0, 0) }));
        }
    }
}
