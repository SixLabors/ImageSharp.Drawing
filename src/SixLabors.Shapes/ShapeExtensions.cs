// <copyright file="ShapeExtensions.cs" company="Scott Williams">
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
    public static class ShapeExtensions
    {
        /// <summary>
        /// Creates a shape rotated by the specified radians around its center.
        /// </summary>
        /// <param name="shape">The shape to rotate.</param>
        /// <param name="radians">The radians to rotate the shape.</param>
        /// <returns></returns>
        public static IShape Rotate(this IShape shape, float radians)
        {
            return shape.Transform(Matrix3x2.CreateRotation(radians, shape.Bounds.Center));
        }
    }
}
