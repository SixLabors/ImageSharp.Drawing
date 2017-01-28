// <copyright file="ClipperExtensions.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using Shaper2D.PolygonClipper;

    /// <summary>
    /// Clipping extensions for shapes
    /// </summary>
    public static class ClipperExtensions
    {
        /// <summary>
        /// Clips the specified holes.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="holes">The holes.</param>
        /// <returns>Returns a new shape with the holes cliped out out the shape.</returns>
        public static IShape Clip(this IShape shape, IEnumerable<IShape> holes)
        {
            var clipper = new PolygonClipper.Clipper();

            clipper.AddShape(shape, ClippingType.Subject);
            clipper.AddShapes(holes, ClippingType.Clip);
            var result = clipper.GenerateClippedShapes();
            return new ComplexPolygon(result);
        }

        /// <summary>
        /// Clips the specified holes.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="holes">The holes.</param>
        /// <returns>Returns a new shape with the holes cliped out out the shape.</returns>
        public static IShape Clip(this IShape shape, params IShape[] holes) => shape.Clip((IEnumerable<IShape>)holes);
    }
}
