// <copyright file="PathExtensions.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
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
        public static IPathCollection Rotate(this IPathCollection path, float radians)
        {
            return path.Transform(Matrix3x2Extensions.CreateRotation(radians, RectangleF.Center(path.Bounds)));
        }

        /// <summary>
        /// Creates a path rotated by the specified degrees around its center.
        /// </summary>
        /// <param name="shape">The path to rotate.</param>
        /// <param name="degrees">The degrees to rotate the path.</param>
        /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
        public static IPathCollection RotateDegree(this IPathCollection shape, float degrees)
        {
            return shape.Rotate((float)(Math.PI * degrees / 180.0));
        }

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="position">The translation position.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPathCollection Translate(this IPathCollection path, PointF position)
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
        public static IPathCollection Translate(this IPathCollection path, float x, float y)
        {
            return path.Translate(new PointF(x, y));
        }

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="scaleX">The amount to scale along the X axis.</param>
        /// <param name="scaleY">The amount to scale along the Y axis.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPathCollection Scale(this IPathCollection path, float scaleX, float scaleY)
        {
            return path.Transform(Matrix3x2.CreateScale(scaleX, scaleY, RectangleF.Center(path.Bounds)));
        }

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="scale">The amount to scale along both the x and y axis.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPathCollection Scale(this IPathCollection path, float scale)
        {
            return path.Transform(Matrix3x2.CreateScale(scale, RectangleF.Center(path.Bounds)));
        }

        /// <summary>
        /// Creates a path rotated by the specified radians around its center.
        /// </summary>
        /// <param name="path">The path to rotate.</param>
        /// <param name="radians">The radians to rotate the path.</param>
        /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
        public static IPath Rotate(this IPath path, float radians)
        {
            return path.Transform(Matrix3x2.CreateRotation(radians, RectangleF.Center(path.Bounds)));
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
        public static IPath Translate(this IPath path, PointF position)
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
            return path.Transform(Matrix3x2.CreateScale(scaleX, scaleY, RectangleF.Center(path.Bounds)));
        }

        /// <summary>
        /// Creates a path translated by the supplied postion
        /// </summary>
        /// <param name="path">The path to translate.</param>
        /// <param name="scale">The amount to scale along both the x and y axis.</param>
        /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
        public static IPath Scale(this IPath path, float scale)
        {
            return path.Transform(Matrix3x2.CreateScale(scale, RectangleF.Center(path.Bounds)));
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
        public static IEnumerable<PointF> FindIntersections(this IPath path, PointF start, PointF end)
        {
            PointF[] buffer = ArrayPool<PointF>.Shared.Rent(path.MaxIntersections);
            try
            {
                var span = new Span<PointF>(buffer);
                int hits = path.FindIntersections(start, end, span);
                PointF[] results = new PointF[hits];
                for (int i = 0; i < hits; i++)
                {
                    results[i] = buffer[i];
                }

                return results;
            }
            finally
            {
                ArrayPool<PointF>.Shared.Return(buffer);
            }
        }
    }
}
