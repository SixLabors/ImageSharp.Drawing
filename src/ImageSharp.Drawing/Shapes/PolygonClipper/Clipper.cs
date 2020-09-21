// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using ClipperLib;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Library to clip polygons.
    /// </summary>
    public class Clipper
    {
        private const float ScalingFactor = 1000.0f;

        private readonly ClipperLib.Clipper innerClipper;
        private readonly object syncRoot = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        public Clipper()
        {
            this.innerClipper = new ClipperLib.Clipper();
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
        public IPath[] GenerateClippedShapes()
        {
            var results = new List<PolyNode>();

            lock (this.syncRoot)
            {
                this.innerClipper.Execute(ClipType.ctDifference, results);
            }

            var shapes = new IPath[results.Count];

            for (int i = 0; i < results.Count; i++)
            {
                var points = new PointF[results[i].Contour.Count];

                for (int j = 0; j < results[i].Contour.Count; j++)
                {
                    IntPoint p = results[i].Contour[j];

                    // to make the floating point polygons compatable with clipper we had
                    // to scale them up to make them ints but still retain some level of precision
                    // thus we have to scale them back down
                    points[j] = new Vector2(p.X / ScalingFactor, p.Y / ScalingFactor);
                }

                shapes[i] = results[i].IsOpen
                    ? new Path(new LinearLineSegment(points))
                    : new Polygon(new LinearLineSegment(points));
            }

            return shapes;
        }

        /// <summary>
        /// Adds the paths.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public void AddPaths(ClippablePath[] paths)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

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
        public void AddPaths(IEnumerable<IPath> paths, ClippingType clippingType)
        {
            if (paths is null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

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
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

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
        /// <exception cref="ClipperException">AddPath: Open paths have been disabled.</exception>
        internal void AddPath(ISimplePath path, ClippingType clippingType)
        {
            ReadOnlySpan<PointF> vectors = path.Points.Span;

            var points = new List<IntPoint>(vectors.Length);
            foreach (PointF v in vectors)
            {
                points.Add(new IntPoint(v.X * ScalingFactor, v.Y * ScalingFactor));
            }

            PolyType type = clippingType == ClippingType.Clip ? PolyType.ptClip : PolyType.ptSubject;
            lock (this.syncRoot)
            {
                this.innerClipper.AddPath(points, type, path.IsClosed);
            }
        }
    }
}