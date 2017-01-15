using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Shaper2D.Tests
{
    public class RectangleTests
    {
        public static TheoryData<TestPoint, TestSize, TestPoint, bool> PointInPolygonTheoryData =
            new TheoryData<TestPoint, TestSize, TestPoint, bool>
            {
               {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(10,10), // test
                    true
                }, //corner is inside
                {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(9,9), // test
                    false
                }, //corner is inside
            };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint location, TestSize size, TestPoint point, bool isInside)
        {
            var shape = new Rectangle(location, size);
            Assert.Equal(isInside, shape.Contains(point));
        }
    }
}
