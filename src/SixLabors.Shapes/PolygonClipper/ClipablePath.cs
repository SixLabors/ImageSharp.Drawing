// <copyright file="ClipablePath.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents a shape and its type for when clipping is applies.
    /// </summary>
    public struct ClipablePath
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipablePath" /> struct.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="type">The type.</param>
        public ClipablePath(IPath path, ClippingType type)
        {
            this.Path = path;
            this.Type = type;
        }

        /// <summary>
        /// Gets the path.
        /// </summary>
        public IPath Path { get; private set; }

        /// <summary>
        /// Gets the type.
        /// </summary>
        /// <value>
        /// The type.
        /// </value>
        public ClippingType Type { get; private set; }
    }
}
