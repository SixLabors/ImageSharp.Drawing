// <copyright file="ClipperException.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Clipper Exception
    /// </summary>
    /// <seealso cref="System.Exception" />
    internal class ClipperException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipperException"/> class.
        /// </summary>
        /// <param name="description">The description.</param>
        public ClipperException(string description)
            : base(description)
        {
        }
    }
}