using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using System.Buffers;
    using System.Numerics;

    public class ComplexPolygonTests
    {
        [Fact]
        public void MissingIntersection()
        {
            var data = new float[6];
            Polygon simplePath = new Polygon(new LinearLineSegment(
                              new Vector2(10, 10),
                              new Vector2(200, 150),
                              new Vector2(50, 300)));

            Polygon hole1 = new Polygon(new LinearLineSegment(
                            new Vector2(65, 137),
                            new Vector2(37, 85),
                            new Vector2(93, 85)));

            var intersections1 = ScanY(hole1, 137, data, 6, 0);
            Assert.Equal(2, intersections1);
            var poly = simplePath.Clip(hole1);

            var intersections = ScanY(poly, 137, data, 6, 0);

            // returns an even number of points
            Assert.Equal(4, intersections);
        }

        public int ScanY(IShape shape, int y, float[] buffer, int length, int offset)
        {
            Vector2 start = new Vector2(shape.Bounds.Left - 1, y);
            Vector2 end = new Vector2(shape.Bounds.Right + 1, y);
            Vector2[] innerbuffer = ArrayPool<Vector2>.Shared.Rent(length);
            try
            {
                int count = shape.FindIntersections(start, end, innerbuffer, length, 0);

                for (int i = 0; i < count; i++)
                {
                    buffer[i + offset] = innerbuffer[i].X;
                }

                return count;
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(innerbuffer);
            }
        }
    }
}
