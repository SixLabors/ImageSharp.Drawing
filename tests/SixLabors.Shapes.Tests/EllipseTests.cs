using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using System.Numerics;

    public class EllipseTests
    {
        [Theory]
        [InlineData(-1, true)]
        [InlineData(0, true)]
        [InlineData(0.00001, false)]
        [InlineData(1, false)]
        public void WidthMustBeGreateThan0(float width, bool throws)
        {
            if (throws)
            {
                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new Ellipse(0,0, width, 99));

                Assert.Equal("width", ex.ParamName);
            }
            else
            {
                var p = new Ellipse(0, 0, width, 99);
                Assert.NotNull(p);
            }
        }

        [Theory]
        [InlineData(-1, true)]
        [InlineData(0, true)]
        [InlineData(0.00001, false)]
        [InlineData(1, false)]
        public void HeightMustBeGreateThan0(float height, bool throws)
        {
            if (throws)
            {
                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new Ellipse(0,0,99, height));

                Assert.Equal("height", ex.ParamName);
            }
            else
            {
                var p = new Ellipse(0, 0, 99, height);
                Assert.NotNull(p);
            }
        }

        [Fact]
        public void ClippingCornerShouldReturn1Points()
        {
            var poly = new Ellipse(50, 50, 30, 50);
            var points = poly.FindIntersections(new Vector2(0, 75), new Vector2(100, 75)).ToArray();

            Assert.Equal(1, points.Length);
        }

        [Fact]
        public void AcrossEllipsShouldReturn2()
        {
            var poly = new Ellipse(50, 50, 30, 50);
            var points = poly.FindIntersections(new Vector2(0, 49), new Vector2(100, 49)).ToArray();

            Assert.Equal(2, points.Length);
        }
    }       
}
