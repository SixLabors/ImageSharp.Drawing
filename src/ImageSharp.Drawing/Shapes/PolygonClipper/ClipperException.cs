// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Clipper Exception
    /// </summary>
    /// <seealso cref="System.Exception" />
    public class ClipperException : Exception
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
