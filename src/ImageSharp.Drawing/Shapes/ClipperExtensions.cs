// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Drawing.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing
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
            var clipper = new Clipper();

            clipper.AddPath(shape, ClippingType.Subject);
            clipper.AddPaths(holes, ClippingType.Clip);

            IPath[] result = clipper.GenerateClippedShapes();

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
