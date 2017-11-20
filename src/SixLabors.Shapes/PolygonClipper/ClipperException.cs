// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.Shapes.PolygonClipper
{
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