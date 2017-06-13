// <copyright file="IPathCollection.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System;
    using System.Collections.Generic;
    using System.Numerics;

    /// <summary>
    /// Represents a logic path that can be drawn
    /// </summary>
    public interface IPathCollection : IEnumerable<IPath>
    {
        /// <summary>
        /// Gets the bounds enclosing the path
        /// </summary>
        RectangleF Bounds { get; }

        /// <summary>
        /// Transforms the path using the specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A new path with the matrix applied to it.</returns>
        IPathCollection Transform(Matrix3x2 matrix);
    }
}