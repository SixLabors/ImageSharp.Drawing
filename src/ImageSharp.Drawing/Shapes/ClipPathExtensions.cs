// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Provides extension methods to <see cref="IPath"/> that allow the clipping of shapes.
    /// </summary>
    public static class ClipPathExtensions
    {
        /// <summary>
        /// Clips the specified subject path with the provided clipping paths.
        /// </summary>
        /// <param name="subjectPath">The subject path.</param>
        /// <param name="clipPaths">The clipping paths.</param>
        /// <returns>The clipped <see cref="IPath"/>.</returns>
        /// <exception cref="ClipperException">Thrown when an error occured while attempting to clip the polygon.</exception>
        public static IPath Clip(this IPath subjectPath, params IPath[] clipPaths)
            => subjectPath.Clip((IEnumerable<IPath>)clipPaths);

        /// <summary>
        /// Clips the specified subject path with the provided clipping paths.
        /// </summary>
        /// <param name="subjectPath">The subject path.</param>
        /// <param name="operation">The clipping operation.</param>
        /// <param name="rule">The intersection rule.</param>
        /// <param name="clipPaths">The clipping paths.</param>
        /// <returns>The clipped <see cref="IPath"/>.</returns>
        /// <exception cref="ClipperException">Thrown when an error occured while attempting to clip the polygon.</exception>
        public static IPath Clip(
            this IPath subjectPath,
            ClippingOperation operation,
            IntersectionRule rule,
            params IPath[] clipPaths)
            => subjectPath.Clip(operation, rule, (IEnumerable<IPath>)clipPaths);

        /// <summary>
        /// Clips the specified subject path with the provided clipping paths.
        /// </summary>
        /// <param name="subjectPath">The subject path.</param>
        /// <param name="clipPaths">The clipping paths.</param>
        /// <returns>The clipped <see cref="IPath"/>.</returns>
        /// <exception cref="ClipperException">Thrown when an error occured while attempting to clip the polygon.</exception>
        public static IPath Clip(this IPath subjectPath, IEnumerable<IPath> clipPaths)
            => subjectPath.Clip(ClippingOperation.Difference, IntersectionRule.EvenOdd, clipPaths);

        /// <summary>
        /// Clips the specified subject path with the provided clipping paths.
        /// </summary>
        /// <param name="subjectPath">The subject path.</param>
        /// <param name="operation">The clipping operation.</param>
        /// <param name="rule">The intersection rule.</param>
        /// <param name="clipPaths">The clipping paths.</param>
        /// <returns>The clipped <see cref="IPath"/>.</returns>
        /// <exception cref="ClipperException">Thrown when an error occured while attempting to clip the polygon.</exception>
        public static IPath Clip(
            this IPath subjectPath,
            ClippingOperation operation,
            IntersectionRule rule,
            IEnumerable<IPath> clipPaths)
        {
            Clipper clipper = new();

            clipper.AddPath(subjectPath, ClippingType.Subject);
            clipper.AddPaths(clipPaths, ClippingType.Clip);

            IPath[] result = clipper.GenerateClippedShapes(operation, rule);

            return new ComplexPolygon(result);
        }
    }
}
