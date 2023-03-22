// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using Clipper2Lib;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Library to clip polygons.
    /// </summary>
    internal class Clipper
    {
        private const float ScalingFactor = 1000.0f;
        private readonly object syncRoot = new();
        private readonly Clipper64 innerClipper;

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        public Clipper() => this.innerClipper = new Clipper64();

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
            Paths64 closedPaths = new();
            Paths64 openPaths = new();

            // TODO: Why are we locking?
            lock (this.syncRoot)
            {
                this.innerClipper.Execute(ClipType.Difference, FillRule.EvenOdd, closedPaths, openPaths);
            }

            var shapes = new IPath[closedPaths.Count + openPaths.Count];

            for (int i = 0; i < closedPaths.Count; i++)
            {
                var points = new PointF[closedPaths[i].Count];

                for (int j = 0; j < closedPaths[i].Count; j++)
                {
                    Point64 p = closedPaths[i][j];

                    // to make the floating point polygons compatable with clipper we had
                    // to scale them up to make them ints but still retain some level of precision
                    // thus we have to scale them back down
                    points[j] = new Vector2(p.X / ScalingFactor, p.Y / ScalingFactor);
                }

                shapes[i] = new Polygon(new LinearLineSegment(points));
            }

            for (int i = 0; i < openPaths.Count; i++)
            {
                var points = new PointF[openPaths[i].Count];

                for (int j = 0; j < openPaths[i].Count; j++)
                {
                    Point64 p = openPaths[i][j];

                    // to make the floating point polygons compatable with clipper we had
                    // to scale them up to make them ints but still retain some level of precision
                    // thus we have to scale them back down
                    points[j] = new Vector2(p.X / ScalingFactor, p.Y / ScalingFactor);
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
            Path64 points = new(vectors.Length);
            for (int i = 0; i < vectors.Length; i++)
            {
                PointF v = vectors[i];
                points.Add(new Point64(v.X * ScalingFactor, v.Y * ScalingFactor));
            }

            PathType type = clippingType == ClippingType.Clip ? PathType.Clip : PathType.Subject;

            // TODO: Why are we locking?
            lock (this.syncRoot)
            {
                this.innerClipper.AddPath(points, type, !path.IsClosed);
            }
        }
    }
}
