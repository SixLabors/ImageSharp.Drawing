using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Shaper2D.Tests
{
    public class PointTests
    {
        [Fact]
        public void EmptyIsDefault()
        {
            Assert.Equal(true, Point.Empty.IsEmpty);
        }

        [Fact]
        public void Addition()
        {
            var actual = new Point(12, 13) + new Point(8, 7);
            Assert.Equal(new Point(20, 20), actual);
        }

        [Fact]
        public void Subtraction()
        {
            var actual = new Point(12, 13) - new Point(2, 2);
            Assert.Equal(new Point(10, 11), actual);
        }

        [Fact]
        public void EqualOperator_True()
        {
            var actual = new Point(12, 13) == new Point(12, 13);
            Assert.True(actual);
        }

        [Fact]
        public void EqualOperator_False()
        {
            var actual = new Point(12, 13) == new Point(1, 3);
            Assert.False(actual);
        }

        [Fact]
        public void Equal_True()
        {
            var actual = new Point(12, 13).Equals((object)new Point(12, 13));
            Assert.True(actual);
        }

        [Fact]
        public void Equal_False_SameType()
        {
            var actual = new Point(12, 13).Equals((object)new Point(1, 3));
            Assert.False(actual);
        }

        [Fact]
        public void Equal_False_DiffType()
        {
            var actual = new Point(12, 13).Equals((object)new object());
            Assert.False(actual);
        }

        [Fact]
        public void NotEqualOperator_False()
        {
            var actual = new Point(12, 13) != new Point(12, 13);
            Assert.False(actual);
        }

        [Fact]
        public void NotEqualOperator_True()
        {
            var actual = new Point(2, 1) != new Point(12, 13);
            Assert.True(actual);
        }

        [Fact]
        public void Offset_Size()
        {
            var actual = new Point(12, 13).Offset(new Size(3, 2));
            Assert.Equal(new Point(15, 15), actual);
        }

        [Fact]
        public void GetHashCodeTest()
        {
            var inst1 = new Point(10, 10);
            var inst2 = new Point(10, 10);

            Assert.Equal(inst1.GetHashCode(), inst2.GetHashCode());
        }

        [Fact]
        public void ToString_Empty()
        {
            Assert.Equal("Point [ Empty ]", Point.Empty.ToString());
        }

        [Fact]
        public void ToString_Val()
        {
            var p = new Point(2,3);
            Assert.Equal("Point [ X=2, Y=3 ]", p.ToString());
        }
    }
}
