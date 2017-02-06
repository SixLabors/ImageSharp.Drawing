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
        public Clipper(IEnumerable<ClipableShape> shapes)
            : this()
        {
            this.AddShapes(shapes);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper" /> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(params ClipableShape[] shapes)
            : this()
        {
            this.AddShapes(shapes);
        }

        /// <summary>
        /// Executes the specified clip type.
        /// </summary>
        /// <returns>
        /// Returns the <see cref="IShape" /> array containing the converted polygons.
        /// </returns>
        public ImmutableArray<IShape> GenerateClippedShapes()
        {
            List<PolyNode> results = new List<PolyNode>();
            lock (this.syncRoot)
            {
                this.innerClipper.Execute(ClipType.ctDifference, results);
            }

            IShape[] shapes = new IShape[results.Count];
            for (var i = 0; i < results.Count; i++)
            {
                var source = results[i].Source;
                var shape = source as IShape;

                if (shape != null)
                {
                    shapes[i] = shape;
                }
                else
                {
                    var wrapped = source as IWrapperPath;
                    if (wrapped != null)
                    {
                        shapes[i] = wrapped.AsShape();
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
                            shapes[i] = new Path(new LinearLineSegment(points)).AsShape();
                        }
                        else
                        {
                            shapes[i] = new Polygon(new LinearLineSegment(points));
                        }
                    }
                }
            }

            return ImmutableArray.Create(shapes);
        }

        /// <summary>
        /// Adds the paths.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public void AddShapes(IEnumerable<ClipableShape> shapes)
        {
            Guard.NotNull(shapes, nameof(shapes));
            foreach (var p in shapes)
            {
                this.AddShape(p.Shape, p.Type);
            }
        }

        /// <summary>
        /// Adds the shapes.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        /// <param name="clippingType">The clipping type.</param>
        public void AddShapes(IEnumerable<IShape> shapes, ClippingType clippingType)
        {
            Guard.NotNull(shapes, nameof(shapes));
            foreach (var p in shapes)
            {
                this.AddShape(p, clippingType);
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="clippingType">The clipping type.</param>
        public void AddShape(IShape shape, ClippingType clippingType)
        {
            Guard.NotNull(shape, nameof(shape));
            foreach (var p in shape.Paths)
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
        internal void AddPath(IPath path, ClippingType clippingType)
        {
            // we are only closed shapes at this point, we need a better
            // way to figure out if a path is a shape etc
            // might have to unify the apis
            var vectors = path.Flatten();
            List<IntPoint> points = new List<ClipperLib.IntPoint>(vectors.Length);
            foreach (var v in vectors)
            {
                points.Add(new IntPoint(v.X * ScalingFactor, v.Y * ScalingFactor));
            }

            PolyType type = clippingType == ClippingType.Clip ? PolyType.ptClip : PolyType.ptSubject;
            lock (this.syncRoot)
            {
                this.innerClipper.AddPath(points, type, true, path);
            }
        }
    }
}