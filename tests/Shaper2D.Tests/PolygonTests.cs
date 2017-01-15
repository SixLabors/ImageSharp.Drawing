using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Shaper2D.Tests
{
    public class LinearPolygonTests
    {
        public static TheoryData<TestPoint[], TestPoint, bool> PointInPolygonTheoryData =
            new TheoryData<TestPoint[],  TestPoint, bool>
            {
               {
                    new TestPoint[] { new Point(10,10), new Point(10,100), new Point(100,100), new Point(100,10) }, // loc
                    new Point(10,10), // test
                    true
                }, //corner is inside
               {
                    new TestPoint[] { new Point(10,10), new Point(10,100), new Point(100,100), new Point(100,10) }, // loc
                    new Point(10,11), // test
                    true
                }, //on line
                {
                    new TestPoint[] { new Point(10,10), new Point(10,100), new Point(100,100), new Point(100,10) }, // loc
                    new Point(9,9), // test
                    false
                }, //corner is inside
            };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint[] controlPoints, TestPoint point, bool isInside)
        {
            var shape = new LinearPolygon(controlPoints.Select(x=>(Point)x).ToArray());
            Assert.Equal(isInside, shape.Contains(point));
        }
    }
}
