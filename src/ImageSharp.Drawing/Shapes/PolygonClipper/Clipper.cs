// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
    /// <summary>
    /// Library to clip polygons.
    /// </summary>
    internal class Clipper
    {
        // To make the floating point polygons compatable with clipper we have to scale them.
        private const float ScalingFactor = 1000F;
        private readonly PolygonClipper polygonClipper;

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        public Clipper()
            => this.polygonClipper = new PolygonClipper();

        /// <summary>
        /// Generates the clipped shapes from the previously provided paths.
        /// </summary>
        /// <param name="operation">The clipping operation.</param>
        /// <param name="rule">The intersection rule.</param>
        /// <returns>The <see cref="T:IPath[]"/>.</returns>
        public IPath[] GenerateClippedShapes(ClippingOperation operation, IntersectionRule rule)
        {
            PathsF closedPaths = new();
            PathsF openPaths = new();

            FillRule fillRule = rule == IntersectionRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero;
            this.polygonClipper.Execute(operation, fillRule, closedPaths, openPaths);

            var shapes = new IPath[closedPaths.Count + openPaths.Count];

            int index = 0;
            for (int i = 0; i < closedPaths.Count; i++)
            {
                PathF path = closedPaths[i];
                var points = new PointF[path.Count];

                for (int j = 0; j < path.Count; j++)
                {
                    points[j] = path[j] / ScalingFactor;
                }

                shapes[index++] = new Polygon(new LinearLineSegment(points));
            }

            for (int i = 0; i < openPaths.Count; i++)
            {
                PathF path = openPaths[i];
                var points = new PointF[path.Count];

                for (int j = 0; j < path.Count; j++)
                {
                    points[j] = path[j] / ScalingFactor;
                }

                shapes[index++] = new Polygon(new LinearLineSegment(points));
            }

            return shapes;
        }

        /// <summary>
        /// Adds the shapes.
        /// </summary>
        /// <param name="paths">The paths.</param>
        /// <param name="clippingType">The clipping type.</param>
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
        internal void AddPath(ISimplePath path, ClippingType clippingType)
        {
            ReadOnlySpan<PointF> vectors = path.Points.Span;
            PathF points = new(vectors.Length);
            for (int i = 0; i < vectors.Length; i++)
            {
                points.Add((Vector2)vectors[i] * ScalingFactor);
            }

            this.polygonClipper.AddPath(points, clippingType, !path.IsClosed);
        }
    }
}
