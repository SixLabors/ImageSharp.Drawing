using SixLabors.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    /// <summary>
    /// see https://github.com/SixLabors/Shapes/issues/16
    /// Also for furter details see https://github.com/SixLabors/Fonts/issues/22 
    /// </summary>
    public class Issue_16
    {
        [Fact]
        public void IndexOutoufRangeException()
        {
            InternalPath p = new InternalPath(new PointF[] { new Vector2(0, 0), new Vector2(0.000000001f, 0), new Vector2(0, 0.000000001f) }, true);

            IEnumerable<PointF> inter = p.FindIntersections(Vector2.One, Vector2.Zero);

            // if simplified to single point then we should never have an intersection
            Assert.Equal(0, inter.Count());
        }
    }
}
