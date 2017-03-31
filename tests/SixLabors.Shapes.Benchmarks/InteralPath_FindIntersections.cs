using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using BenchmarkDotNet.Attributes;
using SixLabors.Shapes;

namespace SixLabors.Shapes.Benchmarks
{
    public class InteralPath_FindIntersections
    {
        [Benchmark]
        public Vector2[] InternalOld()
        {
            var vectors = new Ellipse(new System.Numerics.Vector2(0, 0), new Size(20, 10))
                .Flatten()
                .First().Points.ToArray();

            var path = new InternalPath_Old(vectors, true);

            Vector2[] buffer = new Vector2[path.PointCount*2];
            for (var y = path.Bounds.Top; y < path.Bounds.Bottom; y += (1f / 32f))
            {
                path.FindIntersections(new Vector2(path.Bounds.Left - 1, y), new Vector2(path.Bounds.Right + 1, y), buffer, path.PointCount, 0);
            }
            return buffer;
        }

        [Benchmark]
        public Vector2[] InternalNew()
        {
            var vectors = new Ellipse(new System.Numerics.Vector2(0, 0), new Size(20, 10))
                .Flatten()
                .First().Points.ToArray();

            var path = new InternalPath(vectors, true);

            Vector2[] buffer = new Vector2[path.PointCount*2];
            for (var y = path.Bounds.Top; y < path.Bounds.Bottom; y += (1f / 32f))
            {
                path.FindIntersections(new Vector2(path.Bounds.Left - 1, y), new Vector2(path.Bounds.Right + 1, y), buffer, path.PointCount, 0);
            }
            return buffer;
        }
    }
}
