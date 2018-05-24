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
    /// see https://github.com/SixLabors/Shapes/issues/19
    /// Also for furter details see https://github.com/SixLabors/Fonts/issues/22 
    /// </summary>
    public class Issue_19
    {
        [Fact]
        public void LoosingPartOfLineIfSelfIntersects()
        {
            PointF[] line1 = new PointF[] { new Vector2(117f, 199f), new Vector2(31f, 210f), new Vector2(35f, 191f), new Vector2(117f, 199f), new Vector2(2f, 9f) };
            Path path = new Path(new LinearLineSegment(line1));

            IPath outline = path.GenerateOutline(5f);
            // all points must not be in the outline;
            foreach (PointF v in line1)
            {
                Assert.True(outline.Contains(v), $"Outline does not contain {v}");
            }
        }

        [Fact]
        public void PAthLoosingSelfIntersectingPoint()
        {
            PointF[] line1 = new PointF[] { new Vector2(117f, 199f), new Vector2(31f, 210f), new Vector2(35f, 191f), new Vector2(117f, 199f), new Vector2(2f, 9f) };
            Path path = new Path(new LinearLineSegment(line1));
            IReadOnlyList<PointF> pathPoints = path.Flatten().First().Points;

            // all points must not be in the outline;
            foreach (PointF v in line1)
            {
                Assert.Contains(v, pathPoints);
            }
        }

        [Fact]
        public void InternalPathLoosingSelfIntersectingPoint()
        {
            PointF[] line1 = new PointF[] { new Vector2(117f, 199f), new Vector2(31f, 210f), new Vector2(35f, 191f), new Vector2(117f, 199f), new Vector2(2f, 9f) };
            InternalPath path = new InternalPath(new LinearLineSegment(line1), false);
            IReadOnlyList<PointF> pathPoints = path.Points();

            // all points must not be in the outline;
            foreach (PointF v in line1)
            {
                Assert.Contains(v, pathPoints);
            }
        }
    }
}
