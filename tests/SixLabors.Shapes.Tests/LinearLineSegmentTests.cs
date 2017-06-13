using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using SixLabors.Primitives;
    using System.Numerics;

    public class LinearLineSegmentTests
    {
        [Fact]
        public void SingleSegmentConstructor()
        {
            LinearLineSegment segment = new LinearLineSegment(new Vector2(0, 0), new Vector2(10, 10));
            var flatPath = segment.Flatten();
            Assert.Equal(2, flatPath.Count);
            Assert.Equal(new PointF(0, 0), flatPath[0]);
            Assert.Equal(new PointF(10, 10), flatPath[1]);
        }

        [Fact]
        public void MustHaveAtleast2Points()
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new LinearLineSegment(new[] { new PointF(0, 0) }));
        }

        [Fact]
        public void NullPointsArrayThrowsCountException()
        {
            Assert.ThrowsAny<ArgumentNullException>(() => {
                new LinearLineSegment((PointF[])null);
            });
        }
    }
}
