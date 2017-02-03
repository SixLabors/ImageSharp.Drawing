using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace SixLabors.Shapes
{
    /// <summary>
    /// Conveniance methods that can be applied to shapes and paths
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Creates a shape rotated by the specified radians around its center.
        /// </summary>
        /// <typeparam name="TShape">The type of the shape.</typeparam>
        /// <param name="shape">The shape to rotate.</param>
        /// <param name="radians">The radians to rotate the shape.</param>
        /// <returns></returns>
        public static IShape Rotate(this IShape shape, float radians)
        {
            return shape.Transform(Matrix3x2.CreateRotation(radians, shape.Bounds.Center));
        }

        /// <summary>
        /// Creates a path rotated by the specified radians around its center.
        /// </summary>
        /// <param name="path">The path to rotate.</param>
        /// <param name="radians">The radians to rotate the path.</param>
        /// <returns></returns>
        public static IPath Rotate(this IPath path, float radians)
        {
            return path.Transform(Matrix3x2.CreateRotation(radians, path.Bounds.Center));
        }
    }
}
