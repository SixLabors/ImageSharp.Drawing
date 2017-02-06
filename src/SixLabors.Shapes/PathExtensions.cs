// <copyright file="PathExtensions.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;

    /// <summary>
    /// Conveniance methods that can be applied to shapes and paths
    /// </summary>
    public static class PathExtensions
    {
        /// <summary>
        /// Creates a path rotated by the specified radians around its center.
        /// </summary>
        /// <param name="path">The path to rotate.</param>
        /// <param name="radians">The radians to rotate the path.</param>
        /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
        public static IPath Rotate(this IPath path, float radians)
        {
            return path.Transform(Matrix3x2.CreateRotation(radians, path.Bounds.Center));
        }

        /// <summary>
        /// Creates a path rotated by the specified degrees around its center.
        /// </summary>
        /// <param name="shape">The path to rotate.</param>
        /// <param name="degrees">The degrees to rotate the path.</param>
        /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
        public static IPath RotateDegree(this IPath shape, float degrees)
        {
            return shape.Rotate((float)(Math.PI * degrees / 180.0));
        }

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="position">The translation position.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPath Translate(this IPath path, Vector2 position)
        {
            return path.Transform(Matrix3x2.CreateTranslation(position));
        }

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="x">The amount to translate along the X axis.</param>
        /// <param name="y">The amount to translate along the Y axis.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPath Translate(this IPath path, float x, float y)
        {
            return path.Translate(new Vector2(x, y));
        }
    }
}
