// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.Shapes.PolygonClipper;

namespace SixLabors.Shapes
{
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
        public static IPath Clip(this IPath shape, IEnumerable<IPath> holes)
        {
            Clipper clipper = new PolygonClipper.Clipper();

            clipper.AddPath(shape, ClippingType.Subject);
            clipper.AddPaths(holes, ClippingType.Clip);
            IEnumerable<IPath> result = clipper.GenerateClippedShapes();
            return new ComplexPolygon(result);
        }

        /// <summary>
        /// Clips the specified holes.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="holes">The holes.</param>
        /// <returns>Returns a new shape with the holes cliped out out the shape.</returns>
        public static IPath Clip(this IPath shape, params IPath[] holes) => shape.Clip((IEnumerable<IPath>)holes);
    }
}
