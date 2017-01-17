using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Shaper2D.Tests
{
    public class SizeTests
    {
        [Fact]
        public void EmptyIsDefault()
        {
            Assert.Equal(true, Size.Empty.IsEmpty);
        }

        [Fact]
        public void Addition()
        {
            var actual = new Size(12, 13) + new Size(8, 7);
            Assert.Equal(new Size(20, 20), actual);
        }

        [Fact]
        public void Subtraction()
        {
            var actual = new Size(12, 13) - new Size(2, 2);
            Assert.Equal(new Size(10, 11), actual);
        }

        [Fact]
        public void EqualOperator_True()
        {
            var actual = new Size(12, 13) == new Size(12, 13);
            Assert.True(actual);
        }

        [Fact]
        public void EqualOperator_False()
        {
            var actual = new Size(12, 13) == new Size(1, 3);
            Assert.False(actual);
        }

        [Fact]
        public void Equal_True()
        {
            var actual = new Size(12, 13).Equals((object)new Size(12, 13));
            Assert.True(actual);
        }

        [Fact]
        public void Equal_Empty()
        {
            var actual = default(Size) == Size.Empty;
            Assert.True(actual);
        }

        [Fact]
        public void NotEqual_Empty()
        {
            var actual = default(Size) != Size.Empty;
            Assert.False(actual);
        }

        [Fact]
        public void Equal_False_SameType()
        {
            var actual = new Size(12, 13).Equals((object)new Size(1, 3));
            Assert.False(actual);
        }

        [Fact]
        public void Equal_False_DiffType()
        {
            var actual = new Size(12, 13).Equals((object)new object());
            Assert.False(actual);
        }

        [Fact]
        public void NotEqualOperator_False()
        {
            var actual = new Size(12, 13) != new Size(12, 13);
            Assert.False(actual);
        }

        [Fact]
        public void NotEqualOperator_True()
        {
            var actual = new Size(2, 1) != new Size(12, 13);
            Assert.True(actual);
        }

        [Fact]
        public void GetHashCodeTest()
        {
            var inst1 = new Size(10, 10);
            var inst2 = new Size(10, 10);

            Assert.Equal(inst1.GetHashCode(), inst2.GetHashCode());
        }

        [Fact]
        public void ToString_Empty()
        {
            Assert.Equal("Size [ Empty ]", Size.Empty.ToString());
        }

        [Fact]
        public void ToString_Val()
        {
            var p = new Size(2,3);
            Assert.Equal("Size [ Width=2, Height=3 ]", p.ToString());
        }
    }
}
