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
        private readonly Vector2[] vectors;

        public InteralPath_FindIntersections()
        {
            this.vectors = new Ellipse(new System.Numerics.Vector2(0, 0), new Size(2000, 1000))
                .Flatten()
                .First().Points.ToArray();
        }

        [Benchmark(Baseline = true)]
        public Vector2[] InternalOld()
        {
            Vector2[] buffer = new Vector2[vectors.Length];
            var path = new InternalPath_Old(vectors, true);

            for (var y = path.Bounds.Top; y < path.Bounds.Bottom; y += (1f / 32f))
            {
                path.FindIntersections(new Vector2(path.Bounds.Left - 1, y), new Vector2(path.Bounds.Right + 1, y), buffer, path.PointCount, 0);
            }
            return buffer;
        }

        [Benchmark()]
        public Vector2[] InternalNew()
        {
            Vector2[] buffer = new Vector2[vectors.Length];
            var path = new InternalPath(vectors, true);

            for (var y = path.Bounds.Top; y < path.Bounds.Bottom; y += (1f / 32f))
            {
                path.FindIntersections(new Vector2(path.Bounds.Left - 1, y), new Vector2(path.Bounds.Right + 1, y), buffer, path.PointCount, 0);
            }
            return buffer;
        }
    }
}
