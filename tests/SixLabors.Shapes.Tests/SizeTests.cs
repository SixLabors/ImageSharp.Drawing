using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    public class SizeTests
    {
        [Fact]
        public void Addition()
        {
            Size actual = new Size(12, 13) + new Size(8, 7);
            Assert.Equal(new Size(20, 20), actual);
        }

        [Fact]
        public void Subtraction()
        {
            Size actual = new Size(12, 13) - new Size(2, 2);
            Assert.Equal(new Size(10, 11), actual);
        }

        [Fact]
        public void EqualOperator_True()
        {
            bool actual = new Size(12, 13) == new Size(12, 13);
            Assert.True(actual);
        }

        [Fact]
        public void EqualOperator_False()
        {
            bool actual = new Size(12, 13) == new Size(1, 3);
            Assert.False(actual);
        }

        [Fact]
        public void Equal_True()
        {
            bool actual = new Size(12, 13).Equals((object)new Size(12, 13));
            Assert.True(actual);
        }

        [Fact]
        public void Equal_False_SameType()
        {
            bool actual = new Size(12, 13).Equals((object)new Size(1, 3));
            Assert.False(actual);
        }

        [Fact]
        public void Equal_False_DiffType()
        {
            bool actual = new Size(12, 13).Equals((object)new object());
            Assert.False(actual);
        }

        [Fact]
        public void NotEqualOperator_False()
        {
            bool actual = new Size(12, 13) != new Size(12, 13);
            Assert.False(actual);
        }

        [Fact]
        public void NotEqualOperator_True()
        {
            bool actual = new Size(2, 1) != new Size(12, 13);
            Assert.True(actual);
        }

        [Fact]
        public void GetHashCodeTest()
        {
            Size inst1 = new Size(10, 10);
            Size inst2 = new Size(10, 10);

            Assert.Equal(inst1.GetHashCode(), inst2.GetHashCode());
        }

        [Fact]
        public void ToString_Val()
        {
            Size p = new Size(2,3);
            Assert.Equal("Size [ Width=2, Height=3 ]", p.ToString());
        }
    }
}
