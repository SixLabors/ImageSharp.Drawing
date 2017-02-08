// <copyright file="Clipper.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>
namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Diagnostics;
    using System.Numerics;
    using ClipperLib;

    /// <summary>
    /// Library to clip polygons.
    /// </summary>
    public class Clipper
    {
        private const float ScalingFactor = 1000.0f;

        private readonly ClipperLib.Clipper innerClipper;
        private object syncRoot = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        public Clipper()
        {
            this.innerClipper = new ClipperLib.Clipper();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(IEnumerable<ClipablePath> shapes)
            : this()
        {
            this.AddPaths(shapes);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper" /> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(params ClipablePath[] shapes)
            : this()
        {
            this.AddPaths(shapes);
        }

        /// <summary>
        /// Executes the specified clip type.
        /// </summary>
        /// <returns>
        /// Returns the <see cref="IShape" /> array containing the converted polygons.
        /// </returns>
        public ImmutableArray<IPath> GenerateClippedShapes()
        {
            List<PolyNode> results = new List<PolyNode>();
            lock (this.syncRoot)
            {
                this.innerClipper.Execute(ClipType.ctDifference, results);
            }

            IPath[] shapes = new IPath[results.Count];
            for (var i = 0; i < results.Count; i++)
            {
                var source = results[i].Source;
                var path = source as IPath;

                if (path != null)
                {
                    shapes[i] = path;
                }
                else
                {
                    var points = new Vector2[results[i].Contour.Count];
                    for (var j = 0; j < results[i].Contour.Count; j++)
                    {
                        var p = results[i].Contour[j];

                        // to make the floating point polygons compatable with clipper we had
                        // to scale them up to make them ints but still retain some level of precision
                        // thus we have to scale them back down
                        points[j] = new Vector2(p.X / ScalingFactor, p.Y / ScalingFactor);
                    }

                    if (results[i].IsOpen)
                    {
                        shapes[i] = new Path(new LinearLineSegment(points));
                    }
                    else
                    {
                        shapes[i] = new Polygon(new LinearLineSegment(points));
                    }
                }
            }

            return ImmutableArray.Create(shapes);
        }

        /// <summary>
        /// Adds the paths.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public void AddPaths(IEnumerable<ClipablePath> paths)
        {
            Guard.NotNull(paths, nameof(paths));
            foreach (var p in paths)
            {
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
            Guard.NotNull(paths, nameof(paths));
            foreach (var p in paths)
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
            foreach (var p in path.Flatten())
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
            var vectors = path.Points;
            List<IntPoint> points = new List<ClipperLib.IntPoint>(vectors.Length);
            foreach (var v in vectors)
            {
                points.Add(new IntPoint(v.X * ScalingFactor, v.Y * ScalingFactor));
            }

            PolyType type = clippingType == ClippingType.Clip ? PolyType.ptClip : PolyType.ptSubject;
            lock (this.syncRoot)
            {
                this.innerClipper.AddPath(points, type, path.IsClosed, path);
            }
        }
    }
}