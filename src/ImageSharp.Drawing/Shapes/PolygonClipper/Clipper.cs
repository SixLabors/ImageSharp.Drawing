// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Library to clip polygons.
    /// </summary>
    internal class Clipper
    {
        // To make the floating point polygons compatable with clipper we have to scale them.
        private const float ScalingFactor = 1000F;
        private readonly Shapes.PolygonClipper.PolygonClipper polygonClipper;

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        public Clipper()
            => this.polygonClipper = new Shapes.PolygonClipper.PolygonClipper();

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper" /> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(params ClippablePath[] shapes)
            : this() => this.AddPaths(shapes);

        /// <summary>
        /// Executes the specified clip type.
        /// </summary>
        /// <returns>
        /// Returns the <see cref="IPath" /> array containing the converted polygons.
        /// </returns>
        /// <exception cref="ClipperException">GenerateClippedShapes: Open paths have been disabled.</exception>
        public IPath[] GenerateClippedShapes()
        {
            PathsF closedPaths = new();
            PathsF openPaths = new();

            this.polygonClipper.Execute(ClipType.Difference, FillRule.EvenOdd, closedPaths, openPaths);

            var shapes = new IPath[closedPaths.Count + openPaths.Count];
            const float scale = 1F / ScalingFactor;

            for (int i = 0; i < closedPaths.Count; i++)
            {
                var points = new PointF[closedPaths[i].Count];

                for (int j = 0; j < closedPaths[i].Count; j++)
                {
                    Vector2 v = closedPaths[i][j];
                    points[j] = v * scale;
                }

                shapes[i] = new Polygon(new LinearLineSegment(points));
            }

            for (int i = 0; i < openPaths.Count; i++)
            {
                var points = new PointF[closedPaths[i].Count];

                for (int j = 0; j < closedPaths[i].Count; j++)
                {
                    Vector2 v = closedPaths[i][j];
                    points[j] = v * scale;
                }

                shapes[i] = new Path(new LinearLineSegment(points));
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
            PathF points = new(vectors.Length);
            for (int i = 0; i < vectors.Length; i++)
            {
                Vector2 v = vectors[i];
                points.Add(v * ScalingFactor);
            }

            this.polygonClipper.AddPath(points, clippingType, !path.IsClosed);
        }
    }
}
