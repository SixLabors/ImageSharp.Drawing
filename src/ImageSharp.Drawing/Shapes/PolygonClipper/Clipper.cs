// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using System.Collections.Generic;
using System.Numerics;
using Clipper2Lib;
using Clipper2 = Clipper2Lib.Clipper;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Library to clip polygons.
    /// </summary>
    internal class Clipper
    {
        private const float ScalingFactor = 1000.0f;
        private readonly object syncRoot = new object();
        private readonly Clipper2Lib.Clipper64 innerClipper;


        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        public Clipper()
        {
            this.innerClipper = new Clipper64();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper" /> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(params ClippablePath[] shapes)
            : this()
        {
            this.AddPaths(shapes);
        }

        /// <summary>
        /// Executes the specified clip type.
        /// </summary>
        /// <returns>
        /// Returns the <see cref="IPath" /> array containing the converted polygons.
        /// </returns>
        /// <exception cref="ClipperException">GenerateClippedShapes: Open paths have been disabled.</exception>
        public IPath[] GenerateClippedShapes()
        {
            PolyTree64 results = new();

            lock (this.syncRoot)
            {
                this.innerClipper.Execute(ClipType.Difference, FillRule.EvenOdd, results);
            }

            var shapes = new IPath[results.Count];

            for (int i = 0; i < results.Count; i++)
            {
                var points = new PointF[results[i].Polygon.Count];

                for (int j = 0; j < results[i].Polygon.Count; j++)
                {
                    Point64 p = results[i].Polygon[j];

                    // to make the floating point polygons compatable with clipper we had
                    // to scale them up to make them ints but still retain some level of precision
                    // thus we have to scale them back down
                    points[j] = new Vector2(p.X / ScalingFactor, p.Y / ScalingFactor);
                }

                shapes[i] = results[i].IsHole
                    ? new Path(new LinearLineSegment(points))
                    : new Polygon(new LinearLineSegment(points));
            }

            return shapes;
        }

        /// <summary>
        /// Adds the paths.
        /// </summary>
        /// <param name="paths">The paths.</param>
        /// <exception cref="ClipperException">Open paths have been disabled.</exception>
        public void AddPaths(ClippablePath[] paths)
        {
            Guard.NotNull(paths, nameof(paths));

            for (int i = 0; i < paths.Length; i++)
            {
                ref ClippablePath p = ref paths[i];

                this.AddPath(p.Path, p.Type);
            }
        }

        /// <summary>
        /// Adds the shapes.
        /// </summary>
        /// <param name="paths">The paths.</param>
        /// <param name="clippingType">The clipping type.</param>
        /// <exception cref="ClipperException">Open paths have been disabled.</exception>
        public void AddPaths(IEnumerable<IPath> paths, ClippingType clippingType)
        {
            Guard.NotNull(paths, nameof(paths));

            foreach (IPath p in paths)
            {
                this.AddPath(p, clippingType);
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="clippingType">The clipping type.</param>
        /// <exception cref="ClipperException">Open paths have been disabled.</exception>
        public void AddPath(IPath path, ClippingType clippingType)
        {
            Guard.NotNull(path, nameof(path));

            foreach (ISimplePath p in path.Flatten())
            {
                this.AddPath(p, clippingType);
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="clippingType">Type of the poly.</param>
        /// <exception cref="ClipperException">Open paths have been disabled.</exception>
        internal void AddPath(ISimplePath path, ClippingType clippingType)
        {
            ReadOnlySpan<PointF> vectors = path.Points.Span;
            Path64 points = new(vectors.Length);
            foreach (PointF v in vectors)
            {
                points.Add(new Point64(v.X * ScalingFactor, v.Y * ScalingFactor));
            }

            PathType type = clippingType == ClippingType.Clip ? PathType.Clip : PathType.Subject;
            lock (this.syncRoot)
            {
                this.innerClipper.AddPath(points, type, path.IsClosed);
            }
        }
    }
}
