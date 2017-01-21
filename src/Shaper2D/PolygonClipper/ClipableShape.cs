// <copyright file="ClipableShape.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents a shape and its type for when clipping is applies.
    /// </summary>
    public struct ClipableShape
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipableShape"/> struct.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="type">The type.</param>
        public ClipableShape(IShape shape, ClippingType type)
        {
            this.Shape = shape;
            this.Type = type;
        }

        /// <summary>
        /// Gets the shape.
        /// </summary>
        /// <value>
        /// The shape.
        /// </value>
        public IShape Shape { get; private set; }

        /// <summary>
        /// Gets the type.
        /// </summary>
        /// <value>
        /// The type.
        /// </value>
        public ClippingType Type { get; private set; }
    }
}
