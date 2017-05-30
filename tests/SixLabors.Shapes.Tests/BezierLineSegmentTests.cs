using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using System.Numerics;

    public class BezierLineSegmentTests
    {
        [Fact]
        public void SingleSegmentConstructor()
        {
            BezierLineSegment segment = new BezierLineSegment(new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 0), new Vector2(20, 0));
            System.Collections.Immutable.ImmutableArray<Vector2> points = segment.Flatten();
            Assert.Contains(new Vector2(0, 0), points);
            Assert.Contains(new Vector2(10, 0), points);
            Assert.Contains(new Vector2(20, 0), points);
        }

        [Fact]
        public void MustHaveAtleast4Points()
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new BezierLineSegment(new[] { new Vector2(0, 0) }));
        }
    }
}
