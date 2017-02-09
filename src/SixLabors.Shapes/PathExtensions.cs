// <copyright file="PathExtensions.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Buffers;
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

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="scaleX">The amount to scale along the X axis.</param>
        /// <param name="scaleY">The amount to scale along the Y axis.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPath Scale(this IPath path, float scaleX, float scaleY)
        {
            return path.Transform(Matrix3x2.CreateScale(scaleX, scaleY, path.Bounds.Center));
        }

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="scale">The amount to scale along both the x and y axis.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPath Scale(this IPath path, float scale)
        {
            return path.Transform(Matrix3x2.CreateScale(scale, path.Bounds.Center));
        }

        /// <summary>
        /// Finds the intersections.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>
        /// The points along the line the intersect with the boundaries of the polygon.
        /// </returns>
        public static IEnumerable<Vector2> FindIntersections(this IPath path, Vector2 start, Vector2 end)
        {
            var buffer = ArrayPool<Vector2>.Shared.Rent(path.MaxIntersections);
            try
            {
                var hits = path.FindIntersections(start, end, buffer, path.MaxIntersections, 0);
                for (var i = 0; i < hits; i++)
                {
                    yield return buffer[i];
                }
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(buffer);
            }
        }
    }
}
